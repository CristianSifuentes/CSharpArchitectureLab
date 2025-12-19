// ================================================================
// ConversionsDeepDive.cs
// ================================================================
//
// Goal:
// - Keep your original examples (implicit numeric, explicit cast, Parse, Convert)
// - Add “hard-to-find” internals: IL/JIT lowering, CPU instructions (x64/ARM64),
//   rounding modes, checked/unchecked overflow, Parse/TryParse allocation,
//   culture, UTF-8 parsing, and performance heuristics.
// - Provide extra examples that are GitHub-ready and “senior-level”.
//
// This file is intentionally comment-heavy.
//
// ---------------------------------------------------------------
// 0) Mental model: “conversion” is one of four things
// ---------------------------------------------------------------
//
// In .NET, what looks like a single idea (“convert types”) is actually several:
// 1) Widening numeric conversion (safe, no data loss by range):
//    int -> long, int -> double, etc. (often implicit)
// 2) Narrowing numeric conversion (possible data loss):
//    double -> int, long -> int, etc. (requires explicit cast)
// 3) Text parsing / formatting (string <-> number) with culture + styles
// 4) Reinterpretation (same bits, different type view) — *not* a numeric conversion
//    (BitConverter, MemoryMarshal, Unsafe.As). Powerful but dangerous.
//
// World-class rule:
// - First decide: are you changing *value* (numeric conversion) or changing *representation*
//   (text, bytes, reinterpretation)? The performance + correctness story changes completely.
//
// ---------------------------------------------------------------
// 1) Compiler vs JIT: who decides what happens?
// ---------------------------------------------------------------
//
// Roslyn (C# compiler) emits IL. Conversions become IL opcodes like:
// - conv.i4 / conv.i8 / conv.r4 / conv.r8 (convert between numeric types)
// - conv.ovf.* (checked overflow conversions)
// - call to methods (int.Parse, Convert.ToInt32, etc.)
//
// RyuJIT then lowers IL to machine code for your CPU:
// - x64 examples (conceptual; exact codegen varies):
//   * int -> double: CVTSI2SD (convert signed int to scalar double)
//   * double -> int: CVTTSD2SI (truncate toward zero) or helper calls for edge cases
// - ARM64 examples (conceptual):
//   * int -> double: SCVTF
//   * double -> int: FCVTZS (round toward zero; truncate)
//
// Key nuance:
// - The *exact* instruction selection depends on: CPU, runtime version, tiered compilation,
//   PGO, and the surrounding code (inlining can change everything).
//
// ---------------------------------------------------------------
// 2) Processor-level intuition: rounding, flags, and exceptions
// ---------------------------------------------------------------
//
// Converting floating-point to integer is full of edge-cases:
// - NaN, +/-Infinity
// - values larger than int.MaxValue
// - negative values
// - rounding mode (banker’s rounding vs away-from-zero) — depends on *API*, not just CPU.
//
// CPUs offer instructions that commonly either:
// - truncate toward zero (typical cast semantics)
// - or follow the current FP rounding mode (less common for C# casts)
//
// .NET APIs specify behavior, so JIT may insert helper calls to guarantee it.
//
// ---------------------------------------------------------------
// 3) Cast vs Convert: they are NOT the same thing
// ---------------------------------------------------------------
//
// (int)doubleValue
// - Truncates toward zero (drops fractional part).
// - On overflow: behavior depends on checked/unchecked context.
//
// Convert.ToInt32(doubleValue)
// - Rounds to nearest 32-bit integer using banker's rounding (MidpointToEven).
// - Throws OverflowException if out of range.
//
// Example:
//   Convert.ToInt32(50.5) -> 50  (midpoint rounds to even)
//   Convert.ToInt32(51.5) -> 52
//   (int)50.8 -> 50 (truncate)
//
// ---------------------------------------------------------------
// 4) checked / unchecked: overflow policy is a semantic choice
// ---------------------------------------------------------------
//
// checked:
//   throws OverflowException on overflow in numeric conversions/operations
// unchecked:
//   wraps/truncates according to IL conversion rules (and two's complement behavior)
//
// In performance-sensitive code, avoid exceptions in hot paths.
// Prefer TryParse / range checks.
//
// ---------------------------------------------------------------
// 5) Parsing: int.Parse vs int.TryParse vs Span parsing
// ---------------------------------------------------------------
//
// int.Parse(string):
// - Throws on invalid input (exceptions are expensive if failures are expected)
// - Requires a string (allocation if you constructed it)
//
// int.TryParse(string):
// - No exception; returns bool. Best for robust input handling.
//
// ReadOnlySpan<char> parsing:
// - int.TryParse(ReadOnlySpan<char>, ...) avoids substring allocations.
// - Great for slicing big strings (CSV, logs, protocol frames).
//
// UTF-8 parsing:
// - System.Buffers.Text.Utf8Parser can parse from bytes without creating strings.
// - Great for high-throughput pipelines (network, files, Kestrel).
//
// ---------------------------------------------------------------
// 6) Culture: invisible correctness bug factory
// ---------------------------------------------------------------
//
// Parsing/formatting depends on CultureInfo unless you specify it.
// Always specify culture in:
// - protocols (API contracts, config, CSV you machine-parse, etc.)
// - logs that will be parsed later
//
// Use CultureInfo.InvariantCulture for stable formats.
//
// ---------------------------------------------------------------
// 7) Practical performance heuristics (top 1% stuff)
// ---------------------------------------------------------------
//
// ✔ Use implicit widening conversions freely (safe + usually cheap).
// ✔ For narrowing conversions, decide explicitly:
//    - truncate? round? saturate? throw?
// ✔ Avoid exceptions in hot paths: prefer TryParse + validation.
// ✔ Avoid allocations: use Span parsing for slices; Utf8Parser for bytes.
// ✔ Beware of Convert in tight loops: it may be slower and can throw.
// ✔ For extreme throughput: keep data numeric/bytes; delay text conversion.
//
// ---------------------------------------------------------------
// 8) Your original example + upgraded expert examples
// ---------------------------------------------------------------

