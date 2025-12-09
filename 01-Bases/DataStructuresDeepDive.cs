
// File: DataStructuresDeepDive.cs
// Author: Cristian Sifuentes + ChatGPT
//
// GOAL
// ----
// Use a *systems / compiler / performance engineer* lens to understand:
//   - class
//   - struct
//   - record
// and how they really behave from C# → IL → CLR → CPU.
//
// HIGH-LEVEL MENTAL MODEL
// -----------------------
// When you write:
//
//     var pedro = new User { Name = "Pedro", Age = 33 };
//     pedro.Greet();
//
//     var p = new Point { X = 30, Y = 20 };
//     Console.WriteLine(p);
//
//     var nokia = new CellPhone("Nokia 225", 2024);
//     Console.WriteLine(nokia);
//
// a LOT happens:
//
// 1. Roslyn (C# compiler) parses the syntax and chooses *data structure kinds*:
//
//       class User      → reference type, heap-allocated, ref semantics.
//       struct Point    → value type, stack/inline, copy semantics.
//       record CellPhone(string,int)
//                       → reference type with generated members
//                         (or value type if you use 'record struct').
//
// 2. Roslyn emits IL where:
//       - Reference types are manipulated as OBJ references
//         (managed pointers to heap objects).
//       - Value types are copied bit-by-bit and can live on the stack,
//         in registers, or *inline* within other objects.
//
// 3. The CLR JIT compiles the IL into machine code and decides:
//       - Where to place each instance (stack slot, register, heap).
//       - How to pass parameters (by value, by ref).
//       - When to box / unbox value types.
//
// 4. The CPU sees only:
//       - Bytes in memory (heap / stack).
//       - Pointers (addresses).
//       - Fixed-width registers.
//
//    “class”, “struct”, “record” are *abstractions* that compile down to
//    specific memory layouts, copy strategies, and call conventions.
//
// This file is designed so future-you (and LLMs you use) can reason about
// data structures at a **top-1% engineer** level, not just “class vs struct”
// interview clichés.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

partial class Program
{
    // ---------------------------------------------------------------------
    // PUBLIC ENTRY POINT FOR THIS MODULE
    // ---------------------------------------------------------------------
    static void DataStructuresDeepDive()
    {
        Console.WriteLine("=== Data Structures Deep Dive (class / struct / record) ===");

        BasicSample();                        // Your original idea, upgraded
        ReferenceVsValueMentalModel();        // What “reference type” and “value type” really mean
        ClassLayoutAndHeapSemantics();        // Object header, method table, GC
        StructSemanticsAndCopyCost();         // When structs shine, when they hurt
        RecordSemanticsAndImmutability();     // Value-like behavior on top of classes
        CompositionAndInlineData();           // Structs embedded inside classes
        BoxingUnboxingAndInterfaces();        // Hidden allocations
        MicroBenchmarkClassesVsStructs();     // Performance intuition (conceptual)
    }

    // ---------------------------------------------------------------------
    // 0. BASIC SAMPLE – starting from your original example
    // ---------------------------------------------------------------------
    static void BasicSample()
    {
        Console.WriteLine();
        Console.WriteLine("=== 0. Basic Sample ===");

        User pedro = new User { Name = "Pedro", Age = 33 };
        pedro.Greet();

        Point punto = new Point { X = 30, Y = 20 };
        Console.WriteLine($"Punto ({punto.X},{punto.Y})");

        CellPhone nokia = new CellPhone("Nokia 225", 2024);
        Console.WriteLine(nokia);

        // Mental model:
        //
        //   - 'pedro' is a *reference* variable:
        //         [stack/register] → [heap: User object]
        //
        //   - 'punto' is a *value* variable:
        //         [stack/register: X=30][Y=20]
        //
        //   - 'nokia' is a reference to an immutable record instance:
        //         [stack/register] → [heap: CellPhone { Model, Year }]
    }

