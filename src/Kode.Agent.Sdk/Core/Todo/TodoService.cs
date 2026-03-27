using Microsoft.Extensions.Logging;

namespace Kode.Agent.Sdk.Core.Todo;

/// <summary>
/// Todo item status.
/// </summary>
public enum TodoStatus
{
    Pending,
    InProgress,
    Completed
}

/// <summary>
/// Todo item.
/// </summary>
public record TodoItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public TodoStatus Status { get; init; } = TodoStatus.Pending;
    public string? Assignee { get; init; }
    public string? Notes { get; init; }
    public long CreatedAt { get; init; }
    public long UpdatedAt { get; init; }
}

/// <summary>
/// Todo snapshot for persistence.
/// </summary>
public record TodoSnapshot
{
    public required IReadOnlyList<TodoItem> Todos { get; init; }
    public int Version { get; init; } = 1;
    public long UpdatedAt { get; init; }
}

/// <summary>
/// Todo input for creating/updating items.
/// </summary>
public record TodoInput
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public TodoStatus Status { get; init; } = TodoStatus.Pending;
    public string? Assignee { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// Todo service for managing task lists.
/// </summary>
public class TodoService
{
    private const int MaxInProgress = 1;
    
    private readonly IAgentStore _store;
    private readonly string _agentId;
    private readonly ILogger<TodoService>? _logger;
    private TodoSnapshot _snapshot = new() { Todos = [], Version = 1, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };

    public TodoService(IAgentStore store, string agentId, ILogger<TodoService>? logger = null)
    {
        _store = store;
        _agentId = agentId;
        _logger = logger;
    }

    /// <summary>
    /// Load todos from storage.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var existing = await LoadSnapshotAsync(cancellationToken);
        if (existing != null)
        {
            _snapshot = existing;
        }
    }

    /// <summary>
    /// List all todos.
    /// </summary>
    public IReadOnlyList<TodoItem> List() => _snapshot.Todos;

    /// <summary>
    /// Set all todos (replace existing list).
    /// </summary>
    public async Task SetTodosAsync(
        IEnumerable<TodoInput> todos,
        CancellationToken cancellationToken = default)
    {
        var normalized = todos.Select(Normalize).ToList();
        ValidateTodos(normalized);
        
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _snapshot = new TodoSnapshot
        {
            Todos = normalized.Select(t => t with { UpdatedAt = now }).ToList(),
            Version = _snapshot.Version + 1,
            UpdatedAt = now
        };
        
        await PersistAsync(cancellationToken);
        _logger?.LogDebug("Set {Count} todos", normalized.Count);
    }

    /// <summary>
    /// Update a single todo.
    /// </summary>
    public async Task UpdateAsync(
        TodoInput todo,
        CancellationToken cancellationToken = default)
    {
        var existing = _snapshot.Todos.FirstOrDefault(t => t.Id == todo.Id);
        if (existing == null)
        {
            throw new KeyNotFoundException($"Todo not found: {todo.Id}");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var updated = new TodoItem
        {
            Id = todo.Id,
            Title = todo.Title,
            Status = todo.Status,
            Assignee = todo.Assignee ?? existing.Assignee,
            Notes = todo.Notes ?? existing.Notes,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = now
        };

        var newTodos = _snapshot.Todos.Select(t => t.Id == todo.Id ? updated : t).ToList();
        ValidateTodos(newTodos);

        _snapshot = _snapshot with
        {
            Todos = newTodos,
            Version = _snapshot.Version + 1,
            UpdatedAt = now
        };

        await PersistAsync(cancellationToken);
        _logger?.LogDebug("Updated todo: {Id}", todo.Id);
    }

    /// <summary>
    /// Delete a todo.
    /// </summary>
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var newTodos = _snapshot.Todos.Where(t => t.Id != id).ToList();
        
        _snapshot = _snapshot with
        {
            Todos = newTodos,
            Version = _snapshot.Version + 1,
            UpdatedAt = now
        };

        await PersistAsync(cancellationToken);
        _logger?.LogDebug("Deleted todo: {Id}", id);
    }

    /// <summary>
    /// Add a new todo.
    /// </summary>
    public async Task AddAsync(
        TodoInput todo,
        CancellationToken cancellationToken = default)
    {
        if (_snapshot.Todos.Any(t => t.Id == todo.Id))
        {
            throw new InvalidOperationException($"Todo already exists: {todo.Id}");
        }

        var normalized = Normalize(todo);
        var newTodos = _snapshot.Todos.Append(normalized).ToList();
        ValidateTodos(newTodos);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _snapshot = _snapshot with
        {
            Todos = newTodos,
            Version = _snapshot.Version + 1,
            UpdatedAt = now
        };

        await PersistAsync(cancellationToken);
        _logger?.LogDebug("Added todo: {Id}", todo.Id);
    }

    /// <summary>
    /// Get a todo by ID.
    /// </summary>
    public TodoItem? Get(string id) => _snapshot.Todos.FirstOrDefault(t => t.Id == id);

    /// <summary>
    /// Get todos by status.
    /// </summary>
    public IReadOnlyList<TodoItem> GetByStatus(TodoStatus status) =>
        _snapshot.Todos.Where(t => t.Status == status).ToList();

    /// <summary>
    /// Get the current snapshot version.
    /// </summary>
    public int Version => _snapshot.Version;

    /// <summary>
    /// Get count of todos.
    /// </summary>
    public int Count => _snapshot.Todos.Count;

    private void ValidateTodos(IReadOnlyList<TodoItem> todos)
    {
        var ids = new HashSet<string>();
        var inProgressCount = 0;

        foreach (var todo in todos)
        {
            if (string.IsNullOrEmpty(todo.Id))
            {
                throw new InvalidOperationException("Todo id is required");
            }

            if (!ids.Add(todo.Id))
            {
                throw new InvalidOperationException($"Duplicate todo id: {todo.Id}");
            }

            if (string.IsNullOrWhiteSpace(todo.Title))
            {
                throw new InvalidOperationException($"Todo {todo.Id} must have a title");
            }

            if (todo.Status == TodoStatus.InProgress)
            {
                inProgressCount++;
            }
        }

        if (inProgressCount > MaxInProgress)
        {
            throw new InvalidOperationException("Only one todo can be in progress");
        }
    }

    private static TodoItem Normalize(TodoInput input)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new TodoItem
        {
            Id = input.Id,
            Title = input.Title,
            Status = input.Status,
            Assignee = input.Assignee,
            Notes = input.Notes,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private async Task<TodoSnapshot?> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        // Load from store - this would be implemented in the store interface
        // For now, return null (no existing todos)
        await Task.CompletedTask;
        return null;
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        // Persist to store - this would be implemented in the store interface
        await Task.CompletedTask;
    }
}
