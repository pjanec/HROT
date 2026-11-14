using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Edits;
using Hrot.Map.Common;
using Hrot.Presentation.Edits;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.ScenarioEditor.Tests.Edits;

/// <summary>
/// ⭐ B-14 / Edit-mode undo U2 + U5 (docs/UX/DESIGN_Edit_Mode_Undo.md §2.2) — ONE user action is ONE step of the ONE history: a drag (its preview
/// writes the world directly, so the "before" is taken when it starts) undoes to where it began and redoes to where it ended; a Submit inside it
/// joins it; the same action again within a second coalesces; a cancelled drag records nothing; nothing is recorded outside Edit; a step whose
/// snapshot the ring evicted is greyed; Save is the clean point.
/// </summary>
public sealed class WorldUndoTests
{
    private sealed class Rig : System.IDisposable
    {
        public readonly EntityRepository World = new();
        public readonly EditHistory History = new();
        public readonly Entity Unit;
        public bool Recording = true;
        public double Now;
        public readonly WorldUndoRecorder Recorder;

        public Rig(int maxDepth = WorldSnapshotRing.DefaultMaxDepth)
        {
            HrotSharedComponentRegistry.RegisterAll(World);
            World.RegisterComponent<VehicleState>();
            Unit = World.CreateEntity();
            World.AddComponent(Unit, new SimTransform { Position = new Vector3(10f, 20f, 0f) });
            World.AddComponent(Unit, new NetworkIdentity { Value = 55 });
            World.Tick();
            Recorder = new WorldUndoRecorder(History, new WorldSnapshotRing(maxDepth: maxDepth), () => Recording, () => Now);
            EditSteps.Install(World, Recorder);
        }

        public Vector3 Pos => World.GetComponent<SimTransform>(Unit).Position;

        public void Drag(Vector3 from, Vector3 to, bool cancel = false)
        {
            var g = new EntityDragGizmo(World, Unit);
            g.OnInteractionStarted(default, from);
            g.OnDragUpdate(from + (to - from) * 0.5f);
            g.OnDragUpdate(to);
            if (cancel) g.OnCancel(); else g.OnCommit(to);
        }

        public void Dispose() => World.Dispose();
    }

    /// <summary>🔴 Red-proof (compiles): the drag's <c>EditSteps.Begin</c> moved to the commit ⇒ the "before" is the dragged position ⇒ undo does not move it ⇒ red.</summary>
    [Fact]
    public void B14_ADragIsOneStep_UndoGoesBackToWhereItBegan_RedoToWhereItEnded()
    {
        using var rig = new Rig();
        rig.Drag(new Vector3(10f, 20f, 0f), new Vector3(110f, 220f, 0f));

        Assert.Equal(1, rig.History.Count);
        Assert.Equal("Move", rig.History.UndoLabel);
        Assert.Equal(new Vector3(110f, 220f, 0f), rig.Pos);

        Assert.True(rig.History.Undo());
        Assert.Equal(new Vector3(10f, 20f, 0f), rig.Pos);
        Assert.True(rig.History.Redo());
        Assert.Equal(new Vector3(110f, 220f, 0f), rig.Pos);
    }

    [Fact]
    public void B14_ASubmitInsideABracketJoinsIt_TheSameActionWithinASecondCoalesces_AnotherDoesNot()
    {
        using var rig = new Rig();
        EditSteps.Begin(rig.World, "Move");
        Fdp.Toolkit.Replication.Edits.Edits.Submit(rig.World, EditOp.Write(rig.Unit, Fdp.Toolkit.Replication.Patching.EntityAttributeChange.Double(Fdp.Toolkit.Replication.Patching.AttributeIds.PositionX, 1.0), "Rename"));
        EditSteps.End(rig.World);
        Assert.Equal(1, rig.History.Count);                    // the nested Submit is not a step of its own

        rig.Now = 0.5;
        EditSteps.Begin(rig.World, "Move"); EditSteps.End(rig.World);
        Assert.Equal(1, rig.History.Count);                    // again within 1 s: coalesced

        rig.Now = 0.6;
        Fdp.Toolkit.Replication.Edits.Edits.Submit(rig.World, EditOp.Write(rig.Unit, Fdp.Toolkit.Replication.Patching.EntityAttributeChange.Double(Fdp.Toolkit.Replication.Patching.AttributeIds.PositionX, 2.0), "Rename"));
        Assert.Equal(2, rig.History.Count);                    // a Submit alone is a step
        Assert.Equal("Rename", rig.History.UndoLabel);

        rig.Now = 5;
        EditSteps.Begin(rig.World, "Rename"); EditSteps.End(rig.World);
        Assert.Equal(3, rig.History.Count);                    // the same action, but later: its own step
    }

