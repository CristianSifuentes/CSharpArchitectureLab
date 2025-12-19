// ================================================================
// DateTimeConversionDeepDive.cs
// ================================================================
//
// Goal:
// - Keep your original example (CultureInfo.CurrentCulture, Parse int, Parse DateTime,
//   formatting with :D and :C)
// - Add “hard-to-find” internals:
//   * How DateTime is represented (ticks, Kind) and what CPU actually manipulates
//   * What the compiler emits vs what the runtime (JIT) executes
//   * Globalization pipeline: ICU vs NLS, culture tables, calendars, digits
//   * DateTime.Parse / ParseExact / TryParseExact / styles
//   * Time zone ambiguity (local vs UTC), DST gaps/overlaps, DateTimeOffset
//   * Performance: allocation patterns, fast paths, caching, spans, parsing in hot paths
//   * Correctness: round-trip formats, ISO-8601, invariant culture
//
// This file is intentionally comment-heavy (GitHub-ready).
//
// ---------------------------------------------------------------
// 0) Mental model: parsing a date is NOT “just converting text”
// ---------------------------------------------------------------
//
// Converting "March 2, 2025" into a DateTime involves multiple layers:
//
// 1) Culture tables:
//    - Month names, separators, calendars, digits, AM/PM, ordering, etc.
//
// 2) Parsing logic:
//    - Tokenization and pattern matching
//    - Fallback heuristics (Parse tries many patterns)
//    - Styles (assume local, allow whitespace, adjust to UTC, etc.)
//
// 3) Time zone semantics:
//    - If the string has no offset ("2025-03-02"), it's ambiguous.
//    - DateTimeKind can be Unspecified/Local/Utc.
//    - DST can create “non-existent” local times (spring forward) or duplicates (fall back).
//
// 4) Result representation:
//    - DateTime is a 64-bit value containing "ticks" (100-nanosecond intervals) plus Kind bits.
//    - CPU ultimately operates on that 64-bit integer; everything else is logic around it.
//
// ---------------------------------------------------------------
// 1) CPU-level intuition: what happens at the processor level?
// ---------------------------------------------------------------
//
// At the CPU level, parsing is “string scanning + branching”:
//
// - You iterate over characters (UTF-16 in .NET strings).
// - You classify each char: digit? separator? letter?
// - You accumulate numeric values: year = year*10 + digit
// - You do comparisons against culture-specific tokens ("March", "marzo", etc.)
// - You branch a lot. Branch prediction matters.
// - You allocate sometimes (strings, substrings), which triggers GC.
//
// The biggest performance killers are usually NOT the integer math,
// but memory and control flow:
//
// - cache misses when touching big culture tables
// - allocations from substring/token creation (depending on parsing path)
// - exception paths (int.Parse throwing) which are extremely expensive
//
// ---------------------------------------------------------------
// 2) Compiler vs JIT: who decides what runs?
// ---------------------------------------------------------------
//
// Roslyn (C# compiler):
// - Emits IL for calls like DateTime.Parse(...), string interpolation, etc.
// - The compiler does not “optimize parsing”; it just emits method calls.
//
// CLR + JIT (RyuJIT):
// - Produces machine code for your CPU.
// - Inlines small methods (sometimes), specializes generic methods, etc.
// - However, DateTime.Parse is complex and usually won’t inline.
//   Your optimization lever is *API choice* (TryParseExact, InvariantCulture, etc.)
//   and *data shape* (ISO strings, avoiding heuristics).
//
// Also: Tiered compilation / PGO
// - Hot code may be re-jitted with different codegen based on real execution.
// - The best pattern in hot paths is: avoid slow paths, give the JIT easy loops.
//
// ---------------------------------------------------------------
// 3) DateTime internals (the scientist view)
// ---------------------------------------------------------------
//
// DateTime is essentially:
//
// - ticks: Int64 (100ns intervals since 0001-01-01 00:00:00)
// - Kind: a couple of bits (Unspecified/Local/Utc)
//
// The CPU doesn't “know” about dates.
// It manipulates ticks (a 64-bit integer). Everything else is interpretation.
//
// This matters because:
// - DateTime arithmetic (AddDays/AddHours) is integer math + overflow checks.
// - Formatting/parsing are big routines, but once you have ticks, operations are fast.
//
// ---------------------------------------------------------------
// 4) The correctness trap: DateTime vs DateTimeOffset
// ---------------------------------------------------------------
//
// DateTime (Kind=Unspecified) is a “clock reading without a location”.
// DateTime (Kind=Local/Utc) adds semantics, but still lacks an explicit offset.
//
// DateTimeOffset stores:
// - a DateTime (UTC-ish instant) + an offset (+/-HH:MM)
//
// For APIs and logs: DateTimeOffset is often the safer default.
// For storage: prefer ISO-8601 with offset, or UTC with round-trip format.
//
// ---------------------------------------------------------------
// 5) Culture pitfalls: your code sets es-ES, but parses English
// ---------------------------------------------------------------
//
// You do:
//   CultureInfo.CurrentCulture = "es-ES";
//   DateTime.Parse("March 2, 2025");
//
// This is a *great teaching example* because it demonstrates a real-world gotcha:
//
// - With es-ES, month names are expected in Spanish ("marzo").
// - "March" may fail, or succeed depending on fallback rules / OS globalization.
//
// Correct strategies:
// - Use ParseExact with an explicit culture.
// - Or use InvariantCulture for fixed machine formats.
// - Or parse ISO-8601: "2025-03-02" using invariant rules.
//
// ---------------------------------------------------------------
// 6) Performance heuristics for top-tier engineers
// ---------------------------------------------------------------
//
// ✔ Avoid DateTime.Parse in hot paths or at scale (it tries lots of patterns).
// ✔ Prefer DateTime.TryParseExact with a known format.
// ✔ Prefer ISO-8601: "yyyy-MM-dd" or "O" (round-trip).
// ✔ Prefer DateTimeOffset for external APIs and distributed systems.
// ✔ Prefer TryParse over Parse to avoid exception-driven control flow.
// ✔ For numbers: use TryParse with NumberStyles and IFormatProvider.
// ✔ For formatting: use invariant formats for logs; culture formats for UI.
// ✔ Avoid repeatedly creating CultureInfo; cache it.
// ✔ In high throughput services, consider using DateOnly/TimeOnly when time zones are irrelevant.
//
// ================================================================
// 7) Upgraded version of your class + expert demos
// ================================================================

