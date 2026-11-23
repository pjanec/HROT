using System;
using System.Linq;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.CGF.Configuration;
using Hrot.Editor;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Presentation.Behavior;
using Hrot.Editor.Scenario;
using Hrot.SimHost;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐ <c>CE-3043</c> — the editor's AI section: reads a unit's live task / SOP / ROE and applies an author's change
/// THROUGH THE ONE GATE — at once while paused, by event while running — at origin Superior. 📄
/// <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.6. Runs the production behaviour registry.
/// </summary>
public sealed class EntityAiSectionTests
{
    private sealed class Fixture
    {
        public readonly EntityRepository World = new();
        public readonly BehaviorRegistry Registry = new();
        public readonly EntityAiEditModel Model;
        public readonly Entity Unit;

        public Fixture(Hrot.Presentation.Edits.EditHistory? history = null)
        {
            SimHostComponentRegistry.RegisterAll(World);
            CognitiveComponentRegistry.RegisterAll(World);
            BlueprintTierTable.RegisterAll(World);
            var geo = UtmTransform.AtOrigin(0.0, 0.0, 0.0);
            World.SetSingletonManaged<IGeographicTransform>(geo);
            CgfBehaviorSetup.LoadFromAiAssembly(Registry);
            Model = new EntityAiEditModel(() => World, () => Registry, null, history);
            Unit = World.CreateEntity();
            World.AddComponent(Unit, new BehaviorState());
        }
    }

    /// <summary>⭐⭐ <c>CE-3494</c> (R-3114) — an EDIT-MODE edit is AUTHORED into the unit's <see cref="UnitIntent"/> at Superior: the section
    /// shows it at once, but NOTHING runs until time advances; then the ingress applies it through the one gate and removes the intent.
    /// 🔴 Red-proof: the old direct apply (AssignNow) ⇒ the "nothing runs yet" assertion.</summary>
    [Fact]
    public void CE3494_EditMode_AnEditIsAuthoredIntoTheIntent_AndRunsOnlyWhenTimeAdvances()
    {
        var f = new Fixture();
        Assert.True(f.Model.ApplyTask(f.Unit, "Idle", "{}", running: false));
        Assert.True(f.Model.ApplySop(f.Unit, "BasicInfantrySop", "{}", running: false));
        Assert.True(f.Model.ApplyRoe(f.Unit, RoeFire.HoldFire, RoeReactions.StayOnTask, running: false));

        Assert.Equal(0, f.World.GetComponentRO<BehaviorState>(f.Unit).ActiveBehaviorHash);   // nothing runs yet
        Assert.True(f.World.HasManagedComponent<UnitIntent>(f.Unit));

        var now = f.Model.Read(f.Unit)!;
        Assert.Equal("Idle", now.Task);
        Assert.Equal(BehaviorOrigin.Superior, now.TaskOrigin);
        Assert.Equal("BasicInfantrySop", now.Sop);
        Assert.Equal(BehaviorOrigin.Superior, now.SopOrigin);
        Assert.Equal(RoeFire.HoldFire, now.Fire);
        Assert.Equal(RoeReactions.StayOnTask, now.Reactions);
        Assert.Equal(BehaviorOrigin.Superior, now.RoeSetBy);

        // time advances: the ingress applies the intent through the gate and removes it
        new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(f.Registry).Execute(f.World, 0.016f);
        Assert.False(f.World.HasManagedComponent<UnitIntent>(f.Unit));
        Assert.True(f.Registry.TryGetId("Idle", out int idle));
        Assert.Equal(idle, f.World.GetComponentRO<BehaviorState>(f.Unit).ActiveBehaviorHash);
        Assert.Equal(RoeFire.HoldFire, f.Model.Read(f.Unit)!.Fire);

        // a cleared slot in edit mode is simply not authored
        Assert.True(f.Model.ApplyTask(f.Unit, "Idle", "{}", running: false));
        Assert.True(f.Model.ClearTask(f.Unit, running: false));
        Assert.Null(f.Model.PendingIntent(f.Unit));
    }