    [Fact]
    public void B14_ACancelledDragRecordsNothing_AndLeavesNoStepOpen_NothingIsRecordedOutsideEdit()
    {
        using var rig = new Rig();
        rig.Drag(new Vector3(10f, 20f, 0f), new Vector3(50f, 50f, 0f), cancel: true);
        Assert.Equal(0, rig.History.Count);
        Assert.Equal(new Vector3(10f, 20f, 0f), rig.Pos);

        rig.Drag(new Vector3(10f, 20f, 0f), new Vector3(30f, 30f, 0f));
        Assert.Equal(1, rig.History.Count);                    // the cancelled bracket was closed

        rig.Recording = false; rig.Now = 10;
        rig.Drag(new Vector3(30f, 30f, 0f), new Vector3(60f, 60f, 0f));
        Assert.Equal(1, rig.History.Count);
    }

    /// <summary>⭐ CE-4411 — delete → undo → the entity comes back WITH its network identity (and authority / route), so it is an entity the edit path treats exactly like
    /// the original: deleting it again is accepted ("the entity is not replicated" was the refusal), and so is an edit of it.
    /// 🔴 Red-proof (compiles): drop the identity components from the undo capture (<c>IsReplicationIdentity</c> → <c>false</c>) ⇒ the restored entity has no id ⇒ red.</summary>
    [Fact]
    public void CE4411_AnUndoneDelete_RestoresTheNetworkIdentity_SoItCanBeDeletedAgainAndEdited()
    {
        using var rig = new Rig();
        rig.World.AddComponent(rig.Unit, new NetworkAuthority { PrimaryOwnerId = 1, LocalNodeId = (NodeId)1 });
        rig.World.Tick();

        EditSteps.Begin(rig.World, "Delete"); rig.World.DestroyEntity(rig.Unit); EditSteps.End(rig.World);   // the teardown the owner's system performs, as one step
        Assert.False(rig.World.IsAlive(rig.Unit));

        Assert.True(rig.History.Undo());
        var back = Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(rig.World, 55);
        Assert.NotEqual(Entity.Null, back);                                                      // the id came back with the entity
        Assert.True(rig.World.HasComponent<NetworkAuthority>(back));

        var again = Fdp.Toolkit.Replication.Edits.Edits.Submit(rig.World, EditOp.Delete(back, "Delete"));
        Assert.NotEqual(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused, again.Route);                                  // not "the entity is not replicated"

        rig.Now = 10;
        var rename = Fdp.Toolkit.Replication.Edits.Edits.Submit(rig.World, EditOp.Write(back, Fdp.Toolkit.Replication.Patching.EntityAttributeChange.Double(Fdp.Toolkit.Replication.Patching.AttributeIds.PositionX, 3.0), "Rename"));
        Assert.NotEqual(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused, rename.Route);
    }

    /// <summary>⭐ N-15 (CE-4477) — an entity a host keeps OUT of the scenario (<c>ScenarioIgnoreTag</c>: derived parts, effects, traffic replicas, rounds —
    /// no <c>EntityGuid</c>, by design) stays out after an undo / redo: the tag rides the undo step like the replication identity (CE-4411), so the next save does
    /// not meet a guid-less entity. 📌 Measured: an editor session's save after edit → undo → redo threw "entity 5 has no EntityGuid" (both kernel modes); a save
    /// with no undo saved. 🔴 Red-proof (compiles): drop the tag from <c>IsReplicationIdentity</c> ⇒ the restored entity loses it ⇒ red.</summary>
    [Fact]
    public void N15_AnEntityKeptOutOfTheScenario_StaysOutAfterUndoAndRedo_AndTheSceneStillSaves()
    {
        using var rig = new Rig();
        rig.World.AddComponent(rig.Unit, new Fdp.Toolkit.Scenario.EntityGuid(System.Guid.NewGuid()));
        var derived = rig.World.CreateEntity();
        rig.World.AddComponent(derived, new SimTransform { Position = new Vector3(1f, 2f, 0f) });
        rig.World.AddComponent(derived, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());
        rig.World.Tick();

        rig.Drag(new Vector3(10f, 20f, 0f), new Vector3(40f, 50f, 0f));
        Assert.True(rig.History.Undo());
        Assert.True(rig.History.Redo());

        Assert.True(rig.World.HasComponent<Fdp.Toolkit.Scenario.ScenarioIgnoreTag>(derived));
        var serializer = new Fdp.Toolkit.Scenario.ScenarioSerializerBuilder("Hrot.Scenario").Build();
        serializer.Serialize(rig.World, new Fdp.Toolkit.Scenario.ScenarioHeader("Hrot.Scenario"));   // throws on a guid-less saveable entity (CE-4153)
    }

