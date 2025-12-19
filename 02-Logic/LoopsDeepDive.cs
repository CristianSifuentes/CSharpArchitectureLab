// ================================================================
// LoopsDeepDive.cs
// ================================================================
//
// Goal:
// - Keep your original examples (while, do/while, for, foreach over array/list)
// - Add “hard-to-find” internals:
//   * CPU pipeline + branch prediction in loops
//   * JIT codegen, bounds-check elimination, range checks, loop hoisting
//   * foreach lowering: arrays vs List<T> vs IEnumerable<T>
//   * iterator blocks & state machines (yield) and why they allocate sometimes
//   * tiered compilation + PGO effects on hot loops
//   * cache locality + data layout (often more important than branch cost)
//   * Span<T>, ref foreach, and “zero-allocation iteration” patterns
// - Provide extra examples and “senior-level” heuristics for speed.
//
// This file is intentionally comment-heavy (GitHub-ready).
//
// ---------------------------------------------------------------
// 0) Mental model: loops are “repeat + decide + update”
// ---------------------------------------------------------------
//
// In C#:
//
//   while (cond) { body; }
//   do { body; } while (cond);
//   for (init; cond; step) { body; }
//   foreach (var x in seq) { body; }
//
// At the lowest level, nearly every loop becomes a tiny machine-code pattern:
//
//   L0:
//     evaluate condition (CMP / TEST / etc.)
//     branch back (Jcc / Bcc) if condition true
//
// That branch (the back-edge) is one of the most heavily optimized patterns
// in modern CPUs (predictors have special handling for loop back-edges).
//
// ---------------------------------------------------------------
// 1) CPU-level view: pipeline, branch prediction, and why data matters
// ---------------------------------------------------------------
//
// ✅ Branch prediction in loops
// - CPUs predict branches to keep the instruction pipeline full.
// - A loop back-edge is often highly predictable: “taken” many times,
//   then “not taken” once at the end.
// - That means the loop branch itself is usually cheap.
//
// ✅ What actually kills loop performance (most of the time)
// - Cache misses and memory bandwidth. If your loop touches memory with poor
//   locality (random access), you can pay 100+ cycles per miss.
// - Indirections: pointers to pointers (e.g., linked lists) defeat caching.
// - Bounds checks and virtual/interface dispatch (when not eliminated).
// - Allocations inside the loop (GC pressure).
//
// Heuristic that wins in the real world:
//   “Make your loop touch memory linearly, avoid allocations, and keep the
//    inner body simple enough for the JIT to optimize.”
//
// ---------------------------------------------------------------
// 2) Roslyn vs JIT: who does what with loops?
// ---------------------------------------------------------------
//
// Roslyn (C# compiler):
// - Emits IL with explicit branch instructions.
// - Lowers foreach into enumerator patterns in IL (or special-cases arrays).
//
// RyuJIT (.NET JIT):
// - Turns IL into CPU-specific machine code (x64/ARM64).
// - Decides optimizations such as:
//   * bounds-check elimination (BCE)
//   * loop-invariant code motion (LICM)
//   * range-check hoisting
//   * strength reduction (e.g., i * 4 -> LEA patterns)
//   * inlining (enables further loop optimizations)
//   * auto-vectorization (limited but improving)
// - With Tiered Compilation + PGO, hot loops can be re-jitted with better
//   decisions based on real runtime behavior.
//
// Hard-to-find nuance (practical):
// - Code you run once may be “Tier 0” quick-jitted and less optimized.
// - Hot code may later be “Tier 1” optimized and can get different assembly.
// - That’s why serious perf tests use warmup and multiple iterations.
//
// ---------------------------------------------------------------
// 3) foreach lowering: arrays vs List<T> vs IEnumerable<T>
// ---------------------------------------------------------------
//
// A) foreach over T[] (array)
// - Typically lowered to a simple for loop with index.
// - Very fast; JIT often eliminates bounds checks in simple patterns.
//
// B) foreach over List<T>
// - Uses List<T>.Enumerator (a struct) — usually no allocation.
// - Also fast; but if you upcast to IEnumerable<T>, you may allocate.
//
// C) foreach over IEnumerable<T> (interface)
// - Often uses interface calls (MoveNext/Current) and can allocate the
//   enumerator if it becomes a reference type or gets boxed.
// - This is a common “hidden perf cliff”.
//
// Pro tip:
// - Prefer iterating arrays/Span<T>/List<T> directly in hot paths.
// - Avoid IEnumerable<T> in hot loops unless you *need* composability.
//
// ---------------------------------------------------------------
// 4) Bounds-check elimination (BCE): the “secret sauce” for array loops
// ---------------------------------------------------------------
//
// Example:
//
//   for (int i = 0; i < arr.Length; i++) sum += arr[i];
//
// In many cases, the JIT can prove i is within bounds and remove the check.
// If you write the loop in a weird way (multiple indices, non-linear access),
// BCE may fail and you'll pay a bounds check per iteration.
//
// Pattern that tends to help BCE:
// - Use a local "len = arr.Length"
// - Use i from 0..len-1
// - Keep indexing simple
//
// ---------------------------------------------------------------
// 5) do/while and while: subtle differences
// ---------------------------------------------------------------
//
// while (cond) { body; }
// - checks condition first (can skip body)
//
// do { body; } while (cond);
// - executes body at least once
//
// Codegen differences are usually minor; choose based on correctness and clarity.
//
// ---------------------------------------------------------------
// 6) “Branchless loops” and vectorization: careful with myths
// ---------------------------------------------------------------
//
// - Replacing branches with arithmetic tricks can increase instruction count.
// - Branchless is a win when branches are unpredictable and body is small.
// - For numeric work, the biggest wins often come from:
//   * vectorizing with System.Numerics.Vector<T>
//   * using Span<T> and contiguous memory
//   * reducing memory bandwidth (fewer reads/writes)
//
// Measure before and after.
//
// ---------------------------------------------------------------
// 7) Upgraded version of your class + expert demos
// ---------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using static System.Console;

