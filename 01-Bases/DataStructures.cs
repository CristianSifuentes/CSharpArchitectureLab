// File: DataTypesDeepDive.cs
// Author: You + ChatGPT
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



partial class Program
{
  static void DataStructures()
  {
    User pedro = new User { Name = "Pedro", Age = 33 };
    pedro.Greet();
    Point punto = new Point { X = 30, Y = 20 };
    Console.WriteLine($"Punto ({punto.X},{punto.Y})");
    CellPhone nokia = new CellPhone("Nokia 225", 2024);
    System.Console.WriteLine(nokia);
  }
}
class User
{
  public string? Name { get; set; }
  public int Age { get; set; }

  public void Greet()
  {
    Console.WriteLine($"Hola, soy el usuario {Name} y tengo una edad de {Age} años");
  }
}
struct Point
{
  public int X { get; set; }
  public int Y { get; set; }
}

record CellPhone(string Model, int Year);