using System;
using System.Diagnostics;
using System.Globalization;
using static System.Console;

partial class Program
{
    public static void ConversionToDateTimeDeepDive()
    {
        ConvertionToDateTime_Basics();
        ConvertionToDateTime_CulturePitfall();
        ConvertionToDateTime_TryParseExact_Recommended();
        ConvertionToDateTime_ISO_And_RoundTrip();
        ConvertionToDateTime_DateTimeOffset_And_DST();
        ConvertionToDateTime_Perf_Demo();
    }

    // ------------------------------------------------------------
    // Your original idea, preserved (but corrected + extended)
    // ------------------------------------------------------------
    static void ConvertionToDateTime_Basics()
    {
        WriteLine("== ConvertionToDateTime_Basics ==");

        // Setting the current culture affects:
        // - DateTime parsing/formatting (month names, order, separators)
        // - number formatting (decimal separator, thousands separator, currency)
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");

        int friends = int.Parse("101", CultureInfo.InvariantCulture);

        // ⚠️ If you want culture-specific decimal parsing, you must parse from string
        // using that culture. Here it's a literal double in source code (always '.' as decimal).
        double cost = 25.50;

        // ✅ Prefer invariant formats for data interchange.
        DateTime birthday = DateTime.Parse("2025-03-02", CultureInfo.InvariantCulture);

        WriteLine($"I have {friends} friends to invite to my party.");
        WriteLine($"My birthday celebration will be on {birthday}");
        WriteLine($"Long format (current culture): {birthday:D}");
        WriteLine($"The cost of the entry will be: {cost:C}");
        WriteLine();
    }

