// File: CollectionsListDictionaryDeepDive.cs
// Author: Cristian Sifuentes + ChatGPT
//
// GOAL
// ----
// Explain C# List<T> and Dictionary<TKey,TValue> like a systems / compiler /
// performance engineer, not just as "things that hold objects".
//
// MENTAL MODEL
// ------------
// When you write:
//
//   var names = new List<string> { "Ana", "Carlos", "Juan" };
//   names.Add("Lucia");
//   var students = new Dictionary<int, string>
//   {
//       [1] = "Ana",
//       [2] = "Felipe",
//       [3] = "Elena"
//   };
//
// the following layers are involved:
//
//   1. Roslyn (C# compiler) lowers the syntax into IL, using concrete types
//      like System.Collections.Generic.List`1[System.String] and
//      System.Collections.Generic.Dictionary`2[System.Int32,System.String].
//
//   2. List<T> is essentially:
//        - A reference to a contiguous T[] array on the managed heap.
//        - An integer Count.
//        - A version integer for enumeration safety.
//      Adding elements grows the backing array with Array.Resize and
//      Buffer.Memmove-style copies.
//
//   3. Dictionary<TKey,TValue> is a *hash table* implemented with:
//        - An int[] buckets array (indices into Entries).
//        - An Entry[] entries array (structs storing hashCode, key, value, next).
//        - A load factor threshold that triggers Resize() when exceeded.
//      Lookups use GetHashCode + equality comparison + collision chain walking.
//
//   4. JIT + CPU:
//        - List<T> is cache-friendly (contiguous memory).
//        - Dictionary<TKey,TValue> is cache-unfriendly (pointer chasing).
//        - foreach loops are lowered to index-based for loops (List<T>)
//          or an enumerator struct/class (Dictionary<K,V>).
//
// This file aims to give you a **top 1% engineer** mental model for List/Dictionary:
// syntax → IL → runtime data structures → CPU caches and branches.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

partial class Program
{
    // ---------------------------------------------------------------------
    // PUBLIC ENTRY POINT FOR THIS MODULE
    // ---------------------------------------------------------------------
    // Call CollectionsListDictionaryDeepDive() from your main Program.
    static void CollectionsListDictionaryDeepDive()
    {
        Console.WriteLine("=== List<T> & Dictionary<TKey,TValue> Deep Dive ===");

        BasicListAndDictionarySample();     // Your original idea, upgraded
        ListInternalsAndCapacity();         // How List<T> actually stores data
        ListIterationAndBoundsChecks();     // foreach → for, bounds-check elimination
        DictionaryInternalsAndHashing();    // Buckets, entries, collisions
        DictionaryAdvancedPatterns();       // TryGetValue, custom comparers, etc.
        ListVsDictionaryMicroBenchmark();   // Rough performance intuition
    }

    // ---------------------------------------------------------------------
    // 0. BASIC SAMPLE – starting from your original example, commented
    // ---------------------------------------------------------------------
    static void BasicListAndDictionarySample()
    {
        Console.WriteLine();
        Console.WriteLine("=== 0. Basic List & Dictionary Sample ===");

        // LIST<T>
        // -------
        // Backed internally by a T[] array in managed heap memory.
        // List<string> has fields (conceptually):
        //    T[] _items;
        //    int _size;      // Count
        //    int _version;   // increments on mutation (Add/Remove)
        //
        // The collection itself (the List object header + fields) is small;
        // the heavy part is the separate T[] backing array.
        List<string> names = new List<string> { "Ana", "Carlos", "Juan" };

        // Add() may or may not allocate:
        //   - If Count < Capacity: append at O(1) time, no allocation.
        //   - If Count == Capacity: allocate new array (typically 2x size),
        //     copy existing items, then append.
        names.Add("Lucia");

        Console.WriteLine($"Total de nombres: {names.Count}");
        foreach (var name in names)
        {
            Console.WriteLine(name);
        }

        // Remove() performs linear search (O(n)) to find the item, then shifts
        // the tail of the array one step left (memmove). On large lists, prefer
        // data structures where removal is cheaper or track indices yourself.
        names.Remove("Ana");

        bool isPresent = names.Contains("Ana"); // linear O(n) search
        Console.WriteLine($"¿Ana está en la lista? {isPresent}");

        // DICTIONARY<TKey,TValue>
        // -----------------------
        // Implemented as a hash table with separate arrays for buckets + entries.
        Dictionary<int, string> students = new Dictionary<int, string>
        {
            { 1, "Ana" },
            { 2, "Felipe" },
            { 3, "Elena" }
        };

        Console.WriteLine($"El estudiante con ID 1 es: {students[1]}");

        foreach (var student in students)
        {
            Console.WriteLine($"ID: {student.Key}, Nombre: {student.Value}");
        }

        // Indexer [] throws if the key is not present.
        // For "maybe present" keys, use TryGetValue to avoid exceptions:
        if (students.TryGetValue(99, out var unknown))
        {
            Console.WriteLine($"ID 99: {unknown}");
        }
        else
        {
            Console.WriteLine("ID 99 no encontrado (TryGetValue evita excepción).");
        }
    }