    /// <summary>🔴 Red-proof (compiles): the world step pushed without <c>available</c> ⇒ an evicted step stays enabled ⇒ red.</summary>
    [Fact]
    public void B14_AStepWhoseSnapshotWasEvicted_IsGreyed_SaveIsTheCleanPoint()
    {
        using var rig = new Rig(maxDepth: 2);   // the undo itself captures the AFTER state, so 2 is the smallest ring that keeps a before
        Assert.False(rig.History.IsDirty);
        rig.Drag(new Vector3(10f, 20f, 0f), new Vector3(30f, 30f, 0f));
        Assert.True(rig.History.IsDirty);
        rig.History.MarkSaved();
        Assert.False(rig.History.IsDirty);

        rig.Now = 10;
        rig.Drag(new Vector3(30f, 30f, 0f), new Vector3(60f, 60f, 0f));   // [b1, b2]
        Assert.True(rig.History.IsDirty);
        Assert.True(rig.History.CanUndo);
        Assert.True(rig.History.Undo());                       // captures a2 ⇒ [b2, a2]: b1 is evicted
        Assert.False(rig.History.IsDirty);                     // back at the saved point
        Assert.Equal(new Vector3(30f, 30f, 0f), rig.Pos);
        Assert.False(rig.History.CanUndo);                     // the older step's before-state is gone: greyed, never a partial undo
        Assert.Equal("Move", rig.History.UndoLabel);           // still named

        rig.History.Clear();
        Assert.Equal(0, rig.History.Count);
        Assert.False(rig.History.IsDirty);
    }

    /// <summary>⭐ P-7 7c (D9) — an Orders-tab Apply in Edit mode is ONE undo step by construction: the mission commit is an <c>EditOp.Commit</c> through
    /// <c>Edits.Submit</c>, which brackets it; an ORDER (a Command) is not a world edit and takes no step.</summary>
    [Fact]
    public void P7c_ACommitIsAnUndoStep_AnOrderIsNot()
    {
        using var rig = new Rig();
        Fdp.Toolkit.Replication.Edits.Edits.Submit(rig.World, EditOp.Commit(rig.Unit, _ => { }, "Commit the mission plan"));
        Assert.Equal(1, rig.History.Count);
        Assert.Equal("Commit the mission plan", rig.History.UndoLabel);
        rig.Now = 10;
        Fdp.Toolkit.Replication.Edits.Edits.Submit(rig.World, EditOp.Command(rig.Unit, _ => { }, "Move"));
        Assert.Equal(1, rig.History.Count);
    }

    private sealed class ListOutbox : Fdp.Core.Threading.IPresentationOutbox
    {
        public readonly List<System.Action<EntityRepository>> Items = new();
        public void Post(System.Action<EntityRepository> a) => Items.Add(a);
        public void DrainOn(EntityRepository ui) { var copy = Items.ToArray(); Items.Clear(); foreach (var a in copy) a(ui); }
    }

    /// <summary>
    /// ⭐ N-7 (CE-4469, U-1) — the THREADED editor's undo: the recorder runs on the SIMULATION world (S) and hands each step to the UI's history
    /// through the publish queue; the history's undo / redo are sim jobs that restore S; a step whose snapshot the ring evicted is greyed through
    /// the same queue (the UI never reads the ring); a write made inside an undo job joins that step (Applying is the sim side's).
    /// 🔴 Red-proof (run): the sink's undo running its action at once on the UI thread instead of posting it ⇒ S changes before the sim drains.
    /// </summary>
    [Fact]
    public void N7_TheThreadedUndo_RecordsOnS_UndoesAndRedoesThroughSimJobs_AndGreysAnEvictedStep()
    {
        using var s = new EntityRepository();
        using var ui = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(s);
        var unit = s.CreateEntity();
        s.AddComponent(unit, new SimTransform { Position = new Vector3(10f, 20f, 0f) });
        s.AddComponent(unit, new NetworkIdentity { Value = 55 });
        s.Tick();
        var ring = new WorldSnapshotRing();
        var history = new EditHistory();
        var toUi = new ListOutbox();
        var simJobs = new List<System.Action>();
        var sink = new ThreadedUndoSink(history, toUi, (_, a) => simJobs.Add(a), ring);
        EditSteps.Install(s, new WorldUndoRecorder(sink, ring, () => true));

        // an edit on the sim thread, bracketed
        EditSteps.Begin(s, "Move");
        s.GetComponentRW<SimTransform>(unit).Position = new Vector3(50f, 60f, 0f);
        EditSteps.End(s);
        Assert.Equal(0, history.Count);                       // nothing on the UI history until the publish queue is drained
        toUi.DrainOn(ui);
        Assert.True(history.CanUndo);
        Assert.Equal("Move", history.UndoLabel);

        Assert.True(history.Undo());                          // the UI's undo …
        Assert.Equal(50f, s.GetComponentRO<SimTransform>(unit).Position.X);   // … is a JOB: S untouched until the sim runs it
        Assert.Single(simJobs);
        simJobs[0](); simJobs.Clear();
        Assert.Equal(10f, s.GetComponentRO<SimTransform>(unit).Position.X);
        Assert.Equal(0, history.Position);                    // the one step, undone

        Assert.True(history.Redo());
        simJobs[0](); simJobs.Clear();
        Assert.Equal(50f, s.GetComponentRO<SimTransform>(unit).Position.X);

        ring.Clear();                                         // the snapshots go (a scenario opened, an eviction) …
        toUi.DrainOn(ui);
        Assert.False(history.StepAvailable(0));               // … and the UI's step is greyed through the queue
        Assert.False(history.CanUndo);
    }
}