partial class Program
{
    public static void LoopsDeepDive()
    {
        Loops_Basics();
        Loops_ForeachLowering();
        Loops_BoundsCheckElimination_Demo();
        Loops_BranchPrediction_Demo();
        Loops_Span_ZeroAlloc();
        Loops_Vectorization_Taste();
        Loops_IteratorStateMachine_Warning();
    }

    // ------------------------------------------------------------
    // Your original (preserved), with output enabled.
    // ------------------------------------------------------------
    static void Loops_Basics()
    {
        WriteLine("== Loops_Basics ==");

        // while
        int counter = 1;
        while (counter <= 5)
        {
            WriteLine($"while iteration: {counter}");
            counter++;
        }

        // do/while
        int number = 0;
        do
        {
            WriteLine($"do/while number: {number}");
            number++;
        } while (number < 3);

        // for
        for (int i = 0; i <= 5; i++)
        {
            WriteLine($"for iteration: {i}");
        }

        // Customizing the for
        for (int i = 10; i >= 0; i -= 2)
        {
            WriteLine($"custom for: {i}");
        }

        // foreach - array
        string[] fruits = ["Manzana", "Pera", "Piña"];
        foreach (var fruit in fruits)
        {
            WriteLine($"fruit: {fruit}");
        }

        // foreach - list
        List<string> names = ["Pedro", "Luis", "Nancy"];
        foreach (var name in names)
        {
            WriteLine($"name: {name}");
        }

        WriteLine();
    }

    // ------------------------------------------------------------
    // Foreach lowering & common perf cliffs
    // ------------------------------------------------------------
    static void Loops_ForeachLowering()
    {
        WriteLine("== Loops_ForeachLowering ==");

        var list = new List<int>(capacity: 8) { 1, 2, 3, 4, 5, 6, 7, 8 };

        // Fast path: foreach over List<int> uses a struct enumerator (no alloc).
        long a = 0;
        foreach (var x in list) a += x;
        WriteLine($"foreach List<int> sum={a}");

        // Potential perf cliff: upcast to IEnumerable<int>.
        // Now enumeration can go through interface, and depending on shape,
        // you can lose some optimizations (and sometimes allocate).
        IEnumerable<int> seq = list;
        long b = 0;
        foreach (var x in seq) b += x;
        WriteLine($"foreach IEnumerable<int> sum={b}");

        // Arrays are typically lowered to a simple for loop.
        int[] arr = [1, 2, 3, 4, 5, 6, 7, 8];
        long c = 0;
        foreach (var x in arr) c += x;
        WriteLine($"foreach int[] sum={c}");

        WriteLine();
    }

