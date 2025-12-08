using System;
using System.Numerics;                 //  ← REQUIRED for Vector<T>
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


partial class Program
{
  // --------------------------------------------------------------------
  // PUBLIC ENTRY FOR THIS MODULE
  // --------------------------------------------------------------------
  // Call ShowNumericTypes() from your own Main() in another partial Program.
    static void ShowNumericTypes()
    {
      var integerNumber = 42m;
      double doubleNumber = 3.1416d;
      float floatingNumber = 274f;
      long longNumber = 300_200_100L;
      decimal monetaryNumber = 99.99m;
      Console.WriteLine($"Entero: {integerNumber}");
      Console.WriteLine($"Double: {doubleNumber}");
      Console.WriteLine($"Float: {floatingNumber}");
      Console.WriteLine($"Long: {longNumber}");
      Console.WriteLine($"Decimal: {monetaryNumber}");


      BasicNumericTypesIntro();
      IntegerRangeAndOverflow();
      FloatingPointPrecision();
      DecimalForMoney();
      NumericLiteralsAndTypeInference();
      VectorizationAndSIMD();
    }


    // --------------------------------------------------------------------
    // 1. BASIC NUMERIC TYPES – based on your original example
    // --------------------------------------------------------------------
    static void BasicNumericTypesIntro()
    {
        // NOTE: In your original snippet, `var integerNumber = 42m;` was actually
        // a decimal. Here we separate concerns to make the underlying types clear.

        int    integerNumber  = 42;              // System.Int32 (signed, 32 bits)
        double doubleNumber   = 3.1416d;         // System.Double (IEEE-754 binary64)
        float  floatingNumber = 274f;            // System.Single (IEEE-754 binary32)
        long   longNumber     = 300_200_100L;    // System.Int64 (signed, 64 bits)
        decimal monetaryNumber = 99.99m;         // System.Decimal (128-bit decimal)

        Console.WriteLine($"[Basic]   Int:     {integerNumber}");
        Console.WriteLine($"[Basic]   Double:  {doubleNumber}");
        Console.WriteLine($"[Basic]   Float:   {floatingNumber}");
        Console.WriteLine($"[Basic]   Long:    {longNumber}");
        Console.WriteLine($"[Basic]   Decimal: {monetaryNumber}");

        // IL VIEW (conceptual):
        //
        //   .locals init (
        //     [0] int32   integerNumber,
        //     [1] float64 doubleNumber,
        //     [2] float32 floatingNumber,
        //     [3] int64   longNumber,
        //     [4] valuetype [System.Runtime]System.Decimal monetaryNumber)
        //
        // CPU VIEW:
        //   - int/long:   stored in general-purpose registers (EAX/RAX/RDX...).
        //   - float/double: stored in floating-point/SIMD registers (XMM/YMM).
        //   - decimal:     implemented in software using multiple 32-bit pieces.
    }

    // --------------------------------------------------------------------
    // 2. INTEGER RANGE & OVERFLOW – checked vs unchecked, IL & CPU
    // --------------------------------------------------------------------
    static void IntegerRangeAndOverflow()
    {
        int max = int.MaxValue;
        int min = int.MinValue;

        Console.WriteLine($"[IntRange] int.MinValue = {min}, int.MaxValue = {max}");

        // UNSAFE (default) BEHAVIOR – overflow wraps (two's complement)
        int overflowUnchecked = unchecked(max + 1);
        Console.WriteLine($"[Overflow] unchecked(max + 1) = {overflowUnchecked}");

        // SAFE BEHAVIOR – overflow throws at runtime
        try
        {
            int overflowChecked = checked(max + 1);
            Console.WriteLine($"[Overflow] checked(max + 1) = {overflowChecked}");
        }
        catch (OverflowException ex)
        {
            Console.WriteLine($"[Overflow] checked(max + 1) threw: {ex.GetType().Name}");
        }

        // WHY?
        //
        // Two’s complement representation:
        //   - int is 32 bits. Value range: [-2^31, 2^31 - 1]
        //   - When you add 1 to 0x7FFFFFFF (MaxValue), hardware wraps to 0x80000000,
        //     which is MinValue (-2147483648).
        //
        // IL:
        //   - checked:   add.ovf   // overflow-checked add → throws on overflow
        //   - unchecked: add       // wraps silently
        //
        // Using 'checked' around critical arithmetic can catch logic bugs early,
        // but it has a small runtime cost. In hot loops, you may choose
        // 'unchecked' intentionally after careful reasoning.
    }

    // --------------------------------------------------------------------
    // 3. FLOATING POINT PRECISION – IEEE-754 & bit-level inspection
    // --------------------------------------------------------------------
    static void FloatingPointPrecision()
    {
        double a = 0.1;
        double b = 0.2;
        double c = a + b;

        Console.WriteLine($"[FP] 0.1 + 0.2 = {c:R}  (R = round-trip format)");

        // WHY NOT EXACTLY 0.3?
        //
        // Double is IEEE-754 binary64:
        //   sign:      1 bit
        //   exponent: 11 bits
        //   fraction: 52 bits
        //
        // 0.1 and 0.2 in base-10 are repeating fractions in base-2, so the nearest
        // representable binary values are stored. The addition works on those
        // approximations and produces a nearby but not exact value.

        long rawBits = BitConverter.DoubleToInt64Bits(c);
        Console.WriteLine($"[FP] Bits of (0.1+0.2): 0x{rawBits:X16}");

        // FLOAT vs DOUBLE:
        float  fx = 1f / 10f;    // fewer bits → larger relative error
        double dx = 1d / 10d;
        Console.WriteLine($"[FP] float  1/10 = {fx:R}");
        Console.WriteLine($"[FP] double 1/10 = {dx:R}");

        // CPU VIEW:
        //   - Modern CPUs execute float/double ops in hardware via SSE/AVX units.
        //   - Many operations can be vectorized (SIMD) over arrays of floats/doubles.
        //   - But some operations (division, transcendental functions) are slower.
    }

