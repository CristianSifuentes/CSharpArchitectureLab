// ================================================================
// FizzBuzzDeepDive.cs
// ================================================================
//
// Goal:
// - Start from the classic FizzBuzz (for + if/else if/else).
// - Add “hard-to-find” internals:
//   * What Roslyn emits (IL), what RyuJIT turns into machine code
//   * Branch prediction + CPU pipelines in if/else chains
//   * The real cost of modulo (%) at the CPU level (integer division)
//   * Switch lowering (jump tables) vs if-chains
//   * Tiered compilation + PGO and why warmup matters
//   * Allocation-free output patterns (StringBuilder / buffering)
// - Provide “world-class” variations with measurable performance patterns.
//
// NOTE:
// - This is intentionally comment-heavy and GitHub-ready.
// - Microbench results vary by CPU, OS, .NET version, and console speed.
// - Console I/O dominates time; the optimized versions focus on the *compute*
//   and on *buffering output* to reduce overhead.
//
// ---------------------------------------------------------------
// 0) Mental model: a loop is a hot back-edge + a conditional decision tree
// ---------------------------------------------------------------
//
// Classic FizzBuzz:
//
//   for (int i = 1; i <= 100; i++)
//       if (i % 15 == 0) ...
//       else if (i % 3 == 0) ...
//       else if (i % 5 == 0) ...
//       else ...
//
// At the CPU level, your for-loop is:
// - compare i to end
// - conditional branch back to loop head
//
// The if/else chain is:
// - compute predicate(s)
// - branch to the matching block
//
// Modern CPUs execute instructions speculatively in a pipeline.
// If your branch is predicted correctly → fast.
// If mispredicted → pipeline flush (often ~10–20+ cycles on modern cores).
//
// The tricky part:
// - This is not “if is slow.”
// - It’s “unpredictable branching in a hot loop can be expensive,”
//   and “division/modulo can be expensive.”
//
// ---------------------------------------------------------------
// 1) Compiler vs JIT: who does what?
// ---------------------------------------------------------------
//
// Roslyn (C# compiler):
// - Produces IL (Intermediate Language) + metadata.
// - IL has explicit branch instructions: brtrue, brfalse, beq, etc.
// - 'for' becomes a label + condition branch + increment + back-edge branch.
//
// RyuJIT (runtime JIT compiler):
// - Translates IL → machine code (x64/ARM64).
// - Decides instruction selection (e.g., idiv vs strength reduction),
//   emits branches or conditional moves, hoists loop invariants, etc.
// - Tiered Compilation:
//     Tier 0: quick compile (less optimization) to start fast
//     Tier 1: optimized compile when method is “hot”
// - PGO (Profile-Guided Optimization):
//     uses actual runtime behavior (branch likelihood, type profiles)
//     to generate better code paths.
//
// Implication for “top programmers”:
// - Always warm up hot paths before measuring.
// - Keep code shapes optimization-friendly: simple bounds, simple predicates,
//   clear invariants.
//
// ---------------------------------------------------------------
// 2) The hidden villain: % (modulo) typically uses integer division
// ---------------------------------------------------------------
//
// For integers, x % y is tightly linked to integer division.
// On many CPUs, integer division (IDIV on x64) is *much* slower than add/sub/mul.
// The exact latency/throughput depends on CPU microarchitecture, but it’s
// commonly an order of magnitude slower than simple ALU ops.
//
// In classic FizzBuzz, we do up to 3 modulo ops per iteration (15, 3, 5).
// The JIT may optimize a bit (especially for constants), but division/remainder
// can still be a significant cost in compute-heavy scenarios.
//
// "Scientist mindset":
// - If the loop is huge and I/O is not the bottleneck, reducing division ops
//   can matter.
// - If you're printing to console, I/O dominates and compute optimizations
//   barely move the needle.
//
// ---------------------------------------------------------------
// 3) Branch prediction in FizzBuzz: is it predictable?
// ---------------------------------------------------------------
//
// The pattern is periodic: every 3rd, every 5th, every 15th.
// Predictors can learn patterns, but the decision tree has multiple branches.
// Whether it helps depends on CPU predictor sophistication and code layout.
//
// For some workloads, it’s faster to avoid branching by using:
// - precomputed lookup tables (periodic nature!)
// - counter-based logic (no division)
// - switch on precomputed state
//
// ---------------------------------------------------------------
// 4) Practical performance: avoid per-iteration Console.WriteLine
// ---------------------------------------------------------------
//
// Console.WriteLine is *very* slow compared to arithmetic and branching.
// If you want to show performance differences in a demo, buffer output:
//
// - StringBuilder (good default)
// - Array/string join (careful with allocations)
// - Write once at the end
//
// ---------------------------------------------------------------
// 5) The “top 1%” principle: exploit structure (periodicity)
// ---------------------------------------------------------------
//
// FizzBuzz repeats every LCM(3,5) = 15.
// That means outputs repeat in a cycle of 15.
// We can precompute those 15 tokens once, then reuse.
//
// In real systems, this is the mindset:
// - Find invariants.
// - Find periodicity.
// - Reduce work inside hot loops.
// - Reduce allocations.
// - Improve memory locality.
//
// ================================================================

