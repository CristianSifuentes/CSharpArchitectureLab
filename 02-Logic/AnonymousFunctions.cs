// File: AnonymousFunctions.DeepDive.cs
// Target: .NET 8+
// ------------------------------------------------------------
// Anonymous Functions in C# (delegates + lambdas) — Deep, compiler/JIT, and CPU-level notes
// ------------------------------------------------------------
//
// This file is intentionally comment-heavy for a GitHub repo.
// It mixes: language semantics, IL/JIT behavior, allocation patterns, and micro-performance traps.
//
// ------------------------------------------------------------
// 0) Quick glossary
// ------------------------------------------------------------
// - Anonymous function: either an "anonymous method" (delegate (...) { ... }) or a "lambda" (x => x*x).
// - Delegate: an object that holds (method pointer + optional target object). In .NET, delegate invocation
//   is (conceptually) an indirect call through Invoke(), potentially calling an "open" or "closed" target.
// - Closure: when a lambda/anonymous method captures variables from an outer scope, the compiler generates
//   a "display class" (a heap object) to hold those captured variables, and the lambda becomes an instance method on it.
// - Deferred execution: LINQ operators like Where() typically return an iterator that runs when enumerated.
//
// ------------------------------------------------------------
// 1) What the C# compiler actually emits (high level)
// ------------------------------------------------------------
// A) Non-capturing lambdas (e.g., x => x*x) are usually compiled to a private static method, and then a delegate
//    pointing to that method. The delegate instance may be cached by the runtime (or by your code) to avoid repeats.
// B) Capturing lambdas (e.g., x => x + offset) require a closure object:
//    - Compiler creates a hidden "display class" like <>c__DisplayClass0_0 { public int offset; ... }
//    - The lambda becomes an instance method on that class.
//    - A delegate is created with "target = closureObject, method = instance method".
//    => This almost always allocates at least one object (the closure). Often more (iterator objects, etc.).
//
// ------------------------------------------------------------
// 2) IL/JIT + CPU-ish behavior (useful mental model)
// ------------------------------------------------------------
// - Delegate invocation is an indirect call. Modern JITs can sometimes inline through delegate calls in limited cases,
//   but often the call remains indirect (harder for branch prediction and inlining).
// - Indirect calls:
//   - Can reduce the CPU's ability to predict target (BTB / indirect branch predictor).
//   - Can inhibit inlining => extra call overhead, more register pressure, worse instruction cache usage.
// - Captured variables live in a heap object (closure). Every access is a field load:
//     load "this" (closure), then load field offset.
//   This can add:
//   - Extra pointer chasing (cache miss risk).
//   - Extra loads that compete for CPU ports and registers.
// - Tiered compilation + PGO (Profile-Guided Optimization) in .NET:
//   - Methods start in Tier0 (fast JIT) and can re-JIT to Tier1 after being "hot".
//   - Tier1 may inline more and optimize better, but delegate targets and closures still limit some optimizations.
//
// ------------------------------------------------------------
// 3) Performance rules of thumb (battle-tested)
// ------------------------------------------------------------
// ✅ Prefer static lambdas when you don't need captures:
//    numbers.Where(static n => (n & 1) == 0)
//
// ✅ Avoid captures in hot paths.
//    If you must pass state, consider:
//      - passing it explicitly, or
//      - precomputing values, or
//      - using a custom loop instead of LINQ.
//
// ✅ Avoid LINQ Where/Select in ultra-hot loops (allocations + iterator overhead).
//    Use for/foreach and branchless-ish checks when appropriate.
//
// ✅ If you want maximum throughput, avoid per-iteration delegate allocations.
//    Cache delegates, or use static methods.
//
// ✅ Remember deferred execution: your predicate runs during enumeration, not at Where() call time.
//
// ------------------------------------------------------------
// 4) Advanced notes: "static" lambdas (C# 9+) and why they matter
// ------------------------------------------------------------
// "static" on a lambda means: it is forbidden to capture. This is a *compile-time* guarantee,
// and it helps you prevent accidental closure allocations.
//
// Example:
//    var q = numbers.Where(static n => n % 2 == 0);
// If you try to use outer variables inside, it will fail to compile.
//
// ------------------------------------------------------------
// 5) Another advanced axis: delegates vs Expression trees
// ------------------------------------------------------------
// - Func<...> is executable code (JIT compiled).
// - Expression<Func<...>> is a data structure (AST) used by ORMs/providers (EF Core, etc.)
//   to translate your lambda to SQL or other query languages.
// - Expression trees are slower to build and represent, but they are meant for translation, not raw speed.
//
// ------------------------------------------------------------
// 6) What your sample does (important subtlety)
// ------------------------------------------------------------
// numbers.Where(n => n % 2 == 0) returns an IEnumerable<int> that is lazy.
// The filtering happens inside the foreach.
// There is usually an iterator allocation for Where (unless the source is optimized special-cased).
//
// If you need materialization:
//    var evens = numbers.Where(...).ToArray(); // runs immediately, allocates array
//
// ------------------------------------------------------------
// 7) "Processor-level" intuition for common patterns
// ------------------------------------------------------------
// Hot loop with delegate predicate:
//    foreach (var x in xs) if (pred(x)) ...
// translates (conceptually) to:
//    - load delegate object
//    - load target + method pointer (or call stub)
//    - indirect call
//    - branch on result
//
// Hot loop with inline check:
//    foreach (var x in xs) if ((x & 1) == 0) ...
// translates to:
//    - a couple of arithmetic ops
//    - one conditional branch
// The second is dramatically easier to inline/optimize and branch-predict.
//
// ------------------------------------------------------------
// 8) Practical guidance for "top programmer" level control
// ------------------------------------------------------------
// - Know when abstraction leaks: LINQ and delegates are great, but they can allocate and block inlining.
// - Use "static" lambdas to lock out captures.
// - Understand closure allocations (GC pressure) and iterator allocations.
// - For serious performance, measure with BenchmarkDotNet (recommended). This file includes a simple Stopwatch
//   micro-benchmark, but note: micro-benchmarks are easy to get wrong due to JIT warmup, tiering, CPU frequency scaling.
//
// ------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static System.Console;