    // ---------------------------------------------------------------------
    // 1. REFERENCE vs VALUE – semantic difference, not “heap vs stack” only
    // ---------------------------------------------------------------------
    static void ReferenceVsValueMentalModel()
    {
        Console.WriteLine();
        Console.WriteLine("=== 1. Reference vs Value Types Mental Model ===");

        var user1 = new User { Name = "Ana", Age = 25 };
        var user2 = user1; // Copies the *reference*, not the object

        user2.Age = 30;

        Console.WriteLine($"user1.Age = {user1.Age} (changed via user2)");
        Console.WriteLine($"ReferenceEquals(user1, user2) = {ReferenceEquals(user1, user2)}");

        // VALUE TYPE:
        Point p1 = new Point { X = 10, Y = 20 };
        Point p2 = p1; // Copies the entire value (X,Y) bits

        p2.X = 999;

        Console.WriteLine($"p1.X = {p1.X}, p2.X = {p2.X} (independent copies)");

        // KEY IDEA:
        //
        //   *Reference type variable*  = pointer to an object.
        //   *Value type variable*      = the object itself (bits).
        //
        // Copying a reference variable is O(1) pointer copy.
        // Copying a large struct may be O(N) copy of its fields.
        //
        // This is why large structs (> ~16 bytes) can hurt performance if
        // passed around by value too much.
    }

    // ---------------------------------------------------------------------
    // 2. CLASS LAYOUT & HEAP SEMANTICS – object header, method table, GC
    // ---------------------------------------------------------------------
    static void ClassLayoutAndHeapSemantics()
    {
        Console.WriteLine();
        Console.WriteLine("=== 2. Class Layout & Heap Semantics ===");

        var u = new User { Name = "Carlos", Age = 40 };
        u.Greet();

        // HIGH-LEVEL LAYOUT (simplified, 64-bit):
        //
        //   [obj header][method table pointer][fields...]
        //
        // Object header:
        //   - Sync block index / lock bits.
        //   - GC information.
        //
        // Method table pointer:
        //   - Points to the type’s metadata (vtable, interface maps, etc.).
        //
        // Fields (for User):
        //   - string Name (reference)
        //   - int Age     (value)
        //
        // So in memory (conceptually):
        //
        //   [hdr] [mtbl*] [Name*] [Age]
        //
        // WHERE IT LIVES:
        //
        //   - The *User* instance is on the managed heap.
        //   - The variable 'u' is a pointer on the stack (or in a register).
        //
        // GC:
        //
        //   - The garbage collector relocates objects and updates all
        //     references (called “compaction”).
        //   - Compacting improves cache locality and reduces fragmentation.
        //
        // DESIGN RULE:
        //
        //   - Classes are ideal when:
        //       * You want reference semantics (shared, mutable state).
        //       * Instances are large or long-lived.
        //       * You need polymorphism / inheritance.
    }

    // ---------------------------------------------------------------------
    // 3. STRUCT SEMANTICS & COPY COST – when to choose struct
    // ---------------------------------------------------------------------
    static void StructSemanticsAndCopyCost()
    {
        Console.WriteLine();
        Console.WriteLine("=== 3. Struct Semantics & Copy Cost ===");

        Point p = new Point { X = 1, Y = 2 };

        // Passing a struct *by value* copies its fields.
        MovePoint(p);           // Copy
        MovePointByRef(ref p);  // No copy, but aliasing
        MovePointByIn(in p);    // Read-only ref, no copy, better for large structs

        Console.WriteLine($"After moves, p = ({p.X},{p.Y})");

        // RULE OF THUMB:
        //
        //   - Small, frequently used, immutable → struct can be great
        //     (e.g., numeric types, vectors, DateTime).
        //
        //   - Large, mutable → prefer class or pass struct by ref/in:
        //       struct Big
        //       {
        //           public fixed byte Data[512]; // 512-byte struct
        //       }
        //
        //     Copying this by value in tight loops will destroy performance.
    }

    static void MovePoint(Point p)
    {
        // Local copy of the struct.
        p.X += 10;
        p.Y += 10;
        // Caller’s Point is unchanged.
    }

    static void MovePointByRef(ref Point p)
    {
        // Modifies caller’s instance, no copy.
        p.X += 100;
        p.Y += 100;
    }

    static void MovePointByIn(in Point p)
    {
        // Cannot modify p, but we avoid copying when calling the method.
        // Useful for large readonly structs (e.g., matrices).
        int lengthSquared = p.X * p.X + p.Y * p.Y;
        Console.WriteLine($"Length² (in) = {lengthSquared}");
    }

