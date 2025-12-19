// ================================================================
// FunctionsDeepDive.cs
// ================================================================
//
// Goal
// - Keep your original functions: CalculateArea(width,height) and EvaluateNumber(number)
// - Add “hard-to-find” internals:
//   * What Roslyn emits (IL shape) vs what the JIT (RyuJIT) produces (machine code)
//   * Inlining heuristics, tiered compilation, PGO, devirtualization
//   * CPU-level intuition: pipelines, branch prediction, cmov/select vs branches
//   * Floating-point reality: IEEE-754, NaN, Infinity, -0.0, rounding, FMA
//   * “Fast path” vs “safe path” patterns (Try* APIs, guard clauses)
//   * Microbenchmark pitfalls and what to measure instead
// - Add extra examples you can push to GitHub.
// - Provide clean, modern, .NET 8+ friendly code.
//
// NOTE: This file is intentionally comment-heavy and repo-ready.
//       It is NOT meant to be “minimal.” It is meant to be teachable.
//
// ---------------------------------------------------------------
// 0) Mental model: a “function” is a contract + a calling convention
// ---------------------------------------------------------------
//
// In C#, a method looks like a clean abstraction:
//
//     static double CalculateArea(double width, double height) => width * height;
//
// But on the CPU, it becomes:
// - Parameter passing (registers/stack per ABI)
// - A call instruction (or inlined body)
// - A return value in a designated register (x64: XMM0 for double)
//
// Key idea for top-tier performance:
// ✅ The fastest function is often the one that gets inlined.
// Inlining removes call/return overhead and unlocks further optimizations
// (constant folding, dead-code elimination, bounds-check elimination, etc.).
//
// ---------------------------------------------------------------
// 1) What Roslyn does vs what the JIT does
// ---------------------------------------------------------------
//
// Roslyn (C# compiler):
// - Produces IL + metadata.
// - IL is CPU-agnostic.
// - It preserves high-level constructs (branches, comparisons, calls).
//
// RyuJIT (runtime JIT compiler):
// - Produces actual machine code for x64/ARM64.
// - Decides:
//   * whether to inline
//   * whether to use branchless selects (cmov/csel) or branches
//   * whether to use fused-multiply-add (FMA) in some numeric patterns
//   * whether to reorder computations (within FP rules)
// - With Tiered Compilation, code may be:
//   * Tier 0 (quick JIT) first, then
//   * Tier 1 (optimized) after warmup.
// - With PGO (Profile Guided Optimization), the JIT can specialize decisions
//   based on real runtime behavior (hot paths, branch likelihood).
//
// Consequence:
// ✅ “Same source” can produce different assembly depending on runtime,
//    warmup, and actual inputs. Measure with proper tools.
//
// ---------------------------------------------------------------
// 2) CPU-level intuition: branches vs selects (EvaluateNumber)
// ---------------------------------------------------------------
//
// Your EvaluateNumber:
//
//   if (n > 0) "Positive"
//   else if (n < 0) "Negative"
//   else "Zero"
//
// Two broad machine code strategies:
//
// A) Branching:
//   cmp n, 0
//   jg  POS
//   jl  NEG
//   -> ZERO
//
// B) Branchless select (sometimes):
//   cmp n, 0
//   setg / setl or cmov-like sequences
//
// Modern CPUs predict branches. When prediction is correct, branches are cheap.
// When wrong, the pipeline flush can cost ~10–20+ cycles (varies).
//
// Top-level heuristic:
// ✅ If input distribution is predictable (e.g., 99% positive), branches win.
// ✅ If distribution is random (50/50), branchless can win in hot loops.
// ✅ But branchless increases instruction count; measure first.
//
// ---------------------------------------------------------------
// 3) Floating-point reality: CalculateArea
// ---------------------------------------------------------------
//
// double is IEEE-754 binary64:
// - 1 sign bit, 11 exponent bits, 52 mantissa bits (53 bits precision with hidden bit)
// - Not all decimals are representable exactly.
// - Multiplication is rounded to nearest representable value.
//
// Special values:
// - NaN (Not-a-Number): propagates through most operations
// - Infinity: can appear on overflow or division by 0
// - -0.0: exists and can matter in comparisons like 1/(-0.0) = -Infinity
//
// So "width * height" can be:
// - a normal multiply instruction (mulsd on x64)
// - or combined with other ops via FMA in more complex expressions.
//
// ---------------------------------------------------------------
// 4) Safety and correctness: validating inputs
// ---------------------------------------------------------------
//
// For "area", domain rules matter:
// - Do you allow negative dimensions?
// - Do you allow NaN / Infinity?
// - Is overflow acceptable?
//
// In "world-class" code, you encode domain invariants early.
// Example:
//   if (width < 0 || height < 0) throw ...
//   if (!double.IsFinite(width) || !double.IsFinite(height)) throw ...
//
// ---------------------------------------------------------------
// 5) Microbenchmarks: what can go wrong
// ---------------------------------------------------------------
//
// Stopwatch-based tests are okay for demos, but misleading for real decisions:
// - Tiered compilation warms up
// - CPU frequency scaling varies
// - GC can interrupt
// - JIT may optimize away work if result unused
//
// For real measurements: BenchmarkDotNet + disassembly diagnoser.
//
// In this file we include a small Stopwatch demo, but treat it as educational.
//
// ================================================================
// 6) Upgraded version of your class + expert examples
// ================================================================

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using static System.Console;