    // ---------------------------------------------------------------------
    // 1. LIST<T> INTERNALS – capacity, growth, and memory layout
    // ---------------------------------------------------------------------
    static void ListInternalsAndCapacity()
    {
        Console.WriteLine();
        Console.WriteLine("=== 1. List<T> Internals & Capacity ===");

        var list = new List<int>(); // default capacity is usually 0

        // Capacity is the length of the backing array; Count is how many
        // elements are logically in the list.
        Console.WriteLine($"Initial: Count={list.Count}, Capacity={list.Capacity}");

        for (int i = 0; i < 10; i++)
        {
            list.Add(i);
            Console.WriteLine($"After Add({i}): Count={list.Count}, Capacity={list.Capacity}");
        }

        // PERFORMANCE HACK:
        // If you know roughly how many elements you will add,
        // set Capacity or use the constructor with capacity.
        var bigList = new List<int>(capacity: 1_000_000);
        Console.WriteLine($"BigList: Count={bigList.Count}, Capacity={bigList.Capacity}");

        // This avoids multiple resize/copy cycles and reduces GC pressure.
        // Internally Resize() does something like:
        //
        //   int newCapacity = oldCapacity == 0 ? 4 : oldCapacity * 2;
        //   var newArray = new T[newCapacity];
        //   Array.Copy(_items, 0, newArray, 0, _size);
        //   _items = newArray;
        //
        // JIT & CPU VIEW:
        //   - List<T> gives you a contiguous memory block for elements.
        //   - This is extremely cache-friendly: iterating over List<T> is
        //     similar to iterating over a plain T[] in terms of locality.
        //   - On hot paths, this can be **orders of magnitude faster** than
        //     scattered allocations (e.g., linked lists).
    }

    // ---------------------------------------------------------------------
    // 2. LIST ITERATION & BOUNDS CHECKS – foreach → for
    // ---------------------------------------------------------------------
    static void ListIterationAndBoundsChecks()
    {
        Console.WriteLine();
        Console.WriteLine("=== 2. List Iteration & Bounds Checks ===");

        var list = new List<int>();
        for (int i = 0; i < 10; i++)
            list.Add(i);

        // foreach over List<T> is lowered roughly to:
        //
        //   for (int i = 0; i < list.Count; i++)
        //       Console.WriteLine(list[i]);
        //
        // plus a "version" check to detect modifications during enumeration.
        Console.WriteLine("foreach version:");
        foreach (int x in list)
        {
            Console.Write($"{x} ");
        }
        Console.WriteLine();

        // Manual for-loop is often what the JIT ends up with.
        // BOUNDS CHECK ELIMINATION:
        //   - Normally, list[i] and arr[i] insert a range check:
        //         if ((uint)i >= (uint)length) throw;
        //   - In tight, simple loops the JIT can prove "i < length" and remove
        //     the bounds check, generating extremely tight machine code.
        Console.WriteLine("for version:");
        for (int i = 0; i < list.Count; i++)
        {
            Console.Write($"{list[i]} ");
        }
        Console.WriteLine();

        // High-performance style:
        //   - Cache Count in a local variable (avoids repeated property calls).
        //   - Use index-based access for arrays and lists.
        int count = list.Count;
        int sum = 0;
        for (int i = 0; i < count; i++)
        {
            sum += list[i];
        }
        Console.WriteLine($"Sum (tight loop) = {sum}");
    }

