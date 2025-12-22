partial class Program
{
  static void Methods()
  {
    Car car = new Car();
    car.Model = "Yaris";
    car.Year = 2022;
    WriteLine(car.ShowInfo());
    // car.ShowMessage();
    // car.ShowMessage("Cambiando de modelo");
    // car.ChangeModel("Patrol");
    // WriteLine(car.ShowInfo());

    // Car.GeneralInfo();

    // A constructor
    Car sportsCar = new("Ferrari", 2020);
    WriteLine(sportsCar.ShowInfo());

    //Simplified syntax
    Car collectionCar = new Car { Model = "Cadillac", Year = 1980 };
    WriteLine(collectionCar.ShowInfo());

    // List of objects
    WriteLine("List of cars:");
    List<Car> cars = new()
    {
      new Car(){Model="Duster",Year=2021},
      new Car(){Model="StepWay",Year=2019},
      new Car(){Model="Captur",Year=200},
    };
    foreach (var item in cars)
    {
      WriteLine(item.ShowInfo());
    }
  }
}
class Car
{
  public string? Model { get; set; }
  public int? Year { get; set; }

  //Constructor con parametros
  public Car(string model, int year)
  {
    Model = model;
    Year = year;
  }
  //Default constructor
  public Car() { }

  //Destructor (~)
  ~Car()
  {
    WriteLine("Destructor called. Resource released");
  }

  public void ChangeModel(string newModel)
  {
    Model = newModel;
  }
  public string ShowInfo()
  {
    return $"Automobile: {Model}, Year: {Year}";
  }
  public void ShowMessage() => WriteLine("This is an automobile");
  public void ShowMessage(string message) => WriteLine(message);

  public static void GeneralInfo()
  {
    WriteLine("The automobile is one of the most widely used means of transportation.");
  }
}