    // ------------------------------------------------------------
    // Bounds-check elimination demo (Stopwatch demo; for real, use BenchmarkDotNet)
    // ------------------------------------------------------------
    static void Loops_BoundsCheckElimination_Demo()
    {
        WriteLine("== Loops_BoundsCheckElimination_Demo ==");

        const int N = 2_000_000;
        var data = new int[N];
        for (int i = 0; i < data.Length; i++) data[i] = i;

        // BCE-friendly loop shape
        var sw = Stopwatch.StartNew();
        long s1 = Sum_BCE_Friendly(data);
        sw.Stop();
        WriteLine($"Sum_BCE_Friendly: {s1} in {sw.ElapsedMilliseconds} ms");

        // A more complex indexing pattern that can make BCE harder.
        sw.Restart();
        long s2 = Sum_BCE_Harder(data);
        sw.Stop();
        WriteLine($"Sum_BCE_Harder:   {s2} in {sw.ElapsedMilliseconds} ms");

        WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static long Sum_BCE_Friendly(int[] arr)
    {
        // Classic shape: i from 0..len-1 and arr[i].
        int len = arr.Length;
        long sum = 0;
        for (int i = 0; i < len; i++)
            sum += arr[i];
        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static long Sum_BCE_Harder(int[] arr)
    {
        // Still valid, but “two indices” patterns can sometimes prevent BCE
        // depending on JIT's analysis and transformations.
        // (This is not guaranteed slower; it's a “shape showcase”.)
        int len = arr.Length;
        long sum = 0;
        for (int i = 0, j = len - 1; i < len; i++, j--)
        {
            // Two independent accesses.
            sum += arr[i];
            sum += arr[j];
        }
        return sum;
    }

    // ------------------------------------------------------------
    // Branch prediction demo: predictable vs unpredictable predicates
    // ------------------------------------------------------------
    static void Loops_BranchPrediction_Demo()
    {
        WriteLine("== Loops_BranchPrediction_Demo ==");

        const int N = 2_000_000;
        var predictable = new int[N];
        var unpredictable = new int[N];

        for (int i = 0; i < N; i++)
            predictable[i] = i % 100 == 0 ? -1 : 1; // 99% positive

        var rng = new Random(123);
        for (int i = 0; i < N; i++)
            unpredictable[i] = (rng.Next() & 1) == 0 ? -1 : 1; // ~50/50

        var sw = Stopwatch.StartNew();
        long a = Sum_PositiveOnly(predictable);
        sw.Stop();
        WriteLine($"Predictable: sum={a}, ms={sw.ElapsedMilliseconds}");

        sw.Restart();
        long b = Sum_PositiveOnly(unpredictable);
        sw.Stop();
        WriteLine($"Unpredictable: sum={b}, ms={sw.ElapsedMilliseconds}");

        WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static long Sum_PositiveOnly(int[] data)
    {
        long sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            // Branch predictability matters in tight loops.
            if (data[i] > 0) sum += data[i];
        }
        return sum;
    }

    // ------------------------------------------------------------
    // Span<T> + ref foreach: “zero allocation” iteration patterns
    // ------------------------------------------------------------
    static void Loops_Span_ZeroAlloc()
    {
        WriteLine("== Loops_Span_ZeroAlloc ==");

        int[] data = [10, 20, 30, 40, 50];

        // Span<T> slice without allocating:
        Span<int> slice = data.AsSpan(1, 3); // [20,30,40]

        // ref foreach: iterate by reference to mutate in-place without copies.
        foreach (ref var x in slice)
        {
            x += 1;
        }

        WriteLine($"after ref foreach over Span: {string.Join(",", data)}");

        // ReadOnlySpan: prevents mutation (useful in APIs).
        ReadOnlySpan<int> ro = data.AsSpan();
        int sum = 0;
        for (int i = 0; i < ro.Length; i++) sum += ro[i];
        WriteLine($"ReadOnlySpan sum={sum}");

        WriteLine();
    }

    // ------------------------------------------------------------
    // A taste of vectorization: Vector<T> over a loop (portable SIMD-ish)
    // ------------------------------------------------------------
    static void Loops_Vectorization_Taste()
    {
        WriteLine("== Loops_Vectorization_Taste ==");

        // Vector<T> uses SIMD under the hood when supported.
        // It’s not always the fastest possible approach, but it demonstrates
        // “data-parallel loops” and how layout matters.
        const int N = 1_000_000;
        var a = new float[N];
        var b = new float[N];
        for (int i = 0; i < N; i++) { a[i] = i; b[i] = 2; }

        var sw = Stopwatch.StartNew();
        float s1 = Dot_Scalar(a, b);
        sw.Stop();
        WriteLine($"Dot_Scalar: {s1} in {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        float s2 = Dot_Vectorized(a, b);
        sw.Stop();
        WriteLine($"Dot_Vectorized: {s2} in {sw.ElapsedMilliseconds} ms");

        WriteLine();
    }

    static float Dot_Scalar(float[] x, float[] y)
    {
        float sum = 0;
        for (int i = 0; i < x.Length; i++)
            sum += x[i] * y[i];
        return sum;
    }

    static float Dot_Vectorized(float[] x, float[] y)
    {
        int n = x.Length;
        int width = Vector<float>.Count;

        var acc = Vector<float>.Zero;
        int i = 0;

        // Vectorized loop
        for (; i <= n - width; i += width)
        {
            var vx = new Vector<float>(x, i);
            var vy = new Vector<float>(y, i);
            acc += vx * vy;
        }

        // Reduce vector accumulator to scalar
        float sum = 0;
        for (int lane = 0; lane < width; lane++)
            sum += acc[lane];

        // Remainder loop
        for (; i < n; i++)
            sum += x[i] * y[i];

        return sum;
    }

    // ------------------------------------------------------------
    // Iterator blocks (yield) warning: hidden state machine
    // ------------------------------------------------------------
    static void Loops_IteratorStateMachine_Warning()
    {
        WriteLine("== Loops_IteratorStateMachine_Warning ==");

        // "yield return" builds a compiler-generated state machine class/struct.
        // That can allocate (often does) and introduces extra indirections.
        // Totally fine for clarity, but avoid in ultra-hot paths.

        int count = 0;
        foreach (var x in EvensUpTo(10))
        {
            Write($"{x} ");
            count++;
        }
        WriteLine();
        WriteLine($"yield produced {count} values");
        WriteLine();
    }

    static IEnumerable<int> EvensUpTo(int max)
    {
        for (int i = 0; i <= max; i++)
            if ((i & 1) == 0)
                yield return i;
    }
}

// ---------------------------------------------------------------
// Extra “scientist-level” notes for GitHub
// ---------------------------------------------------------------
//
// 1) Loop-invariant code motion (LICM)
// - The JIT tries to hoist computations that don't change out of the loop.
//   Example: calling arr.Length each iteration can sometimes be hoisted.
//   Writing `int len = arr.Length;` makes the intent obvious and helps.
//
// 2) Range check hoisting and "guarded devirtualization"
// - The JIT may emit a check once (e.g., bounds) and then run a fast loop.
// - It can also speculate on types for interface calls (PGO helps).
//
// 3) `foreach` + mutation hazards
// - `foreach` over List<T> throws if the list is modified during enumeration.
// - Arrays don't have this versioning check.
//
// 4) Cache locality beats clever branches
// - A tight loop over contiguous arrays often wins even with more instructions,
//   because it streams through cache lines efficiently.
// - Random access patterns are performance poison.
//
// 5) How to go “top 1%” on loops in production
// - Benchmark: BenchmarkDotNet (with warmup, multiple runs, outlier handling)
// - Profile: dotnet-trace, PerfView, VTune (if you can)
// - Inspect JIT: BenchmarkDotNet DisassemblyDiagnoser / jit-dasm tools
// - Optimize based on data access, not just syntax.
//
// ================================================================
