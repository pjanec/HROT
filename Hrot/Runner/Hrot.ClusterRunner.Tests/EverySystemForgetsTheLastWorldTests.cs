using System.Collections.Generic;
using Fdp.TestShared;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3294</c> — a system that keeps entities in its own fields forgets them at the world boundary (<c>WorldEpoch.Moved</c>), or the
/// field is named here with why it needs nothing. 📐 Measured before: 41 systems held per-entity state and ONE reset — after an editor reload
/// (<c>SoftClear</c>: the next run's units under the SAME handles) an aircraft never ejected, a crowd destroyed an unrelated new entity, an
/// order with last run's ids was skipped, a passenger was never killed with his vehicle. 📄 <c>docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md</c>
/// §7f. ⚠ The Stride assemblies are NOT walked: <c>HrotStrideApp.Game.Tests</c> links the probe and calls it nowhere (CE-3307).
/// </summary>
public sealed class EverySystemForgetsTheLastWorldTests
{
    /// <summary>Fields that need no reset — each REBUILT before use in every Execute (scratch), or holding no handle across frames.</summary>
    public static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>
    {
        ["Fdp.Toolkit.Vis2D.Systems.HierarchyOrderSystem._visited"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["CarKinem.Systems.PathTraceSystem._writes"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["CarKinem.Systems.SpatialHashSystem._warned"] = "a warn-once log set (cleared on a new terrain fit); changes no behaviour",
        ["CarKinem.Systems.DoorPassageSystem._turn"] = "whose turn it is at each doorway, rebuilt every frame (CE-4040)",
        ["Fdp.Toolkit.Squad.Command.StaffComposerSystem._launches"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Replication.Systems.LightweightRoundIdentitySystem._pending"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Replication.Systems.OwnershipRecomputeSystem._touched"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Replication.Systems.OwnershipRecomputeSystem._order"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Replication.Systems.PartialOwnerReclaimSystem._moves"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Replication.Systems.PartialOwnerReclaimSystem._emptied"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Physics.Systems.HitResolutionSystem._resolved"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Perception.Systems.ActiveSensorTracksUpdateSystem._working"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Perception.Systems.ActiveSensorTracksUpdateSystem._order"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.NetworkSpawning.Systems.SpawnLevelSystem._waiting"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Motion.DescentLandingSystem._done"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Combat.Systems.HandsFuzeSystem._cooked"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Combat.Systems.WeaponAmmoMirrorSystem._first"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Combat.Systems.WeaponAmmoMirrorSystem._links"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Behavior.Systems.CognitiveInterruptSystem._toInit"] = "scratch — filled and emptied within one run",
        ["Fdp.Toolkit.Behavior.Diagnostics.TraceBufferLifecycleSystem._toAddBTree"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Behavior.Diagnostics.TraceBufferLifecycleSystem._toRemoveBTree"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Behavior.Diagnostics.TraceBufferLifecycleSystem._toAddHsm"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Behavior.Diagnostics.TraceBufferLifecycleSystem._toRemoveHsm"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Behavior.Diagnostics.TraceBufferLifecycleSystem._toAddUtility"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Fdp.Toolkit.Behavior.Diagnostics.TraceBufferLifecycleSystem._toRemoveUtility"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Hrot.Common.EntityCreation.SeatPresetSystem._done"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Hrot.Diagnostics.Breakpoints.DataBreakpointSystem._pendingHitsBuffer"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Hrot.SimHost.Systems.InitialBrainMaterializationSystem._done"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Hrot.UI.Common.Effects.EffectSpawnSystem._decals"] = "scratch — rebuilt from the query on every housekeeping pass",
        ["Hrot.SimHost.Systems.EqsResultUpdateSystem._byIndex"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Hrot.SimHost.Systems.EqsResultUpdateSystem._byKey"] = "scratch — cleared at the top of each run and filled from this frame's world",
        ["Hrot.SimHost.Systems.EqsSolverSystem._lastSchedule"] = "this tick's schedule (cleared before it is built); read by diagnostics",
    };

    [Fact]
    public void EverySystemHoldingEntities_ForgetsThemAtTheWorldBoundary()
    {
        var r = WorldBoundaryProbe.Walk(Exempt);
        Assert.True(r.Systems > 150, $"only {r.Systems} systems walked — the probe is not finding the tree.");
        Assert.True(r.Missing.Count == 0, $"{r.Missing.Count} of {r.Holding} systems holding entities neither call WorldEpoch.Moved nor exempt the field:\n  "
                                          + string.Join("\n  ", r.Missing));
        Assert.True(r.UnusedExemptions.Count == 0, "exemptions naming no field any more: " + string.Join(", ", r.UnusedExemptions));
    }
}