    // ---------------------------------------------------------------------
    // 3. DICTIONARY<K,V> INTERNALS – buckets, entries, hash codes
    // ---------------------------------------------------------------------
    static void DictionaryInternalsAndHashing()
    {
        Console.WriteLine();
        Console.WriteLine("=== 3. Dictionary Internals & Hashing ===");

        var map = new Dictionary<int, string>
        {
            [1] = "Ana",
            [42] = "Carlos",
            [1001] = "Elena"
        };

        // LOGICAL OPERATION:
        //   var value = map[key];
        //
        // INTERNAL STEPS (simplified):
        //   1. Compute hashCode = key.GetHashCode() & 0x7FFFFFFF; // ensure non-negative
        //   2. bucketIndex = hashCode % buckets.Length;
        //   3. index = buckets[bucketIndex] - 1; // head of collision chain
        //   4. Walk entries[index].next chain until key.Equals(entries[i].key).
        //
        //   where:
        //     buckets: int[]    (each entry is index+1 into entries[] or 0 if empty)
        //     entries: Entry[]  (struct { int hashCode; int next; TKey key; TValue value; })
        //
        // Collisions are resolved by chaining through the "next" field.
        //
        Console.WriteLine($"Lookup 42 → {map[42]}");
        Console.WriteLine($"ContainsKey(1001) = {map.ContainsKey(1001)}");

        // HASH CODE QUALITY:
        //   - Good distribution of GetHashCode() is critical.
        //   - For custom types, override GetHashCode & Equals consistently.
        //
        //   struct Point { public int X, Y; }
        //   public override int GetHashCode() => HashCode.Combine(X, Y);
        //
        var pointDict = new Dictionary<Point2D, string>(new Point2DComparer())
        {
            [new Point2D(1, 2)] = "P1",
            [new Point2D(10, 20)] = "P2"
        };

        Console.WriteLine($"Point (1,2) → {pointDict[new Point2D(1, 2)]}");

        // CPU REALITY:
        //   - Dictionary<K,V> accesses are O(1) on average, but with a large
        //     constant factor: multiple array loads, branches, and pointer chasing.
        //   - Collisions and poor hash distribution cause longer chains, more
        //     cache misses, and branch mispredictions.
        //   - In **hot, high-frequency paths**, a carefully designed List<T> or
        //     sorted array + binary search can beat Dictionary<K,V>.
    }

    // Simple value-type key for demonstration.
    readonly struct Point2D
    {
        public readonly int X;
        public readonly int Y;

        public Point2D(int x, int y) => (X, Y) = (x, y);
    }

    // Custom comparer: shows how we control hashing & equality semantics.
    sealed class Point2DComparer : IEqualityComparer<Point2D>
    {
        // HashCode.Combine() is a high-quality, well-distributed hash combiner.
        public int GetHashCode(Point2D p) => HashCode.Combine(p.X, p.Y);

        public bool Equals(Point2D p1, Point2D p2) => p1.X == p2.X && p1.Y == p2.Y;
    }

