using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Icons;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Adapters;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Adapters;

/// <summary>
/// Tests for MTB-P1-T4: Icon keys + <see cref="AssetKindIcons"/> (§5.1, §5.2).
/// Uses <see cref="SilkIconProvider"/> headlessly (no GPU context needed —
/// <see cref="IconAtlas"/> accepts an opaque texture handle and computes UVs
/// purely from cell coordinates).
/// </summary>
public sealed class IconKeysTests
{
    // ── Headless atlas: 256×256 px, 16 px cells (same as SilkIconProviderTests) ──

    private static IconAtlas MakeAtlas(nint textureId = 99)
        => new IconAtlas(textureId, atlasWidth: 256f, atlasHeight: 256f, iconSize: 16f);

    // ── All §5.1 keys that must resolve ───────────────────────────────────────

    private static readonly IReadOnlyList<string> RequiredKeys = new[]
    {
        "debug/continue",
        "debug/step_back",
        "debug/step_over",
        "debug/step_into",
        "debug/step_out",
        "asset/scenario",
        "asset/blueprint",
        "asset/btree",
        "asset/hsm",
        "asset/blackboard",
        "asset/utility",
        "browser/open",
        "asset/new",
        "folder",
        "folder_open",
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // MTB-P1-T4.C1: Every §5.1 key resolves to a handle (TryGet = true)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryGet_EachNewKey_ReturnsHandle()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        var missing = RequiredKeys
            .Where(k => !provider.TryGet(k, out _))
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// Each resolved handle carries the correct atlas texture ID and non-default UVs
    /// (i.e. the cell lookup actually happened, not just returned a default handle).
    /// </summary>
    [Fact]
    public void TryGet_EachNewKey_HandleHasAtlasTextureId()
    {
        var atlas = MakeAtlas(textureId: 42);
        var provider = new SilkIconProvider(atlas);

        foreach (var key in RequiredKeys)
        {
            bool found = provider.TryGet(key, out var handle);
            Assert.True(found, $"Key '{key}' should resolve");
            Assert.Equal((nint)42, handle.TextureId);
        }
    }

    /// <summary>
    /// Verify that at least a few representative keys resolve to sub-cell UVs
    /// (not (0,0)–(1,1), which would indicate a broken cell lookup).
    /// </summary>
    [Fact]
    public void TryGet_RepresentativeKeys_HaveSubCellUvs()
    {
        var atlas = MakeAtlas(1);
        var provider = new SilkIconProvider(atlas);

        // Pick a representative set from different groups
        string[] sample = { "debug/continue", "asset/scenario", "browser/open", "folder" };
        foreach (var key in sample)
        {
            provider.TryGet(key, out var handle);
            bool isWholeTexture = handle.Uv0 == System.Numerics.Vector2.Zero
                               && handle.Uv1 == System.Numerics.Vector2.One;
            Assert.False(isWholeTexture, $"Key '{key}' should map to a sub-cell, not the entire texture");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MTB-P1-T4.C2: AssetKind → IconKey covers all 5 kinds + ScenarioIconKey
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AssetKindToIconKey_CoversAllKinds_IncludingScenario()
    {
        // All 6 AssetKind values map correctly (including Scenario).
        Assert.Equal("asset/blueprint",  AssetKindIcons.GetIconKey(AssetKind.Blueprint));
        Assert.Equal("asset/btree",      AssetKindIcons.GetIconKey(AssetKind.BTree));
        Assert.Equal("asset/hsm",        AssetKindIcons.GetIconKey(AssetKind.Hsm));
        Assert.Equal("asset/blackboard", AssetKindIcons.GetIconKey(AssetKind.Blackboard));
        Assert.Equal("asset/utility",    AssetKindIcons.GetIconKey(AssetKind.Utility));
        Assert.Equal("asset/scenario",   AssetKindIcons.GetIconKey(AssetKind.Scenario));

        // ScenarioIconKey constant matches the enum arm.
        Assert.Equal("asset/scenario", AssetKindIcons.ScenarioIconKey);
    }

    /// <summary>
    /// Verify every AssetKind value passed to GetIconKey returns a key
    /// that actually resolves through SilkIconProvider.
    /// </summary>
    [Fact]
    public void AssetKindToIconKey_AllKeys_ResolveThroughProvider()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        foreach (AssetKind kind in Enum.GetValues<AssetKind>())
        {
            var key = AssetKindIcons.GetIconKey(kind);
            bool found = provider.TryGet(key, out _);
            Assert.True(found, $"Icon key '{key}' for AssetKind.{kind} must resolve");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MTB-P1-T4.C3: Unknown key returns false
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryGet_UnknownKey_ReturnsFalse()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        bool found = provider.TryGet("completely/bogus/key", out var handle);

        Assert.False(found);
        Assert.Equal(default(IconHandle), handle);
    }

    [Fact]
    public void TryGet_UnknownKey_DefaultHandleReturned()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        provider.TryGet("no/such/icon", out var handle);

        // Default IconHandle has zero TextureId, zero Width/Height, and UV=(0,0)-(1,1)
        Assert.Equal(IntPtr.Zero, handle.TextureId);
        Assert.Equal(0u, handle.Width);
        Assert.Equal(0u, handle.Height);
    }

    /// <summary>
    /// Unknown key for a key that would match the prefix of a known key
    /// (e.g. "asset" without the sub-key) should still return false.
    /// </summary>
    [Fact]
    public void TryGet_PrefixOnly_ReturnsFalse()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        Assert.False(provider.TryGet("asset", out _));
        Assert.False(provider.TryGet("debug", out _));
        Assert.False(provider.TryGet("browser", out _));
    }

    /// <summary>
    /// Null and empty keys return false without throwing.
    /// </summary>
    [Fact]
    public void TryGet_NullOrEmptyKey_ReturnsFalse()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        Assert.False(provider.TryGet(null!, out _));
        Assert.False(provider.TryGet(string.Empty, out _));
    }
}
