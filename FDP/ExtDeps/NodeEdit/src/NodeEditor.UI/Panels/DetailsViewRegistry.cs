using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.Panels;

/// <summary>
/// Registry of <see cref="IDetailsViewProvider"/> instances.
/// Providers are iterated in descending <see cref="IDetailsViewProvider.Priority"/> order;
/// the first whose <see cref="IDetailsViewProvider.CanHandle"/> returns true wins.
/// </summary>
public interface IDetailsViewRegistry
{
    /// <summary>Register a view provider. Replaces any existing provider with the same type.</summary>
    void Register(IDetailsViewProvider provider);

    /// <summary>
    /// Find the best view for the given target.
    /// Returns null only if no provider handles the target (the caller should use FallbackDetailsView).
    /// </summary>
    IDetailsView? GetViewFor(DetailsTarget target, IDetailsContext ctx);
}

/// <summary>Default implementation of <see cref="IDetailsViewRegistry"/>.</summary>
public sealed class DetailsViewRegistry : IDetailsViewRegistry
{
    private readonly List<IDetailsViewProvider> _providers = [];

    /// <inheritdoc/>
    public void Register(IDetailsViewProvider provider)
    {
        // Replace existing entry of the same concrete type.
        int idx = _providers.FindIndex(p => p.GetType() == provider.GetType());
        if (idx >= 0)
            _providers[idx] = provider;
        else
            _providers.Add(provider);

        // Keep sorted by priority (descending) for fast iteration.
        _providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    /// <inheritdoc/>
    public IDetailsView? GetViewFor(DetailsTarget target, IDetailsContext ctx)
    {
        foreach (var p in _providers)
        {
            if (p.CanHandle(target))
                return p.Build(target, ctx);
        }
        return null;
    }
}
