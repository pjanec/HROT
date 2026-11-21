using System;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Common;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐ Player Join D-9 — the <b>Playable</b> Details section: a toggle (plus side and slot name) that adds / removes <see cref="Playable"/> on the selected unit. Written to the authoring world exactly as the Brain tab
/// writes its settings; the component is recorded and saved with the scenario.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ThePlayableViewTests : IDisposable
{
    public ThePlayableViewTests() { PanelSnapshot.Clear(); PanelSnapshot.CaptureEnabled = false; }
    public void Dispose() { PanelSnapshot.Clear(); PanelSnapshot.CaptureEnabled = false; }

    private static EntityRepository World()
    {
        var w = new EntityRepository();
        w.RegisterComponent<EntityInfo>();
        w.RegisterComponent<Playable>();
        return w;
    }

    private static Entity Unit(EntityRepository w, ForceId side)
    {
        var e = w.CreateEntity();
        w.AddComponent(e, new EntityInfo { Name = new FixedString64("Alpha 1"), ForceId = side });
        return e;
    }

    /// <summary>The toggle ON adds <c>Playable</c> with the unit's own side and name; OFF removes it; side and slot name edit it. ✅ Red-proof: <c>SetPlayable</c> not removing on OFF ⇒ red; not defaulting the side ⇒ red.</summary>
    [Fact]
    public void TheToggle_AddsAndRemovesPlayable_AndSideAndNameEditIt()
    {
        using var w = World();
        var e = Unit(w, ForceId.Hostile);
        Assert.False(EntityPlayableModel.Read(w, e).IsPlayable);

        Assert.True(EntityPlayableModel.SetPlayable(w, e, true));
        var on = EntityPlayableModel.Read(w, e);
        Assert.True(on.IsPlayable); Assert.Equal(ForceId.Hostile, on.Side); Assert.Equal("Alpha 1", on.SlotName);
        Assert.True(w.HasComponent<Playable>(e));

        Assert.True(EntityPlayableModel.SetSide(w, e, ForceId.Friend));
        Assert.True(EntityPlayableModel.SetSlotName(w, e, "Pilot"));
        var edited = EntityPlayableModel.Read(w, e);
        Assert.Equal(ForceId.Friend, edited.Side); Assert.Equal("Pilot", edited.SlotName);

        Assert.True(EntityPlayableModel.SetPlayable(w, e, false));
        Assert.False(w.HasComponent<Playable>(e));
        Assert.False(EntityPlayableModel.SetSide(w, e, ForceId.Hostile));   // nothing to edit
    }

    /// <summary>A host that never registered <c>Playable</c> is not offered the section and the model refuses without throwing; the view applies only to exactly one live entity. ✅ Red-proof: the registered check dropped ⇒ the model throws ⇒ red.</summary>
    [Fact]
    public void TheSection_AppliesOnlyWhereTheComponentIsRegistered_ToOneLiveEntity()
    {
        using var bare = new EntityRepository();
        var e = bare.CreateEntity();
        Assert.False(EntityPlayableModel.SetPlayable(bare, e, true));

        using var w = World();
        var unit = Unit(w, ForceId.Friend);
        var d = EntityPlayableViewDescriptor.For(() => w);
        var ctx = DetailsContext.Empty("Scenario") with { Entities = new[] { unit } };
        // ⭐ U-23 part 5 — a unit the Unit form is offered for gets the form's Playable toggle, not this tab; an entity with no EntityInfo still gets the tab
        Hrot.Presentation.Forms.HrotForms.EnsureRegistered();
        Assert.False(d.AppliesTo(ctx));
        var thing = w.CreateEntity();
        Assert.True(d.AppliesTo(DetailsContext.Empty("Scenario") with { Entities = new[] { thing } }));
        Assert.False(d.AppliesTo(DetailsContext.Empty("Scenario")));
        Assert.False(EntityPlayableViewDescriptor.For(() => bare).AppliesTo(DetailsContext.Empty("Scenario") with { Entities = new[] { e } }));
    }

    /// <summary>The view dumps what a frame showed (<c>GET /panels</c>). ✅ Red-proof: the dump reading a stale state ⇒ red.</summary>
    [Fact]
    public void TheView_DumpsItsState()
    {
        using var w = World();
        var unit = Unit(w, ForceId.Friend);
        EntityPlayableModel.SetPlayable(w, unit, true);
        EntityPlayableModel.SetSlotName(w, unit, "Rifleman");
        var view = new EntityPlayableView(() => w);
        PanelSnapshot.CaptureEnabled = true;
        var vm = view.SimulateDraw(DetailsContext.Empty("Scenario") with { Entities = new[] { unit } }, "details_playable");
        Assert.True(vm.IsPlayable); Assert.Equal("Friend", vm.Side); Assert.Equal("Rifleman", vm.SlotName);
    }
}
