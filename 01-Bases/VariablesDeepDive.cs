// File: VariablesDeepDive.cs
// Author:Cristian Sifuentes Covarrubia + ChatGPT (Deep dive into C# variables)
// Goal: Explain variables like a systems / compiler / performance engineer.

// IMPORTANT MENTAL MODEL
// ----------------------
// In C# you write high-level code like:
//
//     int age = 25;
//
// But a LOT happens underneath:
//
// 1. The C# compiler (Roslyn) translates this into IL (Intermediate Language).
// 2. The JIT compiler (at runtime) translates that IL into machine code for your CPU.
// 3. The CLR runtime decides where that variable "lives":
//    - in a register (fast, inside the CPU)
//    - in a stack slot (part of the call stack frame)
//    - as a field inside an object on the heap
// 4. The CPU finally operates on electrical signals in registers and memory.
//
// This file tries to connect the **high-level view** of variables with the
// **low-level reality** (stack, heap, registers, JIT, caching, etc.)
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;



partial class Program
{
  static void VariablesDeepDive()
  {
    int age = 25;

    string name = "Alice";
    bool isStudent = true;

    Console.WriteLine($"Name: {name} is {age} years old and student status is {isStudent}");
   
    VariablesIntro();
    ValueVsReference();
    StackAndHeapDemo();
    RefAndInParameters();
    SpanAndPerformance();
    ClosuresAndCaptures();
    VolatileAndMemoryModel();
  
  
  }
  
  // ------------------------------------------------------------------------
    // 1. BASIC VARIABLES – BUT WITH A LOW-LEVEL VIEW
    // ------------------------------------------------------------------------
    static void VariablesIntro()
    {
        // At C# level:
        int age = 25;
        string name = "Alice";
        bool isStudent = true;

        Console.WriteLine($"[Intro] Name: {name} is {age} years old and student status is {isStudent}");

        // WHAT ACTUALLY HAPPENS?
        //
        // C# compiler (Roslyn):
        //   - Emits IL roughly like:
        //         .locals int32 V_0  // age
        //                 string V_1 // name
        //                 bool V_2   // isStudent
        //   - age, name, isStudent become **local variables** in IL.
        //
        // JIT compiler:
        //   - Tries to map these locals to CPU registers when possible.
        //   - Might "spill" them to the stack if registers are not enough.
        //
        // STACK vs REGISTERS:
        //   - "int age = 25;" might never live in memory at all:
        //       the JIT can load the constant 25 directly into a register.
        //   - If the JIT needs the value across instructions and lacks registers,
        //       it stores it in a stack slot (part of the stack frame).
        //
        // STRING "Alice":
        //   - String is a REFERENCE type.
        //   - The reference (pointer) is stored as a local variable
        //     (likely in a register or stack slot).
        //   - The actual characters "Alice" live on the managed HEAP,
        //     allocated by the runtime during program startup or when loaded.
        //
        // BOOL isStudent:
        //   - In IL it's a "bool" (System.Boolean), often compiled to a single byte.
        //   - CPU typically uses at least a byte in memory, but in registers
        //     it's just bits in a register.
    }

    // ------------------------------------------------------------------------
    // 2. VALUE TYPES vs REFERENCE TYPES (STACK vs HEAP – BUT NOT ALWAYS)
    // ------------------------------------------------------------------------
    static void ValueVsReference()
    {
        // VALUE TYPE EXAMPLE
        // ------------------
        // struct is a value type. Its data is usually stored "inline"
        // (in the stack frame, in a register, or inside another object).
        PointStruct ps = new PointStruct { X = 10, Y = 20 };

        // REFERENCE TYPE EXAMPLE
        // ----------------------
        // class is a reference type. The variable holds a *reference* (pointer)
        // to an object on the heap.
        PointClass pc = new PointClass { X = 10, Y = 20 };

        Console.WriteLine($"[ValueVsReference] Struct: ({ps.X},{ps.Y}) | Class: ({pc.X},{pc.Y})");

        // LOW LEVEL NOTES:
        //   - PointStruct ps:
        //       IL has a local of type PointStruct.
        //       The struct fields X, Y are just part of that local’s memory.
        //       CPU can load them from a stack slot or register.
        //
        //   - PointClass pc:
        //       pc itself is a 64-bit reference (on 64-bit runtime).
        //       The real data (X, Y) is on the heap.
        //       Access: 1) load reference, 2) follow pointer, 3) load fields.
        //
        // PERFORMANCE IMPLICATION:
        //   - Value types avoid an extra pointer indirection and allocation,
        //     but copying them can be expensive if the struct is large.
        //   - Reference types cost a heap allocation, pointer indirection,
        //     and GC tracking, but are cheap to copy (just copy the reference).
    }

    struct PointStruct
    {
        public int X;
        public int Y;
    }

