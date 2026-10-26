using System;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Terrain;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ <c>CE-3117</c> (R-226) — the debug-trace gizmos draw what their RECORDED components hold, on their own layer bits, and age it by
/// sim time. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a.
/// </summary>
public sealed class DebugTraceGizmoTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly DebugPrimitiveBuffer _draw = new();

    public DebugTraceGizmoTests()
    {
        _w.RegisterComponent<SimTransform>();
        Now(10.0);
    }

    public void Dispose() => _w.Dispose();

    private void Now(double t) => _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = t });

    private DebugPrimitive[] Frame(byte layer) => _draw.GetFrame().ToArray().Where(p => p.DebugLayer == layer).ToArray();

    private void Burst(double at)
    {
        var t = default(DetonationTraces);
        ref var d = ref t.SlotsRW()[0];
        d.Time = at; d.Burst = new Vector3(0, 0, 0.1f); d.FragmentRadius = 15f; d.BlastInjuryRadius = 6f;
        d.RayCount = 2;
        d.Rays[0] = new DetonationRay { To = new Vector3(0, 8, 1.5f), Transmission = 1f };
        d.Rays[1] = new DetonationRay { To = new Vector3(2, 8, 0.3f), StopAt = new Vector3(1, 4, 0.2f), HasStop = 1, Transmission = 0f };
        d.TargetCount = 1;
        d.Targets[0] = new DetonationTarget { At = new Vector3(0, 8, 0), Exposure = 0.5f, Damage = 30f };
        t.Count = 1;
        _w.SetSingletonUnmanaged(in t);
    }

    /// <summary>⭐ CE-3153 — one terrain-stopped shot (the orange case) stamped at <paramref name="at"/>.</summary>
    private void Shot(double at)
    {
        var t = default(ShotTraces);
        ref var s = ref t.SlotsRW()[0];
        s.Seq = 1; s.Time = at;
        s.Muzzle = new Vector3(0, 0, 1.5f);
        s.End    = new Vector3(10, 0, 1.5f);
        s.Outcome = ShotOutcome.StoppedByTerrain;
        s.HasEnd = 1; s.CrossingCount = 0;
        t.Count = 1;
        _w.SetSingletonUnmanaged(in t);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-3153</c> — <b>fire traces expire, and a trace from a PREVIOUS run must not reappear at t = 0.</b>
    /// <para>🔒 User, <c>2026-10-09</c>: <i>"traces can not be persistent, they need to disappear after short time
    /// after being drawn for the first time"</i> — then, after the first attempt: <i>"when i reload the duel
    /// scenario, the orange fire lines are drawn immediately from beginning … time-based fading into
    /// non-existence is not working."</i></para>
    /// <para>🔴 <b>The third case is the one that caught a real reasoning error.</b> Nothing clears the
    /// <c>ShotTraces</c> singleton on a scenario load, and sim time restarts near 0, so last run's traces carry
    /// <c>Time</c> ≈ 100 against <c>now</c> ≈ 0 ⇒ a NEGATIVE age. The first implementation treated negative as
    /// "fresh" (reasoning about a backwards replay seek) and drew the whole previous duel at full alpha. ⛔ Hiding
    /// it is correct on both timelines: after a reload the shot belongs to a run that no longer exists, and on a
    /// backwards seek it has genuinely not been fired yet.</para>
    /// </summary>
    [Fact]
    public void CE3153_AFireTrace_IsDrawnBriefly_ExpiresWithAge_AndNeverReappearsAfterAReload()
    {
        Shot(at: 10.0);
        Now(10.0);
        new FireTraceGizmo().Draw(_w, _draw);
        Assert.NotEmpty(Frame(FireTraceGizmo.FireTraceLayer));

        // Past the cutoff: gone.
        _draw.Clear();
        Now(10.0 + FireTraceGizmo.Shown + 0.5);
        new FireTraceGizmo().Draw(_w, _draw);
        Assert.Empty(Frame(FireTraceGizmo.FireTraceLayer));

        // 🔴 THE RELOAD CASE: the clock restarts while the stale trace keeps its old stamp ⇒ negative age.
        _draw.Clear();
        Now(0.0);
        new FireTraceGizmo().Draw(_w, _draw);
        Assert.Empty(Frame(FireTraceGizmo.FireTraceLayer));
    }

    [Fact]
    public void CE3117_ABurst_DrawsItsRaysAndRings_ForOneSecondOfSimTime()
    {
        Burst(at: 9.6);
        new DetonationGizmo().Draw(_w, _draw);
        var blast = Frame(DebugTraceLayers.Blast);
        Assert.Equal(3, blast.Count(p => p.Shape == DebugPrimitiveShape.Sphere));   // fragment ring, blast ring, the burst
        // the clear ray (1 line) + the blocked ray to its stop (1 line) + the ✕ at the stop (2 lines)
        Assert.Equal(4, blast.Count(p => p.Shape == DebugPrimitiveShape.Line));
        Assert.Contains(blast, p => p.Shape == DebugPrimitiveShape.Line && p.LineEnd == new Vector3(1, 4, 0.2f));
        Assert.Single(blast, p => p.Shape == DebugPrimitiveShape.Text);

        _draw.Clear();
        Now(10.7);                                   // 1.1 s later: gone
        new DetonationGizmo().Draw(_w, _draw);
        Assert.Empty(Frame(DebugTraceLayers.Blast));
    }

    [Fact]
    public void CE3117_AShotSound_ExpandsFromItsSource_AndAHeardEstimateShowsItsUncertainty()
    {
        _w.RegisterComponent<AcousticEmitter>();
        _w.RegisterComponent<HeardTraces>();
        var shooter = _w.CreateEntity();
        _w.AddComponent(shooter, new SimTransform { Position = new Vector3(50, 0, 0) });
        _w.AddComponent(shooter, new AcousticEmitter
        {
            FiringAudibleRange = 200f, ShotTimeLeft = SoundEmissionSystem.SoundLingerSeconds / 2f, ShotX = 50, ShotY = 0,
        });
        var listener = _w.CreateEntity();
        _w.AddComponent(listener, new SimTransform { Position = new Vector3(0, 0, 0) });
        var heard = default(HeardTraces);
        heard.Add(new HeardTrace { Time = 9.8, At = new Vector3(48, 3, 0), Radius = 5f, SourceClass = 4 });
        _w.AddComponent(listener, heard);

        new HearingGizmo().Draw(_w, _draw);
        var layer = Frame(DebugTraceLayers.Hearing);
        var ring = Assert.Single(layer, p => p.Shape == DebugPrimitiveShape.Sphere && p.SphereCenter == new Vector3(50, 0, 0));
        Assert.Equal(100f, ring.SphereRadius, 3);    // half the linger gone = half the audible range
        Assert.Single(layer, p => p.Shape == DebugPrimitiveShape.Sphere && p.SphereCenter == new Vector3(48, 3, 0) && p.SphereRadius == 5f);
        Assert.Single(layer, p => p.Shape == DebugPrimitiveShape.Line && p.LineEnd == new Vector3(48, 3, 0));
    }

    [Fact]
    public void CE3117_ThePlannedPath_DrawsTheLookaheadPointTheControllerSteersAt()
    {
        _w.RegisterComponent<NavState>();
        _w.RegisterComponent<PathTrace>();
        _w.RegisterComponent<VehicleParams>();
        _w.RegisterComponent<VehicleState>();
        var mover = _w.CreateEntity();
        var trace = new PathTrace { TrajectoryId = 1, TotalLength = 100f, Count = 2 };
        trace.PointsRW()[0] = new PathTracePoint { Position = new Vector3(0, 0, 0), S = 0f };
        trace.PointsRW()[1] = new PathTracePoint { Position = new Vector3(100, 0, 0), S = 100f };
        _w.AddComponent(mover, trace);
        _w.AddComponent(mover, new NavState { Mode = KinematicsMode.CustomTrajectory, TrajectoryId = 1, ProgressS = 20f });
        var p = new VehicleParams { WheelBase = 0.5f, LookaheadTimeMin = 0.5f };
        _w.AddComponent(mover, p);
        _w.AddComponent(mover, new VehicleState { Speed = 10f });

        new PlannedPathGizmo().Draw(_w, mover, _draw);

        float ahead = CarKinematicsSystem.PathLookahead(in p, 10f);   // 5 m
        var spheres = Frame(DebugTraceLayers.Paths).Where(x => x.Shape == DebugPrimitiveShape.Sphere).ToArray();
        Assert.Contains(spheres, x => x.SphereCenter == new Vector3(20, 0, 0));            // the progress point
        Assert.Contains(spheres, x => x.SphereCenter == new Vector3(20 + ahead, 0, 0));    // the look-ahead point
    }

    [Fact]
    public void CE3117_ADoorLeaf_LiesAlongTheWallClosed_AndSwingsNinetyDegreesOpen()
    {
        // the leaf footprint BuildDoorLeaves makes for a 1 m door from (0,0) along +X: { p0−n, p1−n, p1+n, p0+n }
        var fp = new[] { new Vector2(0, -0.02f), new Vector2(1, -0.02f), new Vector2(1, 0.02f), new Vector2(0, 0.02f) };
        var (hinge, closedTip) = DoorLeafGizmo.Leaf(fp);
        Assert.Equal(new Vector2(0, 0), hinge);
        Assert.Equal(new Vector2(1, 0), closedTip);
        Assert.Equal(new Vector2(0, 1), DoorLeafGizmo.OpenTip(hinge, closedTip));
    }
    // ⭐ CE-1069 — THE 2-D DOOR PICTURE stays (🔒 user, 2026-10-14: "Door needs the opened closed indication in 2d (the one already
    //   existing)"): with the door ENTITY's symbol gone (CE-1068), every door still draws its leaf line on the Doors layer, coloured by
    //   its live state — shut along the wall, open swung on its hinge, locked purple, destroyed a red cross — and the 2-D map accepts it.
    [Fact]
    public void CE1069_EveryDoorStillDrawsItsStateLine_On2D()
    {
        var terrain = Hrot.Presentation.Tests.Map3D.Map3DTests.BtRange();
        _w.SetSingletonManaged(terrain);
        _w.RegisterComponent<DoorState>();
        _w.RegisterManagedComponent<TerrainObjectKey>();
        var states = new[] { TerrainDoorState.Closed, TerrainDoorState.Open, TerrainDoorState.Locked, TerrainDoorState.Destroyed };
        Assert.True(terrain.Doors.Count >= states.Length, "bt-range has four doors");
        for (int i = 0; i < states.Length; i++)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new DoorState { State = states[i] });
            _w.AddComponent(e, new TerrainObjectKey { Key = terrain.Doors[i].Key });
        }
        new DoorLeafGizmo().Draw(_w, _draw);
        var lines = Frame(DebugTraceLayers.Doors);
        foreach (var l in lines) Assert.NotEqual(PipelineTarget.None, l.TargetView & PipelineTarget.Map2D);   // the 2-D map draws it
        for (int i = 0; i < states.Length; i++)
            Assert.Contains(lines, l => l.Color.Equals(DoorLeafGizmo.ColorOf(states[i])));
        var (hinge, closedTip) = DoorLeafGizmo.Leaf(terrain.DoorLeaves[1].Footprint);
        var openTip = DoorLeafGizmo.OpenTip(hinge, closedTip);
        Assert.Contains(lines, l => l.Color.Equals(DoorLeafGizmo.ColorOf(TerrainDoorState.Open))
                                     && Vector2.Distance(new Vector2(l.LineEnd.X, l.LineEnd.Y), openTip) < 1e-3f);   // swung on its hinge
    }

    // ⭐ CE-3121 — the utility gizmo writes one line per logged decision, AT the unit (the dormant overlay wrote one at the origin).
    [Fact]
    public void CE3121_TheUtilityGizmo_WritesOneLinePerLoggedDecision()
    {
        _w.RegisterComponent<Fdp.Toolkit.Utility.UtilityDecisionLog>();
        var unit = _w.CreateEntity();
        _w.AddComponent(unit, new SimTransform { Position = new Vector3(30, 40, 0) });
        var log = default(Fdp.Toolkit.Utility.UtilityDecisionLog);
        var slots = log.SlotsRW();
        slots[0].DecisionId = 7; slots[0].Winner = 3; slots[0].Margin = 0.25f;
        slots[1].DecisionId = 9; slots[1].Winner = 1; slots[1].Margin = 0.5f;
        _w.AddComponent(unit, log);

        new UtilityDecisionGizmo().Draw(_w, unit, _draw);
        Assert.Equal(2, _draw.GetFrame().ToArray().Count(p => p.Shape == DebugPrimitiveShape.Text));
        // ⭐ CE-1203 (§5e FL-3) — the Utility row: drawn on its own layer, nothing with it hidden
        Fdp.TestShared.GizmoLayerProbe.AssertOnItsOwnLayer(d => new UtilityDecisionGizmo().Draw(_w, unit, d), DebugTraceLayers.Utility);
    }

    // ⭐ CE-3136 (T3) — the ACTIONS gizmo: one line per busy channel, the hold reasons amber, a stale row (older than 1 s of sim
    // time) not drawn. 📄 docs/DESIGN_Ai_Action_Status_Gizmo.md.
    [Fact]
    public void CE3136_TheActionsGizmo_SaysWhyAUnitIsNotFiring()
    {
        _w.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>();
        _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 10.0, DeltaTime = 0.016f, TimeScale = 1f });
        var unit = _w.CreateEntity();
        _w.AddComponent(unit, new SimTransform { Position = new Vector3(5, 5, 0) });
        var status = new Fdp.Toolkit.Behavior.Diagnostics.ActionStatus();
        status.Weapon = new Fdp.Toolkit.Behavior.Diagnostics.ActionStatusRow
            { ActionId = 1, Reason = Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldNotSeen, At = 9.9 };
        status.Locomotion = new Fdp.Toolkit.Behavior.Diagnostics.ActionStatusRow
            { ActionId = 3, Reason = Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Moving, At = 8.0 };   // stale: 2 s old
        _w.AddComponent(unit, status);

        new ActionStatusGizmo().Draw(_w, unit, _draw);
        var texts = _draw.GetFrame().ToArray().Where(p => p.Shape == DebugPrimitiveShape.Text).ToArray();
        Assert.Single(texts);

        var aiming = new Fdp.Toolkit.Behavior.Diagnostics.ActionStatusRow
            { Reason = Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Aiming, Progress = 0.4f, Needed = 0.8f, At = 10.0 };
        Assert.Equal("W aiming 0.4/0.8s", ActionStatusGizmo.Text('W', in aiming, 10.0));
        Assert.Equal("W hold: not seen", ActionStatusGizmo.Text('W', in status.Weapon, 10.0));
        Assert.Null(ActionStatusGizmo.Text('L', in status.Locomotion, 10.0));
        // ⭐ CE-1203 (§5e FL-3) — the Actions row
        Fdp.TestShared.GizmoLayerProbe.AssertOnItsOwnLayer(d => new ActionStatusGizmo().Draw(_w, unit, d), DebugTraceLayers.Actions);
    }

    // ⭐ CE-3136 — the Actions family is a pinnable, scoped family like the others (layer panel + "Pin gizmos" menu).
    [Fact]
    public void CE3136_TheActionsFamily_IsListed_PinnableAndSelectedByDefault()
    {
        Assert.Contains(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Channels, GizmoFamilies.All);
        Assert.Equal("Actions", GizmoFamilies.Label(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Channels));
        Assert.Equal(GizmoScope.PinnedOnly, GizmoFamilies.DefaultScope(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Channels));   // ⭐ CE-1329
        Assert.Equal(Hrot.Common.Constants.GlobalActionIds.PinGizmosActions, Hrot.Common.Diagnostics.Gizmos.GizmoPins.ActionIdOf(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Channels));
    }

    /// <summary>
    /// ⭐ <c>CE-3143</c> — a PART follows its unit: under "selected or pinned" a unit's sensor child draws when the UNIT is selected or
    /// pinned (the user never selects the part), and EQS now defaults to that scope. 🔴 Red-proof: read the selection from the entity
    /// itself in <c>GizmoFamilyVisibilityPolicy.IsEntityVisible</c> and the first assertion fails.
    /// </summary>
    [Fact]
    public void CE3143_APartFollowsItsUnit_WhenTheUnitIsPinned_AndEqsDefaultsToPinnedOnly()
    {
        var eqs = Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Eqs;
        Assert.Equal(GizmoScope.PinnedOnly, GizmoFamilies.DefaultScope(eqs));   // ⭐ CE-1329 — a brain family: pinned units only
        _w.RegisterComponent<Hrot.IG.Components.SelectionState>();
        _w.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
        _w.RegisterComponent<Fdp.Toolkit.Behavior.Diagnostics.DebugState>();
        var unit = _w.CreateEntity();
        _w.AddComponent(unit, new Hrot.IG.Components.SelectionState { IsSelected = false });
        var sensor = _w.CreateEntity();
        _w.AddComponent(sensor, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = unit, InstanceId = 1 });
        var policy = new Hrot.ScenarioEditor.Map.GizmoFamilyVisibilityPolicy(new Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingsRegistry(), eqs);

        Assert.False(policy.IsEntityVisible(_w, sensor));
        _w.SetComponent(unit, new Hrot.IG.Components.SelectionState { IsSelected = true });
        Assert.False(policy.IsEntityVisible(_w, sensor), "⭐ CE-1329 — selection draws no brain family");
        _w.AddComponent(unit, new Fdp.Toolkit.Behavior.Diagnostics.DebugState { Ai = eqs });   // the unit's pin (the store's copy)
        Assert.True(policy.IsEntityVisible(_w, sensor), "the pinned unit's sensor child must draw");
        Assert.Equal(unit, Hrot.ScenarioEditor.Map.GizmoFamilyVisibilityPolicy.UnitOf(_w, sensor));
    }

    // ⭐ CE-3121 — the squad gizmo draws a line from the commander to every LIVING member, at the members' real positions (the
    // dormant overlay drew them origin to origin).
    [Fact]
    public void CE3121_TheSquadGizmo_LinksTheCommanderToItsLivingMembers()
    {
        _w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
        _w.RegisterComponent<Fdp.Toolkit.Squad.SquadCognitiveState>();
        var a = _w.CreateEntity(); _w.AddComponent(a, new SimTransform { Position = new Vector3(10, 0, 0) });
        var b = _w.CreateEntity(); _w.AddComponent(b, new SimTransform { Position = new Vector3(0, 10, 0) });
        var gone = _w.CreateEntity(); _w.AddComponent(gone, new SimTransform { Position = new Vector3(5, 5, 0) });
        var cmd = _w.CreateEntity();
        _w.AddComponent(cmd, new SimTransform { Position = new Vector3(0, 0, 0) });
        var roster = default(Fdp.Core.CommandHierarchy.UnitRoster);
        Fdp.Core.CommandHierarchy.UnitRoster.Add(ref roster, a);
        Fdp.Core.CommandHierarchy.UnitRoster.Add(ref roster, b);
        Fdp.Core.CommandHierarchy.UnitRoster.Add(ref roster, gone);
        _w.AddComponent(cmd, roster);
        _w.AddComponent(cmd, default(Fdp.Toolkit.Squad.SquadCognitiveState));
        _w.DestroyEntity(gone);

        new SquadGizmo().Draw(_w, cmd, _draw);
        var ends = _draw.GetFrame().ToArray().Where(p => p.Shape == DebugPrimitiveShape.Line).Select(p => p.LineEnd).ToArray();
        Assert.Equal(new[] { new Vector3(10, 0, 0), new Vector3(0, 10, 0) }, ends);
        // ⭐ CE-1203 (§5e FL-3) — the Squad & command row
        Fdp.TestShared.GizmoLayerProbe.AssertOnItsOwnLayer(d => new SquadGizmo().Draw(_w, cmd, d), DebugTraceLayers.SquadCommand);
    }

    /// <summary>⭐ <c>CE-1317</c> (Brain View §12c BV-50) — the commander's merged contacts are their OWN family: <see cref="SquadContactsGizmo"/>
    /// draws a sphere and a line from the commander to each, the squad gizmo no longer draws them, and the picture projectors moved to
    /// <c>CommandPicture</c> — so the Brain tab can pin each alone. ✅ Red-proof: the contact loop back in <c>SquadGizmo</c> ⇒ red.</summary>
    [Fact]
    public void CE1317_TheCommandersContactsAndPicture_AreFamiliesOfTheirOwn()
    {
        _w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
        _w.RegisterComponent<Fdp.Toolkit.Squad.SquadCognitiveState>();
        var cmd = _w.CreateEntity();
        _w.AddComponent(cmd, new SimTransform { Position = new Vector3(0, 0, 0) });
        _w.AddComponent(cmd, default(Fdp.Core.CommandHierarchy.UnitRoster));
        var st = default(Fdp.Toolkit.Squad.SquadCognitiveState);
        st.Contacts.Count = 2;
        st.Contacts.Contacts[0] = new Fdp.Toolkit.Squad.SquadContact { PositionX = 40, PositionY = 0 };
        st.Contacts.Contacts[1] = new Fdp.Toolkit.Squad.SquadContact { PositionX = 0, PositionY = 30 };
        _w.AddComponent(cmd, st);

        new SquadGizmo().Draw(_w, cmd, _draw);
        Assert.DoesNotContain(_draw.GetFrame().ToArray(), p => p.Shape == DebugPrimitiveShape.Sphere);
        _draw.Clear();
        new SquadContactsGizmo().Draw(_w, cmd, _draw);
        var frame = _draw.GetFrame().ToArray();
        Assert.Equal(2, frame.Count(p => p.Shape == DebugPrimitiveShape.Sphere));
        Assert.Equal(new[] { new Vector3(40, 0, 0), new Vector3(0, 30, 0) },
            frame.Where(p => p.Shape == DebugPrimitiveShape.Line).Select(p => p.LineEnd).ToArray());   // a line from the commander to each

        static Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags FamilyOf(System.Type t)
            => ((Fdp.Toolkit.Diagnostics.Gizmos.GizmoProjectorAttribute)System.Attribute.GetCustomAttribute(t,
                typeof(Fdp.Toolkit.Diagnostics.Gizmos.GizmoProjectorAttribute))!).Family;
        Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.SquadContacts, FamilyOf(typeof(SquadContactsGizmo)));
        Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.CommandPicture, FamilyOf(typeof(CommandPictureGizmo)));
        Assert.Equal(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.SquadAssignment, FamilyOf(typeof(SquadGizmo)));
    }

    /// <summary>⭐ CE-1203 (§5e FL-1) — the CONTACTS row, split from Perception: the contact line to a remembered unit and a heard contact's
    /// dashed circle are on the Contacts layer (25), not Perception (1, now the vision cones only), and nothing draws with it hidden.</summary>
    [Fact]
    public void CE1203_TheContactLines_AreOnTheContactsLayer()
    {
        _w.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>();
        var unit = _w.CreateEntity();
        _w.AddComponent(unit, new SimTransform { Position = new Vector3(0, 0, 0) });
        var mem = default(Fdp.Toolkit.Perception.Components.TargetMemory);
        Fdp.Toolkit.Perception.Components.TargetMemory.AddOrUpdateTarget(ref mem, entityId: 42, posX: 30f, posY: 40f, scoreBoost: 1f, tick: 0u);
        _w.AddComponent(unit, mem);

        Fdp.TestShared.GizmoLayerProbe.AssertOnItsOwnLayer(d => new Hrot.Common.Diagnostics.Gizmos.LineOfSightGizmo().Draw(_w, unit, d), DebugTraceLayers.Contacts);
        Assert.NotEqual(DebugTraceLayers.Contacts, (byte)1);
    }

    /// <summary>⭐ CE-1203 (§5e FL-1) — the unit → mission-task lines join the PATHS row (5, the family they already belonged to), off the
    /// Entities bit they rode on before.</summary>
    [Fact]
    public void CE1203_TheMissionLines_AreOnThePathsLayer()
    {
        _w.RegisterManagedComponent<Fdp.Toolkit.Behavior.Components.ActiveMissionPlan>();
        var unit = _w.CreateEntity();
        _w.AddComponent(unit, new SimTransform { Position = new Vector3(0, 0, 0) });
        var plan = new Fdp.Toolkit.Behavior.Components.ActiveMissionPlan();
        plan.Plan.Tasks.Add(new Fdp.Toolkit.Behavior.Components.DomainMissionTask { BehaviorParams = "{\"targetLat\":0.001,\"targetLon\":0.001}" });
        _w.SetManagedComponent(unit, plan);
        var gizmo = new MissionPresentationGizmo(new Fdp.Modules.Geographic.Transforms.WGS84Transform(0, 0, 0));

        Fdp.TestShared.GizmoLayerProbe.AssertOnItsOwnLayer(d => gizmo.Draw(_w, unit, d), DebugTraceLayers.Paths);
    }
}