    // ---------------------------------------------------------------------
    // 4. RECORDS – value-like semantics on top of classes
    // ---------------------------------------------------------------------
    static void RecordSemanticsAndImmutability()
    {
        Console.WriteLine();
        Console.WriteLine("=== 4. Records & Immutability ===");

        var phone1 = new CellPhone("Nokia 225", 2024);
        var phone2 = new CellPhone("Nokia 225", 2024);

        Console.WriteLine($"phone1 = {phone1}");
        Console.WriteLine($"phone2 = {phone2}");
        Console.WriteLine($"ReferenceEquals(phone1, phone2) = {ReferenceEquals(phone1, phone2)}");
        Console.WriteLine($"phone1 == phone2 (record equality) = {phone1 == phone2}");

        // WHAT RECORD DOES:
        //
        //   record CellPhone(string Model, int Year);
        //
        // expands roughly to:
        //
        //   class CellPhone
        //   {
        //       public string Model { get; init; }
        //       public int Year { get; init; }
        //
        //       public override bool Equals(object? other) { ... }
        //       public override int GetHashCode() { ... }
        //       public void Deconstruct(out string model, out int year) { ... }
        //       public static bool operator ==(...), !=(...)
        //       public CellPhone With(...) => new CellPhone(...);
        //   }
        //
        // So:
        //   - It’s still a *reference type* (heap-allocated).
        //   - But equality is *value-based* (by properties) not by reference.
        //   - Properties are typically init-only → encourages immutability.
        //
        // Copies:
        var newer = phone1 with { Year = 2025 }; // Non-destructive mutation pattern.
        Console.WriteLine($"newer = {newer}");

        // RECORD STRUCT:
        //
        //   record struct Pixel(int R, int G, int B);
        //
        // generates a *value type* with record-style equality & ToString(),
        // combining the best of both worlds: stack/inline layout + value semantics.
    }

    // ---------------------------------------------------------------------
    // 5. COMPOSITION – structs inside classes (inline data)
    // ---------------------------------------------------------------------
    static void CompositionAndInlineData()
    {
        Console.WriteLine();
        Console.WriteLine("=== 5. Composition & Inline Data ===");

        var entity = new EntityWithPosition
        {
            Id = 1,
            Position = new Point { X = 5, Y = 10 }
        };

        Console.WriteLine(entity);

        // MEMORY LAYOUT (rough mental model):
        //
        //   class EntityWithPosition
        //   {
        //       public int Id;
        //       public Point Position; // struct field
        //   }
        //
        // On the heap:
        //
        //   [hdr][mtbl*][Id][Position.X][Position.Y]
        //
        // The Point struct is *inline* inside the heap object, not separately
        // allocated. This can be vastly more cache-friendly than having a
        // separate reference to another heap object.
        //
        // DESIGN PATTERN:
        //
        //   - Use structs for *pure data* embedded inside aggregate classes
        //     (e.g., vectors, bounds, timestamps).
        //   - This avoids extra allocations and pointer indirections.
    }

    sealed class EntityWithPosition
    {
        public int Id;
        public Point Position;

        public override string ToString() => $"Entity {Id} at ({Position.X},{Position.Y})";
    }

    // ---------------------------------------------------------------------
    // 6. BOXING / UNBOXING – hidden allocations that kill performance
    // ---------------------------------------------------------------------
    static void BoxingUnboxingAndInterfaces()
    {
        Console.WriteLine();
        Console.WriteLine("=== 6. Boxing / Unboxing & Interfaces ===");

        int value = 42;

        // BOXING:
        //   - Converting a value type to object or to an interface type.
        //   - Allocates a new object on the heap containing a copy of the value.
        object boxed = value; // box int → object

        // UNBOXING:
        //   - Extracting the value type from the boxed object.
        //   - Requires a cast and copies the bits back into a value variable.
        int unboxed = (int)boxed;

        Console.WriteLine($"boxed.GetType() = {boxed.GetType().Name}, unboxed = {unboxed}");

        // WHY IT MATTERS:
        //
        //   - Boxing allocates. In hot code paths this leads to GC pressure.
        //   - Avoid boxing in inner loops, especially when working with generic
        //     interfaces like IEnumerable or non-generic collections (ArrayList).

        var numbers = new List<int> { 1, 2, 3, 4, 5 };
        long sum = 0;

        // OK: generic IEnumerable<int> → no boxing.
        foreach (int n in numbers)
        {
            sum += n;
        }

        Console.WriteLine($"Sum (generic list, no boxing) = {sum}");

        // BAD: if you used non-generic IList or object[], each int would be boxed.
        //   var arr = new object[] { 1, 2, 3, 4, 5 }; // each element boxed.
        //
        // RULE:
        //   - Prefer generic collections and generic interfaces.
        //   - Be careful passing structs to APIs expecting object.
    }

