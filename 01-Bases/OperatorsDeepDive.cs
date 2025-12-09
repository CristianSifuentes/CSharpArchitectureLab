// File: OperatorsDeepDive.cs
// Author: Cristian Sifuentes + ChatGPT
// Goal: Explain C# OPERATORS like a systems / compiler / performance engineer.
//
// HIGH-LEVEL MENTAL MODEL
// -----------------------
// When you write:
//
//     int number = 12;
//     bool isEven = number % 2 == 0;
//     bool isGreaterThanTen = number > 10;
//     if (isEven && isGreaterThanTen) { ... }
//
// a lot happens under the hood:
//
// 1. The C# compiler (Roslyn) parses your source into an abstract syntax tree (AST)
//    and binds each operator to a specific semantic:
//      - `%` → integer remainder operator
//      - `==` → equality comparison operator
//      - `>`  → relational operator
//      - `&&` → conditional-AND with short-circuit
//
// 2. Roslyn emits IL instructions like:
//        rem, ceq, cgt, brtrue, brfalse
//    Each IL opcode has a precise stack-machine behavior.
//
// 3. At runtime the JIT turns the IL into machine code using CPU instructions such as:
//        add, sub, imul, idiv, cmp, test, je/jne/jl/jg, setcc, cmov
//    The CPU then updates FLAGS (ZF, SF, CF, OF) which drive branches.
//
// 4. Short-circuiting operators (&&, ||, ??, ??=) compile to conditional branches
//    (or sometimes branchless patterns like `cmov`), which interact with:
//
//      - the branch predictor
//      - instruction pipelines
//      - the CPU’s speculative execution
//
// 5. Overflow behavior depends on checked/unchecked context:
//      - `add` vs `add.ovf` IL
//      - `mul` vs `mul.ovf`
//    This translates to different sequences of machine instructions.
//
// This file is written so you can think about operators the way a **top 1% engineer**
// would: as mappings from syntax → IL → machine code → micro-architectural effects.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

partial class Program
{
    // ---------------------------------------------------------------------
    // PUBLIC ENTRY FOR THIS MODULE
    // ---------------------------------------------------------------------
    // Call OperatorsDeepDive() from your main Program to run all demos.
    static void OperatorsDeepDive()
    {
        Console.WriteLine("=== Operators Deep Dive ===");

        BasicOperatorSample();           // Your original idea, upgraded
        ArithmeticVsBitwise();           // %, /, &, |, ^, shifts
        ShortCircuitAndEvaluationOrder();
        TernaryAndBranchlessThinking();
        ComparisonAndCPUFlags();
        CheckedVsUncheckedArithmetic();
        OperatorPrecedenceAndPitfalls();
        PatternMatchingAndModernOperators();
        MicroBenchmarkShapeForOperators();
    }

    // ---------------------------------------------------------------------
    // 0. BASIC SAMPLE – start from your original example, but commented
    // ---------------------------------------------------------------------
    static void BasicOperatorSample()
    {
        Console.WriteLine();
        Console.WriteLine("=== 0. Basic Operator Sample ===");

        int number = 12;

        // `%` is the remainder operator.
        // For integers it maps to IL `rem` and, on x86/x64, to `idiv` + `edx` remainder.
        bool isEven = number % 2 == 0;

        // `>` is a relational operator using IL `cgt` (compare greater than).
        bool isGreaterThanTen = number > 10;

        // `&&` is *conditional* AND:
        //   - Right side is evaluated only if left side is true.
        //   - This compiles to branches like:
        //         if (!isEven) goto elseLabel;
        //         if (!isGreaterThanTen) goto elseLabel;
        if (isEven && isGreaterThanTen)
        {
            Console.WriteLine($"Number {number} is even and greater than 10");
        }
        else if (!isEven && isGreaterThanTen)
        {
            Console.WriteLine($"Number {number} is odd and greater than 10");
        }
        else
        {
            Console.WriteLine($"Number {number} does not match criteria");
        }

        // The ternary `?:` is an *expression* operator, not a statement.
        // The compiler often emits something like:
        //   if (age > 18) category = "Adult";
        //   else category = "Minor";
        int age = 15;
        string category = age > 18 ? "Adult" : "Minor";
        Console.WriteLine($"Age {age} → Category: {category}");
    }

