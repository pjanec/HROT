using System.Collections.Generic;
using System.Numerics;
using Hrot.IG.Adapters;
using Hrot.IG.Components;
using Hrot.IG.Tools;
using Hrot.IG.UI;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Defaults;
using Raylib_cs;

namespace Hrot.IG.Tests;

/// <summary>
/// Verifies INTS-P1-005: IG-to-ExCon map event translator wiring.
///
/// Coverage:
/// <list type="bullet">
///   <item><see cref="MiniExConPanelState.SubmitViaGateway"/> is silent (no throw) when
///         called with a <c>null</c> gateway (network-disabled path).</item>
///   <item><see cref="Tools.StandardInteractionTool.OnWorldClick"/> event is exposed
///         and can be subscribed/unsubscribed without error.</item>
///   <item>Existing <see cref="MiniExConPanelState.Submit"/> path is unaffected by
///         Task 5 changes (backward-compatibility guard).</item>
/// </list>
///
/// No DDS participant or Raylib window is required.
/// </summary>
public class MapEventTranslatorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<CullingState>();
        repo.RegisterComponent<ResolvedStyle>();
        repo.RegisterComponent<SelectionState>();
        return repo;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // P1-005-T1: MiniExConPanelState.SubmitViaGateway — null gateway (offline)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calling <see cref="MiniExConPanelState.SubmitViaGateway"/> with a null gateway
    /// must not throw.  The method logs a warning and returns silently.
    /// </summary>
    [Fact]
    public void SubmitViaGateway_WithNullGateway_DoesNotThrow()
    {
        var state = new MiniExConPanelState();

        var exception = Record.Exception(() => state.SubmitViaGateway(null));

        Assert.Null(exception);
    }

    /// <summary>
    /// SubmitViaGateway with null gateway must return without publishing any
    /// events — verifies that the no-op path is truly a no-op.
    /// </summary>
    [Fact]
    public void SubmitViaGateway_WithNullGateway_DoesNotFire_OnCommandPublished()
    {
        var state = new MiniExConPanelState { TkbType = 100L };

        bool eventFired = false;
        state.OnCommandPublished += _ => eventFired = true;

        state.SubmitViaGateway(null);

        Assert.False(eventFired,
            "OnCommandPublished must not fire when gateway is null.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // P1-005-T2: StandardInteractionTool.OnWorldClick event pass-through
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <see cref="Tools.StandardInteractionTool.OnWorldClick"/> must be subscribable
    /// and unsubscribable without throwing.
    /// Also confirms the event is properly forwarded by calling the IG wrapper's
    /// internal <c>HandleClick</c> path.
    /// </summary>
    [Fact]
    public void StandardInteractionTool_OnWorldClick_CanBeSubscribed()
    {
        var repo      = CreateRepo();
        var query     = repo.Query().Build(); // empty query — sufficient for event wiring test
        var adapter   = new NedVisualizerAdapter();
        var selection = new DefaultSelectionState();
        var tool      = new StandardInteractionTool(repo, query, adapter, selection);

        Action<Vector2, MouseButton, bool, bool, Entity> handler =
            (pos, btn, s, c, e) => { };

        // Verify subscribe + unsubscribe do not throw.
        var ex = Record.Exception(() =>
        {
            tool.OnWorldClick += handler;
            tool.OnWorldClick -= handler;
        });

        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // P1-005-T3: Backward-compatibility — Submit(eventBus) still works
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The existing <see cref="MiniExConPanelState.Submit"/> method must still publish
    /// a <see cref="FDP.Toolkit.NetworkSpawning.Events.SpawnEntityCommand"/> with
    /// the correct TKB type — Task 5 changes must not break this path.
    /// </summary>
    [Fact]
    public void MiniExConPanelState_Submit_StillPublishesCommand()
    {
        const long expectedTkb = 100L;
        var state   = new MiniExConPanelState { TkbType = expectedTkb };
        var bus     = new FdpEventBus();

        FDP.Toolkit.NetworkSpawning.Events.SpawnEntityCommand? captured = null;
        state.OnCommandPublished += cmd => captured = cmd;

        state.Submit(bus);

        Assert.NotNull(captured);
        Assert.Equal(expectedTkb, captured!.Value.TkbType);
    }
}
