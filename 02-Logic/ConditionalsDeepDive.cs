// ================================================================
// ConditionalsDeepDive.cs
// ================================================================
//
// Goal:
// - Keep your original examples (if/else, ternary, switch, switch expression)
// - Add “hard-to-find” internals: CPU branch prediction, pipelines,
//   JIT codegen patterns (cmov vs branch), switch lowering (jump tables),
//   pattern matching, PGO/tiered compilation, and performance heuristics.
// - Add extra examples that are GitHub-ready and “senior-level”.
//
// This file is intentionally comment-heavy.
//
// ---------------------------------------------------------------
// 0) Mental model: a “conditional” is a control-flow *decision*
// ---------------------------------------------------------------
//
// In C#, conditionals look high-level:
//
//   if (x > 10) A(); else B();
//
// But at the lowest level, a modern CPU executes *instructions* in a pipeline.
// A conditional typically becomes:
//
//   - A compare instruction setting flags (e.g., CMP on x64)
//   - A branch instruction reading those flags (e.g., JG/JLE)
//   - OR a “branchless” conditional move/select idiom (CMOV on x64, CSEL on ARM64)
//
// The choice matters because branches can be *predicted* correctly or not.
// Misprediction can flush the pipeline and cost ~10–20+ cycles depending
// on CPU microarchitecture (varies widely).
//
// ---------------------------------------------------------------
// 1) CPU-level intuition (what changes in the processor?)
// ---------------------------------------------------------------
//
// ✅ Branch prediction
// - CPUs try to guess which way your if will go.
// - If the guess is correct, execution continues smoothly.
// - If wrong, the CPU discards speculatively executed work (pipeline flush)
//   and restarts from the correct path.
//
// ✅ Why “data patterns” matter more than “if statements”
// - If your predicate is stable (e.g., 99% true), the predictor becomes good.
// - If the predicate is random (50/50), mispredicts increase.
// - If your data is clustered, you can often improve prediction by reordering
//   data or checks.
//
// ✅ “Branchless” isn't always faster
// - Replacing a branch with arithmetic or bit tricks can increase instruction
//   count and pressure registers; it may be slower if branch prediction is good.
// - Use branchless techniques when:
//   - branches are unpredictable, AND
//   - both sides are “small”, AND
//   - you’re in a hot loop.
//
// ---------------------------------------------------------------
// 2) Compiler vs JIT: who decides what instruction is emitted?
// ---------------------------------------------------------------
//
// Roslyn (C# compiler):
// - Emits IL (Intermediate Language) and metadata.
// - IL contains branching instructions (brtrue, brfalse, beq, etc.)
//   and structured switch instructions.
//
// RyuJIT (runtime JIT compiler):
// - Converts IL to machine code for the current CPU (x64/ARM64).
// - Decides whether a conditional becomes:
//   - a branch instruction
//   - a conditional move/select (cmov/csel)
//   - a jump table for switch
// - Applies optimizations: inlining, constant folding, bounds-check
//   elimination (sometimes), loop-invariant code motion, etc.
//
// Hard-to-find but practical nuance:
// - .NET has Tiered Compilation. Your code may first run in a quickly-jitted
//   “Tier 0” version, then be re-jitted to an optimized “Tier 1” version.
// - Newer .NET versions can also use PGO (Profile Guided Optimization),
//   where real runtime behavior guides codegen choices.
// - Meaning: “the same source” can produce *different* machine code depending
//   on runtime, warmup, and actual input patterns.
//
// ---------------------------------------------------------------
// 3) if/else: common JIT shapes
// ---------------------------------------------------------------
//
// Example:
//
//   if (x > 0) sum += x; else sum -= x;
//
// Typical codegen shapes:
// A) Branched:
//      cmp x, 0
//      jle ELSE
//      add sum, x
//      jmp END
//    ELSE:
//      sub sum, x
//    END:
//
// B) Branchless (select / cmov):
//      cmp x, 0
//      mov tmp, x
//      neg tmp          // tmp = -x
//      cmovle x, tmp    // x = (x <= 0) ? -x : x
//      add sum, x
//
// JIT chooses based on heuristics. You don’t control it directly,
// but you influence it via:
// - reducing work in each branch
// - simplifying expressions
// - making data patterns predictable (sorting / partitioning)
// - using Math.* helpers sometimes (Abs can be branchless)
// - writing in a “vectorization-friendly” manner (for numeric code)
//
// ---------------------------------------------------------------
// 4) switch: jump tables vs compare chains
// ---------------------------------------------------------------
//
// switch can become:
// - A compare chain (if value count is small or sparse):
//      if (d==1) ... else if (d==2) ... else ...
//
// - A jump table (if cases are dense):
//      bounds check -> index into table -> indirect jump
//
// Jump tables are fast but have overhead:
// - range check
// - an indirect jump (can be harder to predict)
//
// switch expressions can also be lowered into similar structures,
// sometimes including decision DAGs for patterns.
//
// ---------------------------------------------------------------
// 5) Pattern matching: not “just syntax sugar”
// ---------------------------------------------------------------
//
// C# pattern matching (switch expressions, 'is' patterns) often produces
// a decision tree that can be optimized differently than nested ifs.
//
// Examples:
// - Relational patterns: x is >= 0 and < 10
// - Type patterns: obj is string s
// - Property patterns: p is { X: > 0, Y: > 0 }
//
// The JIT can sometimes devirtualize and inline things around these checks,
// depending on exact types and call sites.
//
// ---------------------------------------------------------------
// 6) World-class heuristics for conditionals (practical + battle-tested)
// ---------------------------------------------------------------
//
// ✔ Prefer guard clauses (early return) to reduce nesting.
// ✔ Put the most likely branch first *only if it improves readability*
//   AND you’re in a hot path.
// ✔ In hot loops: reduce unpredictable branches; consider data partitioning.
// ✔ When micro-optimizing: measure with BenchmarkDotNet (Stopwatch is OK for demos).
// ✔ Avoid premature branchless tricks; readability wins unless proven hot.
// ✔ Prefer switch for discrete sets; prefer dictionaries for extensible mappings.
// ✔ Remember: memory access (cache misses) often dominates over branch costs.
//
// ---------------------------------------------------------------
// 7) Upgraded version of your class + expert demos
// ---------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using static System.Console;

