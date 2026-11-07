using System;
using System.Linq;
using Fdp.Core;
using Hrot.Common.Actions;
using Hrot.Common.Editing;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>⭐ B-11 A1 (docs/UX/DESIGN_Action_Catalogue.md §6) — the catalogue's own rules, on a hand-built catalogue.</summary>
public sealed class TheActionCatalogueComposesMenusTests
{
    private static ActionContext For(ActionTarget target) => new(null!, Entity.Null, target, EditingMode.Live, default);

    /// <summary>⭐ One action, one declaration: declaring it twice for the same targets throws. ✅ Red-proof: Declare not checking ⇒ no throw.</summary>
    [Fact]
    public void DeclaringOneActionTwice_Throws()
    {
        var c = new ActionCatalogue();
        c.Declare(new ActionDecl(10, "Center View", ActionGroup.View, 0, ActionTarget.Unit));
        Assert.Throws<InvalidOperationException>(() => c.Declare(new ActionDecl(10, "Center View", ActionGroup.View, 0, ActionTarget.Unit)));
        c.Declare(new ActionDecl(10, "Center View", ActionGroup.View, 0, ActionTarget.Area));   // the same action for another target is fine
    }

    /// <summary>
    /// ⭐ The separator rule: groups in their fixed order, a separator between two NON-EMPTY groups only — never leading, trailing or doubled when a
    /// group offers nothing; rows within a group by Order; a declaration only for its targets. ✅ Red-proof: a separator after every group ⇒ the
    /// empty Embark group adds a second one.
    /// </summary>
    [Fact]
    public void Separators_StandOnlyBetweenNonEmptyGroups_InTheFixedGroupOrder()
    {
        var c = new ActionCatalogue();
        c.Declare(new ActionDecl(3, "Delete", ActionGroup.Destructive, 0, ActionTarget.Unit));
        c.Declare(new ActionDecl(2, "Select", ActionGroup.View, 1, ActionTarget.Unit));
        c.Declare(new ActionDecl(1, "Center", ActionGroup.View, 0, ActionTarget.Unit));
        c.Declare(new ActionDecl(9, "Embark", ActionGroup.Embark, 0, ActionTarget.Unit) { Offer = _ => null });   // offers nothing here
        c.Declare(new ActionDecl(7, "Edit Route", ActionGroup.Edit, 0, ActionTarget.Route));                      // not this target
        c.Declare(new ActionDecl(0, "Orders", ActionGroup.Orders, 0, ActionTarget.Unit)
        {
            Expand = _ => new[] { new ContextMenuItemDto { Id = 400, Label = "Attack" }, new ContextMenuItemDto { Id = 402, Label = "Move" } },
        });

        var rows = MenuComposer.Compose(For(ActionTarget.Unit), c);
        Assert.Equal(new[] { "Attack", "Move", "—", "Center", "Select", "—", "Delete" },
                     rows.Select(r => r.IsSeparator == true ? "—" : r.Label!).ToArray());
    }

