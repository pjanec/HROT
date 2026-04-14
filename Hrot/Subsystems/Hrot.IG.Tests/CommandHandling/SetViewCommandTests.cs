using System;
using System.Numerics;
using Fdp.Core;

namespace Hrot.IG.Tests.CommandHandling;

/// <summary>
/// Unit tests for <see cref="IgApplication"/> handling of
/// <see cref="Hrot.NED.Messages.CommandType.CMD_SET_VIEW"/> — OC1-G002.
/// </summary>
public class SetViewCommandTests : IDisposable
{
    private readonly IgApplication _app;

    public SetViewCommandTests()
    {
        _app = new IgApplication();
        // Factory required so GhostCreationSystem is available for TestHook_InjectEntityMasterDescriptor.
        _app.InitializeEmbedded(headless: true, domainIdOverride: 206, networkFactory: IgTestFactory.CreateHeadless());
    }

    public void Dispose() => _app.Dispose();

    private void RegisterEntityAt(long networkId, float x, float y)
    {
        _app.TestHook_InjectEntityMasterDescriptor((int)networkId, 1001);
        // Set SimTransform directly — no DDS needed.
        _app.TestHook_SetEntitySimTransform(networkId, new SimTransform
        {
            Position = new Vector3(x, y, 0f)
        });
    }

    /// <summary>
    /// OC1-G002 Scenario 1 — camera centers on the entity identified by entityId.
    /// </summary>
    [Fact]
    public void KnownEntity_CameraTargetUpdated()
    {
        RegisterEntityAt(10L, 100f, 200f);

        _app.TestHook_ParseCommandAndSetView("{\"entityId\":10}");

        // The keyboard pan target should be set to the entity's world-space position.
        var target = _app.TestHook_KeyboardPanTarget;
        Assert.Equal(100f, target.X, 1f);
        Assert.Equal(200f, target.Y, 1f);
    }

    /// <summary>
    /// OC1-G002 Scenario 2 — unknown entity: no exception, camera unchanged.
    /// </summary>
    [Fact]
    public void UnknownEntity_NoExceptionCameraUnchanged()
    {
        var before = _app.TestHook_KeyboardPanTarget;
        var ex = Record.Exception(() => _app.TestHook_ParseCommandAndSetView("{\"entityId\":7}"));
        Assert.Null(ex);
        Assert.Equal(before, _app.TestHook_KeyboardPanTarget);
    }

    /// <summary>
    /// OC1-G002 Scenario 3 — empty JSON: silently ignored.
    /// </summary>
    [Fact]
    public void EmptyJson_SilentlyIgnored()
    {
        var ex = Record.Exception(() => _app.TestHook_ParseCommandAndSetView(""));
        Assert.Null(ex);
    }
}