partial class Program
{
    public static void ConditionalsDeepDive()
    {
        Conditionals_Basics();
        Conditionals_Expert_Patterns();
        Conditionals_SwitchLowering_Showcase();
        Conditionals_BranchPrediction_Demo();
        Conditionals_Branchless_Selections();
    }

    // ------------------------------------------------------------
    // Your original: basics + small cleanup.
    // ------------------------------------------------------------
    static void Conditionals_Basics()
    {
        WriteLine("== Conditionals_Basics ==");

        int age = 19;

        if (age >= 18)
            WriteLine("You are of legal age.");
        else
            WriteLine("You are a minor.");

        // Ternary: expression form (often compiles similarly to if/else).
        string message = age >= 18 ? "You are of legal age." : "You are a minor.";
        WriteLine(message);

        // Multiple conditions
        int temperature = 30;

        if (temperature > 35)
            WriteLine("It's very hot.");
        else if (temperature >= 20)
            WriteLine("It's pleasant.");
        else
            WriteLine("It's cold.");

        // Switch statement (can become chain or jump table).
        int day = 3;
        switch (day)
        {
            case 1: WriteLine("Monday"); break;
            case 2: WriteLine("Tuesday"); break;
            case 3: WriteLine("Wednesday"); break;
            default: WriteLine("Invalid day"); break;
        }

        // Switch expression (pattern matching-friendly).
        string dayMessage = day switch
        {
            1 => "Monday",
            2 => "Tuesday",
            3 => "Wednesday",
            _ => "Invalid day"
        };
        WriteLine(dayMessage);

        WriteLine();
    }

