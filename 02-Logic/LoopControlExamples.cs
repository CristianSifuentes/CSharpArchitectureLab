// ================================================================
// LoopControlDeepDive.cs
// ================================================================
//
// Goal:
// - Keep your original examples (break, continue, return, infinite loops)
// - Add “hard-to-find” internals:
//   * How break/continue/return map to IL and machine-code control flow
//   * CPU pipeline + branch prediction implications
//   * JIT transformations: loop canonicalization, range-check hoisting,
//     tiered compilation + PGO effects
//   * finally/using/foreach interactions (disposal + control-flow)
//   * nested-loop strategies, labeled breaks (C# lacks labels, but patterns exist)
//   * performance heuristics for “top 1%” loop control in real systems
//
// This file is intentionally comment-heavy (GitHub-ready).
//
// ---------------------------------------------------------------
// 0) Mental model: loop-control statements are “edge control”
// ---------------------------------------------------------------
//
// In high-level C#:
//
//   break;      // exits the nearest loop or switch
//   continue;   // skips remainder of loop body and jumps to the next iteration
//   return;     // exits the current method (and therefore exits any loop)
//   throw;      // exits via exception (unwinding stack)
//   goto;       // exists, but usually discouraged (except rare patterns)
//
// At the lowest level, these are not “special” CPU features.
// They become *jumps* (branches) in IL and then *branches* in machine code.
//
// Think in terms of control-flow edges:
//
//   loop head -> body -> back-edge (iteration) -> loop head
//               |         ^
//               |         |
//           break edge  continue edge
//               |
//             loop exit
//
// ---------------------------------------------------------------
// 1) CPU-level intuition: why “break/continue” can be fast or costly
// ---------------------------------------------------------------
//
// ✅ Branch prediction
// - Modern CPUs try to predict which branch will be taken.
// - A loop back-edge is usually predictable (“taken” many times, then not taken).
// - A break condition can be predictable (e.g., i==5 always once),
//   or unpredictable (dependent on random input).
//
// Pipeline cost:
// - Correctly predicted branches are relatively cheap.
// - Mispredicted branches can flush the pipeline (often ~10–20+ cycles on x64,
//   but varies by CPU).
//
// Key insight:
// - The cost is not “break” itself; it’s how predictable the branch is,
//   and how expensive the work is around it (plus memory/cache behavior).
//
// ---------------------------------------------------------------
// 2) Compiler vs JIT: what happens where?
// ---------------------------------------------------------------
//
// Roslyn (C# compiler):
// - Emits IL with explicit branch instructions:
//   brtrue/brfalse, br, leave (for exception handling regions), etc.
// - Lowers for/while/do-while into a canonical IL shape.
//
// RyuJIT (.NET JIT):
// - Converts IL -> machine code for current CPU.
// - Applies loop optimizations:
//   * Hoists invariants (Loop-Invariant Code Motion)
//   * Eliminates bounds checks when it can prove safety
//   * Range-check hoisting (“guard once, then fast loop”)
//   * Sometimes unrolls small loops
//   * Uses Tiered Compilation: fast “Tier 0” first, optimized “Tier 1” later
//   * Uses PGO (Profile Guided Optimization) in newer runtimes:
//     real runtime behavior can affect codegen (branch probabilities, devirtualization)
//
// Practical consequence:
// - The same C# loop may produce *different machine code* depending on runtime,
//   warmup, and actual input patterns.
//
// ---------------------------------------------------------------
// 3) Subtle but important: break/continue/return + finally/using
// ---------------------------------------------------------------
//
// In C#, `using` expands to a try/finally that disposes resources.
// That means:
//
//   break/continue/return inside a using block WILL still run Dispose().
//   This can add overhead (often tiny, but important in hot loops).
//
// Example mental expansion:
//
//   using var r = ...;
//   for (...) {
//       if (...) break;
//   }
//
// becomes roughly:
//
//   var r = ...;
//   try {
//       for (...) {
//           if (...) break;
//       }
//   }
//   finally { r.Dispose(); }
//
// For foreach:
// - `foreach` may involve an enumerator that must be disposed.
// - For arrays: no disposal, often lowered to index loop.
// - For List<T>: struct enumerator + usually no allocation; still has a dispose pattern.
// - For IEnumerable<T>: can allocate + interface calls; disposal matters.
//
// ---------------------------------------------------------------
// 4) “World-class” heuristics (things top engineers actually do)
// ---------------------------------------------------------------
//
// ✔ Prefer early-exit (break/return) when it improves clarity.
// ✔ In hot loops, reduce unpredictable branches:
//   - Partition/sort data so “rare” cases are clustered.
//   - Move rarely-taken branches out of the inner loop when possible.
// ✔ Avoid allocating inside loops. “Break” doesn’t save you from GC overhead.
// ✔ Beware hidden work: exceptions, interface dispatch, iterator blocks, using/finally.
// ✔ Measure with BenchmarkDotNet if performance matters.
// ✔ Optimize data access (cache locality) before micro-optimizing branch control.
//
// ---------------------------------------------------------------
// 5) Your original examples, upgraded + expert demos
// ---------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using static System.Console;

