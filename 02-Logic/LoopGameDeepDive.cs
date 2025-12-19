// ================================================================
// LoopGameDeepDive.cs
// ================================================================
//
// Goal
// - Keep your original "LoopGame" idea (while(true) + ReadKey + ESC to exit).
// - Add “hard-to-find” internals:
//   * What var really means (compile-time typing, NOT dynamic).
//   * What while(true) becomes in IL/JIT and CPU terms (loop back-edge branch).
//   * What happens inside Console.ReadKey / Console.WriteLine (OS calls, buffering).
//   * JIT, tiered compilation, PGO, and why console IO dwarfs micro-optimizations.
//   * How to write “world-class” loop code: predictability, allocation control,
//     structured exit, cancellation, and clean output.
// - Provide extra examples you can push to GitHub.
//
// Notes
// - Console apps are IO-bound. The biggest performance cost is typically
//   the OS boundary (syscalls) and terminal rendering, not the loop itself.
// - Still, mastering the mental model makes you dangerous (in a good way).
//
// ---------------------------------------------------------------
// 0) Mental model: your loop is a tiny state machine
// ---------------------------------------------------------------
//
// Your original logic:
//
//   counter = 0
//   print instructions
//   while (true):
//       key = ReadKey(true)
//       if key == ESC: print summary; break;
//       counter++
//
// This is already a classic "event loop":
// - Wait for an input event (key press)
// - Update state (counter)
// - Optionally emit output
// - Exit on a signal
//
// Event loops are the backbone of:
// - games (main loop)
// - UI message pumps
// - network servers (reactor pattern)
// - background workers
//
// ---------------------------------------------------------------
// 1) What "var" REALLY is (compiler-level truth)
// ---------------------------------------------------------------
//
// In C#, `var` is *not* dynamic typing.
// It is compile-time type inference.
//
//   var key = ReadKey(true).Key;
//
// The compiler (Roslyn) infers the type of `key` from the RHS:
//
//   ConsoleKey key = Console.ReadKey(true).Key;
//
// The generated IL contains the concrete type (ConsoleKey).
// There is zero runtime overhead for `var`.
//
// When people say “var is slower”, that’s a myth.
// The only risk is readability if the inferred type is not obvious.
//
// ---------------------------------------------------------------
// 2) while(true): CPU + JIT view
// ---------------------------------------------------------------
//
// High-level:
//
//   while (true) { body }
//
// IL-level: a loop is branching to a label:
//
//   br.s LOOP_START
//   LOOP_BODY:
//     ...
//   LOOP_START:
//     brtrue.s LOOP_BODY   // or unconditional back-edge
//
// JIT-level: on x64/ARM64 it becomes a compare/test + conditional jump,
// or an unconditional jump for `while(true)` back to the header.
//
// CPU-level: the "back-edge branch" (jump back to loop start)
// is one of the most predictable branch patterns for modern predictors.
// The CPU sees: taken, taken, taken, ... taken, then not taken once (when you break).
//
// In practice: the loop control flow is cheap.
// The expensive part is ReadKey + WriteLine (OS/terminal IO).
//
// ---------------------------------------------------------------
// 3) Console.ReadKey + Console.WriteLine: where time REALLY goes
// ---------------------------------------------------------------
//
// Console.ReadKey(intercept: true)
// - Blocks the thread until a key event arrives.
// - Under the hood, Console is implemented via platform-specific drivers.
//   On Windows this goes through Win32 console APIs.
//   On Linux/macOS it typically interacts with the terminal/TTY subsystem.
// - It's a boundary to the OS and can involve system calls, buffering, and
//   translation of key events.
//
// Console.WriteLine(...)
// - Writes to Console.Out (a TextWriter).
// - TextWriter buffers characters and eventually flushes to the OS stream.
// - Formatting (interpolation) can allocate strings.
// - Printing on every iteration can dominate your runtime.
//
// Takeaway: in a console loop, “performance” usually means:
// - reduce output frequency
// - reduce allocations during formatting
// - avoid flushing too often
//
// ---------------------------------------------------------------
// 4) A scientist’s view: caches, branches, and syscalls
// ---------------------------------------------------------------
//
// If your loop body is purely CPU work, then you care about:
// - branch prediction
// - cache locality
// - bounds-check elimination
// - vectorization
//
// In a keyboard event loop, the loop is mostly waiting on IO.
// The CPU is parked/sleeping most of the time.
// Your “speed” is bounded by human keypress rate and terminal speed.
//
// BUT the knowledge transfers: the same loop discipline applies when the
// IO source is a socket and the loop runs 10 million iterations per second.
//
// ---------------------------------------------------------------
// 5) Best-practice exit strategies: break vs return vs cancellation
// ---------------------------------------------------------------
//
// break:
// - Exits the nearest loop only.
// - Useful for "single loop main loop" patterns.
//
// return:
// - Exits the current method immediately (and skips the rest of the method).
// - Still runs finally blocks (important!). See section 8.
//
// cancellation token (advanced):
// - Lets you request shutdown from outside the loop.
// - Standard in production services.
//
// We'll show all three patterns below.
//
// ---------------------------------------------------------------
// 6) JIT/Tiered compilation/PGO note (hard-to-find but real)
// ---------------------------------------------------------------
//
// .NET uses Tiered Compilation:
// - Tier0: fast JIT to get your app running quickly.
// - Tier1: after the runtime detects “hot” methods, it may re-JIT with
//   more optimizations.
// - With PGO (Profile Guided Optimization), runtime behavior can influence
//   devirtualization, inlining, and block ordering.
//
// For console loops, this isn't dramatic, but in server hot paths it matters.
// The meta-lesson: always benchmark after warmup.
//
// ---------------------------------------------------------------
// 7) Upgraded loop game: readable, low-allocation, extensible
// ---------------------------------------------------------------

