partial class Program
{
  static void Properties()
  {
    Animal animal = new Animal("Forest");
    animal.Species = "Wolf";
    animal.Age = 5;
    WriteLine($"Where it lives: {animal.Habitat}, what animal it is: {animal.Species}, what category it has: {animal.Category}");
  }
}
class Animal
{
  public string Species { get; set; } = "Unknown";
  public string Category { get; } = "Vertebrate";
  private int age;
  public int Age
  {
    get { return age; }
    set
    {
      if (value < 0)
      {
        throw new ArgumentException("Age cannot be negative.");
      }
      age = value;
    }
  }
  public string Habitat { get; }
  public Animal(string habitat)
  {
    Habitat = habitat;
  }
}