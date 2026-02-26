using System.Runtime.InteropServices;
using System.Text;
using Fdp.Kernel;

namespace Bagira.IG.Components;

/// <summary>
/// ECS component caching the computed visual rendering state for an entity.
///
/// Written each simulation frame by <c>StyleResolutionSystem</c> from a 3-layer merge:
/// <list type="number">
///   <item>Layer 1 — TKB default: <c>IgVisualDef</c> managed component applied at spawn.</item>
///   <item>Layer 2 — Network override: <c>IgSymbolOverride</c> pushed by the IOS map layer.</item>
///   <item>Layer 3 — User config: <c>MapUserConfig</c> operator settings (highest priority).</item>
/// </list>
///
/// Designed as an unmanaged struct with named fixed-byte string buffers so it remains
/// cache-friendly and allocation-free on the hot path.  Total size is pinned strictly below
/// <see cref="ResolvedStyleConstants.MaxStyleBytes"/> (§CODE-STANDARDS §5).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[ComponentId(GlobalComponentIds.ResolvedStyle)]
public unsafe struct ResolvedStyle
{
    // ── Fixed-buffer string fields ────────────────────────────────────────────
    // Named size constants used here per §CODE-STANDARDS §1.

    private fixed byte _textureName[ResolvedStyleConstants.TextureNameMaxBytes];
    private fixed byte _labelText[ResolvedStyleConstants.LabelTextMaxBytes];

    // ── Tint color (RGBA) ─────────────────────────────────────────────────────

    /// <summary>Red channel of the entity tint colour.</summary>
    public byte TintR;
    /// <summary>Green channel of the entity tint colour.</summary>
    public byte TintG;
    /// <summary>Blue channel of the entity tint colour.</summary>
    public byte TintB;
    /// <summary>Alpha channel of the entity tint colour (255 = fully opaque).</summary>
    public byte TintA;

    // ── Affiliation ───────────────────────────────────────────────────────────

    /// <summary>Resolved force affiliation driving tint selection.</summary>
    public ForceId Affiliation;

    // ── Numeric fields ────────────────────────────────────────────────────────

    /// <summary>
    /// Current damage level in the range [<see cref="ResolvedStyleConstants.DamageMin"/>,
    /// <see cref="ResolvedStyleConstants.DamageMax"/>].
    /// 0 = healthy, 100 = fully destroyed.
    /// Updated from the <c>EntityDamage</c> DDS component.
    /// </summary>
    public float DamageLevel;

    // ── Display flags ─────────────────────────────────────────────────────────

    /// <summary>When <c>true</c> the history-trail renderer draws this entity's movement trail.</summary>
    public bool ShowTrail;

    /// <summary>When <c>true</c> the FOV-sector renderer draws sensor cone overlays.</summary>
    public bool ShowSensors;

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="ResolvedStyle"/> pre-loaded with neutral defaults:
    /// white tint, <see cref="ForceId.Unknown"/> affiliation, zero damage, all flags off.
    /// </summary>
    public static ResolvedStyle CreateDefault()
    {
        var s = new ResolvedStyle();
        s.TintR       = ResolvedStyleConstants.UnknownTintR;
        s.TintG       = ResolvedStyleConstants.UnknownTintG;
        s.TintB       = ResolvedStyleConstants.UnknownTintB;
        s.TintA       = ResolvedStyleConstants.UnknownTintA;
        s.Affiliation = ForceId.Unknown;
        s.DamageLevel = ResolvedStyleConstants.DamageMin;
        s.ShowTrail   = false;
        s.ShowSensors = false;
        return s;
    }

    // ── String helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Copies up to <see cref="ResolvedStyleConstants.TextureNameMaxBytes"/>-1 bytes of
    /// <paramref name="name"/> (UTF-8 encoded) into the fixed texture-name buffer.
    /// The buffer is always null-terminated.
    /// </summary>
    public unsafe void SetTextureName(string? name)
    {
        fixed (byte* buf = _textureName)
            WriteUtf8(buf, ResolvedStyleConstants.TextureNameMaxBytes, name);
    }

    /// <summary>Reads the texture-name fixed buffer as a UTF-8 string.</summary>
    public unsafe string GetTextureName()
    {
        fixed (byte* buf = _textureName)
            return ReadUtf8(buf, ResolvedStyleConstants.TextureNameMaxBytes);
    }

    /// <summary>
    /// Copies up to <see cref="ResolvedStyleConstants.LabelTextMaxBytes"/>-1 bytes of
    /// <paramref name="label"/> (UTF-8 encoded) into the fixed label-text buffer.
    /// The buffer is always null-terminated.
    /// </summary>
    public unsafe void SetLabelText(string? label)
    {
        fixed (byte* buf = _labelText)
            WriteUtf8(buf, ResolvedStyleConstants.LabelTextMaxBytes, label);
    }

    /// <summary>Reads the label-text fixed buffer as a UTF-8 string.</summary>
    public unsafe string GetLabelText()
    {
        fixed (byte* buf = _labelText)
            return ReadUtf8(buf, ResolvedStyleConstants.LabelTextMaxBytes);
    }

    // ── Private byte-buffer helpers ───────────────────────────────────────────

    private static unsafe void WriteUtf8(byte* buf, int capacity, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            buf[0] = 0;
            return;
        }
        // Write up to (capacity - 1) bytes to leave room for the null terminator.
        var dest = new Span<byte>(buf, capacity);
        int written = Encoding.UTF8.GetBytes(text, dest[..^1]);
        dest[written] = 0;
    }

    private static unsafe string ReadUtf8(byte* buf, int capacity)
    {
        int len = 0;
        while (len < capacity && buf[len] != 0)
            len++;
        return len == 0 ? string.Empty : Encoding.UTF8.GetString(new ReadOnlySpan<byte>(buf, len));
    }
}