    // ------------------------------------------------------------
    // Expert patterns: guard clauses, relational patterns, and
    // domain-friendly decision APIs.
    // ------------------------------------------------------------
    static void Conditionals_Expert_Patterns()
    {
        WriteLine("== Conditionals_Expert_Patterns ==");

        // Guard clauses: reduce nesting, improve “happy path” clarity.
        WriteLine(DescribeAccess(age: 19, isSuspended: false));
        WriteLine(DescribeAccess(age: 15, isSuspended: false));
        WriteLine(DescribeAccess(age: 25, isSuspended: true));

        // Relational patterns: express ranges declaratively.
        int temp = 18;
        WriteLine(TempBand(temp));

        // Type pattern + null safety.
        object? payload = "hello";
        WriteLine(DescribePayload(payload));
        payload = null;
        WriteLine(DescribePayload(payload));

        // Property patterns: great for “routing” logic.
        var req = new Request(Path: "/v1/export/call-records", Method: "GET", IsAuthenticated: true);
        WriteLine(Route(req));

        WriteLine();
    }

    static string DescribeAccess(int age, bool isSuspended)
    {
        // In real APIs, the fastest code is code you can reason about.
        // Guard clauses also reduce instruction footprint in deep nesting.
        if (isSuspended) return "Access denied: account suspended.";
        if (age < 0) return "Invalid age.";
        if (age < 18) return "Access denied: minor.";
        return "Access granted.";
    }

    static string TempBand(int temperatureC) => temperatureC switch
    {
        < 0 => "Freezing",
        >= 0 and < 20 => "Cold",
        >= 20 and < 35 => "Pleasant",
        _ => "Hot"
    };

    static string DescribePayload(object? payload) => payload switch
    {
        null => "No payload",
        string s when s.Length <= 5 => $"Short string: '{s}'",
        string s => $"String length {s.Length}",
        int i => $"Int: {i}",
        _ => $"Other: {payload.GetType().Name}"
    };

    public readonly record struct Request(string Path, string Method, bool IsAuthenticated);

    static string Route(Request r) => r switch
    {
        { IsAuthenticated: false } => "401 Unauthorized",
        { Method: "GET", Path: "/v1/health" } => "HealthHandler",
        { Method: "GET", Path: "/v1/export/call-records" } => "CallRecordsExportHandler",
        _ => "404 Not Found"
    };

    // ------------------------------------------------------------
    // Switch lowering showcase: dense vs sparse case sets.
    // (We can't show machine code here without disassembly tools,
    //  but we can write examples that tend to lower differently.)
    // ------------------------------------------------------------
    static void Conditionals_SwitchLowering_Showcase()
    {
        WriteLine("== Conditionals_SwitchLowering_Showcase ==");

        // Dense: 0..6 often becomes a jump table.
        for (int d = 0; d <= 7; d++)
            WriteLine($"Dense {d} => {DenseSwitch(d)}");

        // Sparse: values far apart often become compare chain.
        int[] sparse = { 1, 10, 100, 1000, 42 };
        foreach (var v in sparse)
            WriteLine($"Sparse {v} => {SparseSwitch(v)}");

        WriteLine();
    }

    static string DenseSwitch(int d) => d switch
    {
        0 => "Sun",
        1 => "Mon",
        2 => "Tue",
        3 => "Wed",
        4 => "Thu",
        5 => "Fri",
        6 => "Sat",
        _ => "?"
    };

    static string SparseSwitch(int v) => v switch
    {
        1 => "One",
        10 => "Ten",
        100 => "Hundred",
        1000 => "Thousand",
        _ => "Other"
    };

