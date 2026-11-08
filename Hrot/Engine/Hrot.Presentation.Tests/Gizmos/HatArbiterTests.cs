using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Core.Mission;
using Hrot.Common.Editing;
using Hrot.ScenarioEditor.Map;
using Hrot.ScenarioEditor.Tools;
using Hrot.UI.Common.Facades;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐⭐ Seats and hats H3 (📄 docs/UX/DESIGN_Seats_And_Hats.md §3.3) — the station's ONE hat, derived (R-4164: it follows the run mode, never picked by hand):
/// a hat change disarms every tool but RTS; possessing stands RTS down and release returns to it (R-4163); the indicator shows "seat · hat".
/// </summary>
[Collection(Hrot.Presentation.Tests.ImGuiTestCollection.Name)]
public sealed class HatArbiterTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private EditingMode _mode = EditingMode.Edit;
    private Hrot.Common.Infrastructure.Seat _seat = Hrot.Common.Infrastructure.Seat.Author;   // ⭐ H6 — the Edit hat is the Author's; the status-bar rail names the Instructor

    public HatArbiterTests()
    {
        _w.RegisterComponent<SimTransform>();
        _w.RegisterComponent<NetworkIdentity>();
        _w.RegisterComponent<EntityInfo>();
        _w.SetSingletonManaged(new NetworkEntityMap());
        StationMode.Install(_w, () => _mode, () => _seat);
    }

    public void Dispose() => _w.Dispose();

    private sealed class NoPick : IMapPickService
    {
        public Task<GeoPoint> PickLocationAsync(CancellationToken ct = default) => new TaskCompletionSource<GeoPoint>().Task;
        public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default) => new TaskCompletionSource<int>().Task;
        public Task<IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());
        public int EntityAt(Vector2 worldXY) => -1;
    }

    [Theory]
    [InlineData(false, false, false, EditingMode.Edit, Hat.Edit)]
    [InlineData(false, false, false, EditingMode.Live, Hat.Observe)]
    [InlineData(false, false, true, EditingMode.Live, Hat.Command)]
    [InlineData(false, false, true, EditingMode.Edit, Hat.Edit)]          // RTS has no effect in Edit (Input §6b)
    [InlineData(false, true, true, EditingMode.Live, Hat.Direct)]
    [InlineData(true, true, true, EditingMode.Live, Hat.Embodied)]        // driving a unit wins over everything
    [InlineData(false, false, false, EditingMode.Review, Hat.Observe)]
    public void TheHat_IsDerivedByOnePrecedence(bool embodied, bool directing, bool rts, EditingMode mode, Hat expected)
        => Assert.Equal(expected, HatArbiter.Derive(embodied, directing, rts, mode));

    /// <summary>🔴 Red-proof (compiles): <c>Execute</c> not cancelling on a change ⇒ the placement-like tool survives ▶ ⇒ red; not setting StoodDown ⇒ red.</summary>
    [Fact]
    public void AHatChange_DisarmsEveryToolButRts_PossessingStandsRtsDown_ReleaseReturnsToIt()
    {
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _w });
        Assert.Contains(mi.Hats, mi.InteractionSystems);                  // scheduled wherever the map's interaction runs
        Assert.Same(mi.Hats, HatArbiter.Of(_w));
        mi.Tools.Register(new ToolDescriptor("test.place", "Place", ToolModality.Modal, ToolArbiter.Global), _ => ToolActivationOutcome.Armed);

        Assert.True(mi.Tools.Activate("test.place"));
        mi.Hats.Execute(_w, 0f);
        Assert.Equal(Hat.Edit, mi.Hats.Active);
        Assert.Equal("test.place", mi.Tools.ActiveModal?.Id);             // no change: the tool stays

        _mode = EditingMode.Live;                                           // ▶ — the Edit hat ends
        mi.Hats.Execute(_w, 0f);
        Assert.Equal(Hat.Observe, mi.Hats.Active);
        Assert.Null(mi.Tools.ActiveModal);                                  // the Edit tool is disarmed

        mi.MenuOrders.PickService = () => new NoPick();
        Assert.True(mi.Tools.Activate(ScenarioToolIds.RtsCommand));
        mi.Hats.Execute(_w, 0f);
        Assert.Equal(Hat.Command, mi.Hats.Active);

        mi.Hats.Embodied = true;                                            // possess a unit
        mi.Hats.Execute(_w, 0f);
        Assert.Equal(Hat.Embodied, mi.Hats.Active);
        Assert.Equal(ScenarioToolIds.RtsCommand, mi.Tools.ActiveModal?.Id); // RTS stays checked …
        Assert.True(MapUiStates.RtsOf(_w)!.StoodDown);                      // … but stands down

        mi.Hats.Embodied = false;                                           // release
        mi.Hats.Execute(_w, 0f);
        Assert.Equal(Hat.Command, mi.Hats.Active);
        Assert.False(MapUiStates.RtsOf(_w)!.StoodDown);
    }

    /// <summary>⭐ CE-4329 (R-4170) — the seat · hat line is the STATUS BAR's now (was the toolbar's): rebuilt only when either changes (the same string otherwise — no allocation per frame).</summary>
    [Fact]
    public void TheStatusBarLine_ShowsTheSeatAndTheHat()
    {
        _mode = EditingMode.Live;
        _seat = Hrot.Common.Infrastructure.Seat.Instructor;
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _w });
        var section = new Hrot.UI.Common.Panels.StationSeatStatusBarSection(() => _w);
        var line = section.Line();
        Assert.Equal("Instructor · Observe", line);
        Assert.Same(line, section.Line());
        mi.Hats.Embodied = true;
        Assert.Equal("Instructor · Embodied", section.Line());
        Assert.Null(new Hrot.UI.Common.Panels.StationSeatStatusBarSection(() => null).Line());
    }

    /// <summary>⭐ Seats H6 (§2.2 "hats allowed") — a hat the SEAT may not wear falls to Observe: a Side controller never wears Edit or Embodied, an Observer only Observe, an Instructor not Edit,
    /// a Player not Command; the Author wears them all. 🔴 Red-proof (compiles): <c>Active</c> not clamping ⇒ the Edit-mode and possession rows red.</summary>
    [Theory]
    [InlineData(Hrot.Common.Infrastructure.Seat.SideController, EditingMode.Edit, false, Hat.Observe)]
    [InlineData(Hrot.Common.Infrastructure.Seat.SideController, EditingMode.Live, false, Hat.Observe)]
    [InlineData(Hrot.Common.Infrastructure.Seat.SideController, EditingMode.Live, true, Hat.Observe)]      // possessing a unit is not a hat it may wear
    [InlineData(Hrot.Common.Infrastructure.Seat.Instructor, EditingMode.Edit, false, Hat.Observe)]
    [InlineData(Hrot.Common.Infrastructure.Seat.Instructor, EditingMode.Live, true, Hat.Embodied)]
    [InlineData(Hrot.Common.Infrastructure.Seat.Observer, EditingMode.Edit, false, Hat.Observe)]
    [InlineData(Hrot.Common.Infrastructure.Seat.Observer, EditingMode.Live, true, Hat.Observe)]
    [InlineData(Hrot.Common.Infrastructure.Seat.Author, EditingMode.Edit, false, Hat.Edit)]
    [InlineData(Hrot.Common.Infrastructure.Seat.Player, EditingMode.Live, true, Hat.Embodied)]
    public void H6_AHatTheSeatMayNotWear_FallsToObserve(Hrot.Common.Infrastructure.Seat seat, EditingMode mode, bool embodied, Hat expected)
    {
        _seat = seat; _mode = mode;
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _w });
        mi.Hats.Embodied = embodied;
        Assert.Equal(expected, mi.Hats.Active);
    }

    /// <summary>⭐ Seats H6 — a Side controller commands (the RTS tool arms at the Controller level) and its hat is Command; an Observer cannot arm it and stays Observe.</summary>
    [Fact]
    public void H6_ASideController_WearsCommandOnlyBecauseItMayCommand_AnObserverNever()
    {
        _mode = EditingMode.Live;
        _seat = Hrot.Common.Infrastructure.Seat.SideController;
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _w });
        mi.MenuOrders.PickService = () => new NoPick();
        Assert.True(mi.Tools.Activate(ScenarioToolIds.RtsCommand));
        Assert.Equal(Hat.Command, mi.Hats.Active);
        _seat = Hrot.Common.Infrastructure.Seat.Observer;
        Assert.Equal(Hat.Observe, mi.Hats.Active);   // whatever is armed, the seat's table decides
    }
}
