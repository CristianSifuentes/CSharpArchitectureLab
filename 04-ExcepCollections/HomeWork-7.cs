
partial class Program
{
  static void SalesAnalysis()
  {
    try
    {
      List<Sale> sales =
      [
        new Sale("Laptop", "Electronics", 1500),
        new Sale("Phone", "Electronics", 900),
        new Sale("Chair", "Furniture", 1200),
        new Sale("Desk", "Furniture", 800),
        new Sale("Tablet", "Electronics", 1300),
        new Sale("Lamp", "Lighting", 400)
      ];
      //Filter and show sales with an amount greater than 1000
      var highValueSales = sales.Where(s => s.Amount > 1000);
      WriteLine("Sales with an amount greater than 1000:");
      foreach (var sale in highValueSales)
      {
        WriteLine($"Product: {sale.Product}, Category: {sale.Category}, Amount: {sale.Amount:C}");
      }
      // Group sales by category and calculate the total sales per category.
      // We need to get deep knowledge of LINQ for this
      var salesByCategory = sales.GroupBy(s => s.Category).Select(g => new { Category = g.Key, TotalAmount = g.Sum(s => s.Amount) });
      WriteLine("\nTotal sales by category:");
      foreach (var group in salesByCategory)
      {
        WriteLine($"Category: {group.Category}, Total Sales: {group.TotalAmount:C}");
      }
    }
    catch (Exception ex)
    {
      WriteLine($"Error processing sales: {ex.Message}");
    }

  }
}

class Sale
{
  public string? Product { get; set; }
  public string? Category { get; set; }
  public double Amount { get; set; }

  public Sale(string product, string category, double amount)
  {
    // Propiedades de Venta
    Product = product;
    Category = category;
    Amount = amount;
  }
}