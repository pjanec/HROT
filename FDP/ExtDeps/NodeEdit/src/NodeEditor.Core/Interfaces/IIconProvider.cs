namespace NodeEditor.Core.Interfaces;

/// <summary>Lookup for icons by string key. The host or theme provides icons.</summary>
public interface IIconProvider
{
    /// <summary>Try to resolve an icon key to a renderable handle. Returns false if unknown.</summary>
    bool TryGet(string key, out IconHandle handle);
}

/// <summary>Opaque handle to a renderable icon. Implementation defined by host.</summary>
public readonly record struct IconHandle(nint TextureId, uint Width, uint Height);
