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
}
