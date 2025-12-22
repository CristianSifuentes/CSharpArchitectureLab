using System;
using System.Collections.Generic;
using System.Linq;

namespace CohesiveExample
{
    // ============================================================
    // 2) STATIC CLASS (Utilities / Helper)
    // ============================================================
    public static class Guard
    {
        public static void NotNullOrWhiteSpace(string? value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }

        public static void InRange(int value, int minInclusive, int maxInclusive, string paramName)
        {
            if (value < minInclusive || value > maxInclusive)
                throw new ArgumentOutOfRangeException(paramName, $"Value must be between {minInclusive} and {maxInclusive}.");
        }
    }

    // ============================================================
    // 3) ABSTRACT CLASS (Base contract + shared behavior)
    // ============================================================
    public abstract class Entity
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

        // Shared behavior
        public override string ToString() => $"{GetType().Name}({Id})";

        // Required behavior (polymorphism hook)
        public abstract string Describe();
    }

    // ============================================================
    // 1) CONCRETE CLASS (Normal instantiable class)
    // ============================================================
    public class Employee : Entity
    {
        public string Name { get; private set; }
        public int Level { get; private set; } // 1..10
        public Department Department { get; private set; }

        public Employee(string name, int level, Department department)
        {
            Guard.NotNullOrWhiteSpace(name, nameof(name));
            Guard.InRange(level, 1, 10, nameof(level));

            Name = name;
            Level = level;
            Department = department;
        }

        public void Promote()
        {
            if (Level < 10) Level++;
        }

        public override string Describe()
            => $"Employee: {Name} | Level: {Level} | Dept: {Department.Name}";
    }

    // ============================================================
    // 4) SEALED CLASS (Cannot be inherited)
    // ============================================================
    public sealed class Logger
    {
        public void Info(string message)
            => Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] INFO  {message}");

        public void Warn(string message)
            => Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] WARN  {message}");
    }

    // ============================================================
    // 7) GENERIC CLASS (Type-safe reusable storage)
    // ============================================================
    public class DataStore<T> where T : Entity
    {
        private readonly Dictionary<Guid, T> _items = new();

        public void Add(T entity) => _items[entity.Id] = entity;

        public T? Get(Guid id) => _items.TryGetValue(id, out var value) ? value : null;

        public IReadOnlyCollection<T> GetAll() => _items.Values.ToList().AsReadOnly();
    }

    // ============================================================
    // 6) NESTED CLASS (Implementation detail scoped to outer class)
    // ============================================================
    public class EmployeeDirectory
    {
        private readonly DataStore<Employee> _store;
        private readonly Logger _logger;

        public EmployeeDirectory(DataStore<Employee> store, Logger logger)
        {
            _store = store;
            _logger = logger;
        }

        public void Register(Employee employee)
        {
            _store.Add(employee);
            _logger.Info($"Registered: {employee.Describe()}");
        }

        public IReadOnlyList<Employee> FindByDepartment(string departmentName)
        {
            Guard.NotNullOrWhiteSpace(departmentName, nameof(departmentName));

            var matcher = new DepartmentMatcher(departmentName); // nested helper
            return _store.GetAll().Where(e => matcher.Matches(e.Department)).ToList();
        }

        // Nested helper: only meaningful inside EmployeeDirectory
        private sealed class DepartmentMatcher
        {
            private readonly string _target;

            public DepartmentMatcher(string target)
                => _target = target.Trim();

            public bool Matches(Department dept)
                => string.Equals(dept.Name, _target, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ============================================================
    // 5) PARTIAL CLASS (Split across files; here shown in one file)
    // ============================================================
    public partial class Department
    {
        public string Name { get; }

        public Department(string name)
        {
            Guard.NotNullOrWhiteSpace(name, nameof(name));
            Name = name;
        }
    }

    // Second "part" of Department (imagine this in Department.Extras.cs)
    public partial class Department
    {
        public static Department Engineering() => new("Engineering");
        public static Department Sales() => new("Sales");

        public override string ToString() => Name;
    }

    // ============================================================
    // 8) ANONYMOUS CLASS (Temporary shape, often with LINQ)
    // ============================================================
    // Used in Program.Main below via "select new { ... }"

    public static class Program
    {
        public static void Main()
        {
            var logger = new Logger();
            var store = new DataStore<Employee>();
            var directory = new EmployeeDirectory(store, logger);

            var engineering = Department.Engineering();
            var sales = Department.Sales();

            // Concrete class usage
            var alice = new Employee("Alice", 3, engineering);
            var bob   = new Employee("Bob", 6, sales);
            var carol = new Employee("Carol", 2, engineering);

            directory.Register(alice);
            directory.Register(bob);
            directory.Register(carol);

            alice.Promote();
            logger.Info($"After promote: {alice.Describe()}");

            // Nested class used indirectly by FindByDepartment
            var engEmployees = directory.FindByDepartment("Engineering");
            logger.Info($"Engineering count: {engEmployees.Count}");

            // Anonymous class projection (quick data grouping for display)
            var report = store.GetAll()
                .OrderByDescending(e => e.Level)
                .Select(e => new
                {
                    e.Name,
                    e.Level,
                    Department = e.Department.Name,
                    Created = e.CreatedAt
                })
                .ToList();

            Console.WriteLine("\n--- Report (Anonymous Projection) ---");
            foreach (var row in report)
                Console.WriteLine($"{row.Name,-8} | L{row.Level} | {row.Department,-12} | {row.Created:yyyy-MM-dd}");

            // Abstract class polymorphism in action:
            Entity someEntity = alice; // Employee derives from Entity
            logger.Info($"Polymorphic Describe: {someEntity.Describe()}");

            // Quick demo of reference-type behavior (class is ref type)
            var sameAliceReference = alice;
            sameAliceReference.Promote();
            logger.Warn($"Both references see same state: {alice.Describe()}");
        }
    }
}