    /// <summary>
    /// ⭐ <c>CE-4219</c> ① — an EDIT-mode Apply is ONE entry on the editor's shared undo history (the precedent of <c>RuleDetails</c>): Undo restores the PREVIOUS task / SOP / ROE (or clears it when there was none), Redo re-applies;
    /// a LIVE edit (running) pushes nothing — it lasts the run only. 🔴 Red-proof: <c>ApplyTask</c> not pushing ⇒ the first assertion.
    /// </summary>
    [Fact]
    public void CE4219_AnEditModeApply_IsOneUndoEntry_AndALiveApplyIsNone()
    {
        var history = new Hrot.Presentation.Edits.EditHistory();
        var f = new Fixture(history);

        // task: none → Idle → Demo_TakeCover; undo walks back
        Assert.True(f.Model.ApplyTask(f.Unit, "Idle", "{}", running: false));
        Assert.Equal(1, history.Count);
        Assert.Equal("Set task", history.UndoLabel);
        Assert.True(f.Model.ApplyTask(f.Unit, "Demo_TakeCover", "{}", running: false));
        Assert.Equal(2, history.Count);
        Assert.Equal("Demo_TakeCover", f.Model.Read(f.Unit)!.Task);
        Assert.True(history.Undo());
        Assert.Equal("Idle", f.Model.Read(f.Unit)!.Task);                    // the PREVIOUS task, re-applied
        Assert.True(history.Undo());
        Assert.Null(f.Model.Read(f.Unit)!.Task);                             // there was none: cleared
        Assert.True(history.Redo());
        Assert.Equal("Idle", f.Model.Read(f.Unit)!.Task);                    // redo re-applies

        // SOP and ROE: one entry each, undo restores
        var roeBefore = f.Model.Read(f.Unit)!;
        Assert.True(f.Model.ApplySop(f.Unit, "BasicInfantrySop", "{}", running: false));
        Assert.Equal("Set SOP", history.UndoLabel);
        Assert.True(history.Undo());
        Assert.Null(f.Model.Read(f.Unit)!.Sop);
        Assert.True(f.Model.ApplyRoe(f.Unit, RoeFire.HoldFire, RoeReactions.StayOnTask, running: false));
        Assert.Equal("Set ROE", history.UndoLabel);
        Assert.True(history.Undo());
        Assert.Equal((roeBefore.Fire, roeBefore.Reactions), (f.Model.Read(f.Unit)!.Fire, f.Model.Read(f.Unit)!.Reactions));
        Assert.True(history.Redo());
        Assert.Equal(RoeFire.HoldFire, f.Model.Read(f.Unit)!.Fire);

        // ⭐ CE-3494 §10 (R-3119) — a LIVE edit is the intent too, so it is an undo step like an authored one ⛔ SUPERSEDED: "not an undo step" (an event)
        int count = history.Count;
        Assert.True(f.Model.ApplyTask(f.Unit, "Idle", "{}", running: true));
        Assert.True(f.Model.ApplySop(f.Unit, "BasicInfantrySop", "{}", running: true));
        Assert.True(f.Model.ApplyRoe(f.Unit, RoeFire.HoldFire, RoeReactions.StayOnTask, running: true));
        Assert.Equal(count + 3, history.Count);
    }

    /// <summary>⭐ <c>CE-3494</c> — the authored intent still goes through the ONE gate when it is applied: a Superior intent does not replace
    /// an Operator's running order.</summary>
    [Fact]
    public void CE3494_TheGateStillDecides_AnAuthoredIntentDoesNotReplaceAnOperatorsOrder()
    {
        var f = new Fixture();
        var ingress = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(f.Registry);
        f.World.Bus.PublishManaged(new AssignBehaviorEvent { Entity = f.Unit, BehaviorName = "Idle", JsonParams = "{}", Origin = BehaviorOrigin.Operator });
        f.World.Bus.SwapBuffers();
        ingress.Execute(f.World, 0.016f);

        Assert.True(f.Model.ApplyTask(f.Unit, "Demo_TakeCover", "{}", running: false));   // authored
        f.World.Bus.SwapBuffers();
        ingress.Execute(f.World, 0.016f);                                                  // applied — and refused by rank
        Assert.True(f.Registry.TryGetId("Idle", out int idle));
        Assert.Equal(idle, f.World.GetComponentRO<BehaviorState>(f.Unit).ActiveBehaviorHash);
    }