    // ---------------------------------------------------------------------
    // 1. ARITHMETIC vs BITWISE – think in terms of bits and CPU instructions
    // ---------------------------------------------------------------------
    static void ArithmeticVsBitwise()
    {
        Console.WriteLine();
        Console.WriteLine("=== 1. Arithmetic vs Bitwise ===");

        int x = 42;      // 00101010 in binary
        int y = 15;      // 00001111 in binary

        // ARITHMETIC
        int sum = x + y;     // IL: add      → CPU: add
        int diff = x - y;    // IL: sub      → CPU: sub
        int prod = x * y;    // IL: mul      → CPU: imul
        int quot = x / y;    // IL: div      → CPU: idiv
        int rem = x % y;    // IL: rem      → CPU: idiv, remainder in EDX/RDX

        Console.WriteLine($"[Arith] {x} + {y} = {sum}");
        Console.WriteLine($"[Arith] {x} - {y} = {diff}");
        Console.WriteLine($"[Arith] {x} * {y} = {prod}");
        Console.WriteLine($"[Arith] {x} / {y} = {quot}");
        Console.WriteLine($"[Arith] {x} % {y} = {rem}");

        // BITWISE (logical on individual bits)
        int and = x & y;     // 00101010 & 00001111 = 00001010 (10)
        int or = x | y;     // 00101010 | 00001111 = 00101111 (47)
        int xor = x ^ y;     // 00101010 ^ 00001111 = 00100101 (37)
        int notX = ~x;       // bitwise NOT (two’s complement inversion)
        int shiftLeft = x << 1;  // multiply by 2 for non-negative x
        int shiftRight = x >> 1;  // divide by 2 (arithmetic shift)

        Console.WriteLine($"[Bit] {x} & {y} = {and}");
        Console.WriteLine($"[Bit] {x} | {y} = {or}");
        Console.WriteLine($"[Bit] {x} ^ {y} = {xor}");
        Console.WriteLine($"[Bit] ~{x}     = {notX}");
        Console.WriteLine($"[Bit] {x} << 1 = {shiftLeft}");
        Console.WriteLine($"[Bit] {x} >> 1 = {shiftRight}");

        // SCIENTIST-LEVEL FACT:
        //   - % with a power-of-two divisor (e.g., number % 8) can often be
        //     optimized to `number & (8 - 1)` by JIT (bitmask instead of division).
        //   - Division and modulo are among the slowest scalar integer ops.
        //     Shifts and bitwise ops are usually 1 cycle and easily pipelined.
        //
        // So this:
        //     bool isPowerOfTwoBucket = (value & (size - 1)) == 0;
        // can be much faster than:
        //     bool isPowerOfTwoBucket = value % size == 0;
        // when `size` is a power of two.
    }

    // ---------------------------------------------------------------------
    // 2. SHORT-CIRCUIT & EVALUATION ORDER – &&, ||, and side effects
    // ---------------------------------------------------------------------
    static void ShortCircuitAndEvaluationOrder()
    {
        Console.WriteLine();
        Console.WriteLine("=== 2. Short-Circuit & Evaluation Order ===");

        int leftEvaluations = 0;
        int rightEvaluations = 0;

        bool Left()
        {
            leftEvaluations++;
            Console.WriteLine("Left() evaluated");
            return false;
        }

        bool Right()
        {
            rightEvaluations++;
            Console.WriteLine("Right() evaluated");
            return true;
        }

        // C# guarantees left-to-right evaluation.
        // For `&&`:
        //   - If Left() returns false, Right() will NOT be evaluated.
        bool result = Left() && Right();

        Console.WriteLine($"[ShortCircuit] Result = {result}");
        Console.WriteLine($"[ShortCircuit] Left() calls  = {leftEvaluations}");
        Console.WriteLine($"[ShortCircuit] Right() calls = {rightEvaluations}");

        // WHY THIS MATTERS:
        //
        //   - You can use short-circuit behavior to avoid null-reference accesses:
        //
        //         if (obj != null && obj.Property == 5) ...
        //
        //   - But you must never rely on SideEffect() being executed if it is
        //     on the right side of &&:
        //
        //         if (flag && SideEffect()) { ... }
        //
        //     Here SideEffect() might never run. A top engineer keeps side effects
        //     out of boolean expressions when possible, or is extremely explicit.
    }

