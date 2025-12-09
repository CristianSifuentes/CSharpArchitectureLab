// File: StringTypeDeepDive.cs
// Author: Cristian Sifuentes  + ChatGPT
// Goal: Explain C# STRING TYPE like a systems / compiler / performance engineer.
//
// HIGH-LEVEL MENTAL MODEL
// -----------------------
// When you write:
//
//     string name = "Cristian";
//
// A LOT happens under the hood:
//
// 1. The C# compiler (Roslyn) sees `string` as `System.String`.
//    - It emits IL that manipulates "object references" to String instances.
//    - String literals like "Cristian" are stored in the assembly metadata and usually INTERNED.
//
// 2. At runtime, the CLR creates / reuses a heap object whose layout is approximately:
//
//      [Object header][Method table pointer][Int32 Length][UTF-16 chars...]
//
//    - Length is the number of UTF-16 code units, not "characters" in the human sense.
//    - Chars are 16-bit values (System.Char) representing UTF-16 units, NOT bytes.
//
// 3. The JIT compiles IL to machine code:
//    - References live in CPU registers or on the stack (like any other reference type).
//    - The actual text lives on the managed heap, in contiguous 16-bit elements.
//
// 4. The GC (garbage collector) moves and compacts strings:
//    - Your variables hold references; the GC may MOVE the underlying objects.
//    - This is why raw pointers to string data are dangerous unless pinned.
//
// 5. Strings are IMMUTABLE:
//    - Every logical "change" (concatenation, Replace, ToUpper, etc.) creates a NEW string.
//    - This has huge implications for performance, allocation rate, and GC pressure.
//
// This file is written as if you were preparing to be a **top 1% .NET engineer**,
// connecting high-level C# syntax with the underlying runtime and hardware behavior.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

partial class Program
{
    // ---------------------------------------------------------------------
    // PUBLIC ENTRY FOR THIS MODULE
    // ---------------------------------------------------------------------
    // Call ShowStringType() from your main Program (another partial) to run
    // all demos in this file.
    static void ShowStringType()
    {
        // Your original beginner-style snippet, still valid and useful:
        string name = "Cristian";
        string message = "Hi " + name;
        string interpolatedMessage = $"Hi {name}";
        Console.WriteLine(message);
        Console.WriteLine(interpolatedMessage);
        Console.WriteLine($"Your name has {name.Length} letters (UTF-16 units)");
        Console.WriteLine($"Your name in uppercase is {name.ToUpper()}");
        int number = 13;
        Console.WriteLine(number);
        bool isString = true;
        Console.WriteLine(isString);

        // Now we call advanced demos that explain what REALLY happens:
        Console.WriteLine();
        Console.WriteLine("=== StringType Deep Dive ===");

        BasicStringIntro();
        StringIdentityAndInterning();
        ConcatenationPatternsAndCosts();
        ImmutabilityAndCopyCost();
        ComparisonCultureAndOrdinal();
        UnicodeAndLengthPitfalls();
        EncodingAndBytes();
        SpanBasedStringLikeOps();
        StringBuilderAndPoolingHints();
        MicroBenchmarkShape();
    }

    // ---------------------------------------------------------------------
    // 1. BASIC STRING INTRO – attach low-level meaning to your original idea
    // ---------------------------------------------------------------------
    static void BasicStringIntro()
    {
        string name = "Cristian";               // literal, interned
        string greetConcat = "Hi " + name; // usually string.Concat("Hi ", name)
        string greetInterp = $"Hi {name}"; // also string.Concat for simple cases

        Console.WriteLine("[Basic] name          = " + name);
        Console.WriteLine("[Basic] greetConcat   = " + greetConcat);
        Console.WriteLine("[Basic] greetInterp   = " + greetInterp);
        Console.WriteLine("[Basic] Length(name)  = " + name.Length);
        Console.WriteLine("[Basic] Upper(name)   = " + name.ToUpper());

        // IL VIEW (conceptual):
        //
        //   .locals init (
        //       [0] string name,
        //       [1] string greetConcat,
        //       [2] string greetInterp)
        //
        //   ldstr      "Cristian"           // load interned literal
        //   stloc.0                     // name
        //   ldstr      "Hi "          // literal
        //   ldloc.0                     // name
        //   call       string [System.Runtime]System.String::Concat(string, string)
        //   stloc.1                     // greetConcat
        //
        //   ldstr      "Hi "
        //   ldloc.0
        //   call       string [System.Runtime]System.String::Concat(string, string)
        //   stloc.2                     // greetInterp (for simple case)
        //
        // RUNTIME VIEW:
        //   - name, greetConcat, greetInterp are *references* (pointers) that live
        //     in registers or on the stack.
        //   - The actual text ("Cristian", "Hi Cristian") lives on the managed heap.
    }