using System;
using System.Buffers.Text;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using static System.Console;

partial class Program
{
    public static void ConversionsDeepDive()
    {
        Conversions_Basics();
        Conversions_CheckedUnchecked();
        Conversions_Rounding_Differences();
        Conversions_Parsing_Culture_And_Span();
        Conversions_Utf8_Parsing();
        Conversions_Perf_Demo();
        Conversions_Reinterpretation_Warning();
    }

    // ------------------------------------------------------------
    // Your original: basics (preserved, small cleanup)
    // ------------------------------------------------------------
    static void Conversions_Basics()
    {
        WriteLine("== Conversions_Basics ==");

        int number = 42;

        // Implicit widening: int -> double (no data loss for small integers).
        // JIT typically emits a single int->double conversion instruction.
        double decimalNumber = number;
        WriteLine($"implicit int->double: {decimalNumber}");

        double explicitDecimalNumber = 45.5;

        // Explicit narrowing: double -> int truncates toward zero.
        int integerNumber = (int)explicitDecimalNumber;
        WriteLine($"cast double->int (truncate): {integerNumber}");

        // Parse (throws on invalid input)
        string text = "123";
        int parsedNumber = int.Parse(text, CultureInfo.InvariantCulture);
        WriteLine($"int.Parse: {parsedNumber}");

        // Convert (rounds for double->int using MidpointToEven)
        double anotherDecimalNumber = 50.8;
        int convertedNumber = Convert.ToInt32(anotherDecimalNumber);
        WriteLine($"Convert.ToInt32 (round): {convertedNumber}");

        // Cast truncates
        int castedNumber = (int)anotherDecimalNumber;
        WriteLine($"(int) (truncate): {castedNumber}");

        WriteLine();
    }