    // ---------------------------------------------------------------------
    // 3. TERNARY ?: & BRANCHLESS THINKING – map expressions to hardware
    // ---------------------------------------------------------------------
    static void TernaryAndBranchlessThinking()
    {
        Console.WriteLine();
        Console.WriteLine("=== 3. Ternary ?: & Branchless Thinking ===");

        int value = 7;

        // Simple ternary:
        string parity = (value % 2 == 0) ? "even" : "odd";
        Console.WriteLine($"Value {value} is {parity}");

        // Sometimes the JIT can turn a ternary into *branchless* machine code
        // using `cmov` (conditional move) or bit tricks, which is better for
        // misprediction-heavy code.
        //
        // Example: clamp negative numbers to 0
        int clamped = value < 0 ? 0 : value;

        Console.WriteLine($"Clamped: {clamped}");

        // BRANCHLESS TRICK (conceptual):
        //
        //   int signBit = value >> 31;   // -1 for negative, 0 for non-negative
        //   int abs = (value ^ signBit) - signBit;
        //
        // This computes absolute value using only arithmetic/bitwise operators,
        // which may be faster than a branch-heavy `if` on some hot paths.
        //
        // As a high-level .NET dev you rarely write this manually, but understanding
        // the idea helps you reason about JIT and micro-optimizations.
    }

    // ---------------------------------------------------------------------
    // 4. COMPARISON & CPU FLAGS – what actually drives jumps
    // ---------------------------------------------------------------------
    static void ComparisonAndCPUFlags()
    {
        Console.WriteLine();
        Console.WriteLine("=== 4. Comparison & CPU Flags ===");

        int a = 10;
        int b = 20;

        bool less = a < b;   // IL: clt
        bool equal = a == b;  // IL: ceq
        bool more = a > b;   // IL: cgt

        Console.WriteLine($"[Cmp] {a} <  {b} : {less}");
        Console.WriteLine($"[Cmp] {a} == {b} : {equal}");
        Console.WriteLine($"[Cmp] {a} >  {b} : {more}");

        // MACHINE-LEVEL VIEW (simplified):
        //
        //     cmp  eax, ebx       ; compare a and b
        //     jl   LessLabel      ; jump if less (based on SF, OF flags)
        //     je   EqualLabel     ; jump if equal (ZF flag)
        //
        // Comparisons set the CPU FLAGS register; branches read those flags.
        //
        // BRANCH PREDICTION:
        //   - Modern CPUs guess which way branches go.
        //   - A mispredicted branch flushes the pipeline (tens of cycles).
        //   - Tight loops with unpredictable branches can be much slower.
        //
        // JIT often reorders and simplifies comparison expressions so the branch
        // pattern is friendlier to the predictor.
    }

    // ---------------------------------------------------------------------
    // 5. CHECKED vs UNCHECKED – overflow operators and IL
    // ---------------------------------------------------------------------
    static void CheckedVsUncheckedArithmetic()
    {
        Console.WriteLine();
        Console.WriteLine("=== 5. Checked vs Unchecked Arithmetic ===");

        int max = int.MaxValue;

        // unchecked: overflow wraps around (two’s complement)
        int wrapped = unchecked(max + 1);

        Console.WriteLine($"[Overflow] max        = {max}");
        Console.WriteLine($"[Overflow] unchecked  = {wrapped}");

        try
        {
            // checked: overflow throws System.OverflowException
            int willThrow = checked(max + 1);
            Console.WriteLine($"[Overflow] checked    = {willThrow}");
        }
        catch (OverflowException)
        {
            Console.WriteLine("[Overflow] checked    → OverflowException");
        }

        // IL VIEW (conceptual):
        //
        //   unchecked: add
        //   checked:   add.ovf
        //
        // `add.ovf` performs extra checks and throws if the result cannot be
        // represented in the destination type. The JIT translates this into
        // hardware sequences that detect overflow (using OF/CF flags).
        //
        // DESIGN RULE:
        //   - Use checked arithmetic in code where correctness is critical
        //     and numbers may approach boundaries (e.g., financial systems).
        //   - Use unchecked in performance-critical hot paths where you have
        //     proven that overflow cannot occur or wrapping is intended.
    }

