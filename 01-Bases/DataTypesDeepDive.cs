// File: DataTypes.cs
// Author: Cristian + ChatGPT
// Goal: Explain C# data types like a systems / compiler / performance engineer.
//
// High-level mental model (how ANY data type travels through the stack):
//  1. The C# compiler (Roslyn) translates your code into IL (Intermediate Language).
//  2. The JIT compiler (at runtime) translates that IL into machine code for your CPU.
//  3. The CLR runtime + JIT decide how each data type is represented:
//       - Which IL "stack type" it uses (I4, I8, R8, OBJ, etc.).
//       - Whether it lives in a register, stack slot, or on the managed heap.
//  4. The CPU only sees bits: fixed-width integer registers, floating-point registers,
//     and bytes in memory. “int”, “double”, “string” are *abstractions* on top of this.


using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

partial class Program
{
  static void DataTypesDeepDive()
  {
    var integer = 42;
    double decimalNumber = 3.1416;
    bool isTrue = true;
    char character = 'C';
    string text = "Hi C#";
    Console.WriteLine($"Int: {integer}, Decimal: {decimalNumber}, Boolean: {isTrue}, Char: {character}, Text: {text}");
  
    BasicDataTypesIntro();
    IntegerBitLevel();
    FloatingPointInternals();
    BooleanSemantics();
    CharAndStringInternals();
    StructLayoutAndPadding();
    EnumUnderlyingTypes();
    GenericSpecializationDemo();
  }
  
  // ------------------------------------------------------------------------
    // 1. BASIC DATA TYPES – YOUR ORIGINAL EXAMPLE, WITH LOW-LEVEL MEANING
    // ------------------------------------------------------------------------
    static void BasicDataTypesIntro()
    {
        // 'var' is *compile-time* type inference:
        //   - Roslyn infers the type from the right-hand side.
        //   - IL has a concrete type; there is no "var" at runtime.
        var integer = 42;               // inferred as System.Int32
        double decimalNumber = 3.1416;  // System.Double (64-bit IEEE-754)
        bool isTrue = true;             // System.Boolean (1 byte in IL)
        char character = 'C';           // System.Char (UTF-16 code unit)
        string text = "Hola C#";        // System.String (reference type, heap)

        Console.WriteLine(
            $"[BasicDataTypesIntro] Entero: {integer}, Decimal: {decimalNumber}, " +
            $"Booleano: {isTrue}, Carácter: {character}, Texto: {text}");

        // IL VIEW (conceptual):
        //   .locals init (
        //       [0] int32      integer,
        //       [1] float64    decimalNumber,
        //       [2] bool       isTrue,
        //       [3] char       character,
        //       [4] string     text)
        //
        // JIT / CPU VIEW:
        //   - int32 → loaded into a general-purpose register (e.g., EAX).
        //   - float64 → into an XMM (SSE) register.
        //   - bool → usually a byte in memory, but in registers it's just 0 or 1.
        //   - char → 16-bit integer representing a UTF-16 code unit.
        //   - string → pointer (reference) to a heap object whose layout is:
        //       [method table ptr][length][chars...]
    }

    // ------------------------------------------------------------------------
    // 2. INTEGER TYPES – BITS, TWO’S COMPLEMENT, AND CPU REGISTERS
    // ------------------------------------------------------------------------
    static void IntegerBitLevel()
    {
        // All signed integral types in .NET use two’s complement representation.
        sbyte  s8  = -1;          // 8-bit  signed
        byte   u8  = 255;         // 8-bit  unsigned
        short  s16 = -12345;      // 16-bit signed
        ushort u16 = 65535;       // 16-bit unsigned
        int    s32 = -123456789;  // 32-bit signed
        uint   u32 = 4000000000;  // 32-bit unsigned
        long   s64 = -1234567890123456789L; // 64-bit signed
        ulong  u64 = 18446744073709551615UL;// 64-bit unsigned

        Console.WriteLine($"[IntegerBitLevel] int: {s32}, uint: {u32}");

        // TWO’S COMPLEMENT – how negative numbers exist in hardware:
        //   value = –(2^N – raw_bits) for signed N-bit integers.
        // Example: sbyte s8 = -1 → bits 1111 1111 (0xFF).
        Console.WriteLine($"sbyte -1 raw bits: {Convert.ToString(s8, 2).PadLeft(8, '0')}");

        // BitConverter uses the actual in-memory representation on this architecture.
        byte[] bytes = BitConverter.GetBytes(s32); // 4 bytes (little-endian)
        Console.Write("[IntegerBitLevel] int -123456789 bytes: ");
        foreach (byte b in bytes)
        {
            Console.Write($"{b:X2} ");
        }
        Console.WriteLine();

        // PERFORMANCE NOTE:
        //   - int (Int32) is the "natural" size for most arithmetic on 32/64-bit CPUs.
        //   - Using smaller types (byte, short) rarely makes code faster; the JIT
        //     typically extends them to 32 bits in registers anyway.
        //   - For large numeric arrays, however, byte/short can save memory bandwidth
        //     and cache space, which can indirectly improve speed in data-heavy code.
    }

