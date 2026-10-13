using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

public sealed class TargetViewService(IConfigRepository repository, IAuditService audit) : ITargetViewService
{
    public IReadOnlyList<TargetViewDefinition> List() => repository.Load().TargetViews
        .OrderBy(view => view.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public TargetViewDefinition Find(string nameOrId) => Find(repository.Load(), nameOrId);

    public TargetViewDefinition Save(string name, string expression, string? id = null)
    {
        name = name.Trim();
        expression = expression.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new ArgumentException("Use a view name of 1 to 80 characters.");
        _ = TargetViewExpression.Compile(expression);
        var result = new TargetViewDefinition { Id = id ?? Guid.NewGuid().ToString("N"), Name = name, Expression = expression };
        repository.Update(config =>
        {
            if (config.TargetViews.Any(view => view.Id != result.Id && view.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A target view with that name already exists.");
            if (id is not null && config.TargetViews.All(view => view.Id != id)) throw new ArgumentException("Target view not found.");
            config.TargetViews.RemoveAll(view => view.Id == result.Id);
            config.TargetViews.Add(result);
        });
        audit.Record(new() { Category = "config", EventType = "target-view.saved", Action = result.Name });
        return result;
    }

    public void Delete(string nameOrId)
    {
        string? name = null;
        repository.Update(config =>
        {
            var view = Find(config, nameOrId);
            name = view.Name;
            config.TargetViews.RemoveAll(existing => existing.Id == view.Id);
        });
        audit.Record(new() { Category = "config", EventType = "target-view.deleted", Action = name });
    }

    public IReadOnlyList<ServerHost> Resolve(string nameOrId)
    {
        var config = repository.Load();
        var matches = TargetViewExpression.Compile(Find(config, nameOrId).Expression);
        return config.Hosts.Where(host => matches(host.Tags)).OrderByDescending(host => host.IsFavorite)
            .ThenBy(host => host.Alias, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static TargetViewDefinition Find(AppConfig config, string nameOrId)
    {
        var matches = config.TargetViews.Where(view => view.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
            view.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : throw new ArgumentException(matches.Count == 0
            ? "Target view not found." : "Target view is ambiguous; use its ID.");
    }
}
