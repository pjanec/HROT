using NodeEditor.Core.Interfaces;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>No-op icon provider — the demo uses no texture atlas.</summary>
public sealed class FakeIconProvider : IIconProvider
{
    public bool TryGet(string key, out IconHandle handle)
    {
        handle = default;
        return false;
    }
}