using System;
using System.Diagnostics;
using System.Text;
using System.Runtime.CompilerServices;
using static System.Console;

partial class Program
{
    // Entry you can call from Main or your chapter runner.
    public static void FizzBuzzDeepDive()
    {
        WriteLine("================================================");
        WriteLine("FizzBuzzDeepDive — for/if/else internals + perf");
        WriteLine("================================================\n");

        FizzBuzz_Baseline(1, 100);
        FizzBuzz_Buffered_Baseline(1, 100);

        // “Compute-focused” versions (use buffering to keep I/O from dominating).
        FizzBuzz_Counters_NoModulo(1, 100);
        FizzBuzz_PeriodicLookup(1, 100);

        // Micro-ish measurement (still not BenchmarkDotNet).
        FizzBuzz_PerfShowcase();
    }

    // ------------------------------------------------------------
    // Original baseline: as you wrote it (kept readable).
    // Note: Console I/O dominates; this is pedagogical correctness.
    // ------------------------------------------------------------
    static void FizzBuzz_Baseline(int start, int end)
    {
        WriteLine("== FizzBuzz_Baseline (console per iteration) ==");

        for (int i = start; i <= end; i++)
        {
            if (i % 3 == 0 && i % 5 == 0)
                WriteLine("FizzBuzz");
            else if (i % 3 == 0)
                WriteLine("Fizz");
            else if (i % 5 == 0)
                WriteLine("Buzz");
            else
                WriteLine(i);
        }

        WriteLine();
    }

    // ------------------------------------------------------------
    // Baseline logic + buffered output:
    // - Same predicates
    // - Far fewer Console writes
    // ------------------------------------------------------------
    static void FizzBuzz_Buffered_Baseline(int start, int end)
    {
        WriteLine("== FizzBuzz_Buffered_Baseline (same logic, buffered output) ==");

        var sb = new StringBuilder(capacity: (end - start + 1) * 8);

        for (int i = start; i <= end; i++)
        {
            if (i % 3 == 0 && i % 5 == 0) sb.AppendLine("FizzBuzz");
            else if (i % 3 == 0) sb.AppendLine("Fizz");
            else if (i % 5 == 0) sb.AppendLine("Buzz");
            else sb.AppendLine(i.ToString());
        }

        Write(sb.ToString());
        WriteLine();
    }

    // ------------------------------------------------------------
    // Expert variant #1: Counter-based (no modulo / no division)
    // ------------------------------------------------------------
    static void FizzBuzz_Counters_NoModulo(int start, int end)
    {
        WriteLine("== FizzBuzz_Counters_NoModulo (no %) ==");

        int c3 = 0;
        int c5 = 0;

        var sb = new StringBuilder(capacity: (end - start + 1) * 8);

        for (int i = start; i <= end; i++)
        {
            c3++;
            c5++;

            bool fizz = (c3 == 3);
            bool buzz = (c5 == 5);

            if (fizz) c3 = 0;
            if (buzz) c5 = 0;

            if (fizz && buzz) sb.AppendLine("FizzBuzz");
            else if (fizz) sb.AppendLine("Fizz");
            else if (buzz) sb.AppendLine("Buzz");
            else sb.AppendLine(i.ToString());
        }

        Write(sb.ToString());
        WriteLine();
    }

    // ------------------------------------------------------------
    // Expert variant #2: Periodic lookup (LCM(3,5)=15)
    // ------------------------------------------------------------
    static void FizzBuzz_PeriodicLookup(int start, int end)
    {
        WriteLine("== FizzBuzz_PeriodicLookup (cycle of 15, no % using index counter) ==");

        // Marker approach: '#' means “print i”, otherwise print token.
        ReadOnlySpan<string> cycle =
        [
            "#", "#", "Fizz", "#", "Buzz",
            "Fizz", "#", "#", "Fizz", "Buzz",
            "#", "Fizz", "#", "#", "FizzBuzz"
        ];

        var sb = new StringBuilder(capacity: (end - start + 1) * 8);

        int idx = (start - 1) % 15; // align start (one-time cost)

        for (int i = start; i <= end; i++)
        {
            idx++;
            if (idx == 15) idx = 0;

            var token = cycle[idx];
            if (token == "#") sb.AppendLine(i.ToString());
            else sb.AppendLine(token);
        }

        Write(sb.ToString());
        WriteLine();
    }