    // --------------------------------------------------------------------
    // 4. DECIMAL – BASE-10 ARITHMETIC FOR MONEY
    // --------------------------------------------------------------------
    static void DecimalForMoney()
    {
        decimal price  = 19.99m;
        decimal tax    = 0.16m;
        decimal total1 = price * (1 + tax);

        Console.WriteLine($"[Decimal] price = {price}, tax = {tax}, total = {total1}");

        // CONTRAST WITH DOUBLE:
        double priceD  = 19.99;
        double taxD    = 0.16;
        double total2D = priceD * (1 + taxD);
        Console.WriteLine($"[Decimal] double total ≈ {total2D:R}");

        // Decimal is 128-bit, base-10 friendly:
        //   sign:    1 bit
        //   scale:   5 bits (power of 10)
        //   integer: 96 bits
        //
        // Designed so that values like 0.1, 0.01, 19.99 are represented exactly.
        // This avoids weird cent-level rounding issues in financial apps.
        //
        // PERFORMANCE COST:
        //   - Double operations map directly to CPU hardware instructions.
        //   - Decimal operations are implemented using multiple 32-bit integer
        //     operations in software → slower but more precise in base-10.
    }

    // --------------------------------------------------------------------
    // 5. NUMERIC LITERALS & TYPE INFERENCE – how suffixes change the IL
    // --------------------------------------------------------------------
    static void NumericLiteralsAndTypeInference()
    {
        // BY DEFAULT:
        //   42      → int
        //   42L     → long
        //   42u     → uint
        //   42UL    → ulong
        //   3.14    → double
        //   3.14f   → float
        //   3.14m   → decimal

        var x = 42;        // int
        var y = 42L;       // long
        var z = 3.14;      // double
        var q = 3.14f;     // float
        var r = 3.14m;     // decimal

        Console.WriteLine($"[Literals] x:int={x}, y:long={y}, z:double={z}, q:float={q}, r:decimal={r}");

        // Digit separators (_) are ignored by the compiler, but help humans:
        long big = 1_000_000_000_000L; // easier to read than 1000000000000
        Console.WriteLine($"[Literals] big long = {big}");

        // IL EXAMPLE (conceptual):
        //   ldc.i4.s 42    // small int → pushed as I4
        //   ldc.i8   42    // long literal
        //   ldc.r8   3.14  // double literal
        //   ldc.r4   3.14  // float literal
        //
        // The IL stack has "stack types" (I, I4, I8, R4, R8, O, etc.) and the JIT
        // picks the appropriate machine registers and instructions.
    }

    // --------------------------------------------------------------------
    // 6. VECTORISATION & SIMD – using Vector<T> for numeric performance
    // --------------------------------------------------------------------
    static void VectorizationAndSIMD()
    {
        // System.Numerics.Vector<T> provides a portable abstraction over SIMD.
        // On hardware that supports it, Vector<float>.Count might be 4, 8, or more.
        float[] dataA = { 1, 2, 3, 4, 5, 6, 7, 8 };
        float[] dataB = { 10, 20, 30, 40, 50, 60, 70, 80 };
        float[] result = new float[dataA.Length];

        if (Vector.IsHardwareAccelerated)
        {
            int width = Vector<float>.Count;
            int i = 0;

            // Vectorized loop: processes 'width' elements per iteration.
            for (; i <= dataA.Length - width; i += width)
            {
                var va = new Vector<float>(dataA, i);
                var vb = new Vector<float>(dataB, i);
                var vr = va * vb;  // element-wise multiply via SIMD

                vr.CopyTo(result, i);
            }

            // Remainder loop for leftover elements
            for (; i < dataA.Length; i++)
            {
                result[i] = dataA[i] * dataB[i];
            }
        }
        else
        {
            // Fallback scalar loop
            for (int i = 0; i < dataA.Length; i++)
            {
                result[i] = dataA[i] * dataB[i];
            }
        }

        Console.Write("[SIMD] dataA * dataB = ");
        foreach (var v in result)
        {
            Console.Write(v + " ");
        }
        Console.WriteLine();

        // LOW-LEVEL VIEW:
        //
        //   - The JIT maps Vector<float> operations to SIMD instructions
        //     (e.g., mulps, vmulps) when possible.
        //   - Instead of doing 1 multiply per iteration, the CPU does N in parallel
        //     (N = Vector<float>.Count).
        //   - Memory layout (contiguous float[] array) is critical for SIMD.
        //
        // This is where numeric data types + memory layout + CPU architecture
        // intersect to produce *orders of magnitude* performance gains.
    }

}