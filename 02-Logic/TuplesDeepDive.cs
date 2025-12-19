// ================================================================
// TuplesDeepDive.cs
// ================================================================
//
// Goal:
// - Keep your original examples:
//     * (int, string) and named tuples
//     * Operations returning (Sum, Subtraction)
//     * Deconstruction
// - Add “hard-to-find” internals:
//     * ValueTuple vs Tuple: allocation, layout, copying, and metadata
//     * How Roslyn lowers tuple syntax (names, Item1, deconstruction)
//     * JIT / CPU-level intuition: struct return conventions, registers, copies
//     * When tuples box, when they don't, and how to avoid perf cliffs
//     * Large tuples and “tuple spill” (stack traffic) + cache effects
//     * Pattern matching + switch expressions + tuples as keys
//     * When to use record/struct instead of tuples in production APIs
//
// This file is intentionally comment-heavy and GitHub-ready.
// ================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static System.Console;

partial class Program
{
    // ------------------------------------------------------------
    // Entry point for this topic (call from your Main or runner).
    // ------------------------------------------------------------
    public static void TuplesDeepDive()
    {
        Tuples_Basics();                // your original idea
        Tuples_ValueTupleVsTuple();     // allocation + why ValueTuple wins in hot paths
        Tuples_Deconstruction_Lowering();
        Tuples_ReturnConventions_And_Copies();
        Tuples_LargeTuple_Spill_Demo();
        Tuples_AsKeys_And_PatternMatching();
        Tuples_WhenNotToUse();
    }

    // ============================================================
    // 0) Mental model: “tuple syntax” in C# mostly means ValueTuple
    // ============================================================
    //
    // ✅ (int, string) in modern C# typically means System.ValueTuple<int, string>
    //
    // Contrast:
    //   - System.Tuple<...>   : class (heap allocation), immutable, reference type
    //   - System.ValueTuple<...>: struct (usually stack/local), mutable fields, value type
    //
    // Why you should care:
    // - ValueTuple is often “zero allocation” (unless boxed) and tends to be faster.
    // - Tuple allocates and adds GC pressure.
    //
    // Names in tuples:
    // - (int Number, string Text) carries *element names* in metadata via attributes.
    // - Runtime representation is still ValueTuple with fields Item1/Item2.
    // - Names do NOT change layout. They improve readability and tooling.
    //
    // Hard-to-find nuance:
    // - Element names are preserved for consumers (intellisense / deconstruction)
    //   but at runtime, ValueTuple fields remain Item1, Item2, ...
    // - If you call .ToString() on a ValueTuple, it prints values, not names.
    // ============================================================

    // ------------------------------------------------------------
    // 1) Your original examples (kept, slightly expanded)
    // ------------------------------------------------------------
    static void Tuples_Basics()
    {
        WriteLine("== Tuples_Basics ==");

        (int, string) myTuple = (42, "Hola");
        WriteLine($"Number: {myTuple.Item1}, Text: {myTuple.Item2}");

        (int Number, string Text) myOtherTuple = (33, "Named");
        WriteLine($"Number: {myOtherTuple.Number}, Text: {myOtherTuple.Text}");

        var operations = Operations(20, 10);
        WriteLine($"Sum: {operations.Sum}, Subtraction: {operations.Subtraction}");

        (int sum, int subtraction) = Operations(25, 15);
        WriteLine($"Sum: {sum}, Subtraction: {subtraction}");

        // Tuple literals + deconstruction in one line:
        var (x, y) = (a: 10, b: 99);
        WriteLine($"x={x}, y={y}");

        WriteLine();
    }

    // Your original method (kept)
    static (int Sum, int Subtraction) Operations(int a, int b)
        => (a + b, a - b);

    // ------------------------------------------------------------
    // 2) ValueTuple vs Tuple (heap vs stack) + boxing cliffs
    // ------------------------------------------------------------
    static void Tuples_ValueTupleVsTuple()
    {
        WriteLine("== Tuples_ValueTupleVsTuple ==");

        // ValueTuple: struct (no allocation as a local)
        (int Sum, int Sub) vt = (1, -1);

        // Tuple: class (allocates on heap)
        Tuple<int, int> rt = Tuple.Create(1, -1);

        WriteLine($"ValueTuple (struct) = {vt}");
        WriteLine($"Tuple (class)       = {rt}");

        // Boxing cliff:
        // ValueTuple is a value type. If you store it in object, it boxes (heap alloc).
        object boxed = vt; // ✅ boxes
        WriteLine($"Boxed ValueTuple type = {boxed.GetType().FullName}");

        // Avoid boxing by keeping it generic/typed:
        ConsumeValueTuple(vt);

        WriteLine();
    }

    static void ConsumeValueTuple((int Sum, int Sub) v)
        => WriteLine($"ConsumeValueTuple: {v.Sum}, {v.Sub}");

