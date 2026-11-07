using System;
using Fdp.Core;
using Hrot.Common.Infrastructure;
using Hrot.Editor.AiShared.Rules;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Rules;

/// <summary>
/// ⭐ CE-4292 (docs/UX/DESIGN_Rules_Bundle.md §3, §4 R2 / R3) — who may edit rules is the SEAT, never the host: Author and Instructor get the rule editor
/// services with no seat refusal, every other seat gets the refusal (greyed with its reason, R-4119); a recording gets no services at all and no
/// Game master. 🔴 Red-proof (compiles): <c>MayEdit</c> returning true for every seat ⇒ the Reviewer row ⇒ red.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheRulesBundleTests
{
    private static RulesHostServices Host(Seat seat, bool recording = false, bool canCreate = false, Hrot.Common.Interactions.HostActions? actions = null)
    {
        var world = new EntityRepository();
        return new RulesHostServices(
            World: () => world, TimelineSource: () => null, Cursor: () => null,
            IdPrefix: "t_", Perspective: "Scenario", Colour: System.Numerics.Vector4.One,
            SelectEntity: _ => { }, ShowOnMap: _ => { }, Seat: () => seat,
            Blocked: recording ? () => "a recording is read-only" : null,
            EntityCreation: canCreate ? () => null : null,
            Recording: recording,
            Actions: actions == null ? null : () => actions);
    }

    [Theory]
    [InlineData(Seat.Author, true)]
    [InlineData(Seat.Instructor, true)]
    [InlineData(Seat.SideController, false)]
    [InlineData(Seat.Player, false)]
    [InlineData(Seat.Observer, false)]
    [InlineData(Seat.Reviewer, false)]
    public void CE4292_R2_TheSeatDecidesWhoMayEditRules(Seat seat, bool mayEdit)
    {
        var svc = RuleEditorServicesFactory.Build(Host(seat));
        Assert.NotNull(svc);                                       // a live host always gets the services …
        Assert.Equal(mayEdit, svc!.SeatRefusal!() is null);       // … the SEAT decides, read every frame
        if (!mayEdit) Assert.Contains(seat.ToString(), svc.SeatRefusal!());
        Assert.NotNull(svc.Live); Assert.NotNull(svc.MacroPreview);   // the one factory: every host gets the live + macro services
    }

    [Fact]
    public void CE4292_R3_ARecordingGetsNoEditorAndNoGameMaster_ALiveHostThatCreatesGetsTheGameMaster()
    {
        var onRecording = new RulesBundle(Host(Seat.Reviewer, recording: true));
        Assert.Null(onRecording.Editor);
        Assert.Null(onRecording.GameMaster);
        Assert.Equal("t_rules", onRecording.Rules.Id);
        Assert.Equal("t_mission_summary", onRecording.MissionSummary.Id);
        Assert.Equal("t_trainee_debrief", onRecording.TraineeDebrief.Id);
        Assert.Equal("t_game_summary", onRecording.GameSummary.Id);

        var live = new RulesBundle(Host(Seat.Instructor, canCreate: true));
        Assert.NotNull(live.Editor);
        Assert.NotNull(live.GameMaster);
        Assert.Equal("t_game_master", live.GameMaster!.Id);

        var cannotCreate = new RulesBundle(Host(Seat.Author));
        Assert.Null(cannotCreate.GameMaster);   // a capability the host lacks: no hand-run actions
    }

    /// <summary>⭐ CE-4308 (RX6, DESIGN_Rule_Editor_UX §2.5) — the bundle binds the map menu's rule entries on its HOST's actions (every host that composes it), and each
    /// opens the Rules window on the gallery with the unit as the subject / the area filled in. A host passing no actions binds nothing (its map greys the row).
    /// ✅ Red-proof: <c>BindMenuActions</c> not called ⇒ <c>Has(545)</c> false ⇒ red.</summary>
    [Fact]
    public void CE4308_TheMapMenusRuleEntries_AreBoundByTheBundle_AndOpenTheGalleryPreFilled()
    {
        var actions = new Hrot.Common.Interactions.HostActions();
        var host = Host(Seat.Author, actions: actions);
        var bundle = new RulesBundle(host);
        Assert.True(actions.Actions.Has(Hrot.Common.Constants.GlobalActionIds.AddRuleAboutUnit));
        Assert.True(actions.Actions.Has(Hrot.Common.Constants.GlobalActionIds.AddRuleForArea));
        var world = host.World()!;
        var unit = world.CreateEntity();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        world.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 4242 });
        Assert.True(actions.Actions.TryGetHandler(Hrot.Common.Constants.GlobalActionIds.AddRuleAboutUnit, out var about));
        about(world, unit);
        Assert.True(bundle.Rules.IsOpen);
        Assert.Equal(4242, bundle.Rules.GalleryContext.Subject.Ref.NetworkId);
        Assert.True(actions.Actions.TryGetHandler(Hrot.Common.Constants.GlobalActionIds.AddRuleForArea, out var forArea));
        forArea(world, unit);
        Assert.Equal(4242, bundle.Rules.GalleryContext.Area.Ref.NetworkId);
        Assert.False(bundle.Rules.GalleryContext.HasSubject);

        var bare = new Hrot.Common.Interactions.HostActions();
        _ = new RulesBundle(Host(Seat.Author));
        Assert.False(bare.Actions.Has(Hrot.Common.Constants.GlobalActionIds.AddRuleAboutUnit));
    }
}