    // ------------------------------------------------------------
    // checked vs unchecked: overflow policy
    // ------------------------------------------------------------
    static void Conversions_CheckedUnchecked()
    {
        WriteLine("== Conversions_CheckedUnchecked ==");

        long big = (long)int.MaxValue + 10;

        // unchecked: may wrap/truncate depending on conversion semantics
        int u = unchecked((int)big);
        WriteLine($"unchecked cast long->int: {u} (wrap/truncate)");

        try
        {
            // checked: throws on overflow
            int c = checked((int)big);
            WriteLine($"checked cast long->int: {c} (should not reach)");
        }
        catch (OverflowException)
        {
            WriteLine("checked cast long->int: OverflowException (expected)");
        }

        WriteLine();
    }

    // ------------------------------------------------------------
    // Rounding differences: cast vs Convert vs Math.Round
    // ------------------------------------------------------------
    static void Conversions_Rounding_Differences()
    {
        WriteLine("== Conversions_Rounding_Differences ==");

        double a = 50.5;
        double b = 51.5;

        WriteLine($"(int){a} => {(int)a} (truncate)");
        WriteLine($"Convert.ToInt32({a}) => {Convert.ToInt32(a)} (MidpointToEven)");
        WriteLine($"Convert.ToInt32({b}) => {Convert.ToInt32(b)} (MidpointToEven)");

        // Explicit choice: you control rounding policy
        WriteLine($"Math.Round({a}, AwayFromZero) => {Math.Round(a, MidpointRounding.AwayFromZero)}");
        WriteLine($"Math.Round({a}, ToEven)       => {Math.Round(a, MidpointRounding.ToEven)}");

        // For money: prefer decimal, but still pick rounding explicitly.
        decimal money = 10.005m;
        WriteLine($"decimal Round(10.005, 2, AwayFromZero) => {Math.Round(money, 2, MidpointRounding.AwayFromZero)}");

        WriteLine();
    }