    // ============================================================
    // 3) Roslyn lowering: what the compiler actually emits
    // ============================================================
    //
    // Deconstruction:
    //   (a, b) = someTuple;
    //
    // typically lowers to something like:
    //   var tmp = someTuple;
    //   a = tmp.Item1;
    //   b = tmp.Item2;
    //
    // Which implies:
    // - There may be a copy of the tuple into a temp (struct copy).
    // - In hot code, repeated deconstruction can add extra copies.
    //
    // Optimization trick:
    // - Use 'ref readonly' in advanced scenarios to avoid copying large structs.
    // - Or avoid large tuples altogether for hot paths.
    // ============================================================
    static void Tuples_Deconstruction_Lowering()
    {
        WriteLine("== Tuples_Deconstruction_Lowering ==");

        var t = Operations(100, 7);

        // Deconstruct to locals (nice and readable)
        var (sum, sub) = t;

        WriteLine($"sum={sum}, sub={sub}");

        // If you deconstruct a large struct many times, you can pay extra copies.
        // We'll show that more dramatically later with BigTuple.

        WriteLine();
    }

    // ============================================================
    // 4) CPU/JIT intuition: tuple returns and “struct return conventions”
    // ============================================================
    //
    // At the processor level, returning a tuple is returning a struct.
    //
    // The ABI (calling convention) decides how it is returned:
    // - Small structs often return via registers (fast)
    // - Larger structs return via a hidden “return buffer” pointer (sret),
    //   meaning the caller provides stack space, callee writes into it.
    //
    // Practical implications:
    // - Small tuples like (int,int) are extremely cheap.
    // - Larger tuples may cause more memory traffic (stack loads/stores).
    //
    // You don't need to memorize ABI rules to be elite:
    // Just remember “bigger tuples -> more copies -> more memory traffic”.
    // ============================================================
    static void Tuples_ReturnConventions_And_Copies()
    {
        WriteLine("== Tuples_ReturnConventions_And_Copies ==");

        var sw = Stopwatch.StartNew();
        long acc = 0;

        for (int i = 0; i < 5_000_00; i++)
        {
            // Tiny tuple: usually register-friendly.
            var (a, b) = TinyOps(i);
            acc += a ^ b;
        }

        sw.Stop();
        WriteLine($"Tiny tuple loop acc={acc}, ms={sw.ElapsedMilliseconds}");

        WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static (int A, int B) TinyOps(int x) => (x + 1, x - 1);

    // ------------------------------------------------------------
    // 5) Large tuples: “tuple spill” and why it can get expensive
    // ------------------------------------------------------------
    //
    // ValueTuple nesting:
    // - ValueTuple supports up to 7 elements directly, the 8th is a TRest field
    //   holding another ValueTuple (nesting). That can increase copy cost.
    //
    // Example:
    //   (a,b,c,d,e,f,g,h) becomes ValueTuple<T1..T7, ValueTuple<T8>>
    //
    // Hard-to-find nuance:
    // - Large tuples are still value types, but copying them copies *all* fields.
    // - If you pass them by value or deconstruct repeatedly, you amplify memory traffic.
    // ------------------------------------------------------------
    static void Tuples_LargeTuple_Spill_Demo()
    {
        WriteLine("== Tuples_LargeTuple_Spill_Demo ==");

        const int N = 300_000;

        // Big tuple with many ints — easy to copy a lot by accident.
        var sw = Stopwatch.StartNew();
        long s1 = 0;
        for (int i = 0; i < N; i++)
        {
            var t = BigOps(i);         // returns a big struct
            var (a, b, c, d, e, f, g, h) = t;  // deconstruct (copy temp + reads)
            s1 += a + b + c + d + e + f + g + h;
        }
        sw.Stop();
        WriteLine($"Big tuple (deconstruct) sum={s1}, ms={sw.ElapsedMilliseconds}");

        // A more “copy-aware” approach: keep in a single variable and access fields.
        // (Still may copy depending on JIT, but often reduces deconstruction temps.)
        sw.Restart();
        long s2 = 0;
        for (int i = 0; i < N; i++)
        {
            var t = BigOps(i);
            s2 += t.Item1 + t.Item2 + t.Item3 + t.Item4 + t.Item5 + t.Item6 + t.Item7 + t.Rest.Item1;
        }
        sw.Stop();
        WriteLine($"Big tuple (field access) sum={s2}, ms={sw.ElapsedMilliseconds}");

        WriteLine();
    }

    static (int, int, int, int, int, int, int, int) BigOps(int x)
        => (x, x + 1, x + 2, x + 3, x + 4, x + 5, x + 6, x + 7);

    // ------------------------------------------------------------
    // 6) Tuples as keys + pattern matching (very practical)
    // ------------------------------------------------------------
    //
    // Tuple keys are a clean way to model “multi-dimensional” decisions:
    // - (Method, Path)
    // - (Country, Currency)
    // - (FeatureFlag, TenantTier)
    //
    // ValueTuple implements structural equality + hashing, so it works in Dictionary.
    //
    // Hard-to-find caution:
    // - Structural hashing on big tuples costs more (more fields to combine).
    // - For super hot dictionaries, a custom struct key with hand-tuned hash
    //   can win — but only after measuring.
    // ------------------------------------------------------------
    static void Tuples_AsKeys_And_PatternMatching()
    {
        WriteLine("== Tuples_AsKeys_And_PatternMatching ==");

        // Dictionary with tuple key
        var routes = new Dictionary<(string Method, string Path), string>(StringTupleComparer.Ordinal)
        {
            [("GET", "/v1/health")] = "HealthHandler",
            [("GET", "/v1/export/call-records")] = "CallRecordsExportHandler",
            [("POST", "/v1/export")] = "ExportStartHandler"
        };

        var key = (Method: "GET", Path: "/v1/export/call-records");
        WriteLine($"Lookup {key} => {routes[key]}");

        // Pattern matching on tuples (switch expression)
        var status = (isAuthenticated: true, hasScope: true, isAdmin: false) switch
        {
            (false, _, _) => "401 Unauthorized",
            (true, false, _) => "403 Forbidden (missing scope)",
            (true, true, true) => "200 OK (admin)",
            (true, true, false) => "200 OK (scoped)"
        };
        WriteLine($"Auth decision => {status}");

        WriteLine();
    }

    // A comparer for (string,string) tuple that avoids allocations and supports custom StringComparison.
    // This is “senior-level polish” for route tables in hot code.
    private sealed class StringTupleComparer : IEqualityComparer<(string Method, string Path)>
    {
        public static readonly StringTupleComparer Ordinal = new(StringComparer.Ordinal);

        private readonly StringComparer _cmp;
        public StringTupleComparer(StringComparer cmp) => _cmp = cmp;

        public bool Equals((string Method, string Path) x, (string Method, string Path) y)
            => _cmp.Equals(x.Method, y.Method) && _cmp.Equals(x.Path, y.Path);

        public int GetHashCode((string Method, string Path) obj)
            => HashCode.Combine(_cmp.GetHashCode(obj.Method), _cmp.GetHashCode(obj.Path));
    }

    // ------------------------------------------------------------
    // 7) When *not* to use tuples (top-programmer judgment)
    // ------------------------------------------------------------
    //
    // Use tuples when:
    // ✅ you want to return 2-3 values from a private helper
    // ✅ you’re building quick data plumbing in local scope
    // ✅ the meaning is obvious from names at the call site
    //
    // Avoid tuples when:
    // ❌ you expose them in public APIs where versioning matters
    //     - Adding a field breaks signature, callers must recompile
    // ❌ you have >3-4 fields (use a record/struct)
    // ❌ you need invariants/validation (a type can enforce it)
    // ❌ you need semantic meaning across the codebase (Domain types win)
    //
    // “Elite dev” rule:
    // - Tuples are great for *local* correctness and speed.
    // - Domain models are great for *global* correctness and evolution.
    // ------------------------------------------------------------
    static void Tuples_WhenNotToUse()
    {
        WriteLine("== Tuples_WhenNotToUse ==");

        // Instead of returning a 6-field tuple for an API payload,
        // prefer a named type (record/struct) that can evolve.
        var result = ComputeInvoiceTotals(subtotal: 100m, taxRate: 0.16m, discount: 5m);

        WriteLine($"InvoiceTotals => Subtotal={result.Subtotal}, Tax={result.Tax}, Discount={result.Discount}, Total={result.Total}");

        WriteLine();
    }

    // A record struct: value semantics, named fields, easy to version with care.
    public readonly record struct InvoiceTotals(decimal Subtotal, decimal Tax, decimal Discount, decimal Total);

    static InvoiceTotals ComputeInvoiceTotals(decimal subtotal, decimal taxRate, decimal discount)
    {
        // This is “business logic”, not just a bag of values.
        // A named type communicates intent better than a 4-field tuple.
        var tax = subtotal * taxRate;
        var total = subtotal + tax - discount;
        return new(subtotal, tax, discount, total);
    }
}

// ================================================================
// Extra “scientist-level” notes for GitHub
// ================================================================
//
// 1) Layout & blittability
// - ValueTuple<T1,T2> is a struct with fields Item1, Item2 (sequential in practice,
//   but not guaranteed unless you enforce layout).
// - For interop, prefer explicit structs with [StructLayout] if layout is critical.
//
// 2) Copies & passing by ref
// - Passing a ValueTuple by value copies all fields.
// - For big structs/tuples, prefer:
//      void M(in (..big..) t)   // pass by readonly ref
//      void M(ref (..big..) t)  // pass by ref (mutable)
// - But don’t overdo it: the JIT is good at optimizing small tuples.
//
// 3) Boxing triggers
// - Assigning ValueTuple to object
// - Using non-generic APIs that take object
// - Storing ValueTuple in non-generic collections (ArrayList, etc.)
//
// 4) “Tuples are mutable” (surprise!)
// - ValueTuple fields are mutable by default (it's a struct with fields).
// - That can be a feature or a footgun. Prefer readonly usage patterns.
//
// 5) Public API versioning
// - Returning (int, int) is fine internally.
// - For libraries/SDKs, a named type is usually more maintainable.
//
// If you want, I can also generate:
// - A BenchmarkDotNet suite comparing:
//     * returning tuples vs out parameters
//     * Tuple<...> vs ValueTuple<...>
//     * big tuple copies vs record struct
// - A Dev.to blog version of this deep dive with diagrams.
// ================================================================
