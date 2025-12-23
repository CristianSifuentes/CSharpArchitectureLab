partial class Program
{
  static void WriteFileExample()
  {
    var filePath = "./05-Files/ExampleWriting.txt";
    var content = "This will be added at the end of the file.";
    var streamWriter = new StreamWriter(filePath, append: true);
    streamWriter.WriteLine(content);
    streamWriter.WriteLine("The current time is: " + DateTime.Now.ToString("HH:mm:ss"));
    streamWriter.Dispose();
    WriteLine("File created successfully");
  }
}