    // ------------------------------------------------------------
    // Culture pitfall: English vs Spanish month names
    // ------------------------------------------------------------
    static void ConvertionToDateTime_CulturePitfall()
    {
        WriteLine("== ConvertionToDateTime_CulturePitfall ==");

        var es = CultureInfo.GetCultureInfo("es-ES");
        var en = CultureInfo.GetCultureInfo("en-US");

        // If your UI is Spanish but data is English, you must pick the right culture.
        string englishDate = "March 2, 2025";
        string spanishDate = "2 marzo 2025";

        // Use TryParse to avoid exceptions (exceptions are slow and noisy).
        if (DateTime.TryParse(englishDate, en, DateTimeStyles.None, out var dtEn))
            WriteLine($"Parsed English date using en-US: {dtEn:D}");

        if (DateTime.TryParse(spanishDate, es, DateTimeStyles.None, out var dtEs))
            WriteLine($"Parsed Spanish date using es-ES: {dtEs:D}");

        // What happens if you parse with the wrong culture?
        bool ok = DateTime.TryParse(englishDate, es, DateTimeStyles.None, out var wrong);
        WriteLine($"Parsing '{englishDate}' using es-ES succeeded? {ok} (value: {wrong})");
        WriteLine();
    }

    // ------------------------------------------------------------
    // Recommended: TryParseExact with explicit formats and providers
    // ------------------------------------------------------------
    static void ConvertionToDateTime_TryParseExact_Recommended()
    {
        WriteLine("== ConvertionToDateTime_TryParseExact_Recommended ==");

        // If you control the input format, ParseExact/TryParseExact is the pro move:
        // - fewer heuristics
        // - more predictable
        // - usually faster
        // - safer across machines/OS settings

        var invariant = CultureInfo.InvariantCulture;

        // Example: fixed date format
        string isoDate = "2025-03-02";
        if (DateTime.TryParseExact(
            isoDate,
            "yyyy-MM-dd",
            invariant,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var dt))
        {
            WriteLine($"ISO parsed as UTC: {dt:O} (Kind={dt.Kind})");
        }

        // Example: date+time with seconds
        string logStamp = "2025-03-02 14:35:10";
        if (DateTime.TryParseExact(
            logStamp,
            "yyyy-MM-dd HH:mm:ss",
            invariant,
            DateTimeStyles.AssumeLocal,
            out var local))
        {
            WriteLine($"Log stamp parsed as Local: {local:O} (Kind={local.Kind})");
        }

        WriteLine();
    }

    // ------------------------------------------------------------
    // ISO-8601 and round-trip formats: production logging patterns
    // ------------------------------------------------------------
    static void ConvertionToDateTime_ISO_And_RoundTrip()
    {
        WriteLine("== ConvertionToDateTime_ISO_And_RoundTrip ==");

        // "O" is the round-trip format. If you serialize with "O", you can parse back reliably.
        // Great for logs and interchange.
        DateTime utcNow = DateTime.UtcNow;
        string roundTrip = utcNow.ToString("O", CultureInfo.InvariantCulture);
        WriteLine($"UTC now round-trip string: {roundTrip}");

        if (DateTime.TryParseExact(roundTrip, "O", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed))
        {
            WriteLine($"Parsed back: {parsed:O} (Kind={parsed.Kind})");
        }

        // A warning:
        // - If you serialize a Local DateTime without offset, receivers may interpret differently.
        // - Prefer DateTimeOffset or explicit UTC.
        WriteLine();
    }