    /// <summary>⭐⭐ <c>CE-3494</c> §10 (R-3119) — a LIVE edit (running or paused mid-run) is the unit's INTENT at origin Operator: pending (Read shows it),
    /// no event, undoable; a live clear is a RELEASE (the slot's Clear). ⛔ SUPERSEDED: "a live edit is an event" (S1, CE3043_Running_AnEditIsAnEvent).
    /// 🔴 Red-proof: the live branch publishing again ⇒ no pending task, an event.</summary>
    [Fact]
    public void CE3494_Live_AnEditIsTheIntentAtOperator_AReleaseIsAClear()
    {
        var history = new Hrot.Presentation.Edits.EditHistory();
        var f = new Fixture(history);
        Assert.True(f.Model.ApplyTask(f.Unit, "Idle", "{}", running: true));
        Assert.Equal("Idle", f.Model.Read(f.Unit)!.Task);                          // pending — what the unit does next
        var slot = f.Model.PendingIntent(f.Unit)!.Behavior!;
        Assert.Equal(BehaviorOrigin.Operator, slot.Origin);
        f.World.Bus.SwapBuffers();
        Assert.Empty(f.World.Bus.ReadManaged<AssignBehaviorEvent>());                // no event
        Assert.True(f.Model.ClearTask(f.Unit, running: true));
        Assert.True(f.Model.PendingIntent(f.Unit)!.Behavior!.Clear);
        Assert.Null(f.Model.Read(f.Unit)!.Task);
        Assert.True(history.Undo());                                                 // undo: the order is back
        Assert.Equal("Idle", f.Model.PendingIntent(f.Unit)!.Behavior!.Name);
    }

    [Fact]
    public void CE3043_TheSopRow_OffersOnlyBehavioursKnownToDriveNoChannel()
    {
        var f = new Fixture();
        var choices = f.Model.SopChoices();
        Assert.Contains("BasicInfantrySop", choices);                          // a BTree with a known-empty set
        Assert.DoesNotContain("Idle", choices);                                // hand-written: unknown ⇒ not offered
        foreach (var name in choices)
        {
            Assert.True(f.Registry.TryGetId(name, out int id));
            Assert.True(f.Registry.TryGetDefinition(id, out var d));
            Assert.Empty(d!.WritesChannels!);
        }
        Assert.Contains("Idle", f.Model.TaskChoices());
    }

    /// <summary>⭐ <c>CE-1019</c> — the SOP row offers only SOPs (the generator publishes the ruled mark, "carries SOP orders") and
    /// the task row leaves them out: a channel-free tree that is not an SOP (<c>T01_Sequence</c>) is a task, not an SOP.
    /// 🔴 Red-proof: drop the generator's <c>CarriesSopOrders</c> line → BasicInfantrySop vanishes from the SOP row.</summary>
    [Fact]
    public void CE1019_TheSopRowOffersOnlySops_AndTheTaskRowLeavesThemOut()
    {
        var f = new Fixture();
        var sops = f.Model.SopChoices();
        var tasks = f.Model.TaskChoices();
        Assert.Contains("BasicInfantrySop", sops);
        Assert.Contains("BasicSquadSop", sops);
        Assert.DoesNotContain("T01_Sequence", sops);        // writes no channel, but carries no SOP orders
        Assert.Contains("T01_Sequence", tasks);
        Assert.DoesNotContain("BasicInfantrySop", tasks);
        foreach (var name in sops)
        {
            Assert.True(f.Registry.TryGetId(name, out int id));
            Assert.True(f.Registry.TryGetDefinition(id, out var d));
            Assert.True(d!.CarriesSopOrders, $"{name} is offered as an SOP but carries no SOP orders");
        }
    }

    [Fact]
    public void CE2084_TheSopSlot_RefusesABehaviourKnownToDriveAChannel()
    {
        var f = new Fixture();
        f.Registry.Register(0x7A01, "SopT_Mover", new BehaviorDefinition
        {
            Name = "SopT_Mover", BrainTier = BehaviorConstants.BrainTierBTree, WritesChannels = new[] { typeof(LocomotionChannel) },
        });
        Assert.False(f.Model.ApplySop(f.Unit, "SopT_Mover", "{}", running: false));
        Assert.Null(f.Model.Read(f.Unit)!.Sop);
    }

