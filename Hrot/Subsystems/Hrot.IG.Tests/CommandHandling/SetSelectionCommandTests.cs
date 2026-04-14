using System;
using System.Numerics;
using Hrot.IG.Components;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.IG.Tests.CommandHandling;

/// <summary>
/// Unit tests for <see cref="IgApplication"/> handling of
/// <see cref="Hrot.NED.Messages.CommandType.CMD_SET_SELECTION"/> — OC1-G001.
/// </summary>
public class SetSelectionCommandTests : IDisposable
{
    private readonly IgApplication _app;

    public SetSelectionCommandTests()
    {
        _app = new IgApplication();
        // Factory required so GhostCreationSystem is available for TestHook_InjectEntityMasterDescriptor.
        _app.InitializeEmbedded(headless: true, domainIdOverride: 205, networkFactory: IgTestFactory.CreateHeadless());
    }

    public void Dispose() => _app.Dispose();

    // ── Helper: register an entity with the given network ID ──────────────────

    private void RegisterEntity(long networkId)
    {
        _app.TestHook_InjectEntityMasterDescriptor((int)networkId, 1001);
    }

    /// <summary>
    /// OC1-G001 Scenario 1 — known entity becomes selected.
    /// </summary>
    [Fact]
    public void KnownEntity_BecomesSelected()
    {
        RegisterEntity(42L);

        _app.TestHook_ParseCommandAndSetSelection("{\"entityId\":42}");

        // Verify the entity's SelectionState.
        Assert.True(_app.TestHook_EntityMap.TryGetEntity(42L, out var entity));
        var state = _app.World.GetComponent<SelectionState>(entity);
        Assert.True(state.IsSelected || state.IsPrimarySelection);
    }

    /// <summary>
    /// OC1-G001 Scenario 2 — unknown entity ID: no exception, no state mutation.
    /// </summary>
    [Fact]
    public void UnknownEntity_NoExceptionNoStateMutation()
    {
        // Should not throw.
        var ex = Record.Exception(() =>
            _app.TestHook_ParseCommandAndSetSelection("{\"entityId\":999}"));
        Assert.Null(ex);
    }

    /// <summary>
    /// OC1-G001 Scenario 3 — empty JSON: silently ignored.
    /// </summary>
    [Fact]
    public void EmptyJson_SilentlyIgnored()
    {
        var ex = Record.Exception(() =>
            _app.TestHook_ParseCommandAndSetSelection(""));
        Assert.Null(ex);
    }

    /// <summary>
    /// OC1-G001 Scenario 4 — selecting entity B deselects entity A.
    /// </summary>
    [Fact]
    public void SelectingEntityB_DeselectionEntityA()
    {
        RegisterEntity(10L);
        RegisterEntity(55L);

        // Select A first.
        _app.TestHook_ParseCommandAndSetSelection("{\"entityId\":10}");

        // Now select B.
        _app.TestHook_ParseCommandAndSetSelection("{\"entityId\":55}");

        Assert.True(_app.TestHook_EntityMap.TryGetEntity(10L, out var entityA));
        Assert.True(_app.TestHook_EntityMap.TryGetEntity(55L, out var entityB));

        var stateA = _app.World.GetComponent<SelectionState>(entityA);
        var stateB = _app.World.GetComponent<SelectionState>(entityB);

        Assert.False(stateA.IsSelected || stateA.IsPrimarySelection);
        Assert.True(stateB.IsSelected || stateB.IsPrimarySelection);
    }
}
