using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Icons;
using Hrot.Editor.AiShared.Adapters;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Adapters;

/// <summary>
/// AIE-002 — SilkIconProvider tests.
/// </summary>
public sealed class AIE002_SilkIconProviderTests
{
    // ── Shared atlas (no GPU context required; TextureId is just an opaque int) ──

    /// <summary>Headless atlas: 256x256 px, 16px cells → 16 columns, 16 rows.</summary>
    private static IconAtlas MakeAtlas(nint textureId = 99)
        => new IconAtlas(textureId, atlasWidth: 256f, atlasHeight: 256f, iconSize: 16f);

    // ── AIE-002-01: Known key returns handle with correct atlas texture + UVs ──

    [Fact]
    public void SilkIconProvider_TryGet_KnownKey_ReturnsTrue()
    {
        var atlas    = MakeAtlas(42);
        var provider = new SilkIconProvider(atlas);

        bool found = provider.TryGet("bt/sequence", out _);

        Assert.True(found);
    }

    [Fact]
    public void SilkIconProvider_TryGet_KnownKey_ReturnsHandleWithAtlasTextureId()
    {
        var atlas    = MakeAtlas(textureId: 77);
        var provider = new SilkIconProvider(atlas);

        provider.TryGet("bt/sequence", out var handle);

        Assert.Equal((nint)77, handle.TextureId);
    }

    [Fact]
    public void SilkIconProvider_TryGet_KnownKey_ReturnsHandleWithUvMatchingAtlasCell()
    {
        // bt/sequence maps to cell "c9" in the default table.
        var atlas    = MakeAtlas(1);
        var provider = new SilkIconProvider(atlas);
        var cell     = provider.KeyToCellMap["bt/sequence"];
        var (expectedUv0, expectedUv1) = atlas.GetUvCoordinates(cell);

        provider.TryGet("bt/sequence", out var handle);

        Assert.Equal(expectedUv0, handle.Uv0);
        Assert.Equal(expectedUv1, handle.Uv1);
    }

    [Fact]
    public void SilkIconProvider_TryGet_KnownKey_UvsAreNotWholeTexture_ForSubCell()
    {
        // Any mapped key other than "a1" should not cover the entire texture.
        var atlas    = MakeAtlas(1);
        var provider = new SilkIconProvider(atlas);

        provider.TryGet("bt/action", out var handle);

        // UV pair must not be (0,0)–(1,1) – it must address a sub-cell.
        bool isWholeTexture = handle.Uv0 == Vector2.Zero && handle.Uv1 == Vector2.One;
        Assert.False(isWholeTexture, "A silk atlas cell should not cover the entire texture.");
    }

    // ── AIE-002-02: Unknown key returns false without throwing ─────────────────

    [Fact]
    public void SilkIconProvider_TryGet_UnknownKey_ReturnsFalse()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        bool found = provider.TryGet("no/such/icon", out _);

        Assert.False(found);
    }

    [Fact]
    public void SilkIconProvider_TryGet_NullKey_ReturnsFalse_NoThrow()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        // Must not throw even for null.
        bool found = provider.TryGet(null!, out _);

        Assert.False(found);
    }

    [Fact]
    public void SilkIconProvider_TryGet_EmptyKey_ReturnsFalse_NoThrow()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        bool found = provider.TryGet(string.Empty, out _);

        Assert.False(found);
    }

    // ── AIE-002-03: Coverage — every BTree and HSM catalog icon key is mapped ──

    // All static icon keys used by BTreeNodeCatalog.
    private static readonly IReadOnlyList<string> BTreeCatalogIconKeys = new[]
    {
        "bt/sequence",
        "bt/selector",
        "bt/observer_selector",
        "bt/parallel",
        "bt/root",
        "bt/composite",
        "bt/action",
        "bt/condition",
        "bt/wait",
        "bt/subtree",
        "bt/leaf",
        "bt/decorator",
    };

    // All static icon keys used by HsmNodeCatalog.
    private static readonly IReadOnlyList<string> HsmCatalogIconKeys = new[]
    {
        "hsm/state_simple",
        "hsm/state_composite",
        "hsm/state_parallel",
        "hsm/state_final",
        "hsm/state_history",
        "hsm/state_deep_history",
    };

    [Fact]
    public void SilkIconProvider_CoversAllBTreeCatalogIconKeys()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        var missing = BTreeCatalogIconKeys
            .Where(k => !provider.KeyToCellMap.ContainsKey(k))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void SilkIconProvider_CoversAllHsmCatalogIconKeys()
    {
        var provider = new SilkIconProvider(MakeAtlas());

        var missing = HsmCatalogIconKeys
            .Where(k => !provider.KeyToCellMap.ContainsKey(k))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void SilkIconProvider_CoversAllBTreeAndHsmCatalogKeys()
    {
        // Combined assertion matching the success condition name from TASK-DETAIL.
        var provider  = new SilkIconProvider(MakeAtlas());
        var allKeys   = BTreeCatalogIconKeys.Concat(HsmCatalogIconKeys).ToList();
        var missing   = allKeys
            .Where(k => !provider.TryGet(k, out _))
            .ToList();

        Assert.Empty(missing);
    }

    // ── AIE-002-04: Custom cell map override works ────────────────────────────

    [Fact]
    public void SilkIconProvider_CustomCellMap_OverridesDefault()
    {
        var atlas    = MakeAtlas(5);
        var custom   = new Dictionary<string, string> { ["custom/icon"] = "b2" };
        var provider = new SilkIconProvider(atlas, custom);

        bool found = provider.TryGet("custom/icon", out var handle);

        Assert.True(found);
        var (uv0, uv1) = atlas.GetUvCoordinates("b2");
        Assert.Equal(uv0, handle.Uv0);
        Assert.Equal(uv1, handle.Uv1);
    }

    // ── AIE-002-05: Construction is headless (no GPU calls) ──────────────────

    [Fact]
    public void SilkIconProvider_ConstructionDoesNotThrow()
    {
        // Verifies the ctor is safe to call without a GPU context.
        var atlas    = MakeAtlas(0);
        var provider = new SilkIconProvider(atlas);

        Assert.NotNull(provider);
    }
}