    class PointClass
    {
        public int X;
        public int Y;
    }

    // ------------------------------------------------------------------------
    // 3. STACK, HEAP, ESCAPE ANALYSIS (WHY SOME THINGS ALLOCATE)
    // ------------------------------------------------------------------------
    static void StackAndHeapDemo()
    {
        // Case 1: Local struct that DOES NOT ESCAPE the method.
        // The JIT can keep this entirely in registers or stack.
        HeavyStruct local = CreateHeavyStructNoEscape();
        Console.WriteLine($"[StackAndHeapDemo] local.Value = {local.Value}");

        // Case 2: Struct stored inside a heap object => always on heap.
        HeavyHolder holder = new HeavyHolder
        {
            // HeavyStruct is now a field of a heap-allocated object.
            // The struct's bits live *inside* that heap object.
            Heavy = CreateHeavyStructNoEscape()
        };

        Console.WriteLine($"[StackAndHeapDemo] holder.Heavy.Value = {holder.Heavy.Value}");

        // Case 3: Capturing a local variable in a closure =>
        // the variable is moved to a heap-allocated "display class".
        int counter = 0;
        Action action = () =>
        {
            // This lambda captures "counter".
            // The compiler transforms this roughly into:
            //   class DisplayClass { public int counter; }
            //   var display = new DisplayClass();
            //   display.counter = 0;
            //   Action action = () => { display.counter++; ... }
            counter++;
            Console.WriteLine($"[StackAndHeapDemo] counter in closure: {counter}");
        };

        action();
        action();

        // At this point, "counter" is no longer a simple stack local.
        // It is part of a HEAP object created to support the closure.

        // This transformation is sometimes called "closure lifting" or "lambda lifting".
        // It is a key optimization point when you care about allocations.
    }

    struct HeavyStruct
    {
        // Large struct just to exaggerate cost of copying.
        public long A, B, C, D;
        public int Value;
    }

    class HeavyHolder
    {
        public HeavyStruct Heavy;
    }

    static HeavyStruct CreateHeavyStructNoEscape()
    {
        HeavyStruct hs;
        hs.A = 1;
        hs.B = 2;
        hs.C = 3;
        hs.D = 4;
        hs.Value = 42;
        // hs does not "escape" the method until it is returned as a value.
        // The JIT simply returns this by value (sometimes in registers).
        return hs;
    }

    // ------------------------------------------------------------------------
    // 4. REF, IN, and PERFORMANCE (ALIASSING AND COPY COST)
    // ------------------------------------------------------------------------
    static void RefAndInParameters()
    {
        HeavyStruct hs = CreateHeavyStructNoEscape();

        // Passing by value: entire struct is copied.
        IncrementValueByCopy(hs);

        // Passing by reference: no copy of HeavyStruct, only a pointer.
        IncrementValueByRef(ref hs);

        // Passing by readonly reference: caller avoids copy; callee cannot modify.
        IncrementValueByIn(in hs);

        Console.WriteLine($"[RefAndInParameters] hs.Value = {hs.Value}");

        // Low-level perspective:
        //   - By value:
        //       IL copies every field of the struct into a parameter slot.
        //       JIT then passes many bytes (or uses hidden pointer / copying code).
        //
        //   - ref / in:
        //       Only a pointer is passed (8 bytes on 64-bit).
        //       Function parameters are aliasing the same memory.
        //
        // RISK:
        //   - ref parameters introduce aliasing: multiple references to the same
        //     memory region. This can:
        //       - make reasoning harder
        //       - make some optimizations harder (similar to C's aliasing issues)
    }

    static void IncrementValueByCopy(HeavyStruct hs)
    {
        // Modifies a copy; caller does NOT see this change.
        hs.Value++;
    }

    static void IncrementValueByRef(ref HeavyStruct hs)
    {
        // Modifies the caller's instance in-place.
        hs.Value++;
    }

    static void IncrementValueByIn(in HeavyStruct hs)
    {
        // hs is readonly. The compiler forbids writes:
        // hs.Value++; // <- not allowed
        // But we can *read* without copying the entire struct.
        int tmp = hs.Value;
        // Low-level: parameter hs is a pointer + "readonly" enforced by C# compiler.
        _ = tmp;
    }