    // ------------------------------------------------------------
    // Branch prediction demo:
    // Two loops with same work, different data patterns.
    // - Predictable predicate -> often faster
    // - Unpredictable predicate -> often slower
    //
    // ⚠️ This is just a demo with Stopwatch; real benchmarking
    // should use BenchmarkDotNet with warmups and multiple runs.
    // ------------------------------------------------------------
    static void Conditionals_BranchPrediction_Demo()
    {
        WriteLine("== Conditionals_BranchPrediction_Demo ==");

        const int N = 2_000_000;
        var predictable = new int[N];
        var unpredictable = new int[N];

        // Predictable: mostly positive.
        for (int i = 0; i < N; i++)
            predictable[i] = i % 100 == 0 ? -1 : 1; // 99% true branch

        // Unpredictable: pseudo-random.
        var rng = new Random(123);
        for (int i = 0; i < N; i++)
            unpredictable[i] = (rng.Next() & 1) == 0 ? -1 : 1; // ~50/50

        var sw = Stopwatch.StartNew();
        long a = SumPositive(predictable);
        sw.Stop();
        WriteLine($"Predictable sum={a}, ms={sw.ElapsedMilliseconds}");

        sw.Restart();
        long b = SumPositive(unpredictable);
        sw.Stop();
        WriteLine($"Unpredictable sum={b}, ms={sw.ElapsedMilliseconds}");

        WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static long SumPositive(int[] data)
    {
        long sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            // Hot-spot: branch predictability matters.
            if (data[i] > 0) sum += data[i];
        }
        return sum;
    }

    // ------------------------------------------------------------
    // Branchless selections:
    // Sometimes you can use Math.* to express intent and let JIT choose.
    // Example: Abs / Max / Min often lower to efficient instructions.
    // ------------------------------------------------------------
    static void Conditionals_Branchless_Selections()
    {
        WriteLine("== Conditionals_Branchless_Selections ==");

        int x = -42;

        // Express intent: "absolute value"
        // JIT may use branchless instructions for int abs, depending on CPU/JIT.
        int abs1 = Math.Abs(x);

        // Manual branch version:
        int abs2 = x < 0 ? -x : x;

        // "Clamp" using Min/Max (clear intent, often good codegen).
        int clamped = Math.Clamp(x, 0, 100);

        WriteLine($"x={x}, Abs(Math)={abs1}, Abs(ternary)={abs2}, Clamp={clamped}");

        // When you really need strict branchless (rare), you can do bit tricks,
        // but it reduces readability and can be slower on some CPUs.
        // Keep it for “measured hot paths only”.
        int abs3 = Abs_BitTrick(x);
        WriteLine($"Abs(bit-trick)={abs3}");

        WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Abs_BitTrick(int v)
    {
        // Two's complement trick:
        // mask = v >> 31  (all 1s if negative, 0 if positive)
        // (v ^ mask) - mask
        int mask = v >> 31;
        return (v ^ mask) - mask;
    }
}

// ---------------------------------------------------------------
// Extra “scientist-level” notes for GitHub:
//
// 1) Speculation & side-channels
// - Branches can influence speculative execution.
// - Security-sensitive code sometimes uses “constant-time” techniques to avoid
//   data-dependent branches that leak timing information.
//
// 2) switch vs dictionary
// - switch is great for small, closed sets and allows JIT to emit jump tables.
// - dictionaries are great for extensible mappings (plugins, config-driven),
//   but cost hashing + memory indirections (cache effects).
//
// 3) Branch cost vs memory cost
// - Often, memory access dominates (cache misses can be 100+ cycles).
// - Before micro-optimizing branches, look at allocations, cache locality,
//   and algorithmic complexity.
//
// 4) If you want to go “full pro” in this repo:
// - Add BenchmarkDotNet benchmarks for each function here.
// - Use dotnet-trace / PerfView to see branch-misses and hot paths.
// - Use JIT disassembly (e.g., with BenchmarkDotNet diagnosers) to see
//   actual emitted instructions.
// ================================================================
