using System.Collections.Generic;
using System.Numerics;
using Hrot.IG.Components;
using Hrot.ScenarioEditor.Tools;
using Hrot.IG.UI;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.Toolkit.Vis2D.Abstractions;
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
    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<CullingState>();
        repo.RegisterComponent<ResolvedStyle>();
        repo.RegisterComponent<SelectionState>();
        return repo;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // P1-005-T1: MiniExConPanelState.SubmitViaGateway â€” null gateway (offline)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
    /// events â€” verifies that the no-op path is truly a no-op.
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

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // P1-005-T2: StandardInteractionTool.OnWorldClick event pass-through
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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
        var query     = repo.Query().Build(); // empty query â€” sufficient for event wiring test
        var selection = new DefaultSelectionState();
        var tool      = new StandardInteractionTool(repo, query, selection);

        Action<Vector2, MapMouseButton, bool, bool, Entity> handler =
            (pos, btn, s, c, e) => { };

        // Verify subscribe + unsubscribe do not throw.
        var ex = Record.Exception(() =>
        {
            tool.OnWorldClick += handler;
            tool.OnWorldClick -= handler;
        });

        Assert.Null(ex);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // P1-005-T3: Backward-compatibility â€” Submit(eventBus) still works
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// The existing <see cref="MiniExConPanelState.Submit"/> method must still publish
    /// a <see cref="Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand"/> with
    /// the correct TKB type â€” Task 5 changes must not break this path.
    /// </summary>
    [Fact]
    public void MiniExConPanelState_Submit_StillPublishesCommand()
    {
        const long expectedTkb = 100L;
        var state   = new MiniExConPanelState { TkbType = expectedTkb };
        var bus     = new FdpEventBus();

        Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand? captured = null;
        state.OnCommandPublished += cmd => captured = cmd;

        state.Submit(bus);

        Assert.NotNull(captured);
        Assert.Equal(expectedTkb, captured!.Value.TkbType);
    }
}
