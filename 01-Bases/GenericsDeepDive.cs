// ================================================================
// GenericsDeepDive.cs
// ================================================================
//
// Goal:
// - Keep your original example (GetArrayLength<T>, Box<T>)
// - Add “hard-to-find” internals: JIT behavior, code sharing vs specialization,
//   boxing, constraints, variance, CPU-level intuition, and performance patterns.
// - Provide extra examples you can push to GitHub.
//
// NOTE: This file is intentionally comment-heavy (GitHub-ready).
//
// ---------------------------------------------------------------
// 0) Mental model: what “Generics” really means in .NET
// ---------------------------------------------------------------
//
// ✅ C# generics are *reified* at runtime.
// - The runtime knows the actual T (it’s not erased like Java’s type erasure).
// - That matters for: typeof(T), reflection, constraints, runtime checks,
//   and (most importantly) performance optimizations.
//
// ✅ JIT + CLR generate machine code per “generic instantiation group”:
// - For reference types: often one shared machine-code body is used.
//   Example: List<string> and List<object> typically share the same JITed code
//   because references are pointer-sized and have uniform representation.
//
// - For value types (structs): the JIT usually generates a specialized version
//   per T because the layout differs.
//   Example: List<int> gets code specialized for int (no boxing),
//            List<Guid> gets code specialized for Guid (different size/layout).
//
// The key: value types have different physical representation -> specialization
// is often needed for correctness + speed.
//
// ---------------------------------------------------------------
// 1) CPU-level intuition: what changes at the processor level?
// ---------------------------------------------------------------
//
// When you write generic code like:
//
//    static T Add<T>(T a, T b) => ...;
//
// You’re asking: “Please generate a single algorithm that can operate over
// unknown types.”
//
// At the CPU level, there is NO such thing as “unknown type” execution.
// The CPU executes instructions over registers/memory with fixed sizes.
//
// So the runtime resolves the abstraction in one of these ways:
//
// A) Shared code path (common for reference-type instantiations):
//    - CPU sees pointers (addresses).
//    - Operations are generally loads/stores/calls through method tables.
//
// B) Specialized code path (common for value-type instantiations):
//    - CPU sees actual raw bits of the struct/int/etc.
//    - JIT can use optimal instructions (e.g., integer add, SIMD, etc.).
//
// This is why generics in .NET can be “zero-cost abstractions” (often).
//
// ---------------------------------------------------------------
// 2) Compiler vs JIT: who does what?
// ---------------------------------------------------------------
//
// C# compiler (Roslyn):
// - Produces IL (Intermediate Language) + metadata.
// - It does NOT produce final machine code.
// - It emits generic definitions with metadata tokens indicating generic
//   parameters (like !0 for T in IL).
//
// CLR + JIT (RyuJIT):
// - Takes IL and compiles to machine code (x64/ARM64).
// - “Closes” generic methods/types using runtime type handles.
// - Applies optimizations: inlining, devirtualization (sometimes),
//   bounds-check elimination (sometimes), loop hoisting, etc.
//
// “Hard-to-find” nuance:
// - If your generic method uses constraints, JIT can emit direct calls.
//   Example: where T : struct or where T : IFoo
//   The JIT can use “constrained callvirt” in IL and then optimize.
//
// ---------------------------------------------------------------
// 3) Boxing traps and how to avoid them
// ---------------------------------------------------------------
//
// Value types become objects only via boxing.
// Boxing allocates on the heap and copies bits -> slower, more GC pressure.
//
// Common boxing triggers:
// - Using non-generic interfaces (IEnumerable vs IEnumerable<T> sometimes)
// - Calling virtual methods via object
// - Using EqualityComparer<T>.Default incorrectly? (Usually OK)
// - Using string interpolation with unconstrained T if it routes through object
//
// Tip: prefer generic APIs (IEnumerable<T>, IComparer<T>, EqualityComparer<T>).
// Tip: add constraints where meaningful (where T : struct) to avoid some paths.
//
// ---------------------------------------------------------------
// 4) “Generic sharing” under the hood
// ---------------------------------------------------------------
//
// For reference types, JIT may create one shared code body.
// But it still must preserve type safety.
// It uses runtime “RGCTX” (runtime generic context) / dictionaries to resolve:
// - method pointers
// - type handles
// - static generic fields, etc.
//
// That dictionary is like: “for this closed generic instantiation, here are
// the runtime-resolved handles you need.”
//
// ---------------------------------------------------------------
// 5) Practical performance heuristics for world-class code
// ---------------------------------------------------------------
//
// ✔ Prefer generics over object to avoid boxing and runtime casts.
// ✔ Prefer constraints to let the JIT emit faster code paths.
// ✔ In hot paths, avoid interface dispatch; prefer static dispatch when possible.
// ✔ Use Span<T>/ReadOnlySpan<T> for slicing without allocations.
// ✔ Use where T : unmanaged when you need raw memory operations safely.
// ✔ Use RuntimeHelpers.IsReferenceOrContainsReferences<T>() to optimize clears.
//
// ---------------------------------------------------------------
// 6) Your original example + upgraded expert examples
// ---------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