#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using static System.Console;

partial class Program
{
    // Entry point for quick testing (optional).
    // If your project already has a Main, call LoopGame_DeepDive() from there.
    public static void LoopGame_DeepDive()
    {
        LoopGame_Baseline();
        WriteLine();
        LoopGame_WithStats_AndThrottledOutput();
        WriteLine();
        LoopGame_WithCancellationDemo();
    }

    // ------------------------------------------------------------
    // A) Your original loop (cleaned, still the same behavior)
    // ------------------------------------------------------------
    static void LoopGame_Baseline()
    {
        int counter = 0;

        WriteLine("🎮 Press any key to increment the counter");
        WriteLine("🔴 Press ESC to exit.\n");

        while (true)
        {
            // var here is inferred as ConsoleKey
            var key = ReadKey(intercept: true).Key;

            if (key == ConsoleKey.Escape)
            {
                WriteLine($"You pressed {counter} keys before exiting");
                WriteLine("🟢 Program terminated");
                break;
            }

            counter++;
        }
    }

    // ------------------------------------------------------------
    // B) Senior version: stats, time measurement, reduced IO
    // ------------------------------------------------------------
    static void LoopGame_WithStats_AndThrottledOutput()
    {
        int counter = 0;

        // Stopwatch uses a high-resolution timer (platform dependent).
        // For "scientist mode" measurements, this is better than DateTime.
        var sw = Stopwatch.StartNew();

        WriteLine("🎮 LoopGame v2 (stats)");
        WriteLine(" - Any key increments");
        WriteLine(" - ESC exits");
        WriteLine(" - Prints status every 10 presses (reduces terminal IO)\n");

        while (true)
        {
            var keyInfo = ReadKey(intercept: true);  // ConsoleKeyInfo (struct)
            var key = keyInfo.Key;                  // ConsoleKey (enum)

            if (key == ConsoleKey.Escape)
            {
                sw.Stop();
                WriteLine();
                WriteLine($"✅ Total keys: {counter}");
                WriteLine($"⏱️  Elapsed:   {sw.Elapsed}");
                WriteLine("🟢 Program terminated");
                break;
            }

            counter++;

            // Throttle output: printing per key is expensive and noisy.
            if ((counter % 10) == 0)
            {
                // Note: string interpolation creates a string.
                // That's fine here, but in extreme hot paths you'd avoid it.
                WriteLine($"Keys pressed: {counter}");
            }
        }
    }