    // ------------------------------------------------------------------------
    // 3. FLOATING POINT – IEEE-754, PRECISION, AND PITFALLS
    // ------------------------------------------------------------------------
    static void FloatingPointInternals()
    {
        double a = 0.1;
        double b = 0.2;
        double c = a + b;

        Console.WriteLine($"[FloatingPointInternals] 0.1 + 0.2 = {c:R}");

        // WHY NOT 0.3 EXACTLY?
        //
        // Double is IEEE-754 binary64:
        //   sign:      1 bit
        //   exponent: 11 bits (biased)
        //   mantissa: 52 bits (fraction)
        //
        // 0.1 and 0.2 are not exactly representable in base-2, so the nearest
        // representable binary fractions are stored. When added, the small errors
        // combine, and the result is slightly off from 0.3.
        //
        // Bit-level view:
        long bits = BitConverter.DoubleToInt64Bits(c);
        Console.WriteLine($"Bits of (0.1+0.2): 0x{bits:X16}");

        // decimal type:
        //   - 128 bits total: 96-bit integer + scaling factor (base-10 exponent).
        //   - Designed for financial apps where base-10 precision matters more
        //     than raw performance.
        decimal d1 = 0.1m;
        decimal d2 = 0.2m;
        decimal d3 = d1 + d2;
        Console.WriteLine($"[FloatingPointInternals] decimal 0.1m + 0.2m = {d3}");

        // PERFORMANCE TRADEOFF:
        //   - double: implemented using CPU FPU/SSE instructions → very fast.
        //   - decimal: implemented in software (multiple 32-bit operations) →
        //              significantly slower but more precise for decimal fractions.
    }

    // ------------------------------------------------------------------------
    // 4. BOOLEAN – LOGICAL TYPE ON TOP OF BITS AND FLAGS
    // ------------------------------------------------------------------------
    static void BooleanSemantics()
    {
        bool flag = true;

        // The CLI specification defines Boolean as a 1-byte type with values 0 or 1.
        // However, CPUs typically work with 32/64-bit registers, so in registers
        // it's just a 0 or non-zero integer.
        Console.WriteLine($"[BooleanSemantics] flag = {flag}");

        // Example: JIT often compiles 'if (flag)' like this (conceptually):
        //   cmp   byte ptr [flag], 0
        //   je    label_false
        //   ; body of if
        //
        // Many boolean expressions come from comparisons that set CPU flags:
        int x = 5, y = 10;
        bool less = x < y; // uses CPU comparison instruction + conditional set.
        Console.WriteLine($"x < y = {less}");
    }

    // ------------------------------------------------------------------------
    // 5. CHAR & STRING – UNICODE, UTF-16, IMMUTABILITY, INTERNING
    // ------------------------------------------------------------------------
    static void CharAndStringInternals()
    {
        char ch = 'C';
        Console.WriteLine($"[CharAndStringInternals] char: {ch}, code unit: {(int)ch}");

        // .NET System.Char is a UTF-16 code unit (16 bits).
        //   - For most common characters: 1 char = 1 Unicode code point.
        //   - For supplementary characters (outside BMP): surrogate pairs
        //     are used (two chars representing one code point).

        string s1 = "Hola C#";
        string s2 = "Hola C#";

        // Strings are immutable, heap-allocated, and may be INTERNED:
        //   - For literals, the CLR often keeps a single instance in the intern pool.
        //   - So s1 and s2 may reference the same object.
        Console.WriteLine($"ReferenceEquals(s1, s2): {object.ReferenceEquals(s1, s2)}");

        // Memory layout (implementation detail but important to understand):
        //   [object header][method table pointer][int32 Length][UTF-16 chars...]
        //
        // This means:
        //   - Accessing s[i] is O(1).
        //   - Length is cached (no need to scan).
        //
        // Encoding to UTF-8:
        byte[] utf8 = Encoding.UTF8.GetBytes(s1);
        Console.Write("[CharAndStringInternals] UTF-8 bytes: ");
        foreach (byte b in utf8)
        {
            Console.Write($"{b:X2} ");
        }
        Console.WriteLine();

        // PERFORMANCE NOTES:
        //   - Avoid repeated string concatenation in loops → use StringBuilder,
        //     or string.Create/Span<char> for advanced scenarios.
        //   - Strings are reference types → each new value is a new allocation.
    }

