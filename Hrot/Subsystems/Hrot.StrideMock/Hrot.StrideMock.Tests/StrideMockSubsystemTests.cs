using System;
using System.Numerics;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Editor;
using Xunit;

namespace Hrot.StrideMock.Tests;

/// <summary>
/// Unit tests for <see cref="StrideMockSubsystem"/> covering all SC_SM006_x
/// success conditions.
/// </summary>
public sealed class StrideMockSubsystemTests : IDisposable
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SubsystemConfig HeadlessConfig() => new SubsystemConfig
    {
        DomainId      = 1,
        Headless      = true,
        OwnWindow     = false,
        NodeId        = 700,
        SubsystemName = "StrideMock",
    };

    private readonly StrideMockSubsystem _subsystem = new StrideMockSubsystem(new OfflineNetworkFactory());

    public void Dispose()
    {
        _subsystem.Shutdown();
    }

    // ── SC_SM006_1: Name ─────────────────────────────────────────────────────

    /// <summary>SC_SM006_1: Name property returns "StrideMock".</summary>
    [Fact]
    public void Name_ReturnsStrideMock()
    {
        Assert.Equal("StrideMock", _subsystem.Name);
    }

    // ── SC_SM006_2: TitleBarColor ─────────────────────────────────────────────

    /// <summary>SC_SM006_2: TitleBarColor is orange (0.8, 0.4, 0.1, 1.0).</summary>
    [Fact]
    public void TitleBarColor_IsOrange()
    {
        var expected = new Vector4(0.8f, 0.4f, 0.1f, 1f);
        Assert.Equal(expected, _subsystem.TitleBarColor);
    }

    // ── SC_SM006_3: Constructor null-guard ────────────────────────────────────

    /// <summary>SC_SM006_3: Constructor with null factory throws ArgumentNullException.</summary>
    [Fact]
    public void Constructor_NullFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new StrideMockSubsystem(null!));
    }

    // ── SC_SM006_3 (Initialize): Headless Initialize does not throw ───────────

    /// <summary>SC_SM006_3 (Initialize): Initialize with headless config does not throw.</summary>
    [Fact]
    public void Initialize_HeadlessConfig_DoesNotThrow()
    {
        var ex = Record.Exception(() => _subsystem.Initialize(HeadlessConfig()));
        Assert.Null(ex);
    }

    // ── SC_SM006_4: GetCameraView after Initialize ────────────────────────────

    /// <summary>SC_SM006_4: GetCameraView() returns non-null after Initialize.</summary>
    [Fact]
    public void GetCameraView_AfterInitialize_ReturnsNonNull()
    {
        _subsystem.Initialize(HeadlessConfig());

        var view = _subsystem.GetCameraView();

        Assert.NotNull(view);
    }

    // ── SC_SM006_5: ApplyCameraView roundtrip ─────────────────────────────────

    /// <summary>SC_SM006_5: ApplyCameraView changes camera Target and Zoom.</summary>
    [Fact]
    public void ApplyCameraView_SetsTargetAndZoom()
    {
        _subsystem.Initialize(HeadlessConfig());

        var view = new MapCameraView { Target = new Vector2(100f, 200f), Zoom = 2.5f };
        _subsystem.ApplyCameraView(view);

        var result = _subsystem.GetCameraView()!.Value;
        Assert.Equal(100f, result.Target.X, precision: 1);
        Assert.Equal(200f, result.Target.Y, precision: 1);
        Assert.Equal(2.5f, result.Zoom,     precision: 2);
    }

    // ── SC_SM006_6: Update in headless does not throw ─────────────────────────

    /// <summary>SC_SM006_6: Update(dt) does not throw in headless mode (no Raylib input).</summary>
    [Fact]
    public void Update_HeadlessAfterInitialize_DoesNotThrow()
    {
        _subsystem.Initialize(HeadlessConfig());

        var ex = Record.Exception(() => _subsystem.Update(0.016f));
        Assert.Null(ex);
    }

    // ── SC_SM006_7: DrawWorld in headless returns early ───────────────────────

    /// <summary>SC_SM006_7: DrawWorld() does not throw in headless mode (guard skips rendering).</summary>
    [Fact]
    public void DrawWorld_HeadlessAfterInitialize_DoesNotThrow()
    {
        _subsystem.Initialize(HeadlessConfig());

        var ex = Record.Exception(() => _subsystem.DrawWorld());
        Assert.Null(ex);
    }

    // ── SC_SM006_8: DrawUI in headless returns early ──────────────────────────

    /// <summary>SC_SM006_8: DrawUI() does not throw in headless mode (guard skips rendering).</summary>
    [Fact]
    public void DrawUI_HeadlessAfterInitialize_DoesNotThrow()
    {
        _subsystem.Initialize(HeadlessConfig());

        var ex = Record.Exception(() => _subsystem.DrawUI());
        Assert.Null(ex);
    }

    // ── SC_SM006_9: Shutdown does not throw ───────────────────────────────────

    /// <summary>SC_SM006_9: Shutdown() disposes the core without throwing.</summary>
    [Fact]
    public void Shutdown_AfterInitialize_DoesNotThrow()
    {
        _subsystem.Initialize(HeadlessConfig());

        var ex = Record.Exception(() => _subsystem.Shutdown());
        Assert.Null(ex);
    }

    /// <summary>Shutdown() before Initialize does not throw (safe no-op).</summary>
    [Fact]
    public void Shutdown_BeforeInitialize_DoesNotThrow()
    {
        var sub = new StrideMockSubsystem(new OfflineNetworkFactory());
        var ex = Record.Exception(() => sub.Shutdown());
        Assert.Null(ex);
    }
}
