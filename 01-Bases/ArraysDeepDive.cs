// File: ArraysDeepDive.cs
// Author: Cristian Sifuentes + ChatGPT
// Goal: Explain C# ARRAYS like a systems / compiler / performance engineer.
//
// HIGH-LEVEL MENTAL MODEL
// -----------------------
// When you write:
//
//     int[] numbers = new int[5];
//     numbers[0] = 1;
//     numbers[1] = 3;
//
// The following layers are involved:
//
// 1. C# COMPILER (ROSLYN)
//    - Sees `int[]` as "single-dimensional, zero-based array of System.Int32".
//    - Emits IL that calls `newobj int32[]::.ctor(int32)`.
//    - Indexing like `numbers[i]` emits IL `ldelem.i4` / `stelem.i4` plus
//      a range check (`ldlen` + comparison).
//
// 2. CLR TYPE SYSTEM
//    - All arrays are *reference types*, even `int[]`.
//    - The runtime allocates a single managed object whose layout is
//      approximately:
//
//          [Object header][Method table ptr][Int32 Length][T elements...]
//
//      For `int[]` each element is a 32-bit integer stored contiguously.
//    - `Length` is stored once per array, not recomputed.
//
// 3. JIT (JUST-IN-TIME COMPILER)
//    - Translates IL into machine code.
//    - May *eliminate bounds checks* in tight loops if it can prove the index
//      is in range.
//    - Emits CPU instructions like `mov`, `add`, `cmp`, `jl`, etc.
//    - Uses pointer arithmetic to address element `i`:
//
//          address = baseAddress + (i * sizeof(T))
//
// 4. CPU / MEMORY HIERARCHY
//    - Elements are laid out contiguously, which is friendly for:
//        * CPU caches (spatial locality).
//        * SIMD vectorization.
//        * prefetching.
//    - Accessing arr[i] is O(1) and extremely fast IF you are cache-friendly.
//
// 5. GC (GARBAGE COLLECTOR)
//    - Arrays live on the managed heap.
//    - Large arrays (>= 85 KB by default) go to the Large Object Heap (LOH).
//      LOH is collected but not compacted by default, which matters for
//      fragmentation and long-running processes.
//
// This file is written so you can reason about arrays like a **top 1% .NET
// engineer**: connecting C# syntax → IL → machine code → caches and GC.

using System;
using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

partial class Program
{
    // ---------------------------------------------------------------------
    // PUBLIC ENTRY FOR THIS MODULE
    // ---------------------------------------------------------------------
    // Call ArraysDeepDive() from your main Program to run all demos.
    static void ArraysDeepDive()
    {
        Console.WriteLine("=== Arrays Deep Dive ===");

        BasicArraySample();                // Start from your original example
        LayoutZeroInitAndRefSemantics();   // How arrays look in memory
        IndicesAndRanges();                // ^ and .. indexing
        ForeachVsForAndBoundsChecks();     // Perf considerations
        MultiDimensionalVsJagged();        // 2D representations
        SpanInteropAndUnsafeView();        // Span<T>, MemoryMarshal
        ArrayPoolAndLargeObjectHeap();     // Pooling + LOH behavior
        VectorizationShape();              // SIMD on arrays
        MicroBenchmarkShapeForArrays();    // How to measure perf
    }