partial class Program
{
    // ------------------------------------------------------------
    // Your original delegates (kept, but explained)
    // ------------------------------------------------------------

    // Anonymous method syntax: "delegate(int number) { ... }"
    // - Non-capturing => typically becomes a private static method in IL, and this field stores a delegate instance.
    // - Stored in a static readonly field => avoids repeated allocations (good).
    static readonly Func<int, int> square = delegate (int number)
    {
        // JIT can inline this multiplication easily when called directly.
        // Through delegate Invoke() it may or may not inline depending on tiering and heuristics.
        return number * number;
    };

    // Lambda syntax: "x => x * x"
    // - Non-capturing, also typically compiled to a static method.
    // - Stored/cached in static readonly field => avoids per-call delegate allocations.
    static readonly Func<int, int> lambdaSquare = x => x * x;

    // ------------------------------------------------------------
    // Entry demo
    // ------------------------------------------------------------
    static void AnonymousFunctions()
    {
        WriteLine("=== Anonymous Functions Deep Dive Demo ===");
        WriteLine(square(5));
        WriteLine(lambdaSquare(10));

        List<int> numbers = [1, 2, 3, 4, 5];

        // Deferred execution: no filtering happens yet.
        var evenNumbers = numbers.Where(n => n % 2 == 0);

        WriteLine("Deferred execution: iterating now...");
        foreach (var even in evenNumbers)
        {
            WriteLine(even);
        }

        WriteLine();
        Examples_NonCapturingVsCapturing();
        WriteLine();
        Examples_StaticLambda_NoCaptureGuarantee();
        WriteLine();
        Examples_LINQ_vs_Loop_PerfShape();
        WriteLine();
        MicroBenchmark_Stopwatch();
    }

    // ------------------------------------------------------------
    // Example 1: Non-capturing vs capturing lambdas
    // ------------------------------------------------------------
    static void Examples_NonCapturingVsCapturing()
    {
        WriteLine("=== 1) Non-capturing vs Capturing ===");

        // Non-capturing predicate: usually no closure allocation.
        Func<int, bool> isEven = n => (n & 1) == 0;

        // Capturing predicate: captures 'threshold' => closure allocation.
        int threshold = 3;
        Func<int, bool> greaterThanThreshold = n => n > threshold;

        // Subtle bug pattern: closure captures the VARIABLE, not the VALUE.
        // If 'threshold' changes later, the lambda sees the updated value (because it's a field on the closure object).
        threshold = 10;

        var numbers = new[] { 1, 2, 3, 4, 5, 11, 12 };

        WriteLine("Even numbers:");
        foreach (var n in numbers.Where(isEven))
            Write($"{n} ");
        WriteLine();

        WriteLine("Greater-than-threshold (threshold was changed to 10 after lambda creation):");
        foreach (var n in numbers.Where(greaterThanThreshold))
            Write($"{n} ");
        WriteLine();

        // If you wanted to capture the VALUE at the time, do:
        int snapshot = threshold; // snapshot = 10 now
        Func<int, bool> greaterThanSnapshot = n => n > snapshot; // captures snapshot (still a capture, but stable)
        WriteLine("Greater-than-snapshot (stable):");
        foreach (var n in numbers.Where(greaterThanSnapshot))
            Write($"{n} ");
        WriteLine();
    }

