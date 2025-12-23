partial class Program
{
  static void PathExample()
  {
    var filePath = "./05-Files/Example.txt";
    var fileName = Path.GetFileName(filePath);
    WriteLine($"file name: {fileName}");
    var fileExtension = Path.GetExtension(filePath);
    WriteLine($"file extension: {fileExtension}");
    var directoryName = Path.GetDirectoryName(filePath);
    WriteLine($"directory name: {directoryName}");
    var combinedPath = Path.Combine("C:", "User", "Documents", "Example.txt");
    WriteLine($"combined path: {combinedPath}");
    var fullFilePath = Path.GetFullPath(filePath);
    WriteLine($"full file path: {fullFilePath}");
  }
}