    // ---------------------------------------------------------------------
    // 2. STRING IDENTITY & INTERNING – why two equal strings can be one object
    // ---------------------------------------------------------------------
    static void StringIdentityAndInterning()
    {
        string a = "Hi";      // literal from metadata → interned
        string b = "Hi";      // same literal → same interned instance
        string c = string.Copy(a); // forces a NEW string with same content

        Console.WriteLine();
        Console.WriteLine("=== Interning & Identity ===");
        Console.WriteLine($"[Intern] a == b (value)           : {a == b}");  // true
        Console.WriteLine($"[Intern] ReferenceEquals(a, b)    : {ReferenceEquals(a, b)}"); // usually true
        Console.WriteLine($"[Intern] a == c (value)           : {a == c}");  // true
        Console.WriteLine($"[Intern] ReferenceEquals(a, c)    : {ReferenceEquals(a, c)}"); // false
        Console.WriteLine($"[Intern] IsInterned(a) != null    : {string.IsNullOrEmpty(string.IsInterned(a)) == false}");

        // ABSTRACT VIEW:
        //   - The CLR maintains an "intern pool" of strings.
        //   - All string literals in an assembly are typically interned.
        //   - When you compare literal "Hi" references, they usually point
        //     to the exact same heap object.
        //
        // WHY YOU CARE AS A TOP ENGINEER:
        //   - ReferenceEquals(x, y) is O(1) pointer comparison.
        //   - a == b for strings is *value* comparison: it walks over char data.
        //   - For frequently repeated critical keys (e.g., protocol tokens),
        //     interning can reduce memory usage and speed up comparisons,
        //     but over-interning can increase GC pressure and pin memory.
    }

    // ---------------------------------------------------------------------
    // 3. CONCATENATION PATTERNS – +, interpolation, String.Concat, StringBuilder
    // ---------------------------------------------------------------------
    static void ConcatenationPatternsAndCosts()
    {
        Console.WriteLine();
        Console.WriteLine("=== Concatenation Patterns ===");

        string name = "Cristian";

        // 1) + operator
        string hello1 = "Hi " + name;

        // 2) interpolation
        string hello2 = $"Hi {name}";

        // 3) string.Concat
        string hello3 = string.Concat("Hi ", name);

        Console.WriteLine("[Concat] hello1 = " + hello1);
        Console.WriteLine("[Concat] hello2 = " + hello2);
        Console.WriteLine("[Concat] hello3 = " + hello3);

        // Under simple conditions the compiler normalizes (1) and (2) to (3).
        // For many pieces, it might emit:
        //
        //   string result = string.Concat(new [] { part1, part2, part3, ... });
        //
        // EXPENSIVE PATTERN (NAIVE LOOP):
        string resultBad = "";
        for (int i = 0; i < 5; i++)
        {
            // Allocates a NEW string on each iteration:
            // resultBad = string.Concat(resultBad, i.ToString());
            resultBad += i;
        }
        Console.WriteLine("[Concat] resultBad (naive loop) = " + resultBad);

        // BETTER PATTERN: use StringBuilder for repeated concatenations.
        var sb = new StringBuilder();
        for (int i = 0; i < 5; i++)
        {
            sb.Append(i);
        }
        string resultGood = sb.ToString();
        Console.WriteLine("[Concat] resultGood (StringBuilder) = " + resultGood);

        // HIGH-LEVEL RULE:
        //   - Few pieces? `+` or interpolation is fine – compiler is smart.
        //   - Many pieces or loops? Prefer StringBuilder or string.Create/Span<char>.
        //
        // MICRO-FACT:
        //   - Every new string = new heap allocation (length * 2 bytes + header).
        //   - High allocation rate → more work for GC → potential pauses.
    }

