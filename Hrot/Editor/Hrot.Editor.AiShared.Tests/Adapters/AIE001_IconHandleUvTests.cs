using System.Numerics;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Adapters;

/// <summary>
/// AIE-001 — IconHandle UV-rect support.
/// Verifies the whole-texture default and that explicit UV coordinates are
/// stored and retrievable.
/// </summary>
public sealed class AIE001_IconHandleUvTests
{
    // ── AIE-001-01: Default UVs cover the whole texture ───────────────────────

    [Fact]
    public void IconHandle_DefaultUvs_CoverWholeTexture()
    {
        // Whole-texture 3-arg constructor.
        var handle = new IconHandle(textureId: 42, width: 16, height: 16);

        Assert.Equal(Vector2.Zero, handle.Uv0);
        Assert.Equal(Vector2.One, handle.Uv1);
    }

    [Fact]
    public void IconHandle_WholeTex_TextureIdAndSizePreserved()
    {
        var handle = new IconHandle(textureId: 99, width: 32, height: 64);

        Assert.Equal((nint)99, handle.TextureId);
        Assert.Equal(32u, handle.Width);
        Assert.Equal(64u, handle.Height);
    }

    // ── AIE-001-02: Explicit UV sub-rect is forwarded correctly ───────────────

    [Fact]
    public void IconHandle_ExplicitUv_StoredExactly()
    {
        var uv0 = new Vector2(0.25f, 0.0f);
        var uv1 = new Vector2(0.50f, 0.0625f);

        var handle = new IconHandle(textureId: 7, width: 16, height: 16, uv0, uv1);

        Assert.Equal(uv0, handle.Uv0);
        Assert.Equal(uv1, handle.Uv1);
    }

    [Fact]
    public void IconHandle_ExplicitUv_TextureIdPreserved()
    {
        var handle = new IconHandle(textureId: 123, width: 16, height: 16,
                                   new Vector2(0.1f, 0.2f), new Vector2(0.3f, 0.4f));
        Assert.Equal((nint)123, handle.TextureId);
    }

    // ── AIE-001-03: UV sub-rect round-trip through IIconProvider ─────────────

    [Fact]
    public void IconHandle_RoundTripThroughProvider_UvPreserved()
    {
        // A minimal IIconProvider stub that returns a handle with known UVs.
        var uv0 = new Vector2(0.5f, 0.0f);
        var uv1 = new Vector2(0.5625f, 0.0625f);
        IIconProvider provider = new FixedUvProvider(uv0, uv1);

        bool found = provider.TryGet("test/icon", out var got);

        Assert.True(found);
        Assert.Equal(uv0, got.Uv0);
        Assert.Equal(uv1, got.Uv1);
    }

    // ── AIE-001-04: Equality semantics ───────────────────────────────────────

    [Fact]
    public void IconHandle_SameValues_AreEqual()
    {
        var a = new IconHandle(1, 16, 16, Vector2.Zero, Vector2.One);
        var b = new IconHandle(1, 16, 16, Vector2.Zero, Vector2.One);

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void IconHandle_DifferentUv_AreNotEqual()
    {
        var a = new IconHandle(1, 16, 16, Vector2.Zero,           Vector2.One);
        var b = new IconHandle(1, 16, 16, new Vector2(0.5f, 0f), Vector2.One);

        Assert.NotEqual(a, b);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal IIconProvider that always returns a handle with the configured UVs.
    /// Models the "icon draw seam" tested in AIE-001-03.
    /// </summary>
    private sealed class FixedUvProvider : IIconProvider
    {
        private readonly Vector2 _uv0, _uv1;

        public FixedUvProvider(Vector2 uv0, Vector2 uv1)
        {
            _uv0 = uv0;
            _uv1 = uv1;
        }

        public bool TryGet(string key, out IconHandle handle)
        {
            handle = new IconHandle(textureId: 1, width: 16, height: 16, _uv0, _uv1);
            return true;
        }
    }
}
