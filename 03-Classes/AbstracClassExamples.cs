partial class Program
{
  static void AbstracClassExamples()
  {
    HomeAppliance myWasher = new WashingMachine { Brand = "Samdung" };
    HomeAppliance myMicrowave = new Microwave { Brand = "DG" };
    myWasher.ShowBrand();
    myWasher.TurnOn();
    myMicrowave.ShowBrand();
    myMicrowave.TurnOn();
  }
}
abstract class HomeAppliance
{
  public string? Brand { get; set; }

  public abstract void TurnOn();

  public void ShowBrand()
  {
    WriteLine($"The brand of the appliance: {Brand}");
  }
}
class WashingMachine : HomeAppliance
{
  public override void TurnOn()
  {
    WriteLine("🌀 The washing machine has started the washing cycle");
  }
}

class Microwave : HomeAppliance
{
  public override void TurnOn()
  {
    WriteLine("🔥 The microwave is heating the food.");
  }
}