partial class Program
{
    public static void FunctionsDeepDive()
    {
        Functions_Basics();
        Functions_NumericEdgeCases();
        Functions_BranchingVsBranchless_Demo();
        Functions_InliningAndCallOverhead_Demo();
        Functions_ContractStyle_APIs();
    }

    // ------------------------------------------------------------
    // Your original functions (kept), with small upgrades:
    // - AggressiveInlining hints intent (not a guarantee).
    // - Guard clauses demonstrate domain correctness.
    // ------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double CalculateArea(double width, double height)
    {
        // Domain choice: disallow negative dimensions.
        // This is not “free”—it adds branches—but often worth it for correctness.
        if (width < 0 || height < 0)
            throw new ArgumentOutOfRangeException("Dimensions must be non-negative.");

        // If you accept NaN/Infinity, remove these checks.
        if (!double.IsFinite(width) || !double.IsFinite(height))
            throw new ArgumentException("Dimensions must be finite (not NaN/Infinity).");

        return width * height;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static string EvaluateNumber(int number)
    {
        // Branch pattern: JIT often emits compare + conditional branches.
        // With PGO, the JIT may bias layout based on observed likelihood.
        if (number > 0)
            return "Positive";
        else if (number < 0)
            return "Negative";
        else
            return "Zero";
    }

    static void Functions_Basics()
    {
        WriteLine("== Functions_Basics ==");

        var area = CalculateArea(4.5, 2.23);
        WriteLine($"The area is: {area}");

        var evaluatedNumber = EvaluateNumber(-45);
        WriteLine($"The evaluated number is: {evaluatedNumber}");

        WriteLine();
    }

    // ------------------------------------------------------------
    // Numeric edge cases: IEEE-754, -0.0, NaN, Infinity
    // ------------------------------------------------------------
    static void Functions_NumericEdgeCases()
    {
        WriteLine("== Functions_NumericEdgeCases ==");

        // -0.0 exists in IEEE-754.
        // It prints as "0" typically, but it behaves differently in some ops.
        double negZero = -0.0;
        WriteLine($"negZero == 0.0? {negZero == 0.0}");
        WriteLine($"1.0 / negZero = {1.0 / negZero}  (expect -Infinity)");

        // NaN comparisons are always false (even NaN == NaN).
        double nan = double.NaN;
        WriteLine($"NaN == NaN? {nan == double.NaN}");
        WriteLine($"double.IsNaN(nan)? {double.IsNaN(nan)}");

        // Infinity shows overflow or division by 0.
        double inf = double.PositiveInfinity;
        WriteLine($"IsFinite(inf)? {double.IsFinite(inf)}");

        // Demonstrate “safe” area with finite checks (will throw for NaN/Inf).
        TryRun(() => CalculateArea(1, 2));
        TryRun(() => CalculateArea(double.NaN, 2));              // throws
        TryRun(() => CalculateArea(double.PositiveInfinity, 2)); // throws
        TryRun(() => CalculateArea(-1, 2));                      // throws

        WriteLine();
    }

    static void TryRun(Action action)
    {
        try
        {
            action();
            WriteLine("OK");
        }
        catch (Exception ex)
        {
            WriteLine($"EX: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------
    // Branching vs branchless selection demo for EvaluateNumber
    // (educational; not a replacement for BenchmarkDotNet)
    // ------------------------------------------------------------
    static void Functions_BranchingVsBranchless_Demo()
    {
        WriteLine("== Functions_BranchingVsBranchless_Demo ==");

        const int N = 2_000_000;

        // Predictable distribution: mostly positive.
        var predictable = new int[N];
        for (int i = 0; i < N; i++)
            predictable[i] = i % 100 == 0 ? 0 : 1;

        // Unpredictable distribution: pseudo-random.
        var rng = new Random(123);
        var unpredictable = new int[N];
        for (int i = 0; i < N; i++)
            unpredictable[i] = (rng.Next() & 3) - 1; // -1,0,1

        var sw = Stopwatch.StartNew();
        int a = CountPositive_Branchy(predictable);
        sw.Stop();
        WriteLine($"Predictable CountPositive_Branchy={a}, ms={sw.ElapsedMilliseconds}");

        sw.Restart();
        int b = CountPositive_Branchy(unpredictable);
        sw.Stop();
        WriteLine($"Unpredictable CountPositive_Branchy={b}, ms={sw.ElapsedMilliseconds}");

        // A “branchless-ish” version using comparisons + arithmetic.
        // Not always faster; it trades branches for extra instructions.
        sw.Restart();
        int c = CountPositive_Branchless(predictable);
        sw.Stop();
        WriteLine($"Predictable CountPositive_Branchless={c}, ms={sw.ElapsedMilliseconds}");

        sw.Restart();
        int d = CountPositive_Branchless(unpredictable);
        sw.Stop();
        WriteLine($"Unpredictable CountPositive_Branchless={d}, ms={sw.ElapsedMilliseconds}");

        WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static int CountPositive_Branchy(int[] data)
    {
        int count = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] > 0) count++;
        }
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static int CountPositive_Branchless(int[] data)
    {
        int count = 0;
        for (int i = 0; i < data.Length; i++)
        {
            // In IL/JIT, comparisons can become setcc + add.
            // bool -> int conversion can become 0/1 without branches.
            count += data[i] > 0 ? 1 : 0;
        }
        return count;
    }

    // ------------------------------------------------------------
    // Inlining and call overhead: why small functions matter
    // ------------------------------------------------------------
    static void Functions_InliningAndCallOverhead_Demo()
    {
        WriteLine("== Functions_InliningAndCallOverhead_Demo ==");

        const int N = 5_000_000;
        double w = 1.23456789;
        double h = 9.87654321;

        // Important: prevent JIT from optimizing away the loop by using result.
        double sink = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++)
            sink += MultiplyInlineCandidate(w, h);
        sw.Stop();
        WriteLine($"InlineCandidate loop: sink={sink}, ms={sw.ElapsedMilliseconds}");

        sink = 0;
        sw.Restart();
        for (int i = 0; i < N; i++)
            sink += MultiplyNoInline(w, h);
        sw.Stop();
        WriteLine($"NoInline loop:       sink={sink}, ms={sw.ElapsedMilliseconds}");

        WriteLine();

        // You should expect the "noinline" version to often be slower
        // due to call/return overhead and fewer optimization opportunities,
        // but results vary by runtime/JIT/tiering and CPU.
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double MultiplyInlineCandidate(double a, double b) => a * b;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static double MultiplyNoInline(double a, double b) => a * b;

    // ------------------------------------------------------------
    // Contract-style APIs: "Try" patterns and domain-centric results
    // ------------------------------------------------------------
    static void Functions_ContractStyle_APIs()
    {
        WriteLine("== Functions_ContractStyle_APIs ==");

        // A “Try” API avoids exceptions on hot paths (exceptions are expensive).
        if (TryCalculateArea(4.5, 2.23, out var okArea, out var reason))
            WriteLine($"TryCalculateArea OK: {okArea}");
        else
            WriteLine($"TryCalculateArea FAIL: {reason}");

        if (TryCalculateArea(double.NaN, 2, out _, out reason))
            WriteLine("Unexpected");
        else
            WriteLine($"TryCalculateArea FAIL: {reason}");

        // Richer classification: extend EvaluateNumber to include parity & ranges.
        WriteLine(ClassifyNumber(0));
        WriteLine(ClassifyNumber(7));
        WriteLine(ClassifyNumber(-42));
        WriteLine(ClassifyNumber(int.MinValue));

        WriteLine();
    }

    static bool TryCalculateArea(double width, double height, out double area, out string reason)
    {
        // "Try" pattern: fast, predictable control-flow.
        // Great when invalid inputs are common and you want to avoid throwing.
        if (width < 0 || height < 0)
        {
            area = default;
            reason = "Dimensions must be non-negative.";
            return false;
        }

        if (!double.IsFinite(width) || !double.IsFinite(height))
        {
            area = default;
            reason = "Dimensions must be finite.";
            return false;
        }

        area = width * height;
        reason = "OK";
        return true;
    }

    static string ClassifyNumber(int n)
    {
        // Advanced conditional patterns:
        // - switch expressions
        // - guards (when)
        // - bit tricks for parity (n & 1)
        //
        // CPU note: parity checks are usually 1 instruction (AND + test).
        return n switch
        {
            0 => "Zero",
            > 0 when (n & 1) == 0 => "Positive even",
            > 0 => "Positive odd",
            < 0 when (n & 1) == 0 => "Negative even",
            _ => "Negative odd"
        };
    }
}

// ================================================================
// Extra “scientist-level” notes (keep in repo)
// ================================================================
//
// 1) Calling conventions (very condensed)
// - On x64 Windows, doubles typically pass in XMM registers (XMM0..XMM3).
// - Return double typically comes back in XMM0.
// - If inlined, the “call boundary” disappears entirely.
//
// 2) EvaluateNumber string returns
// - Returning string literals is cheap (interned) and does not allocate each call.
// - But repeated string concatenations inside hot loops can allocate and kill perf.
//
// 3) Exceptions are for exceptional paths
// - Throwing is expensive: stack capture + unwinding + potential logging.
// - Use Try* patterns in parsing, validation, or hot loops.
//
// 4) Floating-point determinism
// - Different CPUs (x64 vs ARM64) or different JIT behaviors can yield slightly
//   different last-bit results in complex FP expressions.
// - For financial logic: prefer decimal or integer (cents) arithmetic.
//
// 5) How to go “top 1%” in function performance work
// - Use BenchmarkDotNet
// - Warm up (tiered compilation)
// - Use DisassemblyDiagnoser to see actual JIT output
// - Profile real workloads (dotnet-trace/PerfView)
// - Optimize based on data and allocations first, then micro-level branching.
//
// ================================================================