    [Fact]
    public void CE3043_TheParamsForm_WritesWhatTheBehaviourReads()
    {
        // the authored params type of a real generated behaviour; the form's JSON must parse back through BehaviorParams
        var f = new Fixture();
        var type = f.Registry.GetRegisteredNames().Select(f.Model.ParamsTypeOf).First(t => t is { IsValueType: true })!;
        object value = BehaviorParamsForm.Hydrate(type, "{}");
        string json = BehaviorParamsForm.ToJson(value, type);
        Assert.Equal(json, BehaviorParamsForm.ToJson(BehaviorParamsForm.Hydrate(type, json), type));
    }

    /// <summary>⭐ <c>CE-1314</c> (R-1036, BV-38) — the AI editing is a SECTION of the Brain tab, ⛔ no longer its own tab.
    /// 🔴 Red-proof: register it on the workspace again ⇒ red.</summary>
    [Fact]
    public void CE1314_TheBrainTab_OffersTheAiEditing_AsASection_AndThereIsNoAiTab()
    {
        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));
        Assert.DoesNotContain(EntityAiDetailsViewDescriptor.ViewId, editor.ScenarioWorkspace!.DetailsViews.All.Select(d => d.Id));
        Assert.Contains(EntityAiDetailsViewDescriptor.ViewId, ScenarioBrain(editor).Embeds.Select(d => d.Id));
    }

    private static Hrot.Editor.AiShared.Shell.EntityBrainView ScenarioBrain(EditorSubsystem editor)
        => (Hrot.Editor.AiShared.Shell.EntityBrainView)editor.ScenarioWorkspace!.DetailsViews.All
            .Single(d => d.Id == Hrot.Editor.AiShared.Shell.EntityBrainViewDescriptor.ViewId).Create();

    /// <summary>
    /// ⭐⭐ <c>CE-1023</c> — the forwarding rail, on the CONSTRUCTED view: the editor holds the map-pick adapter, so the AI section's
    /// params forms get picks, each row in its own scope. 🔴 It passed <c>pickerCtx: null</c> — no Pick button on any field.
    /// </summary>
    [Fact]
    public void CE1023_TheEditorsAiSection_GetsThePicks_ScopedPerRow()
    {
        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));
        var view = (EntityAiDetailsView)ScenarioBrain(editor).EmbedInstance(EntityAiDetailsViewDescriptor.ViewId);

        var task = Assert.IsType<Fdp.Presentation.Editing.ScopedMapPickContext>(view.PicksFor("$.task"));
        Assert.Equal("$.task.Vehicle", task.Path("$.Vehicle"));
        Assert.Equal("$.sop.Vehicle", ((Fdp.Presentation.Editing.ScopedMapPickContext)view.PicksFor("$.sop")!).Path("$.Vehicle"));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1023</c> F1 — the AI section's edit service draws an <c>EntityRef</c> param as ONE leaf (the pickable kind), not a
    /// container. 🔴 <c>AiFacetEditService</c> lacked the standard leaves, so the field was a read-only struct with a
    /// <c>NetworkId</c> child — no pick could ever land.
    /// </summary>
    [Fact]
    public void CE1023_TheAiSectionsEditService_DrawsAnEntityRefParamAsOneLeaf()
    {
        var svc = Hrot.Editor.AiShared.Inspector.ActionBinding.AiFacetEditService.Build();
        var session = BehaviorParamsForm.Open(svc, typeof(Hrot.Map.Definitions.Behavior.FireAtTargetParamsJsonDto), "{\"targetNetworkId\":5}");
        var node = Find(session.Document.Root, "$.TargetNetworkId");
        Assert.NotNull(node);
        Assert.Empty(node!.Children);
        Assert.NotEqual(StructEdit.Core.EditNodeKind.Struct, node.Kind);
    }

    private static StructEdit.Core.EditNode? Find(StructEdit.Core.EditNode n, string path)
        => n.JsonPath == path ? n : n.Children.Select(c => Find(c, path)).FirstOrDefault(x => x != null);
}
