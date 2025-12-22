partial class Program
{
  static void Visibility()
  {
    Jedi jedi = new Jedi();
    jedi.PowerLevel = 5000;
    jedi.LightsaberColor = "azul";

    // jedi.UseForce();
    // WriteLine(jedi.PublicField);
    // WriteLine(jedi.PrivateField);
    // WriteLine(jedi.ProtectedField);
    // jedi.RevealSecrets();

    Sith sith = new Sith();
    sith.PowerLevel = 4000;
    sith.LightsaberColor = "red";
    sith.UseForce();
    // sith.ShowProtected();
  }
}
interface IForceUser
{
  int PowerLevel { get; set; }
  string? LightsaberColor { get; set; }

  void UseForce();
}
class Jedi : IForceUser
{
  public string PublicField = "I am a Jedi and my power is known.";

  private string PrivateField = "My deepest thoughts are private.";

  protected string ProtectedField = "The dark side must not know my secrets.";
  public int PowerLevel { get; set; }
  public string? LightsaberColor { get; set; }

  public void UseForce()
  {
    WriteLine($"Soy un jedi con un sable de  luz {LightsaberColor} y mi nivel de poder es: {PowerLevel}");
  }

  private void Meditate()
  {
    WriteLine("I am in deep meditation with the Force");
  }
  protected void Train()
  {
    WriteLine("I am training to become the best Jedi.");
  }
  public void RevealSecrets()
  {
    WriteLine(ProtectedField);
    WriteLine(PrivateField);
    Meditate();
  }
}
class Sith : Jedi, IForceUser
{
  public new void UseForce()
  {
    WriteLine($"I am a Sith with a lightsaber color {LightsaberColor} and my power level is: {PowerLevel}");
  }
  public void ShowProtected()
  {
    WriteLine(ProtectedField);
    Train();
  }
}