    // ------------------------------------------------------------
    // Example 2: static lambda prevents accidental capture
    // ------------------------------------------------------------
    static void Examples_StaticLambda_NoCaptureGuarantee()
    {
        WriteLine("=== 2) static lambda: capture-proof ===");

        var numbers = Enumerable.Range(1, 10).ToArray();

        // This cannot capture outer variables. If you try, it fails at compile time.
        var evens = numbers.Where(static n => (n & 1) == 0);

        WriteLine("Evens (static predicate):");
        foreach (var n in evens)
            Write($"{n} ");
        WriteLine();

        // If you need state, pass it explicitly (design) rather than capturing (hidden allocation).
        int threshold = 6;

        // Pattern: use a local function that takes state explicitly.
        static bool GreaterThan(int n, int t) => n > t;

        // Here we avoid capture by using a method group via a small adapter:
        // Note: this adapter lambda DOES capture 'threshold' if written as n => GreaterThan(n, threshold).
        // Alternative: use a loop. Or build a predicate factory that returns cached delegates for common thresholds.
        // For clarity, we'll show the capture, but in hot code you'd avoid it.
        var gt = numbers.Where(n => GreaterThan(n, threshold));
        WriteLine("Greater-than (uses adapter, captures threshold):");
        foreach (var n in gt)
            Write($"{n} ");
        WriteLine();
    }

    // ------------------------------------------------------------
    // Example 3: LINQ vs loop (shape of costs)
    // ------------------------------------------------------------
    static void Examples_LINQ_vs_Loop_PerfShape()
    {
        WriteLine("=== 3) LINQ vs loop: cost model ===");

        // LINQ version:
        // - Where creates an iterator object (allocation) in many cases.
        // - Predicate is invoked via delegate (possible indirect call).
        // - Great readability, sometimes perfectly fine.
        var xs = Enumerable.Range(1, 20).ToArray();
        var linqEvens = xs.Where(static n => (n & 1) == 0);

        WriteLine("LINQ evens:");
        foreach (var n in linqEvens)
            Write($"{n} ");
        WriteLine();

        // Loop version:
        // - No iterator allocation.
        // - Condition can be inlined trivially.
        // - Often much faster for hot paths.
        WriteLine("Loop evens:");
        for (int i = 0; i < xs.Length; i++)
        {
            int n = xs[i];
            if ((n & 1) == 0)
                Write($"{n} ");
        }
        WriteLine();

        // Note: modern .NET is good, but physics is physics: fewer allocations + fewer indirect calls usually wins.
    }

    // ------------------------------------------------------------
    // Example 4: A tiny micro-benchmark (Stopwatch) — with warnings
    // ------------------------------------------------------------
    static void MicroBenchmark_Stopwatch()
    {
        WriteLine("=== 4) MicroBenchmark (Stopwatch) — indicative only ===");
        WriteLine("NOTE: For real results use BenchmarkDotNet (warmup, iteration control, tiering/PGO awareness).");

        const int N = 5_000_000;
        var xs = new int[1024];
        for (int i = 0; i < xs.Length; i++) xs[i] = i;

        // Warmup: reduces one-time JIT and tiering effects.
        int warm = 0;
        for (int i = 0; i < 100_000; i++) warm += lambdaSquare(i);

        // Case A: delegate call (cached)
        var swA = Stopwatch.StartNew();
        int sumA = 0;
        for (int i = 0; i < N; i++)
        {
            // Delegate Invoke: may be indirect call. Still often fast, but harder to inline.
            sumA += lambdaSquare(xs[i & 1023]);
        }
        swA.Stop();

        // Case B: direct inline computation (no delegate)
        var swB = Stopwatch.StartNew();
        int sumB = 0;
        for (int i = 0; i < N; i++)
        {
            int x = xs[i & 1023];
            sumB += x * x;
        }
        swB.Stop();

        WriteLine($"Delegate lambdaSquare sum={sumA} time={swA.ElapsedMilliseconds} ms");
        WriteLine($"Inline multiply     sum={sumB} time={swB.ElapsedMilliseconds} ms");

        // You should usually observe inline faster, but the point isn't the numbers:
        // It's the mechanism:
        // - Delegate call may block inlining and add indirection.
        // - Inline code gives the JIT full freedom (inlining, constant folding, vectorization opportunities).
    }

    // ------------------------------------------------------------
    // Extra: "impressive" expert patterns (read carefully)
    // ------------------------------------------------------------

    // 1) Predicate factory: creates a capturing lambda (closure allocation).
    //    Useful sometimes, but don't do this per-request in hot services without caching strategy.
    static Func<int, bool> MakeGreaterThanPredicate(int threshold)
        => n => n > threshold; // captures threshold (closure)

    // 2) Prefer explicit state in tight loops (no closures):
    static int SumGreaterThan_NoCapture(int[] xs, int threshold)
    {
        int sum = 0;
        for (int i = 0; i < xs.Length; i++)
        {
            int x = xs[i];
            if (x > threshold) sum += x;
        }
        return sum;
    }

    // 3) Beware of multi-cast delegates:
    //    Delegate can represent an invocation list. Invoke() then loops over targets.
    //    Usually not relevant for Func, but it's core to event handlers and can surprise perf.
}
