using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using FDP.Eqs;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Spatial.Eqs;
using Fbt.Kernel;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Stance;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2073</c> — the tunables of the posture's own two sensors (cover and retreat), which the decision scores.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PostureSensorsParams
    {
        /// <summary>How far from the unit the two queries look (m).</summary>
        public float SearchRadius;
        /// <summary>The sensors publish a new answer only when a top score moved by more than this.</summary>
        public float ScoreDeltaThreshold;
        /// <summary>Which forces count as threats for the exposure scoring (bit N = force N; 0 = every acquired contact).</summary>
        public uint FactionFilter;
    }

    /// <summary>⭐ <c>CE-2073</c> — the posture's sensors and the threat they are pointed at.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PostureSensorsState
    {
        /// <summary>The cover sensor (<see cref="FindCoverFromTarget"/>).</summary>
        public EqsSensorHandle Cover;
        /// <summary>The retreat sensor (<see cref="FindSafeRetreatPoint"/>).</summary>
        public EqsSensorHandle Retreat;
        /// <summary>The threat both are pointed at.</summary>
        public Entity Threat;
        /// <summary>⭐ <c>CE-3063</c> ③ — or the HEARD contact (memory id; 0 = none) …</summary>
        public long HeardId;
        /// <summary>… and the point they were pointed at.</summary>
        public Vector3 HeardPoint;
    }

    /// <summary>⭐ <c>CE-3084</c> — the approach decision's sensors (<see cref="PostureNodes.ApproachSensors"/>) and the threat they
    /// are pointed at — <see cref="PostureSensorsState"/>'s shape, for the flank and firing-position queries.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ApproachSensorsState
    {
        /// <summary>The flank sensor (<see cref="FindFlankingPosition"/>).</summary>
        public EqsSensorHandle Flank;
        /// <summary>The firing-position sensor (<see cref="FindOpenFiringPosition"/>).</summary>
        public EqsSensorHandle Firing;
        /// <summary>⭐ WL-4 W-12f — the throwing-position sensor (<see cref="FindThrowingPosition"/>), only for a unit that carries a
        /// throwable, pointed at the threat's LAST-KNOWN point …</summary>
        public EqsSensorHandle Throw;
        /// <summary>… which is this.</summary>
        public Vector3 ThrowPoint;
        /// <summary>The threat both are pointed at.</summary>
        public Entity Threat;
        /// <summary>Or the HEARD contact (memory id; 0 = none) …</summary>
        public long HeardId;
        /// <summary>… and the point they were pointed at.</summary>
        public Vector3 HeardPoint;
    }

    /// <summary>⭐ <c>CE-2073</c> — how the posture fires.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EngageParams
    {
        /// <summary>Seconds between shots (the AimAndFire executor's cooldown).</summary>
        public float CooldownSeconds;
    }

    /// <summary>⭐ <c>CE-2073</c> — what <see cref="PostureNodes.Engage"/> is firing at.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EngageState
    {
        /// <summary>The threat the weapon is aimed at (Null = not firing).</summary>
        public Entity Threat;
    }

    /// <summary>⭐ AQ89 C/D (R-269) — the Suppress branch's closing gate (<see cref="PostureNodes.WillingToClose"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WillingToCloseParams
    {
        /// <summary>1 = the task was ordered as an assault (<c>CombatPosture</c>'s <c>assault</c>): obedience decides, not bravery.</summary>
        public byte Assault;
    }

    /// <summary>⭐ <c>CE-3170</c> — the target an attack order named (<see cref="PostureNodes.UntilTargetDown"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DesignateParams
    {
        /// <summary>The target (<see cref="Entity.PackedValue"/>; 0 = none named).</summary>
        public long TargetPacked;
    }

    /// <summary>⭐ <c>CE-3170</c> — <see cref="PostureNodes.UntilTargetDown"/>'s working memory.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DesignateState
    {
        /// <summary>1 once the unit was told where its target is.</summary>
        public byte Told;
    }

    /// <summary>⭐ <c>CE-2073</c> — where the posture advances to, and how.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AdvanceParams
    {
        /// <summary>The objective (world position) — the mission task's goal.</summary>
        public Vector3 Objective;
        /// <summary>Travel speed (m/s).</summary>
        public float Speed;
        /// <summary>Distance from the objective that counts as arrived (m).</summary>
        public float ArrivalRadius;
        /// <summary>Seconds between shots while advancing.</summary>
        public float CooldownSeconds;
    }

    /// <summary>⭐ <c>CE-2073</c> — the advance in progress.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AdvanceState
    {
        /// <summary>1 once the move to the objective was issued.</summary>
        public byte Moving;
        /// <summary>What the weapon is aimed at while advancing.</summary>
        public EngageState Fire;
        /// <summary>⭐ <c>CE-3170</c> — where the move was issued to (a hunted target that moves further re-issues it).</summary>
        public Vector3 IssuedTo;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2073</c> — the CombatPosture behaviour's own nodes (the rest are <see cref="Fdp.Toolkit.Utility.UtilityNodes"/>
    /// and <see cref="EqsTacticsNodes"/>). 📄 <c>docs/DESIGN_Decision_Layer.md</c> §3.3b. The threat is always the unit's top
    /// one (the starter threat ranking, <see cref="EqsTacticsNodes.TopThreat"/>).
    /// </summary>
    public static class PostureNodes
    {
        /// <summary>The posture's sensor sites (with the run's owner stamp they find this run's sensors again).</summary>
        public const int CoverSite = 0x20730001, RetreatSite = 0x20730002;

        /// <summary>
        /// Keeps the posture's cover and retreat sensors pointed at the unit's top threat, so the decision's
        /// <c>EqsTopScore</c> inputs read them (<c>UnitSensors.OfTemplate</c> finds the current run's own first). Running.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus PostureSensors(ref PostureSensorsParams p, ref PostureSensorsState ws, Entity self, EntityRepository world)
        {
            // ⭐ CE-3063 ③ — an entity, or a HEARD contact's point: cover is scored against either.
            KeepPair(ref ws.Cover, CoverSite, FindCoverFromTarget.BlueprintId, ref ws.Retreat, RetreatSite, FindSafeRetreatPoint.BlueprintId,
                     ref ws.Threat, ref ws.HeardId, ref ws.HeardPoint, in p, self, world);
            return NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-3084</c> — the approach's sensor sites.</summary>
        public const int FlankScoreSite = 0x30840001, FiringScoreSite = 0x30840002;
        /// <summary>⭐ WL-4 W-12f — the throwing-position score sensor's site.</summary>
        public const int ThrowScoreSite = 0x40120002;

        /// <summary>
        /// ⭐ <c>CE-3084</c> (§3.3e A2) — keeps the APPROACH decision's flank and firing-position sensors pointed at the unit's top
        /// threat, so its <c>EqsTopScore</c> inputs read them before a manoeuvre runs (the manoeuvres own a sensor only while
        /// running). The same body as <see cref="PostureSensors"/>. Running.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus ApproachSensors(ref PostureSensorsParams p, ref ApproachSensorsState ws, Entity self, EntityRepository world)
        {
            KeepPair(ref ws.Flank, FlankScoreSite, FindFlankingPosition.BlueprintId, ref ws.Firing, FiringScoreSite, FindOpenFiringPosition.BlueprintId,
                     ref ws.Threat, ref ws.HeardId, ref ws.HeardPoint, in p, self, world);
            // ⭐ WL-4 W-12f (Weapon_Layers §5f L-5) — a grenadier's third sensor: where to THROW from, around the threat's
            //   LAST-KNOWN point (never the live entity). A unit with no throwable keeps none (it would only cost a query).
            if (Fdp.Toolkit.Combat.HiddenTargetThrow.ThrowableMount(world, self, out _, out _, out _, out _)
                && EqsTacticsNodes.LastKnownAim(world, self, ws.Threat, out var throwAim))
            {
                bool repoint = !ws.Throw.IsValid || Vector3.Distance(throwAim.Point, ws.ThrowPoint) > EqsTacticsNodes.RepointMetres;
                var near = p;
                near.SearchRadius = EqsTacticsNodes.ThrowSearchRadius;
                ws.Throw = Keep(ws.Throw, world, self, ThrowScoreSite, FindThrowingPosition.BlueprintId, in near, in throwAim, repoint);
                if (repoint) ws.ThrowPoint = throwAim.Point;
            }
            else if (ws.Throw.IsValid)
            {
                Drop(world, ws.Throw);
                ws.Throw = default;
            }
            return NodeStatus.Running;
        }

        /// <summary>The ONE body of the sensor-keeping nodes: two sensors on the top threat (an entity, or a heard point), re-pointed
        /// when it moves.</summary>
        private static void KeepPair(ref EqsSensorHandle a, int siteA, uint templateA, ref EqsSensorHandle b, int siteB, uint templateB,
                                     ref Entity threatEntity, ref long heardId, ref Vector3 heardPoint,
                                     in PostureSensorsParams p, Entity self, EntityRepository world)
        {
            var was = new ThreatAim(threatEntity, heardId, heardPoint);
            if (!EqsTacticsNodes.TopAim(world, self, in was, out var threat)) return;   // nothing to score yet
            bool retarget = EqsTacticsNodes.Moved(in was, in threat);
            a = Keep(a, world, self, siteA, templateA, in p, in threat, retarget);
            b = Keep(b, world, self, siteB, templateB, in p, in threat, retarget);
            if (retarget || heardId != threat.HeardId) heardPoint = threat.Point;
            threatEntity = threat.Entity;
            heardId = threat.HeardId;
        }

        /// <summary>Fires at the unit's top threat (re-aims when it changes); stops firing when nothing is remembered. Running.
        /// Failure when the unit has no weapon channel. ⭐ The ROE is enforced by the fire executor (<c>CE-2075</c>).</summary>
        [SharedAiAction]
        public static NodeStatus Engage(ref EngageParams p, ref EngageState ws, Entity self, EntityRepository world)
            => Fire(world, self, ref ws, p.CooldownSeconds) ? NodeStatus.Running : NodeStatus.Failure;

        /// <summary>
        /// ⭐⭐ AQ89 A (R-269) — <see cref="Engage"/> that SUPPRESSES: aimed fire while the top threat is seen, and while it is hidden
        /// slow rounds at where the memory last placed it (the weapon brain's blind fire) instead of an idle hold — they strike his
        /// cover and spoil his aim (<c>UnderFire</c>, AQ85 E). Running; Failure when the unit has no weapon channel.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Suppress(ref EngageParams p, ref EngageState ws, Entity self, EntityRepository world)
            => Fire(world, self, ref ws, p.CooldownSeconds, blind: true) ? NodeStatus.Running : NodeStatus.Failure;

        /// <summary>
        /// ⭐⭐ AQ89 C (R-269) — may the Suppress branch CLOSE to throw? All of: the top threat identified, remembered live and HIDDEN
        /// now · the unit carries something to throw · it is WILLING (<see cref="TemperamentOf.Willing"/> — its obedience when the
        /// task was ordered as an assault by anyone but its own SOP, else its bravery; R-212: a threshold, no dice). ⭐ Once the close
        /// is under way (<paramref name="ws"/> = the close's own state, moving) a glimpse of the target does not stop it — the
        /// close fires at him aimed then (WL-4), and the deactivator resets the state when the branch is left.
        /// </summary>
        [SharedAiCondition]
        public static bool WillingToClose(ref WillingToCloseParams p, ref EqsTacticsState ws, Entity self, EntityRepository world)
        {
            if (!EqsTacticsNodes.TopThreat(world, self, Entity.Null, out var threat) || !world.IsAlive(threat)) return false;
            if (!Fdp.Toolkit.Combat.HiddenTargetThrow.ThrowableMount(world, self, out _, out _, out _, out _)) return false;
            if (!Fdp.Toolkit.Combat.HiddenTargetThrow.LastKnown(world, self, threat, out _)) return false;
            if (ws.Moving == 0 && Fdp.Toolkit.Perception.SightNow.Sees(world, self, threat)) return false;
            return TemperamentOf.Willing(world, self, OrderedAssault(world, self, p.Assault != 0));
        }

        /// <summary>⭐ AQ89 D — an <c>assault</c> counts as an ORDER only when someone above the unit started the task (a superior,
        /// an operator, an editor/scenario assignment) — never when its own SOP or a reaction did.</summary>
        internal static bool OrderedAssault(EntityRepository world, Entity self, bool assault)
        {
            if (!assault) return false;
            if (!world.HasComponent<BehaviorState>(self)) return true;
            var origin = world.GetComponentRO<BehaviorState>(self).Origin;
            return origin != BehaviorOrigin.Sop && origin != BehaviorOrigin.Reaction;
        }

        /// <summary>
        /// Moves to the objective while firing at the top threat. Success on arrival — the posture's (and the mission task's)
        /// end; Failure when the unit cannot move. A failed move is issued again.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus AdvanceAndAttack(ref AdvanceParams p, ref AdvanceState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<LocomotionChannel>(self)) return NodeStatus.Failure;
            Fire(world, self, ref ws.Fire, p.CooldownSeconds);   // no weapon: it still advances

            // ⭐ CE-3170 (docs/DESIGN_Entity_Control.md C-5) — HUNTING an attack order's target: the objective follows where the memory
            //   last placed it, and arriving does not end the posture — the target going down does (UntilTargetDown).
            bool hunting = DesignatedTarget.Of(world, self, out var hunted) && CombatLife.IsAlive(world, hunted);
            var objective = hunting && HiddenTargetThrow.LastKnown(world, self, hunted, out var seenAt) ? seenAt : p.Objective;
            if (ws.Moving == 1 && hunting && Vector3.Distance(objective, ws.IssuedTo) > p.ArrivalRadius) ws.Moving = 0;   // it moved
            if (ws.Moving == 1)
            {
                var status = LocomotionMoveTo.Status(world, self);
                if (status == NodeStatus.Success && hunting) return NodeStatus.Running;   // there: the fight goes on
                if (status == NodeStatus.Success) { StopFiring(world, self, ref ws.Fire); return NodeStatus.Success; }
                if (status == NodeStatus.Running) return NodeStatus.Running;
                ws.Moving = 0;   // failed or taken over: issue it again
            }
            if (!LocomotionMoveTo.Issue(world, self, objective, p.Speed, p.ArrivalRadius)) return NodeStatus.Failure;
            ws.Moving = 1;
            ws.IssuedTo = objective;
            return NodeStatus.Running;
        }

        /// <summary>Stays where it is: stops a move the posture issued. Running.</summary>
        [SharedAiAction]
        public static NodeStatus Hold(Entity self, EntityRepository world)
        {
            StopMoving(world, self);
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐ <c>CE-3090</c> — a WOUNDED unit with no cover: stays where it is, lies down (<c>CE-2121</c> — the body performs it, the
        /// node does not wait) and returns fire at its top threat (re-aims when it changes). Running; a unit with no weapon channel
        /// or no stance still holds. 📄 <c>docs/DESIGN_Decision_Layer.md</c> §3.3f, §3.3g.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus HoldProne(ref EngageParams p, ref EngageState ws, Entity self, EntityRepository world)
        {
            StopMoving(world, self);
            StanceRequest.Set(world, self, StanceId.Prone, GoProneSeconds);
            Fire(world, self, ref ws, p.CooldownSeconds);
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3170</c> (<c>docs/DESIGN_Entity_Control.md</c> C-5) — an ATTACK order's target: keeps it designated for this run
        /// (<see cref="DesignatedTarget"/> — the threat ranking's one reader puts it first, so every branch fights IT) and ends the
        /// posture with Success once it is out of the fight (<see cref="CombatLife"/>) — the order succeeded. On the first tick the
        /// unit is TOLD where it is: the target enters its memory as a reported contact (no sensor modality, so it does not read as
        /// in sight); after that fog of war applies. No target named = Failure: it ABSTAINS, so the RequireOne Parallel it sits in ends
        /// with its siblings (⭐ CE-2195 — as Running it outlived a failed sibling: a vehicle whose cycle found no hide sat in a dead
        /// reaction for ever, and the SOP could not react again).
        /// </summary>
        [SharedAiAction]
        public static NodeStatus UntilTargetDown(ref DesignateParams p, ref DesignateState ws, Entity self, EntityRepository world)
        {
            if (p.TargetPacked == 0) return NodeStatus.Failure;
            var target = new Entity((ulong)p.TargetPacked);
            if (!CombatLife.IsAlive(world, target)) return NodeStatus.Success;
            Designate(world, self, target, ref ws.Told);
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐ <c>CE-3170</c> / <c>CE-2230</c> — the ONE designation step: <paramref name="target"/> designated (the threat ranking puts it
        /// first) and, the first time (<paramref name="told"/> 0 → 1), entered in the unit's memory as a reported contact — no sensor
        /// modality, so it does not read as in sight. Shared by <see cref="UntilTargetDown"/> and the vehicle cycle's named target.
        /// </summary>
        internal static void Designate(EntityRepository world, Entity self, Entity target, ref byte told)
        {
            if (!DesignatedTarget.Of(world, self, out var designated) || !designated.Equals(target))
                DesignatedTarget.Set(world, self, target);
            if (told != 0) return;
            told = 1;
            if (world.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(self) && world.HasComponent<SimTransform>(target))
            {
                var at = world.GetComponentRO<SimTransform>(target).Position;
                ref var mem = ref world.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(self);
                Fdp.Toolkit.Perception.Components.TargetMemory.AddOrUpdateTarget(ref mem, (long)target.PackedValue, at.X, at.Y, ReportedScore,
                    ((Fdp.ModuleHost.Abstractions.ISimulationView)world).Tick, (Fdp.Toolkit.Perception.Components.SensorModality)0, at.Z);
            }
        }

        /// <summary>⭐ <c>CE-3170</c> — the memory score a reported target enters with: a contact just seen (live ≈ 14 s, then fading and
        /// forgotten ≈ 70 s unconfirmed — <c>PerceptionConstants.LiveFreshness</c>, <c>ForgetThreatScore</c>).</summary>
        public const float ReportedScore = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;

        /// <summary>
        /// ⭐ <c>CE-3082</c> D1 — true when THIS advance arrived: its move was issued and the channel reports Success. The HSM
        /// host's finish (an HSM activity's status is discarded, so the posture HSM leaves Advance for its Final state on this
        /// guard). Reads the SAME working state <see cref="AdvanceAndAttack"/> writes. 📄 <c>docs/DESIGN_Decision_Layer.md</c> §3.3c.
        /// </summary>
        [SharedAiCondition]
        public static bool Arrived(ref AdvanceParams p, ref AdvanceState ws, Entity self, EntityRepository world)
            => ws.Moving == 1 && world.HasComponent<LocomotionChannel>(self)
               && LocomotionMoveTo.Status(world, self) == NodeStatus.Success;

        /// <summary>Leaving the posture: its sensors go.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.PostureNodes.PostureSensors")]
        public static void Deactivate_PostureSensors(ref PostureSensorsParams p, ref PostureSensorsState ws, Entity self, EntityRepository world)
        {
            Drop(world, ws.Cover);
            Drop(world, ws.Retreat);
            ws = default;
        }

        /// <summary>⭐ <c>CE-3084</c> — leaving the approach: its two sensors go.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.PostureNodes.ApproachSensors")]
        public static void Deactivate_ApproachSensors(ref PostureSensorsParams p, ref ApproachSensorsState ws, Entity self, EntityRepository world)
        {
            Drop(world, ws.Flank);
            Drop(world, ws.Firing);
            Drop(world, ws.Throw);   // ⭐ WL-4
            ws = default;
        }

        /// <summary>Leaving the Suppress branch: the weapon stops.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.PostureNodes.Engage")]
        public static void Deactivate_Engage(ref EngageParams p, ref EngageState ws, Entity self, EntityRepository world)
            => StopFiring(world, self, ref ws);

        /// <summary>⭐ AQ89 A — leaving the suppression: the weapon stops.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.PostureNodes.Suppress")]
        public static void Deactivate_Suppress(ref EngageParams p, ref EngageState ws, Entity self, EntityRepository world)
            => StopFiring(world, self, ref ws);

        /// <summary>⭐ <c>CE-3090</c> — leaving the prone hold: the weapon stops and the unit gets up (<c>CE-2121</c>).</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.PostureNodes.HoldProne")]
        public static void Deactivate_HoldProne(ref EngageParams p, ref EngageState ws, Entity self, EntityRepository world)
        {
            StopFiring(world, self, ref ws);
            StanceRequest.Set(world, self, StanceId.Standing, GetUpSeconds);
        }

        /// <summary>⭐ <c>CE-2121</c> — first-cut body timings (the fake backend uses them as the transition time).</summary>
        public const float GoProneSeconds = 1.0f, GetUpSeconds = 0.8f;

        /// <summary>Leaving the advance: the move and the weapon stop.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.PostureNodes.AdvanceAndAttack")]
        public static void Deactivate_AdvanceAndAttack(ref AdvanceParams p, ref AdvanceState ws, Entity self, EntityRepository world)
        {
            StopFiring(world, self, ref ws.Fire);
            if (ws.Moving == 1) StopMoving(world, self);
            ws = default;
        }

        // ── the shared steps ─────────────────────────────────────────────────────────────────────────

        // ⭐ CE-3136 P-6 — internal: PeekAndFire keeps its two sensors through it.
        internal static EqsSensorHandle Keep(EqsSensorHandle handle, EntityRepository world, Entity self, int site, uint template,
                                            in PostureSensorsParams p, in ThreatAim threat, bool retarget)
        {
            var config = new EqsSensor
            {
                BlueprintId         = template,
                Epoch               = 1,
                SearchRadius        = p.SearchRadius,
                FactionFilter       = p.FactionFilter,
                PublishPolicy       = (byte)EqsPublishPolicy.ScoreDelta,
                ScoreDeltaThreshold = p.ScoreDeltaThreshold,
                ContextSlot0        = self,
            };
            EqsTacticsNodes.Point(ref config, in threat);
            var child = handle.IsValid && world.IsAlive(handle.ChildId) ? handle.ChildId : Entity.Null;
            if (child.IsNull)
            {
                child = EqsChildSensor.Ensure(world, self, site, in config);
                if (child.IsNull) return default;
                if (!EqsTacticsNodes.PointsAt(world.GetComponentRO<EqsSensor>(child), in threat)) EqsChildSensor.Refresh(world, child, in config);
                return new EqsSensorHandle(child);
            }
            if (retarget) EqsChildSensor.Refresh(world, child, in config);
            return handle;
        }

        internal static void Drop(EntityRepository world, EqsSensorHandle handle)
        {
            if (handle.IsValid && world.IsAlive(handle.ChildId)) EqsChildSensor.Destroy(world, handle.ChildId);
        }

        /// <summary>Aims the weapon at the top threat (a new command only when the target changed or the last one failed).
        /// False when the unit has no weapon channel. ⭐ <c>CE-2108</c>: the ONE fire step — <see cref="EqsTacticsNodes"/>
        /// fires on the move through it too.</summary>
        internal static bool Fire(EntityRepository world, Entity self, ref EngageState ws, float cooldown, int rounds = 0, bool blind = false)
        {
            if (!world.HasComponent<WeaponChannel>(self)) return false;
            if (!EqsTacticsNodes.TopThreat(world, self, ws.Threat, out var threat) || !world.IsAlive(threat))
            {
                StopFiring(world, self, ref ws);
                return true;
            }
            return Fire(world, self, ref ws, cooldown, rounds, threat, blind);
        }

        /// <summary>
        /// ⭐ <c>CE-3158</c> G4 / L5 — the same ONE aimed step at a NAMED target (no re-ranking): what the caller checked for sight is
        /// what is shot at. Used by the fire-from-cover node's locked target and by <c>CgfNodes.Action_FireAtTarget</c>.
        /// </summary>
        internal static unsafe bool Fire(EntityRepository world, Entity self, ref EngageState ws, float cooldown, int rounds, Entity threat, bool blind = false)
        {
            if (!world.HasComponent<WeaponChannel>(self)) return false;
            if (threat.IsNull || !world.IsAlive(threat)) { StopFiring(world, self, ref ws); return true; }
            ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
            if (world.HasComponent<BehaviorState>(self)) channel.BehaviorInstanceId = world.GetComponent<BehaviorState>(self).InstanceId;
            byte blindFire = blind ? (byte)1 : (byte)0;
            bool reissue = !threat.Equals(ws.Threat)
                        || channel.ActiveAction != CombatConstants.ActionIdAimAndFire
                        || channel.Status == NodeStatus.Failure
                        || Unsafe.As<byte, AimAndFireParams>(ref channel.Params[0]).BlindFire != blindFire;   // ⭐ AQ89 A
            if (!reissue) return true;
            // ⭐ CE-3089 (G7, backend — a cross-lane line, said in the P2 handoff's SYNC) — the posture / tactics fire step lets the
            //   executor choose the weapon per shot (25 mm at infantry, TOW at a tank). 📄 Utility demo design §12 W3.
            Unsafe.As<byte, AimAndFireParams>(ref channel.Params[0]) = new AimAndFireParams
                { Target = threat, CooldownSeconds = cooldown, Mount = AimAndFireParams.MountAuto, Rounds = rounds, BlindFire = blindFire };   // ⭐ CE-3136 P-6 — B7's count
            unchecked { channel.ActionInstanceId++; }
            channel.ActiveAction = CombatConstants.ActionIdAimAndFire;
            channel.Status = NodeStatus.Running;
            ws.Threat = threat;
            return true;
        }

        internal static void StopFiring(EntityRepository world, Entity self, ref EngageState ws)
        {
            if (!ws.Threat.IsNull && world.HasComponent<WeaponChannel>(self))
            {
                ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
                if (channel.ActiveAction == CombatConstants.ActionIdAimAndFire)
                {
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; }
                }
            }
            ws.Threat = Entity.Null;
        }

        private static void StopMoving(EntityRepository world, Entity self) => LocomotionMoveTo.Stop(world, self);
    }
}