    // ------------------------------------------------------------------------
    // 6. STRUCT LAYOUT & PADDING – HOW FIELD ORDER AFFECTS SIZE & CACHE
    // ------------------------------------------------------------------------
    // LayoutKind.Sequential means fields are laid out in memory in the order declared,
    // with padding added according to alignment rules.
    [StructLayout(LayoutKind.Sequential)]
    struct PackedExample1
    {
        public bool Flag; // 1 byte, but typically aligned to 4/8 bytes.
        public double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PackedExample2
    {
        public double Value;
        public bool Flag;
    }

    static void StructLayoutAndPadding()
    {
        // Marshal.SizeOf gives the unmanaged size; useful for understanding layout.
        int size1 = Marshal.SizeOf<PackedExample1>();
        int size2 = Marshal.SizeOf<PackedExample2>();

        Console.WriteLine($"[StructLayoutAndPadding] Size1 (bool,double) = {size1} bytes");
        Console.WriteLine($"[StructLayoutAndPadding] Size2 (double,bool) = {size2} bytes");

        // WHY MIGHT THEY DIFFER?
        //
        // Alignment rules want double to start at an 8-byte boundary on 64-bit CPUs.
        // In PackedExample1:
        //   offset 0: bool Flag      (1 byte)
        //   offset 1–7: padding      (7 bytes)
        //   offset 8: double Value   (8 bytes)
        //   total: 16 bytes
        //
        // In PackedExample2:
        //   offset 0: double Value   (8 bytes)
        //   offset 8: bool Flag      (1 byte)
        //   offset 9–15: padding     (7 bytes)
        //   total: 16 bytes as well on many runtimes, but in more complex
        //   structures, field order can significantly reduce padding.
        //
        // PERFORMANCE IMPLICATION:
        //   - Smaller structs → more fit in cache lines → fewer cache misses.
        //   - For arrays of structs in hot loops, field ordering can be a real
        //     micro-optimization.
    }

    // ------------------------------------------------------------------------
    // 7. ENUMS – TYPE-SAFE NAMES OVER INTEGER REPRESENTATIONS
    // ------------------------------------------------------------------------
    enum Status : byte // underlying type explicitly set to byte
    {
        None = 0,
        Started = 1,
        Completed = 2,
        Failed = 3
    }

    static void EnumUnderlyingTypes()
    {
        Status st = Status.Completed;
        Console.WriteLine($"[EnumUnderlyingTypes] Status = {st}, raw = {(byte)st}");

        // Enums are value types whose underlying storage is an integral type
        // (byte, sbyte, short, ushort, int, uint, long, ulong).
        //
        // In IL, an enum is essentially:
        //   .class public auto ansi sealed Status
        //       extends [System.Runtime]System.Enum
        //   {
        //       .field public static literal valuetype Status None = int32(0)
        //       ...
        //       .field public specialname rtspecialname int32 value__
        //   }
        //
        // The JIT treats the "value__" field as the actual numeric value.
        // This keeps enum operations as fast as integer operations at runtime.
    }

    // ------------------------------------------------------------------------
    // 8. GENERICS & REIFICATION – DIFFERENT JIT CODE PER TYPE
    // ------------------------------------------------------------------------
    static void GenericSpecializationDemo()
    {
        // .NET generics are *reified* (not erased like Java generics):
        //   - The JIT generates specialized machine code for value types.
        //   - Reference types often share generic code.
        //
        // Example: List<int> and List<string> are different instantiations.
        //   - List<int> stores Int32 in a contiguous int[] array (no boxing).
        //   - List<object> would store references (pointers) instead.

        var listInt = new SimpleList<int>();
        listInt.Add(10);
        listInt.Add(20);

        var listDouble = new SimpleList<double>();
        listDouble.Add(3.14);
        listDouble.Add(2.71);

        Console.WriteLine($"[GenericSpecializationDemo] Sum<int> = {listInt.Sum()}");
        Console.WriteLine($"[GenericSpecializationDemo] Sum<double> = {listDouble.Sum()}");

        // CPU / JIT DETAIL:
        //   - For SimpleList<int>, the JIT can use 32-bit integer registers and SIMD
        //     optimizations for Sum().
        //   - For SimpleList<double>, it uses floating-point registers (XMM/YMM).
        //   - This is one reason generics with value types are so efficient in .NET.
    }

    // A minimal generic list to illustrate IL/JIT behavior.
    class SimpleList<T> where T : struct
    {
        private T[] _items;
        private int _count;

        public SimpleList()
        {
            _items = new T[4];
            _count = 0;
        }

        public void Add(T item)
        {
            if (_count == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }
            _items[_count++] = item;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Sum()
        {
            // For numeric value types this is just a demo; in real code you'd
            // probably constrain T further or specialize via generics / interfaces.
            dynamic sum = default(T);
            for (int i = 0; i < _count; i++)
            {
                sum += (dynamic)_items[i];
            }
            return (T)sum;
        }
    }
}