partial class Program
{
    static void GenericsDeepDive()
    {
        Generics();
        Generics_Advanced_Perf();
        Generics_Constraints_And_Boxing();
        Generics_Span_And_Unmanaged();
        Generics_TypeIdentity_And_Reification();
    }

    // ------------------------------------------------------------
    // Original: simple generic method + generic class.
    // ------------------------------------------------------------
    static void Generics()
    {
        string[] names = { "Juan", "Luis", "Diana" };
        int[] numbers = { 1, 2, 3, 25 };

        Console.WriteLine($"Tamaño del arreglo númerico:  {GetArrayLength(numbers)}");
        Console.WriteLine($"Tamaño del arreglo nombres: {GetArrayLength(names)}");

        Box<int> numberBox = new Box<int> { Content = 50 };
        Box<string> stringBox = new Box<string> { Content = "Ahora soy texto" };

        numberBox.Show();
        stringBox.Show();
    }

    // Generic method: IL has a generic parameter token (!0).
    // JIT will specialize for value-types and share for ref-types (often).
    static int GetArrayLength<T>(T[] array) => array.Length;

    // ------------------------------------------------------------
    // Advanced: performance micro-patterns & avoiding allocations
    // ------------------------------------------------------------
    static void Generics_Advanced_Perf()
    {
        Console.WriteLine("\n== Generics_Advanced_Perf ==");

        // Using generics avoids boxing and avoids runtime casts.
        // We'll compare a generic sum vs an object-based sum.

        const int N = 5_000_00; // keep moderate for demo
        int[] data = new int[N];
        for (int i = 0; i < data.Length; i++) data[i] = i;

        var sw = Stopwatch.StartNew();
        long sum1 = Sum_GenericInt(data); // no boxing, tight loop
        sw.Stop();
        Console.WriteLine($"Sum_GenericInt: {sum1} in {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        long sum2 = Sum_ObjectBased(data); // boxing + virtual/object paths
        sw.Stop();
        Console.WriteLine($"Sum_ObjectBased: {sum2} in {sw.ElapsedMilliseconds} ms");

        // In real apps, the difference scales with volume + GC pressure.
    }

    static long Sum_GenericInt(int[] arr)
    {
        long s = 0;
        for (int i = 0; i < arr.Length; i++) s += arr[i];
        return s;
    }

    static long Sum_ObjectBased(int[] arr)
    {
        // ⚠️ Every int becomes object -> boxing allocation (huge GC cost).
        long s = 0;
        for (int i = 0; i < arr.Length; i++)
        {
            object boxed = arr[i]; // boxing allocation
            s += (int)boxed;       // unboxing
        }
        return s;
    }

    // ------------------------------------------------------------
    // Constraints: enabling “better codegen” and correctness
    // ------------------------------------------------------------
    static void Generics_Constraints_And_Boxing()
    {
        Console.WriteLine("\n== Generics_Constraints_And_Boxing ==");

        // Example: Avoid boxing by using generic comparer.
        var a = new Point2(10, 20);
        var b = new Point2(10, 20);

        // Value types: EqualityComparer<T>.Default typically avoids boxing
        // because it uses specialized comparers for many types.
        Console.WriteLine($"Point2 equals (generic): {GenericEquals(a, b)}");

        // Another constraint example: new() allows instantiation.
        var created = CreateDefault<Box<string>>();
        created.Content = "Created via generic new() constraint";
        created.Show();

        // Interface constraint: allows calling interface members on T.
        // JIT emits constrained calls, can optimize.
        Console.WriteLine($"Squared via constraint: {SquareConstrained(new IntLike(12))}");
    }

    static bool GenericEquals<T>(T x, T y)
        => EqualityComparer<T>.Default.Equals(x, y);

    static T CreateDefault<T>() where T : new()
        => new T();

    static int SquareConstrained<T>(T value) where T : IIntLike
        => value.IntValue * value.IntValue;

    // ------------------------------------------------------------
    // Span<T> + unmanaged constraint: low-level performance patterns
    // ------------------------------------------------------------
    static void Generics_Span_And_Unmanaged()
    {
        Console.WriteLine("\n== Generics_Span_And_Unmanaged ==");

        // Span<T> is stack-only ref struct: no heap allocation for slices.
        int[] arr = { 10, 20, 30, 40, 50 };
        Span<int> window = arr.AsSpan(1, 3); // [20,30,40] without allocating

        // Modify through span:
        window[0] = 999;
        Console.WriteLine($"arr after span edit: {string.Join(",", arr)}");

        // unmanaged constraint allows safe “raw” memory operations.
        // Example: compute byte size at compile-time / JIT-time.
        Console.WriteLine($"sizeof(Point2) = {SizeOf<Point2>()} bytes");
        Console.WriteLine($"sizeof(int)    = {SizeOf<int>()} bytes");

        // Fast clear optimization:
        // If T contains references, clearing must write nulls for GC correctness.
        // If T has no references, runtime can clear faster (memset).
        Console.WriteLine($"Point2 contains refs? {RuntimeHelpers.IsReferenceOrContainsReferences<Point2>()}");
        Console.WriteLine($"string contains refs? {RuntimeHelpers.IsReferenceOrContainsReferences<string>()}");
    }

    static int SizeOf<T>() where T : unmanaged
        => Marshal.SizeOf<T>();

    // ------------------------------------------------------------
    // Reification: you can observe T at runtime
    // ------------------------------------------------------------
    static void Generics_TypeIdentity_And_Reification()
    {
        Console.WriteLine("\n== Generics_TypeIdentity_And_Reification ==");

        PrintGenericIdentity(123);
        PrintGenericIdentity("hello");
        PrintGenericIdentity(new Point2(1, 2));

        // “Hard-to-find” nuance:
        // - typeof(T) is resolved using runtime handles.
        // - For generics, runtime uses type handles + generic dictionaries.
        // - This is why .NET can preserve full generic type info at runtime.
    }

    static void PrintGenericIdentity<T>(T value)
    {
        Console.WriteLine($"T = {typeof(T).FullName}, value = {value}");
    }
}

// ------------------------------------------------------------
// Generic container (your original Box<T>), upgraded with:
// - aggressive inlining hints
// - ToString usage patterns
// - “struct vs class” mental model notes
// ------------------------------------------------------------
class Box<T>
{
    public T? Content { get; set; }