    // ---------------------------------------------------------------------
    // 4. IMMUTABILITY & COPY COST – every change creates a new string
    // ---------------------------------------------------------------------
    static void ImmutabilityAndCopyCost()
    {
        Console.WriteLine();
        Console.WriteLine("=== Immutability & Copy Cost ===");

        string original = "csharp";
        string upper = original.ToUpper();   // new string
        string replaced = original.Replace("c", "C"); // new string

        Console.WriteLine($"[Imm] original = {original}");
        Console.WriteLine($"[Imm] upper    = {upper}");
        Console.WriteLine($"[Imm] replaced = {replaced}");

        // Strings cannot be modified in place:
        //
        //   original[0] = 'C'; // COMPILE ERROR
        //
        // This simplifies reasoning and thread safety but means:
        //
        //   - Many "small" modifications in hot paths are dangerous.
        //   - They generate many short-lived objects in Gen0, which the
        //     GC must collect frequently.
        //
        // PATTERN TO WATCH FOR:
        //
        //   - Logging frameworks,
        //   - serializers,
        //   - high-throughput APIs that generate JSON/XML/text,
        //
        // should avoid naive `+` concatenations inside tight loops.
    }

    // ---------------------------------------------------------------------
    // 5. COMPARISON – culture vs ordinal, case sensitivity, perf vs correctness
    // ---------------------------------------------------------------------
    static void ComparisonCultureAndOrdinal()
    {
        Console.WriteLine();
        Console.WriteLine("=== Comparison: Culture vs Ordinal ===");

        string s1 = "café";
        string s2 = "CAFE";

        // 1) Ordinal comparison (raw UTF-16 code units)
        bool ordinalEqual = string.Equals(s1, s2,
            StringComparison.OrdinalIgnoreCase);

        // 2) Culture-sensitive comparison (current culture)
        bool cultureEqual = string.Equals(s1, s2,
            StringComparison.CurrentCultureIgnoreCase);

        Console.WriteLine($"[Cmp] OrdinalIgnoreCase : {ordinalEqual}");
        Console.WriteLine($"[Cmp] CurrentCultureIgnoreCase: {cultureEqual}");

        // WHY THIS MATTERS:
        //
        //   - StringComparison.Ordinal / OrdinalIgnoreCase:
        //       * Compares numeric code units (fast, stable).
        //       * Best for protocols, IDs, file paths, technical tokens.
        //
        //   - Culture-based comparisons:
        //       * Uses rules of a specific culture (e.g., "tr-TR" Turkish).
        //       * Can treat different sequences as equal from the user's POV.
        //       * Slower, but necessary for correct user-facing UI behavior.
        //
        // As a top-tier engineer you must choose intentionally:
        //   - Security, keys, IDs → Ordinal / OrdinalIgnoreCase.
        //   - User-visible sorting / searching → Culture-sensitive.
    }

    // ---------------------------------------------------------------------
    // 6. UNICODE & LENGTH – Length is UTF-16 units, not grapheme clusters
    // ---------------------------------------------------------------------
    static void UnicodeAndLengthPitfalls()
    {
        Console.WriteLine();
        Console.WriteLine("=== Unicode & Length Pitfalls ===");

        string plain = "Cristian";
        string emoji = "👍";          // one visible symbol, two UTF-16 code units
        string combined = "ñ";       // sometimes composed as 'n' + combining tilde

        Console.WriteLine($"[Len] \"{plain}\"   Length = {plain.Length}");
        Console.WriteLine($"[Len] \"{emoji}\"   Length = {emoji.Length}");
        Console.WriteLine($"[Len] \"{combined}\" Length = {combined.Length}");

        // ABSTRACT REALITY:
        //
        //   - .NET string = sequence of UTF-16 code units.
        //   - Length = count of 16-bit units, not "glyphs" / grapheme clusters.
        //
        // IMPLICATIONS:
        //   - Substring, Remove, etc. can split surrogate pairs / combining sequences.
        //   - For advanced internationalization, you may need:
        //       * Rune (System.Text.Rune) for Unicode scalar values.
        //       * StringInfo / TextElementEnumerator to enumerate grapheme clusters.
    }

    // ---------------------------------------------------------------------
    // 7. ENCODING & BYTES – how strings travel across networks & disks
    // ---------------------------------------------------------------------
    static void EncodingAndBytes()
    {
        Console.WriteLine();
        Console.WriteLine("=== Encoding & Bytes ===");

        string text = "Hi, 🌍";

        // UTF-8 is dominant over the wire and in files.
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        byte[] utf16 = Encoding.Unicode.GetBytes(text); // UTF-16 LE

        Console.Write("[Enc] UTF-8  bytes: ");
        foreach (var b in utf8) Console.Write($"{b:X2} ");
        Console.WriteLine();

        Console.Write("[Enc] UTF-16 bytes: ");
        foreach (var b in utf16) Console.Write($"{b:X2} ");
        Console.WriteLine();

        // PROCESSOR-LEVEL VIEW:
        //
        //   - CPU only sees bytes in memory/cache.
        //   - Encoding is a *convention* that maps bytes ↔ code points.
        //   - When you call Encoding.UTF8.GetBytes, .NET executes a tight loop
        //     (often vectorized) converting internal UTF-16 to UTF-8.
        //
        // DESIGN RULE:
        //   - Inside .NET: string (UTF-16) is natural.
        //   - At boundaries (network, disk, DB): choose encoding explicitly
        //     (usually UTF-8) and be consistent.
    }