partial class Program
{
    public static void LoopControlDeepDive()
    {
        LoopControlExamples_Basics();
        LoopControl_NestedLoops_EarlyExitPatterns();
        LoopControl_Using_Finally_Interaction();
        LoopControl_PredictabilityDemo();
        LoopControl_SafeInfiniteLoops();
    }

    // ------------------------------------------------------------
    // Your original: break, continue, return, infinite loop patterns.
    // (Preserved, with output enabled and a few safety tweaks.)
    // ------------------------------------------------------------
    static void LoopControlExamples_Basics()
    {
        WriteLine("== LoopControlExamples_Basics ==");

        // break: exits nearest loop immediately
        for (int i = 0; i < 10; i++)
        {
            if (i == 5)
            {
                WriteLine("break at i=5");
                break;
            }
            WriteLine($"i={i}");
        }

        WriteLine("--");

        // continue: skips remainder of body, jumps to next iteration
        for (int i = 0; i < 10; i++)
        {
            if (i == 5 || i == 7)
            {
                WriteLine($"continue at i={i}");
                continue;
            }
            WriteLine($"i={i}");
        }

        WriteLine("--");

        // return: exits the method (strongest “early exit”)
        // We'll demonstrate with a helper method so we don't exit the whole demo.
        WriteLine($"Return demo result: {ReturnOnValue(3)}");
        WriteLine($"Return demo result: {ReturnOnValue(99)}");

        WriteLine("--");

        // Infinite loops: two common forms
        // 1) while(true) { ... }
        // 2) for(;;) { ... }
        //
        // The JIT typically generates very similar code for both.
        // We'll keep it safe by breaking quickly.
        for (; ; )
        {
            WriteLine("This would run forever unless we break.");
            break;
        }

        WriteLine();
    }

    static string ReturnOnValue(int trigger)
    {
        for (int i = 0; i < 10; i++)
        {
            if (i == trigger)
                return $"Returned early at i={i}";
        }
        return "Completed loop without return";
    }

    // ------------------------------------------------------------
    // Nested loops: how to “break out of two loops” (C# patterns)
    // ------------------------------------------------------------
    //
    // C# has no labeled break like Java.
    // Typical patterns used in production:
    //  A) Use a boolean flag
    //  B) Extract into a method and return
    //  C) Use exceptions (rare; usually not worth it)
    //  D) Use goto (rare; sometimes used for clarity in low-level parsers)
    //
    // In hot paths, method extraction + return can be both clean and fast,
    // because the JIT can inline small helpers.
    // ------------------------------------------------------------
    static void LoopControl_NestedLoops_EarlyExitPatterns()
    {
        WriteLine("== LoopControl_NestedLoops_EarlyExitPatterns ==");

        // Pattern A: flag
        bool found = false;
        int fx = -1, fy = -1;

        for (int y = 0; y < 5 && !found; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                if ((x * 10 + y) == 32)
                {
                    found = true;
                    fx = x; fy = y;
                    break; // breaks inner loop
                }
            }
        }
        WriteLine($"Flag pattern found={found} at (x={fx}, y={fy})");

        // Pattern B: return from helper (often the cleanest)
        var res = FindValueInGrid(target: 32);
        WriteLine($"Return pattern found={res.found} at (x={res.x}, y={res.y})");