    // ---------------------------------------------------------------------
    // 4. ADVANCED DICTIONARY PATTERNS – TryAdd, TryGetValue, value semantics
    // ---------------------------------------------------------------------
    static void DictionaryAdvancedPatterns()
    {
        Console.WriteLine();
        Console.WriteLine("=== 4. Advanced Dictionary Patterns ===");

        var cache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // TryAdd() → O(1) average insertion but without throwing on duplicates.
        cache.TryAdd("Ana", 1);
        cache.TryAdd("ANA", 99); // ignored because comparer is case-insensitive

        Console.WriteLine($"Cache['ana'] = {cache["ana"]}");

        // PATTERN: Increment counters in a dictionary-like way.
        var wordCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        void AddWord(string word)
        {
            // Using TryGetValue to avoid double hashing (ContainsKey(hash) + indexer(hash)).
            if (wordCounts.TryGetValue(word, out var count))
            {
                wordCounts[word] = count + 1;
            }
            else
            {
                wordCounts[word] = 1;
            }
        }

        AddWord("csharp");
        AddWord("csharp");
        AddWord("dotnet");

        foreach (var kv in wordCounts)
        {
            Console.WriteLine($"{kv.Key} → {kv.Value}");
        }

        // HIGH-PERFORMANCE NOTE:
        //
        //   - Prefer TryGetValue over:
        //        if (dict.ContainsKey(k)) v = dict[k];
        //     because it computes the hash code **once**.
        //
        //   - Use specialized comparers (StringComparer.OrdinalIgnoreCase, etc.)
        //     rather than rolling your own string comparisons.
        //
        //   - For read-heavy workloads where the set of keys is mostly static,
        //     consider ImmutableDictionary or frozen dictionaries for better
        //     cache behavior and thread-safety.
    }

    // ---------------------------------------------------------------------
    // 5. MICRO-BENCHMARK SHAPE – List vs Dictionary lookup cost
    // ---------------------------------------------------------------------
    static void ListVsDictionaryMicroBenchmark()
    {
        Console.WriteLine();
        Console.WriteLine("=== 5. List vs Dictionary Micro-benchmark (Conceptual) ===");

        const int N = 200_000;
        const int targetKey = N - 1;

        var list = new List<int>(N);
        var dict = new Dictionary<int, int>(N);

        for (int i = 0; i < N; i++)
        {
            list.Add(i);
            dict[i] = i;
        }

        // Warm-up JIT
        for (int i = 0; i < 5_000; i++)
        {
            _ = list[list.Count - 1];
            _ = dict[targetKey];
        }

        static long MeasureTicks(string label, Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            Console.WriteLine($"{label}: {sw.ElapsedTicks} ticks");
            return sw.ElapsedTicks;
        }

        // Linear search in list (O(N))
        MeasureTicks("List linear search (Contains)", () =>
        {
            bool found = list.Contains(targetKey);
            if (!found) throw new Exception("Should find target.");
        });

        // Direct index in list (O(1)) – but only if the index is known.
        MeasureTicks("List direct index", () =>
        {
            int value = list[targetKey]; // requires knowing the index
            if (value != targetKey) throw new Exception("Wrong value.");
        });

        // Dictionary key lookup (O(1) average)
        MeasureTicks("Dictionary key lookup", () =>
        {
            if (!dict.TryGetValue(targetKey, out var v) || v != targetKey)
                throw new Exception("Wrong value.");
        });

        // SCIENTIST-LEVEL TAKEAWAYS:
        //
        //   - Big-O is not the whole story:
        //       * O(1) dictionary lookup has non-trivial constant costs
        //         (hashing, pointer chasing, branches).
        //       * O(N) list search can be faster for small N because it is
        //         branch-predictable and cache-friendly.
        //
        //   - DESIGN RULE:
        //       * When you need random key-based access over a large set,
        //         Dictionary<K,V> is the right mental model.
        //       * When you mostly iterate sequentially, List<T> (or T[]) gives
        //         you maximum cache utilization and higher raw throughput.
        //
        //   - LLM-READY INSIGHT:
        //       * When prompting an LLM to generate data-structure-heavy code,
        //         you can explicitly state these tradeoffs:
        //             "Use a List<T> for hot loops over dense data,
        //              use Dictionary<K,V> only for sparse key lookups."
        //         That moves you closer to *intentional* architecture instead of
        //         "Dictionary for everything".
    }
}

