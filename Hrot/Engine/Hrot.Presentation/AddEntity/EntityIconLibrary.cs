using System.Numerics;
using Fdp.Toolkit.Orchestration;
using NodeEditor.Core.Interfaces;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ <c>CE-1017</c> S5 — the entity icon library: serves <c>entity/&lt;name&gt;</c> icon keys as textures, for the Add
/// Entity picker's rows and preview pane. 📄 docs/DESIGN_Add_Entity_Picker.md D3c, S5.
///
/// <para>⭐ Where an icon comes from: <c>&lt;icons folder&gt;/&lt;name&gt;.png</c> — ⭐ <c>CE-4089</c>: the NAS's <c>assets/icons/</c>
/// (<see cref="ContentLayout.IconAssets"/>; a dev start seeds it from the repository's <c>assets/icons/</c>, rasterized by
/// <c>scripts/render-entity-icons.py</c> from the hand-drawn SVG sources in <c>assets/icons/src</c>) — the folder <c>IgVisualDef.IconName</c> names, so a
/// TKB author adds art without a rebuild. ⛔ Nothing is embedded in the assembly any more and nothing is read from the exe folder. A name with no art
/// falls back to <c>_point</c>, so a TKB that names a missing icon still shows a marker, not a gap.</para>
///
/// <para>⭐ Every other key goes to <paramref name="fallback"/> — the host's existing provider (silk atlas: folder icons,
/// node icons) — so one provider serves a whole picker. Textures are uploaded lazily on first use (inside the ImGui
/// frame, where a GL context exists) and cached; a failed upload (texture id 0, a headless GL) is cached as "no icon".</para>
/// </summary>
public sealed class EntityIconLibrary : IIconProvider
{
    /// <summary>Key prefix this library serves.</summary>
    public const string Prefix = EntityTypeCatalog.IconKeyPrefix;
    /// <summary>Icon used for a name with no art.</summary>
    public const string MissingName = "_point";

    private readonly IIconProvider? _fallback;
    private readonly Func<string, byte[]?> _png;
    private readonly Func<byte[], (nint TextureId, int Width, int Height)?> _upload;
    private readonly Dictionary<string, IconHandle?> _cache = new(StringComparer.Ordinal);

    /// <summary>Creates the library.</summary>
    /// <param name="fallback">Serves every non-<c>entity/</c> key (the host's icon provider), or null.</param>
    /// <param name="overrideDirectory">The folder the PNGs are read from; null ⇒ the NAS's <c>assets/icons</c>.</param>
    /// <param name="upload">Turns PNG bytes into a texture; null ⇒ raylib. Tests inject a fake.</param>
    public EntityIconLibrary(IIconProvider? fallback = null, string? overrideDirectory = null,
                             Func<byte[], (nint TextureId, int Width, int Height)?>? upload = null)
    {
        _fallback = fallback;
        string dir = overrideDirectory ?? ContentLayout.IconAssets(ContentLayout.NasRoot);
        _png = name => ReadPng(dir, name);
        _upload = upload ?? RaylibUpload;
    }

    /// <summary>The icon names (<c>*.png</c> without the extension, ordinal order) in <paramref name="iconsFolder"/>.</summary>
    public static IReadOnlyList<string> NamesIn(string iconsFolder) => !Directory.Exists(iconsFolder)
        ? Array.Empty<string>()
        : Directory.EnumerateFiles(iconsFolder, "*.png", SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    /// <inheritdoc/>
    public bool TryGet(string key, out IconHandle handle)
    {
        if (key is null || !key.StartsWith(Prefix, StringComparison.Ordinal))
        {
            if (_fallback is not null) return _fallback.TryGet(key!, out handle);
            handle = default;
            return false;
        }

        string name = key[Prefix.Length..];
        if (!_cache.TryGetValue(name, out var cached))
        {
            cached = Load(name) ?? (name == MissingName ? null : Load(MissingName));
            _cache[name] = cached;
        }
        handle = cached ?? default;
        return cached is not null;
    }

    private IconHandle? Load(string name)
    {
        var bytes = _png(name);
        if (bytes is null) return null;
        var tex = _upload(bytes);
        if (tex is not { } t || t.TextureId == 0) return null;
        return new IconHandle(t.TextureId, (uint)t.Width, (uint)t.Height, Vector2.Zero, Vector2.One);
    }

    private static byte[]? ReadPng(string dir, string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..")) return null;
        try
        {
            var path = Path.Combine(dir, name + ".png");
            if (File.Exists(path)) return File.ReadAllBytes(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private static (nint, int, int)? RaylibUpload(byte[] png)
    {
        var img = Raylib_cs.Raylib.LoadImageFromMemory(".png", png);
        if (img.Width == 0) return null;
        var tex = Raylib_cs.Raylib.LoadTextureFromImage(img);
        Raylib_cs.Raylib.UnloadImage(img);
        if (tex.Id == 0) return null;
        Raylib_cs.Raylib.SetTextureFilter(tex, Raylib_cs.TextureFilter.Bilinear);
        return ((nint)tex.Id, tex.Width, tex.Height);
    }
}