    /// <summary>
    /// ⭐ B-11 A3 (design §8) — THE one mode rule the gate and the catalogue share: a recording is Review whatever the cluster says; Preview and
    /// Live by their states; a Replay state Review; Idle and Edit states Edit. ✅ Red-proof: Loading* mapped to Edit ⇒ the Preview / Live rows fail.
    /// </summary>
    [Theory]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.Idle, false, EditingMode.Edit)]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.OperatingEdit, false, EditingMode.Edit)]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.LoadingPreview, false, EditingMode.Preview)]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.OperatingPreview, false, EditingMode.Preview)]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.OperatingLive, false, EditingMode.Live)]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.LoadingLive, false, EditingMode.Live)]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay, false, EditingMode.Review)]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.OperatingLive, true, EditingMode.Review)]
    public void TheStationMode_IsTheClustersState_AndARecordingIsReview(Fdp.Toolkit.Orchestration.ClusterState state, bool showsRecording, EditingMode expected)
        => Assert.Equal(expected, StationMode.From(state, showsRecording));

    /// <summary>⭐ B-11 A3 — a declaration hidden in a mode offers nothing there; the group order follows the mode (Edit first in Edit).</summary>
    [Fact]
    public void AnActionHiddenInAMode_IsNotOffered_AndTheGroupOrderFollowsTheMode()
    {
        var c = new ActionCatalogue();
        c.Declare(new ActionDecl(1, "Center", ActionGroup.View, 0, ActionTarget.Unit));
        c.Declare(new ActionDecl(2, "Rotate", ActionGroup.Edit, 0, ActionTarget.Unit) { Modes = ActionModes.InEdit });
        c.Declare(new ActionDecl(3, "Move", ActionGroup.Orders, 0, ActionTarget.Unit) { Modes = ActionModes.Running });
        string Rows(EditingMode m) => string.Join(",", MenuComposer.Compose(new ActionContext(null!, Entity.Null, ActionTarget.Unit, m, default), c)
            .Select(r => r.IsSeparator == true ? "—" : r.Label!));
        Assert.Equal("Rotate,—,Center", Rows(EditingMode.Edit));
        Assert.Equal("Move,—,Center", Rows(EditingMode.Live));
        Assert.Equal("Center", Rows(EditingMode.Review));
    }

    /// <summary>⭐ Seats H2 (docs/UX/DESIGN_Seats_And_Hats.md §3.2, CE-4279) — the composer GREYS (never hides) what the seat may not do, with the reason:
    /// a Player gets no orders, no edits, no debug — only the View group; a side controller's orders are greyed on another side's unit; an instructor's
    /// edits are greyed in Edit mode and offered while running. 🔴 Red-proof (compiles): <c>SeatRefusal</c> answering null ⇒ every row enabled ⇒ red.</summary>
    [Fact]
    public void H2_TheSeatGreysWhatItMayNotDo_WithTheReason_NeverHides()
    {
        var c = new ActionCatalogue();
        c.Declare(new ActionDecl(1, "Center", ActionGroup.View, 0, ActionTarget.Unit));
        c.Declare(new ActionDecl(2, "Rotate", ActionGroup.Edit, 0, ActionTarget.Unit));
        c.Declare(new ActionDecl(3, "Move", ActionGroup.Orders, 0, ActionTarget.Unit));
        c.Declare(new ActionDecl(4, "AI trace", ActionGroup.Debug, 0, ActionTarget.Unit));
        string Rows(EditingMode m, EditingLevel level, ForceId? side = null, ForceId? unit = null)
            => string.Join(",", MenuComposer.Compose(new ActionContext(null!, Entity.Null, ActionTarget.Unit, m, default) { Level = level, Side = side, UnitSide = unit }, c)
                .Where(r => r.IsSeparator != true).Select(r => r.Label + (r.Enabled == false ? "(x)" : "")));

        Assert.Equal("Move,Center,Rotate,AI trace", Rows(EditingMode.Live, EditingLevel.Full));
        Assert.Equal("Move(x),Center,Rotate(x),AI trace(x)", Rows(EditingMode.Live, EditingLevel.None));             // a Player: View only, the rest greyed
        Assert.Equal("Move,Center,Rotate(x),AI trace(x)", Rows(EditingMode.Live, EditingLevel.Controller, ForceId.Friend, ForceId.Friend));
        Assert.Equal("Move(x),Center,Rotate(x),AI trace(x)", Rows(EditingMode.Live, EditingLevel.Controller, ForceId.Friend, ForceId.Hostile));   // not its side
        Assert.Equal("Rotate(x),Center,Move,AI trace(x)", Rows(EditingMode.Edit, EditingLevel.Instructor));           // an instructor does not author
        Assert.Equal("Move,Center,Rotate,AI trace(x)", Rows(EditingMode.Live, EditingLevel.Instructor));              // …but edits a running exercise

        var player = MenuComposer.Compose(new ActionContext(null!, Entity.Null, ActionTarget.Unit, EditingMode.Live, default) { Level = EditingLevel.None }, c);
        Assert.Equal(ActionContext.SeatText(EditingLevel.None), player.First(r => r.Label == "Move").Tooltip);
    }

    /// <summary>⭐ Seats H2 — the station's seat on the ONE station source: its level (D4) and its side ("Blue" = Friend); none installed ⇒ Full, every side.
    /// And the gate's reasons: an instructor refused only in Edit mode, a side controller refused edits but not orders.</summary>
    [Fact]
    public void H2_TheStationSourceCarriesTheSeat_AndTheGateRefusesByIt()
    {
        using var w = new EntityRepository();
        Assert.Equal(EditingLevel.Full, StationMode.LevelOf(w));
        Assert.Null(StationMode.SideOf(w));
        StationMode.Install(w, () => EditingMode.Live, () => Hrot.Common.Infrastructure.Seat.SideController, () => StationMode.ParseSide("Blue"));
        Assert.Equal(EditingLevel.Controller, StationMode.LevelOf(w));
        Assert.Equal(ForceId.Friend, StationMode.SideOf(w));
        Assert.Equal(ForceId.Hostile, StationMode.ParseSide("hostile"));
        Assert.Null(StationMode.ParseSide(""));
        Assert.Null(StationMode.ParseSide("Purple"));

        Assert.Equal(HrotEditGate.InstructorDoesNotAuthor, HrotEditGate.Reason(EditingMode.Edit, EditingLevel.Instructor));
        Assert.Null(HrotEditGate.Reason(EditingMode.Live, EditingLevel.Instructor));
        Assert.Null(HrotEditGate.Reason(EditingMode.Edit, EditingLevel.Full));
        Assert.NotNull(HrotEditGate.Reason(EditingMode.Live, EditingLevel.Controller));
        var gate = new HrotEditGate(() => EditingMode.Live, () => EditingLevel.Controller);
        Assert.Null(gate.Refuse(w, Fdp.Toolkit.Replication.Edits.EditOp.Command(Entity.Null, _ => { }, "Move")));   // an order is a controller's job
    }
}