    // ------------------------------------------------------------
    // Parsing: culture + NumberStyles + Span parsing (no substr allocations)
    // ------------------------------------------------------------
    static void Conversions_Parsing_Culture_And_Span()
    {
        WriteLine("== Conversions_Parsing_Culture_And_Span ==");

        // Culture example: decimal separators differ.
        string us = "1234.56";
        string es = "1234,56";

        // Always be explicit for protocols:
        double du = double.Parse(us, CultureInfo.InvariantCulture);
        WriteLine($"double.Parse Invariant (1234.56) => {du}");

        // Parsing with specific culture:
        var spanish = CultureInfo.GetCultureInfo("es-ES");
        double de = double.Parse(es, spanish);
        WriteLine($"double.Parse es-ES (1234,56) => {de}");

        // TryParse: robust, avoids exceptions for expected invalid data.
        string maybe = "12x3";
        if (!int.TryParse(maybe, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            WriteLine($"int.TryParse('{maybe}') => false (no exception)");

        // Span parsing: avoid substring allocations when slicing.
        // Imagine parsing a protocol line without creating many substrings.
        ReadOnlySpan<char> line = "ID=42;TEMP=30;";
        var idSlice = line.Slice(3, 2); // "42" without allocating
        if (int.TryParse(idSlice, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            WriteLine($"Span TryParse ID => {id}");

        WriteLine();
    }

    // ------------------------------------------------------------
    // UTF-8 parsing: bytes -> number without creating strings
    // ------------------------------------------------------------
    static void Conversions_Utf8_Parsing()
    {
        WriteLine("== Conversions_Utf8_Parsing ==");

        // This is how high-throughput pipelines avoid allocations:
        // parse numbers from byte buffers (network/files) directly.
        ReadOnlySpan<byte> utf8 = "12345"u8;

        if (Utf8Parser.TryParse(utf8, out int value, out int consumed) && consumed == utf8.Length)
            WriteLine($"Utf8Parser int => {value} (consumed {consumed} bytes)");

        ReadOnlySpan<byte> utf8Double = "1234.50"u8;
        if (Utf8Parser.TryParse(utf8Double, out double dv, out int c2) && c2 == utf8Double.Length)
            WriteLine($"Utf8Parser double => {dv}");

        WriteLine();
    }

    // ------------------------------------------------------------
    // Perf demo (Stopwatch): Parse vs TryParse, Convert vs cast
    // NOTE: Real perf should use BenchmarkDotNet. This is GitHub-friendly demo.
    // ------------------------------------------------------------
    static void Conversions_Perf_Demo()
    {
        WriteLine("== Conversions_Perf_Demo ==");

        const int N = 300_000;

        // 1) Cast vs Convert in a loop
        double x = 12345.678;
        var sw = Stopwatch.StartNew();
        long acc1 = 0;
        for (int i = 0; i < N; i++)
            acc1 += (int)x; // truncation
        sw.Stop();
        WriteLine($"cast double->int: acc={acc1}, ms={sw.ElapsedMilliseconds}");

        sw.Restart();
        long acc2 = 0;
        for (int i = 0; i < N; i++)
            acc2 += Convert.ToInt32(x); // rounding + range checks
        sw.Stop();
        WriteLine($"Convert.ToInt32: acc={acc2}, ms={sw.ElapsedMilliseconds}");

        // 2) Parse vs TryParse on valid data
        string s = "12345";
        sw.Restart();
        long p1 = 0;
        for (int i = 0; i < N; i++)
            p1 += int.Parse(s, CultureInfo.InvariantCulture);
        sw.Stop();
        WriteLine($"int.Parse(valid): acc={p1}, ms={sw.ElapsedMilliseconds}");

        sw.Restart();
        long p2 = 0;
        for (int i = 0; i < N; i++)
        {
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v);
            p2 += v;
        }
        sw.Stop();
        WriteLine($"int.TryParse(valid): acc={p2}, ms={sw.ElapsedMilliseconds}");

        // 3) TryParse on invalid data (where it shines)
        string bad = "12x3";
        sw.Restart();
        int failCount = 0;
        for (int i = 0; i < N; i++)
        {
            if (!int.TryParse(bad, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                failCount++;
        }
        sw.Stop();
        WriteLine($"int.TryParse(invalid): fails={failCount}, ms={sw.ElapsedMilliseconds}");

        // Avoid doing int.Parse on invalid data in hot paths (exceptions are expensive).

        WriteLine();
    }

    // ------------------------------------------------------------
    // Reinterpretation warning: NOT a numeric conversion.
    // This is about viewing the same bits as another type.
    // Use in low-level code only (interop, serialization, perf hacks).
    // ------------------------------------------------------------
    static void Conversions_Reinterpretation_Warning()
    {
        WriteLine("== Conversions_Reinterpretation_Warning ==");

        float f = 1.0f;
        int bits = BitConverter.SingleToInt32Bits(f);
        WriteLine($"float {f} bits: 0x{bits:X8}");

        // Reverse: interpret int bits as float
        float f2 = BitConverter.Int32BitsToSingle(bits);
        WriteLine($"bits back to float: {f2}");

        // MemoryMarshal example (span-based, avoids allocations)
        int value = 0x3F800000; // IEEE-754 bits for 1.0f
        Span<int> ints = stackalloc int[1] { value };
        Span<float> floats = MemoryMarshal.Cast<int, float>(ints);
        WriteLine($"MemoryMarshal.Cast<int,float>: {floats[0]}");

        WriteLine();
    }
}

// ---------------------------------------------------------------
// Extra “scientist-level” notes for GitHub
// ---------------------------------------------------------------
//
// 1) Why exceptions are expensive in parsing
// - Exceptions are optimized for “rare, exceptional” conditions.
// - Throwing captures stack info and disrupts hot-path optimizations.
// - If invalid input is expected, use TryParse.
//
// 2) Saturating conversions (clamp) as a deliberate policy
// - Sometimes you want: out-of-range -> min/max instead of throw/wrap.
// - Example pattern:
//     int sat = (int)Math.Clamp(x, int.MinValue, int.MaxValue);
//
// 3) JIT + tiered compilation + PGO
// - Your first run might be Tier 0 code (fast-to-JIT, less optimized).
// - Hot paths can be re-JITed to Tier 1; PGO can change inlining/branches.
// - For real measurement: warmup + BenchmarkDotNet.
//
// ================================================================