    // ---------------------------------------------------------------------
    // 6. PRECEDENCE & PITFALLS – read operators like a compiler
    // ---------------------------------------------------------------------
    static void OperatorPrecedenceAndPitfalls()
    {
        Console.WriteLine();
        Console.WriteLine("=== 6. Operator Precedence & Pitfalls ===");

        int x = 2;
        int y = 3;
        int z = 4;

        // Multiplication has higher precedence than addition.
        int result1 = x + y * z;      // 2 + (3 * 4) = 14
        int result2 = (x + y) * z;    // (2 + 3) * 4 = 20

        Console.WriteLine($"[Prec] x + y * z  = {result1}");
        Console.WriteLine($"[Prec] (x + y)* z = {result2}");

        // Logical operators: && has higher precedence than ||.
        bool flag = true || false && false;        // true || (false && false) → true
        bool flagParen = (true || false) && false; // (true || false) && false → false

        Console.WriteLine($"[Prec] true || false && false      = {flag}");
        Console.WriteLine($"[Prec] (true || false) && false    = {flagParen}");

        // PITFALL: assignment vs comparison
        //
        //   if (flag = SomeCheck())  // BUG; assigns, then tests the value
        //
        // C# reduces this by requiring bool for if conditions, but it’s still
        // good practice to keep comparisons explicit and even use Yoda-style
        // equality in some contexts:
        //
        //   if (0 == value) ...
        //
        // Which avoids accidentally writing `value = 0` in languages that allow it.
    }

    // ---------------------------------------------------------------------
    // 7. MODERN OPERATORS – ??, ??=, pattern matching as "semantic operators"
    // ---------------------------------------------------------------------
    static void PatternMatchingAndModernOperators()
    {
        Console.WriteLine();
        Console.WriteLine("=== 7. Modern Operators & Pattern Matching ===");

        string? maybeName = null;

        // Null-coalescing operator:
        string displayName = maybeName ?? "Unknown";

        Console.WriteLine($"[NullCoalesce] {displayName}");

        // Null-coalescing assignment:
        maybeName ??= "Initialized";
        Console.WriteLine($"[NullCoalesceAssign] {maybeName}");

        object obj = 42;

        // Pattern matching `is` with type + condition:
        if (obj is int n && n > 10)
        {
            Console.WriteLine($"[Pattern] obj is int and > 10: {n}");

            // `switch` expression is also a high-level "operator":
            string classification = n switch
            {
                < 0 => "negative",
                0 => "zero",
                < 10 => "small positive",
                _ => "large positive"
            };

            Console.WriteLine($"[Pattern] {n} classified as {classification}");
        }
        else
        {
            Console.WriteLine("[Pattern] obj is not an int > 10");
        }

        // COMPILER VIEW:
        //
        //   - Many pattern matching constructs are lowered to chains of `isinst`,
        //     comparisons, and branches.
        //   - For integral switches, the JIT can generate jump tables or binary
        //     search trees, optimizing the dispatch.
        //
        // These "modern" operators are syntactic sugar over powerful, optimized
        // IL constructs – knowing this helps you reason about complexity and speed.
    }


    // ---------------------------------------------------------------------
    // 8. MICRO-BENCHMARK SHAPE – how to measure operator performance
    // ---------------------------------------------------------------------
    static void MicroBenchmarkShapeForOperators()
    {
        Console.WriteLine();
        Console.WriteLine("=== 8. Micro-benchmark Shape (Conceptual) ===");

        // GOAL:
        //   Compare two operator-based strategies scientifically.
        //   Example: `%` vs bitwise `&` for power-of-two modulus.

        const int N = 1_000_000;
        const int mask = 1024 - 1; // power-of-two - 1

        int Modulo(int i) => i % 1024;
        int Bitmask(int i) => i & mask;

        // Warm up JIT
        for (int i = 0; i < 10_000; i++)
        {
            _ = Modulo(i);
            _ = Bitmask(i);
        }

        // NOTE: BenchmarkDotNet is the real tool; this is educational only.
        static (TimeSpan elapsed, long alloc) Measure(string label, Func<int, int> func, int iterations)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            int sum = 0;
            for (int i = 0; i < iterations; i++)
            {
                sum += func(i);
            }

            sw.Stop();
            long afterAlloc = GC.GetAllocatedBytesForCurrentThread();

            Console.WriteLine($"{label}: sum={sum}  time={sw.Elapsed.TotalMilliseconds:F2} ms  alloc={afterAlloc - beforeAlloc} bytes");
            return (sw.Elapsed, afterAlloc - beforeAlloc);
        }

        Measure("Modulo  ", Modulo, N);
        Measure("Bitmask ", Bitmask, N);

        // SCIENTIST-LEVEL MINDSET:
        //
        //   - You *hypothesize* that one combination of operators is faster.
        //   - You design a controlled experiment.
        //   - You measure time AND allocations.
        //   - You repeat under different hardware / .NET versions.
        //
        // Operators are not just syntax; they are choices that propagate all the way
        // down to CPU pipelines and cache behavior. Top engineers always validate
        // those choices with data.
    }
}
