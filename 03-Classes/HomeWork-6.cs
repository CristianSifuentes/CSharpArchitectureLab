partial class Program
{
  static void ShowEmpoyeesInformation()
  {
    // 7. Traverse the list of employees and display the information of each one using the "ShowInfo()" method.
    List<Employee> employees = new List<Employee>();
    employees.Add(new TeamLeader("Carlos", 5000));
    employees.Add(new Developer("Ana", 4000));
    employees.Add(new TeamLeader("Laura", 6000));
    employees.Add(new Developer("Carlos", 3500));
    WriteLine("Employee list: ");
    foreach (var employee in employees)
    {
      employee.ShowInfo();
    }
  }
}
class Employee
{
  protected string? Name { get; set; }
  protected string? Position { get; set; }
  protected double Salary { get; set; }

  public Employee(string name, double salary, string position)
  {
    Name = name;
    Salary = salary;
    Position = position;
  }
  public virtual double CalculateBonus()
  {
    return Salary * 0.05;
  }
  public void ShowInfo()
  {
    WriteLine($"Name: {Name}, Position: {Position}, Salary: {Salary:C}, Calculated Bonus: {CalculateBonus():C}");
  }
}
class TeamLeader : Employee
{
  public TeamLeader(string name, double salary) : base(name, salary, "Team Leader") { }
  public override double CalculateBonus()
  {
    return Salary * 0.10;
  }
}

class Developer : Employee
{
  public Developer(string name, double salary) : base(name, salary, "Developer") { }
  public override double CalculateBonus()
  {
    return Salary * 0.07;
  }
}