    // JIT hint: small methods often inline automatically,
    // but you can express intent (not a guarantee).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Show()
    {
        // Note: interpolation calls Content?.ToString()
        // For value types, this can call constrained ToString without boxing
        // depending on JIT decisions and call site.
        Console.WriteLine($"Contenido: {Content}");
    }
}

// ------------------------------------------------------------
// A “blittable” struct: no references, predictable layout.
// This is good for perf and interop. (unmanaged)
// ------------------------------------------------------------
[StructLayout(LayoutKind.Sequential)]
public readonly struct Point2 : IEquatable<Point2>
{
    public readonly int X;
    public readonly int Y;

    public Point2(int x, int y) { X = x; Y = y; }

    public bool Equals(Point2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Point2 p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X},{Y})";
}

// ------------------------------------------------------------
// A tiny interface used to demonstrate constrained generic calls.
// The point: constraint enables calling members on T.
// ------------------------------------------------------------
public interface IIntLike
{
    int IntValue { get; }
}

public readonly struct IntLike : IIntLike
{
    public int IntValue { get; }
    public IntLike(int v) => IntValue = v;
}

// ------------------------------------------------------------
// Extra “scientist-level” notes (keep in file for GitHub):
// ------------------------------------------------------------
//
// IL-level clue (conceptual):
// - A generic method parameter is referenced in IL as !0, !1, etc.
// - Calls on generic T often use "constrained." prefix to avoid boxing
//   and still allow virtual/interface dispatch safely.
//
// Example idea (not exact IL here, but typical pattern):
//     constrained. !!T
//     callvirt instance string [System.Runtime]System.Object::ToString()
//
// Why it matters:
// - For value-type T, constrained call avoids boxing before calling ToString.
// - For reference-type T, it just behaves like a normal callvirt.
//
// ------------------------------------------------------------
// If you want next-level next step:
// - I can add a section showing a “role vs scope” validator middleware
//   using ClaimsPrincipal + policy-based authorization,
//   and compare it with a custom middleware approach.
// - Or I can add a BenchmarkDotNet benchmark suite in this same repo
//   so you can produce real perf charts for your GitHub README.
// ------------------------------------------------------------