    // ---------------------------------------------------------------------
    // 0. BASIC SAMPLE – your original example, upgraded with comments
    // ---------------------------------------------------------------------
    static void BasicArraySample()
    {
        Console.WriteLine();
        Console.WriteLine("=== 0. Basic Array Sample ===");

        // This allocates a managed object on the heap:
        //   - Length = 5
        //   - Elements are zero-initialized: [0, 0, 0, 0, 0]
        int[] numbers = new int[5];

        numbers[0] = 1;
        numbers[1] = 3;

        // C# 12 collection expression syntax:
        // The compiler translates this to `new int[] { 5, 10, 15, 20, 25, 30 }`
        int[] numbersArray = [5, 10, 15, 20, 25, 30];

        // Indexing:
        Console.WriteLine($"First element      = {numbersArray[0]}");
        Console.WriteLine($"Third element      = {numbersArray[2]}");

        // Length property:
        Console.WriteLine($"Number of elements = {numbersArray.Length}");

        // From the end of the array using C# index-from-end operator ^
        Console.WriteLine($"Last element       = {numbersArray[^1]}");
        Console.WriteLine($"Second from last   = {numbersArray[^2]}");

        // Ranges to get subarrays (this actually creates NEW arrays)
        int[] firstThree = numbersArray[..3];  // indices [0, 1, 2]
        int[] fromIndexTwo = numbersArray[2..]; // indices [2..end)

        Console.WriteLine("From index 2 (2..):");
        foreach (var number in fromIndexTwo)
        {
            Console.WriteLine(number);
        }

        // NOTE: The range syntax is *very* convenient, but each slice is a
        // new array allocation. For perf-critical code where you just want a
        // "view" over existing data, Span<T> is usually better.
    }

    // ---------------------------------------------------------------------
    // 1. LAYOUT, ZERO-INIT & REFERENCE SEMANTICS
    // ---------------------------------------------------------------------
    static void LayoutZeroInitAndRefSemantics()
    {
        Console.WriteLine();
        Console.WriteLine("=== 1. Layout, Zero-Init & Reference Semantics ===");

        int[] a = new int[4]; // [0, 0, 0, 0]
        int[] b = a;          // b references the same array as a

        a[0] = 123;

        Console.WriteLine($"a[0] = {a[0]}");
        Console.WriteLine($"b[0] = {b[0]}");
        Console.WriteLine($"ReferenceEquals(a, b) = {ReferenceEquals(a, b)}");

        // FACTS:
        //  - Arrays are always reference types:
        //        int[] a = new int[4];
        //    The variable 'a' holds a *reference* (a managed pointer).
        //  - Zero-initialization:
        //    When the CLR allocates the array object, it zeroes the memory region
        //    for its elements:
        //        [0, 0, 0, 0] for int[]
        //        [null, null, ...] for reference-type arrays
        //
        // Under the hood (simplified):
        //
        //    // allocate
        //    obj = GCHeap::Alloc(size);
        //    // zero memory
        //    memset(obj + headerSize, 0, length * sizeof(T));
        //
        //  - Zero-init is guaranteed by the CLR type system and is required for
        //    type safety. There is no "uninitialized" element case.
    }

    // ---------------------------------------------------------------------
    // 2. INDICES & RANGES – ^ and .., and what they really mean
    // ---------------------------------------------------------------------
    static void IndicesAndRanges()
    {
        Console.WriteLine();
        Console.WriteLine("=== 2. Indices & Ranges ===");

        int[] data = [10, 20, 30, 40, 50];

        // Index-from-end:
        //   data[^1] → data[data.Length - 1]
        //   data[^k] → data[data.Length - k]
        int last = data[^1];
        int secondLast = data[^2];

        Console.WriteLine($"Last        : {last}");
        Console.WriteLine($"Second last : {secondLast}");

        // Range:
        //   data[1..4] ≈ slice from index 1 (inclusive) to 4 (exclusive)
        int[] mid = data[1..4]; // [20, 30, 40]

        Console.WriteLine("Range 1..4:");
        foreach (var n in mid)
        {
            Console.WriteLine(n);
        }

        // COMPILER VIEW:
        //
        //   - Index and Range are *value types*:
        //        System.Index, System.Range
        //   - data[^1] is lowered roughly like:
        //        data[data.Length - 1]
        //
        //   - data[1..4] is lowered to:
        //        var range = new Range(1, 4);
        //        int length = range.GetOffsetAndLength(data.Length, out int start);
        //        int[] slice = new int[length];
        //        Array.Copy(data, start, slice, 0, length);
        //
        // This means that Ranges create *new arrays* and copy data.
        // They are beautiful for readability but not free in terms of GC / CPU.
    }