    // ------------------------------------------------------------
    // DateTimeOffset: safer for distributed systems + time zone semantics
    // ------------------------------------------------------------
    static void ConvertionToDateTime_DateTimeOffset_And_DST()
    {
        WriteLine("== ConvertionToDateTime_DateTimeOffset_And_DST ==");

        // If the string includes an offset, DateTimeOffset preserves it:
        string withOffset = "2025-03-02T10:15:00-06:00";

        if (DateTimeOffset.TryParse(withOffset, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var dto))
        {
            WriteLine($"DTO: {dto:O}");
            WriteLine($"DTO.UtcDateTime: {dto.UtcDateTime:O}");
            WriteLine($"Offset minutes: {dto.Offset.TotalMinutes}");
        }

        // DST note:
        // - Some local times do not exist in some zones (spring forward).
        // - Some exist twice (fall back).
        //
        // In business apps: store instants in UTC (DateTimeOffset/UTC) and
        // convert to local only for display.
        WriteLine();
    }

    // ------------------------------------------------------------
    // Performance demo: Parse vs TryParseExact (demo-only)
    // ------------------------------------------------------------
    static void ConvertionToDateTime_Perf_Demo()
    {
        WriteLine("== ConvertionToDateTime_Perf_Demo ==");

        // ⚠️ Stopwatch is a demo tool. For serious measurement, use BenchmarkDotNet.
        // We'll compare:
        // - DateTime.Parse (heuristics)
        // - DateTime.TryParseExact (single known format)

        const int N = 200_000;
        string[] samples = new string[N];

        // Generate ISO strings (best-case for TryParseExact)
        for (int i = 0; i < N; i++)
            samples[i] = "2025-03-02";

        var inv = CultureInfo.InvariantCulture;

        var sw = Stopwatch.StartNew();
        long ticks1 = 0;
        for (int i = 0; i < N; i++)
        {
            // Parse will try multiple patterns (slower in general).
            var dt = DateTime.Parse(samples[i], inv);
            ticks1 += dt.Ticks;
        }
        sw.Stop();
        WriteLine($"Parse:         {sw.ElapsedMilliseconds} ms (acc={ticks1})");

        sw.Restart();
        long ticks2 = 0;
        for (int i = 0; i < N; i++)
        {
            // Exact format: fewer code paths.
            if (DateTime.TryParseExact(samples[i], "yyyy-MM-dd", inv, DateTimeStyles.None, out var dt))
                ticks2 += dt.Ticks;
        }
        sw.Stop();
        WriteLine($"TryParseExact: {sw.ElapsedMilliseconds} ms (acc={ticks2})");

        WriteLine();
    }
}

// ================================================================
// Extra “scientist-level” notes (keep for GitHub):
// ================================================================
//
// 1) Globalization engine (ICU vs NLS):
// - On Windows, .NET can use NLS (Windows globalization APIs).
// - On Linux/macOS, .NET typically uses ICU.
// - Culture behavior, casing, and parsing edge cases can differ subtly.
//   That’s why production services should prefer invariant/ISO for interchange.
//
// 2) Why exceptions are slow in parsing:
// - Throwing involves building stack traces, unwinding, and metadata work.
// - It defeats branch prediction and adds heavy control flow.
// - In hot paths: always prefer TryParse / TryParseExact.
//
// 3) Micro-optimization that actually matters:
// - Use a stable input format (ISO).
// - Use TryParseExact with explicit format strings.
// - Cache IFormatProvider (CultureInfo) and DateTimeStyles.
// - Avoid parsing inside loops if you can parse once and reuse.
//
// 4) DateOnly/TimeOnly (modern modeling):
// - If you are modeling a birthday, you often want DateOnly, not DateTime.
// - If you are modeling “store opens at 09:00”, you want TimeOnly.
// - They avoid accidental time zone semantics.
//
// 5) Logging guidance for elite teams:
// - Log UTC instants as DateTimeOffset in "O" format.
// - Example: 2025-03-02T16:15:00.1234567Z
// - This is human-readable, sortable, and round-trippable.
// ================================================================
