partial class Program
{
  static string? amount;
  static void HandleException()
  {
    try
    {
      // int number = 10;
      // int result = number / 0;
      Write("Enter an amount:\n: ");
      amount = ReadLine();
      if (string.IsNullOrEmpty(amount)) return;

      if (double.TryParse(amount, out double amountValue))
      {
        WriteLine($"The amount you entered is: {amountValue:C}");
      }
      else
      {
        WriteLine("Could not convert the text to a number");
      }
      // double amountValue = double.Parse(amount);
      ValidateAge(16);
    }
    catch (DivideByZeroException)
    {
      Console.ForegroundColor = ConsoleColor.Red;
      WriteLine("Error: Division by zero");
    }
    catch (FormatException) when (amount?.Contains('$') == true)
    {
      Console.ForegroundColor = ConsoleColor.Red;
      WriteLine("No need to use '$'");
    }
    catch (Exception ex)
    {
      Console.ForegroundColor = ConsoleColor.Red;
      WriteLine(ex.Message);
    }
    finally
    {
      Console.ResetColor();
      WriteLine("This will always execute...");
    }
  }
  static void ValidateAge(int age)
  {
    if (age < 18)
    {
      throw new ArgumentException("Age must be greater than 18");
    }
  }
}