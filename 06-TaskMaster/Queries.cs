using System.Data;
using BetterConsoles.Tables;
using BetterConsoles.Tables.Configuration;

namespace TaskMaster
{
  public class Queries(List<Task> _tasks)
  {
    private List<Task> Tasks = _tasks;

    public void ListTasks()
    {
      Console.ForegroundColor = ConsoleColor.DarkBlue;
      Console.WriteLine("-----List of tasks-----");
      Table table = new Table("Id", "Description", "State");
      foreach (var task in Tasks)
      {
        table.AddRow(task.Id, task.Description, task.Completed ? "Completed" : "");
      }
      table.Config = TableConfig.Unicode();

      Write(table.ToString());
      ReadKey();
    }
    public List<Task> AddTask()
    {
      try
      {
        ResetColor();
        Clear();
        Console.WriteLine("---Add task---");
        Console.WriteLine("Enter the task description: ");
        var description = Console.ReadLine()!;
        Task newTask = new Task(Utils.GenerateId(), description);
        Tasks.Add(newTask);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Task added successfully");
        Console.ResetColor();
        return Tasks;
      }
      catch (Exception ex)
      {
        ForegroundColor = ConsoleColor.Red;
        WriteLine(ex.Message);
        return Tasks;
      }
    }
    public List<Task> MarkAsCompleted()
    {
      try
      {
        Console.ResetColor();
        Console.Clear();
        Console.WriteLine("---Mark task as completed---");
        Console.Write("Enter the id of the task to mark as completed: ");
        var id = Console.ReadLine()!;
        Task task = Tasks.Find(t => t.Id == id)!;
        if (task == null)
        {
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine("Task with the provided ID was not found");
          Console.ResetColor();
          return Tasks;
        }
        task.Completed = true;
        task.ModifiedAt = DateTime.Now;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Task marked as completed successfully");
        Console.ResetColor();
        return Tasks;
      }
      catch (Exception ex)
      {
        Console.ForegroundColor = ConsoleColor.Red;
        WriteLine(ex.Message);
        return Tasks;
      }
    }
    public List<Task> EditTask()
    {
      try
      {
        Console.ResetColor();
        Console.Clear();
        Console.WriteLine("---Edit task---");
        Console.Write("Enter the id to edit the task: ");
        var id = Console.ReadLine()!;
        Task task = Tasks.Find(t => t.Id == id)!;
        if (task == null)
        {
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine("Task with the provided ID was not found");
          Console.ResetColor();
          return Tasks;
        }
        Console.Write("Enter the task description: ");
        var description = Console.ReadLine()!;
        task.Description = description;
        task.ModifiedAt = DateTime.Now;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Task edited successfully");
        Console.ResetColor();
        return Tasks;
      }
      catch (Exception ex)
      {
        Console.ForegroundColor = ConsoleColor.Red;
        WriteLine(ex.Message);
        return Tasks;
      }
    }
    public List<Task> RemoveTask()
    {
      try
      {
        ResetColor();
        Console.Clear();
        Console.WriteLine("---Remove task---");
        Console.Write("Enter the id to remove the task: ");
        var id = Console.ReadLine()!;
        Task task = Tasks.Find(t => t.Id == id)!;
        if (task == null)
        {
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine("Task with the provided ID was not found");
          Console.ResetColor();
          return Tasks;
        }
        Tasks.Remove(task);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Task removed successfully");
        Console.ResetColor();
        return Tasks;
      }
      catch (Exception ex)
      {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(ex.Message);
        return Tasks;
      }
    }
    public void TasksByState()
    {
      Clear();
      try
      {
        ResetColor();
        WriteLine("---Tareas por estado ---");
        WriteLine("1. Completadas");
        WriteLine("2. Pedientes");
        Write("Ingrese la opción de las tareas a mostrar: ");
        string taskState = ReadLine()!;
        if (taskState != "1" && taskState != "2")
        {
          ForegroundColor = ConsoleColor.Red;
          WriteLine("Opción inválida");
          ResetColor();
          return;
        }
        bool completed = taskState == "1";
        List<Task> filteredTasks = Tasks.Where(t => t.Completed == completed).ToList();
        if (filteredTasks.Count == 0)
        {
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine("No se encontrarón tareas con el estado solicitado");
          Console.ResetColor();
          return;
        }
        Console.ForegroundColor = completed ? ConsoleColor.Green : ConsoleColor.Red;
        Table table = new Table("Id", "Descripción", "Estado");
        foreach (var task in filteredTasks)
        {
          table.AddRow(task.Id, task.Description, task.Completed ? "Completada" : "");
        }
        table.Config = TableConfig.Unicode();

        Write(table.ToString());
        ReadKey();

      }
      catch (Exception ex)
      {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Ocurrió un error al filtrar las tareas: {ex.Message}");
      }
    }
    public void TasksByDescription()
    {
      Clear();
      try
      {
        Console.ResetColor();
        Console.WriteLine("---Tareas por descripción---");
        Console.Write("Ingrese la descripción de las tareas a buscar: ");
        string description = Console.ReadLine()!;
        List<Task> matchingTasks = Tasks.FindAll(t => t.Description?.Contains(description, StringComparison.OrdinalIgnoreCase) ?? false);
        if (matchingTasks.Count == 0)
        {
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine("No se encontrarón tareas con la descripción proporcionada.");
          Console.ResetColor();
          return;
        }
        Table table = new Table("Id", "Descripción", "Estado");
        foreach (var task in matchingTasks)
        {
          table.AddRow(task.Id, task.Description, task.Completed ? "Completed" : "");
        }
        table.Config = TableConfig.Unicode();

        Console.Write(table.ToString());
        Console.ReadKey();

      }
      catch (Exception ex)
      {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"An error occurred while filtering tasks by description: {ex.Message}");
      }
    }
  }
}