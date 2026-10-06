using System.Collections.Generic;
using System.Numerics;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// ⭐ CE-1001 — one document's view of a SHARED picker registry. Sources it registers (<c>nodes.all</c>,
/// <c>nodes.by-pin</c>, …) are stored under a per-document prefix, and lookups prefer them; keys it never registered
/// fall through to the shared registry unchanged.
/// <para>📐 Why: every AI editor document used to register its node pickers straight into the one process-wide
/// registry, so the LAST opened BTree/Blueprint document owned <c>nodes.all</c> for every canvas — an HSM canvas
/// (which registered none) offered BTree or Blueprint nodes, and two open Blueprint documents offered each other's.
/// Two producers for one slot, bound by registration order.</para>
/// </summary>
public sealed class ScopedPickerRegistry : IPickerRegistry
{
    private readonly IPickerRegistry _inner;
    private readonly string _prefix;
    private readonly HashSet<string> _own = new();

    public ScopedPickerRegistry(IPickerRegistry inner, string scope)
    {
        _inner = inner;
        _prefix = scope + "/";
    }

    /// <summary>The key this scope stores <paramref name="sourceKey"/> under in the shared registry.</summary>
    public string Resolve(string sourceKey) => _own.Contains(sourceKey) ? _prefix + sourceKey : sourceKey;

    public void Register<TItem>(string sourceKey, IPickerSource<TItem> source)
    {
        _own.Add(sourceKey);
        _inner.Register(_prefix + sourceKey, source);
    }

    public IPickerSource<TItem>? Get<TItem>(string sourceKey) => _inner.Get<TItem>(Resolve(sourceKey));

    public void Open(
        string sourceKey,
        Vector2 screenPos,
        System.Action<object> onPick,
        System.Action? onCancel = null,
        IReadOnlyDictionary<string, object?>? context = null)
        => _inner.Open(Resolve(sourceKey), screenPos, onPick, onCancel, context);

    public void DrawFrame() => _inner.DrawFrame();
}