    // ---------------------------------------------------------------------
    // 3. FOREACH vs FOR & BOUNDS CHECKS – perf-critical thinking
    // ---------------------------------------------------------------------
    static void ForeachVsForAndBoundsChecks()
    {
        Console.WriteLine();
        Console.WriteLine("=== 3. foreach vs for & Bounds Checks ===");

        int[] data = new int[10];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = i;
        }

        int sumFor = 0;
        int sumForeach = 0;

        // FOR LOOP:
        //   JIT can often remove the bounds check inside the loop because:
        //     - 'i' starts at 0
        //     - loop condition ensures i < data.Length
        //   This turns element access into straight pointer arithmetic.
        for (int i = 0; i < data.Length; i++)
        {
            sumFor += data[i];
        }

        // FOREACH LOOP:
        //   For arrays, foreach is *special-cased* by the JIT:
        //     - It behaves close to a for loop in performance.
        //     - It uses pointer-like iteration instead of IEnumerator<T>.
        foreach (var v in data)
        {
            sumForeach += v;
        }

        Console.WriteLine($"Sum (for)      = {sumFor}");
        Console.WriteLine($"Sum (foreach)  = {sumForeach}");

        // BOUNDS CHECKS:
        //
        //   - Each data[i] access in IL is normally guarded by a range check:
        //
        //       IL:
        //          ldloc data
        //          ldloc i
        //          ldelem.i4
        //
        //     The JIT injects a comparison against Length, throwing
        //     IndexOutOfRangeException if out of range.
        //
        //   - For *simple loops*, the JIT recognizes the pattern and hoists /
        //     eliminates redundant checks, making the inner loop tight.
        //
        // PRACTICAL RULE:
        //   - Write clear loops. Let the JIT optimize.
        //   - Do not micro-opt based on fear of bounds checks until you measure.
    }

    // ---------------------------------------------------------------------
    // 4. MULTIDIMENSIONAL vs JAGGED ARRAYS – layout & performance
    // ---------------------------------------------------------------------
    static void MultiDimensionalVsJagged()
    {
        Console.WriteLine();
        Console.WriteLine("=== 4. Multidimensional vs Jagged Arrays ===");

        // MULTIDIMENSIONAL (RECTANGULAR) ARRAY
        int[,] grid = new int[3, 3]; // 3x3 matrix

        // JAGGED ARRAY: array of int[]; rows can have different lengths
        int[][] jagged =
        [
            new[] { 1, 2, 3 },
            new[] { 4, 5 },
            new[] { 6, 7, 8, 9 }
        ];

        // Fill grid with row*10 + col
        for (int row = 0; row < grid.GetLength(0); row++)
        {
            for (int col = 0; col < grid.GetLength(1); col++)
            {
                grid[row, col] = row * 10 + col;
            }
        }

        Console.WriteLine("Rectangular grid:");
        for (int row = 0; row < grid.GetLength(0); row++)
        {
            for (int col = 0; col < grid.GetLength(1); col++)
            {
                Console.Write(grid[row, col].ToString().PadLeft(3));
            }
            Console.WriteLine();
        }

        Console.WriteLine("Jagged array:");
        for (int row = 0; row < jagged.Length; row++)
        {
            for (int col = 0; col < jagged[row].Length; col++)
            {
                Console.Write(jagged[row][col].ToString().PadLeft(3));
            }
            Console.WriteLine();
        }

        // PERFORMANCE INSIGHT:
        //
        //   - int[,] (multidimensional) is a *single* object:
        //       elements are contiguous, but indexing involves more math.
        //       IL uses opcodes like `ldelem` with 2D index checks.
        //
        //   - int[][] (jagged) is:
        //       * one outer array of references
        //       * N inner arrays, each contiguous
        //
        //   - For many scenarios, jagged arrays are faster because:
        //       * access is a simple pointer + index for each row
        //       * they are more friendly to the JIT, especially in generic code.
        //
        //   - But rectangular arrays are more convenient for true matrices.
        //
        // Top-level rule: measure. Many high-performance numeric libraries
        // prefer jagged arrays or Span<T> over multidimensional arrays.
    }

    // ---------------------------------------------------------------------
    // 5. SPAN<T> INTEROP & UNSAFE VIEWS – treating arrays as raw memory
    // ---------------------------------------------------------------------
    static void SpanInteropAndUnsafeView()
    {
        Console.WriteLine();
        Console.WriteLine("=== 5. Span<T> Interop & Unsafe Views ===");

        int[] data = [1, 2, 3, 4, 5];

        // Implicit conversion: int[] → Span<int>
        Span<int> span = data;

        // Modify via Span<int> (no extra allocation):
        span[0] = 10;
        span[1] = 20;

        Console.WriteLine("Array after Span<int> modifications:");
        foreach (var v in data)
        {
            Console.WriteLine(v);
        }

        // Slice without allocations:
        Span<int> middle = span[1..^1]; // elements [1, 2, 3] in-place view
        Console.WriteLine("Middle slice via Span<int>:");
        foreach (var v in middle)
        {
            Console.WriteLine(v);
        }

        // UNSAFE VIEW (ADVANCED):
        //
        //   - MemoryMarshal.CreateSpan allows you to create a Span<int> from a
        //     ref int and length. This is low-level and must be used carefully.
        //
        ref int first = ref MemoryMarshal.GetArrayDataReference(data);
        Span<int> customSpan = MemoryMarshal.CreateSpan(ref first, data.Length);

        customSpan[2] = 999; // modifies data[2]

        Console.WriteLine("Array after MemoryMarshal.CreateSpan:");
        foreach (var v in data)
        {
            Console.WriteLine(v);
        }

        // WHY THIS MATTERS:
        //
        //   - Span<T> gives you low-level, stack-only, bounds-checked views over
        //     arrays (and other memory), often enabling allocation-free APIs.
        //   - This is one of the core primitives used by high-performance .NET
        //     libraries (e.g., System.Text.Json, Kestrel, etc.).
    }

    // ---------------------------------------------------------------------
    // 6. ARRAYPOOL & LARGE OBJECT HEAP – reducing GC pressure
    // ---------------------------------------------------------------------
    static void ArrayPoolAndLargeObjectHeap()
    {
        Console.WriteLine();
        Console.WriteLine("=== 6. ArrayPool & Large Object Heap (LOH) ===");

        // Large arrays (>= 85,000 bytes by default) go to the LOH:
        //   new byte[100_000]  → ~100 KB, ends up on LOH
        //
        // LOH is not compacted regularly, so frequent large-array allocations
        // can fragment memory.
        //
        // ArrayPool<T> lets you RENT and RETURN arrays to avoid constant
        // allocations and GC pressure.

        const int size = 100_000; // 100k bytes (~100 KB)
        byte[] pooled = ArrayPool<byte>.Shared.Rent(size);

        try
        {
            // Use only the first `size` bytes; the pool might return a larger buffer.
            Span<byte> slice = pooled.AsSpan(0, size);
            slice.Clear(); // zero the slice

            // Simulate work:
            slice[0] = 123;
            slice[^1] = 45;

            Console.WriteLine($"Pooled array length: {pooled.Length}");
            Console.WriteLine($"First / Last byte   : {slice[0]} / {slice[^1]}");
        }
        finally
        {
            // Always return to the pool; do NOT use 'pooled' after this.
            ArrayPool<byte>.Shared.Return(pooled, clearArray: false);
        }

        // DESIGN RULE:
        //
        //   - Use ArrayPool<T> when:
        //       * you frequently allocate medium/large arrays
        //       * arrays are short-lived or reused in a tight loop
        //   - This reduces:
        //       * Gen0 pressure
        //       * LOH fragmentation
        //   - But you must treat rented arrays as "borrowed" memory:
        //       * Clear or overwrite sensitive data before returning if needed.
    }

    // ---------------------------------------------------------------------
    // 7. VECTORIZATION SHAPE – using SIMD on arrays
    // ---------------------------------------------------------------------
    static void VectorizationShape()
    {
        Console.WriteLine();
        Console.WriteLine("=== 7. Vectorization Shape (SIMD over arrays) ===");

        // System.Numerics.Vector<T> can use SIMD registers (e.g., AVX2) when the
        // JIT and CPU support it, processing multiple elements per instruction.

        float[] src = new float[1024];
        float[] dst = new float[1024];

        for (int i = 0; i < src.Length; i++)
        {
            src[i] = i;
        }

        ScaleArraySimd(src, dst, 1.5f);
        Console.WriteLine($"SIMD sample: dst[10] = {dst[10]}  (should be 10 * 1.5 = 15)");

        // In real applications you would measure whether this is faster than a
        // scalar loop; on modern CPUs, well-written SIMD loops can be 2–8x faster.
    }

    /// <summary>
    /// Scales src into dst using SIMD where possible.
    /// </summary>
    static void ScaleArraySimd(float[] src, float[] dst, float factor)
    {
        if (src.Length != dst.Length)
            throw new ArgumentException("Lengths must match");

        int length = src.Length;
        int vectorSize = Vector<float>.Count; // 4 or 8 depending on hardware

        int i = 0;
        var vf = new Vector<float>(factor);

        // Process Vector<float>.Count elements at a time
        for (; i <= length - vectorSize; i += vectorSize)
        {
            var vSrc = new Vector<float>(src, i);
            (vSrc * vf).CopyTo(dst, i);
        }

        // Handle remaining elements
        for (; i < length; i++)
        {
            dst[i] = src[i] * factor;
        }
    }

    // ---------------------------------------------------------------------
    // 8. MICRO-BENCHMARK SHAPE – how to measure array performance
    // ---------------------------------------------------------------------
    static void MicroBenchmarkShapeForArrays()
    {
        Console.WriteLine();
        Console.WriteLine("=== 8. Micro-benchmark Shape (Conceptual) ===");

        const int N = 1_000_000;
        int[] data = new int[N];

        for (int i = 0; i < N; i++)
        {
            data[i] = i;
        }

        // Compare:
        //   - Sum using for
        //   - Sum using foreach
        //   - Sum using Span<int>

        int SumFor(int[] arr)
        {
            int sum = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                sum += arr[i];
            }
            return sum;
        }

        int SumForeach(int[] arr)
        {
            int sum = 0;
            foreach (var v in arr)
            {
                sum += v;
            }
            return sum;
        }

        int SumSpan(int[] arr)
        {
            int sum = 0;
            Span<int> span = arr;
            for (int i = 0; i < span.Length; i++)
            {
                sum += span[i];
            }
            return sum;
        }

        static (TimeSpan elapsed, long alloc) Measure<T>(string label, Func<T, int> func, T arg)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            int sum = func(arg);

            sw.Stop();
            long after = GC.GetAllocatedBytesForCurrentThread();

            Console.WriteLine($"{label}: sum={sum}, time={sw.Elapsed.TotalMilliseconds:F2} ms, alloc={after - before} bytes");
            return (sw.Elapsed, after - before);
        }

        Measure("SumFor     ", SumFor, data);
        Measure("SumForeach ", SumForeach, data);
        Measure("SumSpan    ", SumSpan, data);

        // SCIENTIST-LEVEL MINDSET:
        //
        //   - Form a hypothesis: "Span<int> will be as fast as for-loop indexing."
        //   - Design a benchmark like above.
        //   - Measure on your real hardware, with real .NET version.
        //   - Use BenchmarkDotNet in serious work to handle warmup, noise,
        //     statistics, etc.
        //
        // Arrays are the foundation of many data structures and hot loops.
        // Top engineers do not *guess* about performance; they measure it.
    }
}

