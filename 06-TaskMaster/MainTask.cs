using TaskMaster;
namespace TaskMaster
{
  partial class Program
  {
    static FileActions<Task> fileActions = new("./06-TaskMaster/tasks.json");
    static List<Task> tasks = fileActions.ReadFile();
    static Queries queries = new(tasks);

    public static void TaskMaster()
    {
      bool salir = false;
      while (!salir)
      {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("------Menú de tareas------");
        Console.WriteLine("\n1. List tareas");
        Console.WriteLine("2. Add tarea");
        Console.WriteLine("3. Mark tarea as completed");
        Console.WriteLine("4. Edit tarea");
        Console.WriteLine("5. Remove tarea");
        Console.WriteLine("6. Query tareas by state");
        Console.WriteLine("7. Query tarea by description");
        Console.WriteLine("8. Exit");
        Console.Write("\nSelect an option: ");

        switch (Console.ReadLine())
        {
          case "1":
            queries.ListTasks();
            break;
          case "2":
            AddTask();
            break;
          case "3":
            MarkAsCompleted();
            break;
          case "4":
            EditTask();
            break;
          case "5":
            RemoveTask();
            break;
          case "6":
            queries.TasksByState();
            break;
          case "7":
            queries.TasksByDescription();
            break;
          case "8":
            salir = true;
            Console.Clear();
            break;
          default:
            Console.Clear();
            Console.WriteLine("Invalid option. Please try again.");
            break;
        }
      }
    }
    public static void AddTask()
    {
      try
      {
        var tasks = queries.AddTask();
        fileActions.WriteFile(tasks);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"An error occurred while adding the task: {ex.Message}");
      }
    }
    public static void MarkAsCompleted()
    {
      try
      {
        var tasks = queries.MarkAsCompleted();
        fileActions.WriteFile(tasks);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"An error occurred while marking the task as completed: {ex.Message}");
      }
    }
    public static void EditTask()
    {
      try
      {
        var tasks = queries.EditTask();
        fileActions.WriteFile(tasks);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"An error occurred while editing the task: {ex.Message}");
      }
    }
    public static void RemoveTask()
    {
      try
      {
        var tasks = queries.RemoveTask();
        fileActions.WriteFile(tasks);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"An error occurred while removing the task: {ex.Message}");
      }
    }
  }
}