    // ------------------------------------------------------------
    // Performance showcase (compute-focused, no console spam).
    // ------------------------------------------------------------
    static void FizzBuzz_PerfShowcase()
    {
        WriteLine("== FizzBuzz_PerfShowcase (compute-focused) ==");

        const int start = 1;
        const int end = 5_000_00; // 500k (adjust as you like)

        // Warmup: tiered compilation / JIT.
        _ = FizzBuzz_BufferToString_Baseline(start, 1000);
        _ = FizzBuzz_BufferToString_Counters(start, 1000);
        _ = FizzBuzz_BufferToString_Lookup(start, 1000);

        var sw = Stopwatch.StartNew();
        int len1 = FizzBuzz_BufferToString_Baseline(start, end).Length;
        sw.Stop();
        WriteLine($"Baseline(%)  : {sw.ElapsedMilliseconds} ms, outLen={len1}");

        sw.Restart();
        int len2 = FizzBuzz_BufferToString_Counters(start, end).Length;
        sw.Stop();
        WriteLine($"Counters(no%): {sw.ElapsedMilliseconds} ms, outLen={len2}");

        sw.Restart();
        int len3 = FizzBuzz_BufferToString_Lookup(start, end).Length;
        sw.Stop();
        WriteLine($"Lookup(15)   : {sw.ElapsedMilliseconds} ms, outLen={len3}");

        WriteLine();
    }

    // ------------------------------------------------------------
    // Compute-only helpers (return string, no console writes).
    // ------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static string FizzBuzz_BufferToString_Baseline(int start, int end)
    {
        var sb = new StringBuilder(capacity: (end - start + 1) * 8);

        for (int i = start; i <= end; i++)
        {
            if (i % 3 == 0 && i % 5 == 0) sb.AppendLine("FizzBuzz");
            else if (i % 3 == 0) sb.AppendLine("Fizz");
            else if (i % 5 == 0) sb.AppendLine("Buzz");
            else sb.AppendLine(i.ToString());
        }

        return sb.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static string FizzBuzz_BufferToString_Counters(int start, int end)
    {
        var sb = new StringBuilder(capacity: (end - start + 1) * 8);

        int c3 = 0, c5 = 0;

        for (int i = start; i <= end; i++)
        {
            c3++; c5++;

            bool fizz = (c3 == 3);
            bool buzz = (c5 == 5);

            if (fizz) c3 = 0;
            if (buzz) c5 = 0;

            if (fizz && buzz) sb.AppendLine("FizzBuzz");
            else if (fizz) sb.AppendLine("Fizz");
            else if (buzz) sb.AppendLine("Buzz");
            else sb.AppendLine(i.ToString());
        }

        return sb.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static string FizzBuzz_BufferToString_Lookup(int start, int end)
    {
        var sb = new StringBuilder(capacity: (end - start + 1) * 8);

        ReadOnlySpan<string> cycle =
        [
            "#", "#", "Fizz", "#", "Buzz",
            "Fizz", "#", "#", "Fizz", "Buzz",
            "#", "Fizz", "#", "#", "FizzBuzz"
        ];

        int idx = (start - 1) % 15;

        for (int i = start; i <= end; i++)
        {
            idx++;
            if (idx == 15) idx = 0;

            var token = cycle[idx];
            if (token == "#") sb.AppendLine(i.ToString());
            else sb.AppendLine(token);
        }

        return sb.ToString();
    }
}

// ================================================================
// Extra “scientist-level” notes you can keep in your repo
// ================================================================
//
// 1) Why modulo is expensive (CPU view)
// - Remainder needs division logic. Division is a complex operation,
//   often microcoded or multi-cycle.
// - Many CPUs cannot pipeline division as efficiently as add/mul.
// - Replacing division with counters is a common hot-loop technique.
//
// 2) Branch prediction and periodic patterns
// - FizzBuzz is periodic (15). Predictors can sometimes learn it,
//   but multi-branch decision trees are still harder than a single loop back-edge.
// - A lookup table makes control flow very regular.
//
// 3) JIT optimizations you “unlock” by code shape
// - Keep loop bounds simple, use locals for invariants.
// - Avoid virtual/interface calls in hot loops.
// - Reduce allocations (especially inside loop body).
//
// 4) Measuring like a pro
// - Use BenchmarkDotNet with warmup + multiple launches.
// - Add DisassemblyDiagnoser to inspect JIT output.
// - Profile with dotnet-trace / PerfView to find real bottlenecks.
//
// 5) “LLM-ready code” angle (why this matters)
// - Structured, predictable code is easier for humans AND LLMs to reason about.
// - The same practices that help the JIT (simple shapes) also help code review,
//   refactoring, and prompt-based generation.
//
// ================================================================