    // ---------------------------------------------------------------------
    // 7. MICRO-BENCHMARK SHAPE – classes vs structs (conceptual)
    // ---------------------------------------------------------------------
    static void MicroBenchmarkClassesVsStructs()
    {
        Console.WriteLine();
        Console.WriteLine("=== 7. Micro-benchmark: Class vs Struct (Conceptual) ===");

        const int N = 200_000;

        var classArray = new User[N];
        var structArray = new Point[N];

        for (int i = 0; i < N; i++)
        {
            classArray[i] = new User { Name = "User" + i, Age = i };
            structArray[i] = new Point { X = i, Y = i };
        }

        long ClassSumAges()
        {
            long sum = 0;
            for (int i = 0; i < classArray.Length; i++)
            {
                // One pointer load + one int field load per iteration.
                sum += classArray[i].Age;
            }
            return sum;
        }

        long StructSumXs()
        {
            long sum = 0;
            for (int i = 0; i < structArray.Length; i++)
            {
                // Directly reading int from contiguous memory.
                sum += structArray[i].X;
            }
            return sum;
        }

        static long MeasureTicks(string label, Func<long> func)
        {
            var sw = Stopwatch.StartNew();
            long result = func();
            sw.Stop();
            Console.WriteLine($"{label}: result={result}, ticks={sw.ElapsedTicks}");
            return sw.ElapsedTicks;
        }

        // Warm up JIT
        ClassSumAges();
        StructSumXs();

        MeasureTicks("Class  sum Age", ClassSumAges);
        MeasureTicks("Struct sum X ", StructSumXs);

        // SCIENTIST-LEVEL TAKEAWAYS:
        //
        //   - Struct arrays are extremely cache-friendly: all data is contiguous,
        //     no pointer chasing. Great for numeric / game-engine style workloads.
        //
        //   - Class arrays store *references*; each User object is elsewhere on
        //     the heap. The CPU performs an extra pointer load followed by a field
        //     load, which can result in more cache misses.
        //
        //   - However, structs are copied by value. If you pass them around a lot
        //     or have large structs, the cost of copying can dominate.
        //
        //   - BenchmarkDotNet is the proper tool for real measurements; this is
        //     just a mental-model demo.
    }
}

// =====================================================================
// DATA STRUCTURES DEFINITIONS (your original types, enriched)
// =====================================================================

// CLASS – reference type, heap-allocated, reference semantics.
class User
{
    // Auto-properties:
    //   - Backed by compiler-generated fields.
    //   - For reference types like string, just store another pointer.
    public string? Name { get; set; }
    public int Age { get; set; }

    public void Greet()
    {
        Console.WriteLine($"Hola, soy el usuario {Name} y tengo una edad de {Age} años");
    }

    // Example of a non-virtual instance method – easiest for the JIT to inline.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAdult() => Age >= 18;
}

// STRUCT – value type, can live on stack or inline inside other objects.
struct Point
{
    public int X { get; set; }
    public int Y { get; set; }

    // Structs should be small, immutable where possible, and represent a single
    // logical value. Avoid parameterless mutable structs in public APIs.
}

// RECORD – reference type with value-based equality & nice ToString().
record CellPhone(string Model, int Year);
// You could also use:  record struct CellPhoneStruct(string Model, int Year);
// to get value-type behavior with record conveniences.

// =====================================================================
// LLM-READY INSIGHT
// =====================================================================
// When you ask an LLM to generate data-structure-heavy C# code, you can
// use this mental model explicitly in your prompts:
//
//   - "Use small immutable structs for math-like data (Point, Vector3)."
//   - "Use classes for large aggregate entities with shared mutable state."
//   - "Use records when I want value-based equality and immutability."
//   - "Avoid boxing value types by using generic collections and interfaces."
//   - "Prefer struct fields inline inside classes for hot numeric data."
//
// This turns the LLM into a *partner* that respects low-level constraints,
// instead of generating “works but slow” code.