    // ------------------------------------------------------------------------
    // 5. SPAN<T> AND STACKALLOC – VARIABLES VERY CLOSE TO THE METAL
    // ------------------------------------------------------------------------
    static void SpanAndPerformance()
    {
        // Span<T> is a ref struct that represents a contiguous region of memory.
        // It can point to:
        //   - stack memory (via stackalloc)
        //   - managed arrays (on the heap)
        //   - unmanaged memory (via Unsafe / NativeMemory / P/Invoke)
        //
        // Here we allocate 4 ints on the STACK, not on the heap.
        Span<int> stackNumbers = stackalloc int[4];

        stackNumbers[0] = 10;
        stackNumbers[1] = 20;
        stackNumbers[2] = 30;
        stackNumbers[3] = 40;

        int sum = 0;
        for (int i = 0; i < stackNumbers.Length; i++)
        {
            sum += stackNumbers[i];
        }

        Console.WriteLine($"[SpanAndPerformance] Sum of stack numbers = {sum}");

        // Low-level:
        //   - stackalloc reserves a block of memory in the current stack frame.
        //   - Span<int> is like (pointer, length) with extra safety checks.
        //   - No GC allocation; lifetime is bound to the current stack frame.
        //
        // CPU-level:
        //   - The array is laid out contiguously in memory.
        //   - This is cache-friendly: the CPU can prefetch sequential elements.
        //   - This pattern is ideal for SIMD / vectorization optimizations
        //     that the JIT might perform.
    }

    // ------------------------------------------------------------------------
    // 6. CLOSURES AND CAPTURED VARIABLES – HIDDEN HEAP ALLOCATIONS
    // ------------------------------------------------------------------------
    static void ClosuresAndCaptures()
    {
        int local = 10;

        // Lambda capturing "local"
        Func<int, int> add = x =>
        {
            // The compiler turns this into something like:
            //   class DisplayClass { public int local; }
            //   var display = new DisplayClass { local = 10 };
            //   Func<int, int> add = x => display.local + x;
            return local + x;
        };

        int result = add(5);
        Console.WriteLine($"[ClosuresAndCaptures] result = {result}");

        // WHY THIS MATTERS:
        //   - Because of the capturing, "local" now lives in a heap object.
        //   - That extra heap allocation increases GC pressure and cache usage.
        //
        // MICRO-OPTIMIZATION:
        //   - For hot paths (tight loops, high-frequency calls), avoiding
        //     allocations due to closures can significantly improve performance.
        //   - Techniques:
        //       * Static lambdas with explicit state
        //       * Rewriting code to avoid capturing outer locals
        //       * Using struct-based function objects in some patterns
    }

    // ------------------------------------------------------------------------
    // 7. VOLATILE, MEMORY MODEL, AND MULTI-CORE REALITY
    // ------------------------------------------------------------------------
    static volatile int _flag = 0;
    static int _nonVolatileCounter = 0;

    static void VolatileAndMemoryModel()
    {
        // This is NOT a complete threading example (no threads started here),
        // but we document the idea for educational purposes.

        // The C# / .NET memory model allows the CPU and compiler/JIT
        // to reorder some instructions as long as single-threaded semantics
        // appear preserved.
        //
        // In multi-threaded code, this can lead to surprising behaviors
        // if variables are accessed without proper synchronization.
        //
        // "volatile" tells the JIT and CPU:
        //   - don't cache this value in a register indefinitely
        //   - insert appropriate memory barriers so that reads/writes
        //     are observed in a consistent order across cores.

        _flag = 1; // volatile write: cannot be reordered past certain fences.
        _nonVolatileCounter++; // normal write: can be reordered more freely.

        // Typical pattern in lock-free algorithms:
        //   Thread A:
        //       data = 42;
        //       flag = 1; // volatile write
        //
        //   Thread B:
        //       if (flag == 1) // volatile read
        //           Console.WriteLine(data); // guaranteed to see data = 42
        //
        // Without volatile or other synchronization (locks, Interlocked),
        // CPU caches and reordering could make Thread B see flag == 1
        // but a stale value of data.
        //
        // NOTE:
        //   - volatile is a low-level tool; most of the time higher-level
        //     primitives (lock, Monitor, Interlocked, etc.) are preferable.
    }

    // ------------------------------------------------------------------------
    // 8. AGGRESSIVE INLINING HINTS (HOW THE JIT TREATS SMALL FUNCTIONS)
    // ------------------------------------------------------------------------
    // This method is small enough that the JIT will likely inline it anyway,
    // but the attribute documents our intention and can influence the JIT.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int FastAdd(int a, int b)
    {
        // Inlining means:
        //   - instead of calling FastAdd(a, b), the JIT literally injects
        //     "a + b" where the call would be.
        //   - This removes the overhead of a call and may enable further
        //     optimizations (constant folding, CSE, vectorization).
        return a + b;
    }

    static void PerformanceNotes()
    {
        // Example usage:
        int x = 10;
        int y = 20;
        int z = FastAdd(x, y);

        Console.WriteLine($"[PerformanceNotes] z = {z}");

        // CPU-LEVEL VIEW:
        //   - After inlining, the code might be:
        //       mov eax, x
        //       add eax, y
        //   - No function call overhead.
        //
        // For micro-optimizations, variable placement (register vs stack),
        // inlining, and constant folding together can make seemingly
        // "simple" code extremely fast once compiled.
    }


}