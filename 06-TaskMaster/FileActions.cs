using System.Text.Json;

namespace TaskMaster
{
  public class FileActions<T>
  {
    private string filePath;

    public FileActions(string name)
    {
      filePath = name;
    }

    private readonly JsonSerializerOptions _optionsWrite =
      new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly JsonSerializerOptions _optionsRead =
      new() { PropertyNameCaseInsensitive = true };

    public void WriteFile(List<T> data)
    {
      try
      {
        string content = JsonSerializer.Serialize(data, _optionsWrite);

        StreamWriter sw = new StreamWriter(filePath);
        sw.Write(content);
        sw.Dispose();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Changes saved successfully!");
        Console.ResetColor();
      }
      catch (IOException ex)
      {
        Console.WriteLine($"Error reading the file: {ex.Message}");
      }
      catch (Exception ex)
      {
        Console.WriteLine($"An error occurred while writing to the file: {ex.Message}");
      }
    }

    public List<T> ReadFile()
    {
      try
      {
        StreamReader sr = new StreamReader(filePath);
        string rawData = sr.ReadToEnd();
        List<T> data = JsonSerializer.Deserialize<List<T>>(rawData, _optionsRead)!;
        sr.Dispose();
        return data;
      }
      catch (IOException ex)
      {
        Console.WriteLine($"Error reading the file: {ex.Message}");
        return new List<T>();
      }
      catch (Exception ex)
      {
        Console.WriteLine($"An error occurred while reading the file: {ex.Message}");
        return new List<T>();
      }
    }
  }
}