    // ------------------------------------------------------------
    // C) Production-style: cancellation + structured exit
    // ------------------------------------------------------------
    static void LoopGame_WithCancellationDemo()
    {
        int counter = 0;
        WriteLine("🎮 LoopGame v3 (cancellation-aware)");
        WriteLine(" - Any key increments");
        WriteLine(" - ESC exits");
        WriteLine(" - Ctrl+C also exits gracefully\n");

        // In console apps, Ctrl+C triggers a CancelKeyPress event.
        // We'll set a flag; in services you'd use CancellationTokenSource.
        bool cancelRequested = false;

        ConsoleCancelEventHandler handler = (_, e) =>
        {
            // Prevent immediate termination so we can print a summary.
            e.Cancel = true;
            cancelRequested = true;
        };

        Console.CancelKeyPress += handler;

        try
        {
            while (!cancelRequested)
            {
                // If there's no key available, avoid blocking so we can notice Ctrl+C quickly.
                // This is a "polling loop" variant.
                if (!KeyAvailable)
                {
                    // Sleep a tiny amount to avoid spinning at 100% CPU.
                    // In a real-time loop you'd use a frame budget (e.g., 16ms).
                    Thread.Sleep(10);
                    continue;
                }

                var key = ReadKey(intercept: true).Key;

                if (key == ConsoleKey.Escape)
                    break;

                counter++;
            }
        }
        finally
        {
            // ✅ finally ALWAYS runs on break/return/exception
            // (unless the process is killed externally).
            Console.CancelKeyPress -= handler;

            WriteLine();
            WriteLine($"🧾 Summary: keys pressed = {counter}");
            WriteLine("🟢 Program terminated (gracefully)");
        }
    }
}

// ---------------------------------------------------------------
// 8) The "finally" guarantee (compiler + runtime concept)
// ---------------------------------------------------------------
//
// A common misconception: “break/return skips cleanup.”
// Not in .NET.
//
// If you have:
//
//   try { while(true) { ... break; } }
//   finally { Cleanup(); }
//
// The compiler emits IL that ensures finally runs on all normal exits.
//
// This is the foundation of `using` (which expands to try/finally).
//
// In real systems, this matters for:
// - releasing file handles
// - flushing buffers
// - returning pooled objects
// - stopping timers / event handlers
//
// ---------------------------------------------------------------
// 9) Micro-optimizations that DO matter (sometimes)
// ---------------------------------------------------------------
//
// In CPU-bound loops (not this ReadKey loop), these are real wins:
//
// - Avoid allocations per iteration (no new strings, no LINQ).
// - Avoid interface dispatch in hot loops.
// - Keep indexing patterns simple to help bounds-check elimination.
// - Use Span<T> / ReadOnlySpan<T> for slicing without allocations.
// - Use AggressiveInlining/AggressiveOptimization only where measured.
//
// For Console loops, the best optimizations are usually:
// - print less
// - buffer output and write in chunks
// - avoid calling WriteLine inside the hot loop unless necessary
//
// ---------------------------------------------------------------
// 10) Next-level: if you want to go "top programmer" in this repo
// ---------------------------------------------------------------
//
// Add a benchmark project:
// - BenchmarkDotNet to compare different loop shapes.
// Add profiling notes:
// - dotnet-trace / PerfView for real CPU/GC insight.
// Add a disassembly section:
// - BenchmarkDotNet DisassemblyDiagnoser to see JIT assembly.
// Add an “event loop” evolution:
// - Replace Console.ReadKey with channels/async streams,
//   then compare latency and throughput.
//
// ================================================================