    // ---------------------------------------------------------------------
    // 8. SPAN-BASED OPS – using Span<char> to reduce allocations
    // ---------------------------------------------------------------------
    static void SpanBasedStringLikeOps()
    {
        Console.WriteLine();
        Console.WriteLine("=== Span<char> & stackalloc ===");

        // GOAL:
        //   Demonstrate creating temporary text without allocating multiple
        //   intermediate strings.

        // Allocate a small buffer on the STACK, not the heap.
        Span<char> buffer = stackalloc char[32];

        // Write into Span<char> manually:
        string name = "Cristian";
        string prefix = "Hi ";

        int pos = 0;
        prefix.AsSpan().CopyTo(buffer.Slice(pos));
        pos += prefix.Length;

        name.AsSpan().CopyTo(buffer.Slice(pos));
        pos += name.Length;

        // Create a single string from that buffer:
        string hello = new string(buffer.Slice(0, pos));

        Console.WriteLine("[Span] " + hello);

        // UNDER THE HOOD:
        //
        //   - Span<char> is a ref struct: (pointer, length) tracked by the JIT.
        //   - stackalloc reserves space in the current stack frame → no GC.
        //   - AsSpan() exposes a view over existing string data (no copy).
        //
        //   This pattern is useful in parsers, formatters, and performance-critical
        //   code where you want fine control over allocations.
    }

    // ---------------------------------------------------------------------
    // 9. STRINGBUILDER & POOLING HINTS – scalable concatenation patterns
    // ---------------------------------------------------------------------
    static void StringBuilderAndPoolingHints()
    {
        Console.WriteLine();
        Console.WriteLine("=== StringBuilder & Pooling Hints ===");

        string[] items = { "alpha", "beta", "gamma", "delta" };

        // BAD: repeated concatenation in a loop
        string bad = "";
        foreach (var item in items)
        {
            bad += item + ";"; // New string each time
        }

        // BETTER: StringBuilder
        var sb = new StringBuilder(capacity: 64); // pre-size when possible
        foreach (var item in items)
        {
            sb.Append(item).Append(';');
        }

        string good = sb.ToString();

        Console.WriteLine("[SB]   bad  = " + bad);
        Console.WriteLine("[SB]   good = " + good);

        // ADVANCED IDEA (not implemented here, just conceptual):
        //
        //   - ArrayPool<char> + StringBuilder (with custom chunk handling)
        //   - string.Create(length, state, (span, state) => { ... })
        //
        // These techniques:
        //   - Reuse buffers instead of constantly allocating new arrays.
        //   - Reduce GC pressure in high-throughput scenarios.
    }

    // ---------------------------------------------------------------------
    // 10. MICRO-BENCHMARK SHAPE – how to measure string perf (conceptual)
    // ---------------------------------------------------------------------
    static void MicroBenchmarkShape()
    {
        Console.WriteLine();
        Console.WriteLine("=== Micro-benchmark Shape (Conceptual) ===");

        // We will NOT implement a full benchmark framework here, but we sketch
        // how you would compare two string strategies in a scientific way:
        //
        //   1. Warm up the JIT (run the code a few times).
        //   2. Use Stopwatch to measure elapsed time over MANY iterations.
        //   3. Use GC.GetAllocatedBytesForCurrentThread() to measure allocations.
        //
        // Example pattern (pseudo-code):
        //
        //     var sw = Stopwatch.StartNew();
        //     long before = GC.GetAllocatedBytesForCurrentThread();
        //
        //     for (int i = 0; i < N; i++)
        //         MethodUnderTest();
        //
        //     sw.Stop();
        //     long after = GC.GetAllocatedBytesForCurrentThread();
        //
        //     Console.WriteLine($"Time: {sw.Elapsed}, Alloc: {after - before} bytes");
        //
        // Use BenchmarkDotNet in real projects; it handles warmup, noise,
        // statistics, outliers, CPU affinity, etc.
        //
        // As a "scientist-level" engineer, you ALWAYS:
        //   - Form hypotheses about string performance.
        //   - Design repeatable benchmarks.
        //   - Validate results with measurements, not intuition.
    }
}