        // Pattern D: goto (use sparingly; can be clear in parsers/state machines)
        int gx = -1, gy = -1;
        bool gfound = false;

        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                if ((x * 10 + y) == 32)
                {
                    gx = x; gy = y; gfound = true;
                    goto Found;
                }
            }
        }

    Found:
        WriteLine($"goto pattern found={gfound} at (x={gx}, y={gy})");

        WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static (bool found, int x, int y) FindValueInGrid(int target)
    {
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                if ((x * 10 + y) == target)
                    return (true, x, y);

        return (false, -1, -1);
    }

    // ------------------------------------------------------------
    // using/finally interaction: loop control still runs Dispose()
    // ------------------------------------------------------------
    static void LoopControl_Using_Finally_Interaction()
    {
        WriteLine("== LoopControl_Using_Finally_Interaction ==");

        // Even if we break/continue/return, Dispose() will still run.
        // That’s correctness. In hot loops, it can be overhead.
        using var t = new TraceResource("R1");

        for (int i = 0; i < 3; i++)
        {
            if (i == 1)
            {
                WriteLine("break inside using");
                break;
            }
            WriteLine($"work i={i}");
        }

        WriteLine("Exited loop; Dispose will run at end of scope.");
        WriteLine();
    }

    private sealed class TraceResource : IDisposable
    {
        private readonly string _name;
        public TraceResource(string name)
        {
            _name = name;
            WriteLine($"[{_name}] acquired");
        }

        public void Dispose()
        {
            WriteLine($"[{_name}] disposed (finally path)");
        }
    }

    // ------------------------------------------------------------
    // Predictability demo: same loop structure, different branch patterns
    // ------------------------------------------------------------
    //
    // - A predictable early exit tends to be cheap
    // - An unpredictable early exit can be costly due to mispredictions
    //
    // ⚠️ Stopwatch is a demo tool. For real conclusions, use BenchmarkDotNet.
    // ------------------------------------------------------------
    static void LoopControl_PredictabilityDemo()
    {
        WriteLine("== LoopControl_PredictabilityDemo ==");

        const int N = 5_000_00; // moderate for a demo
        var predictable = new int[N];
        var unpredictable = new int[N];

        // Predictable: exit near the end consistently.
        for (int i = 0; i < N; i++) predictable[i] = i;

        // Unpredictable: exit position depends on pseudo-random sentinel.
        var rng = new Random(123);
        int sentinel = rng.Next(0, N);
        for (int i = 0; i < N; i++) unpredictable[i] = i;
        unpredictable[sentinel] = -1; // sentinel forces break at unknown point

        var sw = Stopwatch.StartNew();
        int p = ScanUntilNegative(predictable); // never breaks early (predictable)
        sw.Stop();
        WriteLine($"Predictable scan idx={p}, ms={sw.ElapsedMilliseconds}");

        sw.Restart();
        int u = ScanUntilNegative(unpredictable); // breaks at random point
        sw.Stop();
        WriteLine($"Unpredictable scan idx={u}, ms={sw.ElapsedMilliseconds}");

        WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static int ScanUntilNegative(int[] data)
    {
        // A typical pattern in parsers/decoders:
        // scan until a sentinel, then break.
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] < 0)
                return i;   // return is an “early exit” (like break + return value)
        }
        return -1;
    }

    // ------------------------------------------------------------
    // Safe infinite loops: production patterns
    // ------------------------------------------------------------
    //
    // In production you almost never want truly infinite loops.
    // You want “run until canceled” loops:
    // - cancellation token
    // - time budget
    // - bounded iterations
    //
    // This is essential in services, workers, background jobs.
    // ------------------------------------------------------------
    static void LoopControl_SafeInfiniteLoops()
    {
        WriteLine("== LoopControl_SafeInfiniteLoops ==");

        // Time-budget loop: runs until time budget is exceeded.
        var budget = TimeSpan.FromMilliseconds(50);
        var sw = Stopwatch.StartNew();
        int iters = 0;

        while (true)
        {
            iters++;
            // simulated small work
            if (iters % 10_000 == 0 && sw.Elapsed > budget)
                break;
        }

        WriteLine($"Time-budget loop iterations={iters}, elapsed={sw.ElapsedMilliseconds}ms");

        // for(;;) form is equivalent; choose the style your team reads fastest.
        int iters2 = 0;
        sw.Restart();

        for (; ; )
        {
            iters2++;
            if (iters2 % 10_000 == 0 && sw.Elapsed > budget)
                break;
        }

        WriteLine($"Time-budget for(;;) iterations={iters2}, elapsed={sw.ElapsedMilliseconds}ms");
        WriteLine();
    }
}

// ---------------------------------------------------------------
// Extra “scientist-level” notes for GitHub (deep internals)
// ---------------------------------------------------------------
//
// 1) IL patterns you’ll see (conceptual):
// - break/continue are branches (br/brtrue/brfalse) to different labels.
// - return is `ret` (but inside try/finally regions you may see `leave` first).
// - In try/finally, the compiler uses `leave` to ensure finally executes.
//
// 2) Exception regions change control-flow lowering:
// - A `break` inside a try block may become a `leave` instruction in IL.
// - That’s because IL must guarantee finally blocks execute on exit.
// - JIT then maps it to machine code that runs finally logic.
//
// 3) foreach + control flow:
// - If enumerator is disposable, the compiler emits try/finally around the loop.
// - break/return/throw all go through that finally path to Dispose().
// - Arrays are special-cased to avoid enumerators/disposal.
//
// 4) PGO and “hot vs cold” loop paths:
// - With PGO, the runtime can learn which branch is more likely,
//   and JIT may lay out code so the hot path is fall-through,
//   and the cold path is out-of-line (better I-cache behavior).
//
// 5) Code layout matters:
// - In tight loops, instruction-cache and branch-target-buffer effects matter.
// - Keeping the hot path contiguous can be more impactful than micro tricks.
//
// 6) If you want to go full-pro in this repo:
// - Add BenchmarkDotNet tests for each pattern (break vs return vs flags).
// - Use DisassemblyDiagnoser to inspect the JIT output.
// - Use dotnet-trace/PerfView to view hotspots and branch behavior.
// ================================================================
