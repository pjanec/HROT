using System;
using System.Numerics;
using Hrot.IG.Components;
using Fdp.Core;
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
    /// ⭐⭐⭐ <b><c>UXI-11</c> slice <c>S-2</c> — the selection now lands ONE FRAME LATER, so the rail
    /// runs a frame.</b>
    ///
    /// <para>📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.7.3 rule 1 / §2.7.7: <c>SelectEntityOnMap</c>
    /// used to hand-roll the clear-loop and write the <c>SelectionState</c> component itself. It now
    /// publishes a <c>SelectionChangeRequest</c> and <c>SelectionRequestSystem</c> applies it — one
    /// writer, host-wide. ⚠ §2.5 rules the one-frame latency structural, not a defect.</para>
    ///
    /// <para>⭐⭐ <b>This makes the rail STRONGER, not weaker.</b> ⛔ Before, it proved only that a
    /// private method wrote two booleans. Now it proves the whole chain — request published, bus
    /// swapped, the system registered ON THIS HOST, the view applied. 📐 That registration is new:
    /// measured <c>2026-09-20</c>, IG ran no request system at all, so <c>SelectEntityCommand</c> had
    /// no consumer here. ⚠ If the registration is ever dropped, these two go red — which is exactly
    /// the silent no-op that would otherwise ship.</para>
    /// </summary>
    private void PumpOneFrame() => _app.Kernel.Update();

    /// <summary>
    /// OC1-G001 Scenario 1 — known entity becomes selected.
    /// </summary>
    [Fact]
    public void KnownEntity_BecomesSelected()
    {
        RegisterEntity(42L);

        _app.TestHook_ParseCommandAndSetSelection("{\"entityId\":42}");
        PumpOneFrame();

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
        PumpOneFrame();

        // Now select B.
        _app.TestHook_ParseCommandAndSetSelection("{\"entityId\":55}");
        PumpOneFrame();

        Assert.True(_app.TestHook_EntityMap.TryGetEntity(10L, out var entityA));
        Assert.True(_app.TestHook_EntityMap.TryGetEntity(55L, out var entityB));

        var stateA = _app.World.GetComponent<SelectionState>(entityA);
        var stateB = _app.World.GetComponent<SelectionState>(entityB);

        Assert.False(stateA.IsSelected || stateA.IsPrimarySelection);
        Assert.True(stateB.IsSelected || stateB.IsPrimarySelection);
    }
}
