using Fdp.Toolkit.Determinism;   // ⭐ CE-2188 — SimRng moved to Fdp.Toolkits
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using FDP.Eqs;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2165</c> V4 (D-J) — how a vehicle leaves its firing point.</summary>
    public enum WithdrawMode : byte
    {
        /// <summary>From the hull: front armour ≥ 2 × side armour (a tank) ⇒ <see cref="Reverse"/>; else <see cref="Scoot"/>.</summary>
        Auto = 0,
        /// <summary>Back straight into the SAME hide, front still to the threat.</summary>
        Reverse = 1,
        /// <summary>Drive forward on to a NEW hide (a lighter vehicle: shoot and scoot), firing on the move where the mount can.</summary>
        Scoot = 2,
    }

    /// <summary>⭐ <c>CE-2165</c> V4 — the phases of <see cref="VehicleFightFromCoverNodes.VehicleFightFromCover"/> (§4's state diagram).</summary>
    public enum VehiclePhase : byte { Choose = 0, MoveToHide, Hidden, Exposing, Engaging, Watching, Withdrawing,
        /// <summary>⭐ CE-2196 / CE-2197 — no hide it can reach (none answers, or the hull cannot drive): fight from where it stands.</summary>
        StandAndFight }

    /// <summary>
    /// ⭐ <c>CE-2165</c> V4 — the designer-edited parameters of <see cref="VehicleFightFromCoverNodes.VehicleFightFromCover"/>
    /// (docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md §4). ⭐ A zero means "the default" (<see cref="VehicleFightFromCoverNodes.Effective"/>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VehicleFightFromCoverParams
    {
        /// <summary>See <see cref="WithdrawMode"/>.</summary>
        public WithdrawMode WithdrawMode;
        /// <summary>The longest an exposure lasts (s) · how long a firing point is held with nothing to shoot (s).</summary>
        public float ExposeSeconds, WatchSeconds;
        /// <summary>A random wait in this range while hidden (s).</summary>
        public float HideSecondsMin, HideSecondsMax;
        /// <summary>Aimed rounds per exposure.</summary>
        public int RoundsPerExposure;
        /// <summary>Exposures from one hide before moving to another (0 ⇒ Reverse 2, Scoot 1).</summary>
        public int ExposuresPerPosition;
        /// <summary>The exposure budget = this × the aim time of the quickest dangerous threat in sight (programme §3).</summary>
        public float AimBudgetFactor;
        /// <summary>A threat counts for the budget when its danger TO ME (<see cref="ThreatDanger.Of"/>) is at least this.</summary>
        public float AtDangerMin;
        /// <summary>How far the hide query looks (m) · how far round the hide the firing-point query looks (m).</summary>
        public float SearchRadius, FiringSearchRadius;
        /// <summary>Drive speed between hide and firing point (m/s; 0 ⇒ 5 — a creep, CE-2198: at the type's top speed a tank
        /// overshot its hide by its braking distance, ~26 m, into a building) · seconds between rounds (0 ⇒ the weapon's own).</summary>
        public float Speed, FireCooldownSeconds;
        /// <summary>With no hide for this long the vehicle fights from where it stands (s; CE-2196 — was: the node answered Failure).</summary>
        public float NoCoverSeconds;
        /// <summary>A new hide at least this far from the current one (m).</summary>
        public float MinRelocateMetres;
        /// <summary>Which forces count as threats for the queries (0 = every acquired contact) · a top score must move by this to publish.</summary>
        public uint FactionFilter;
        public float ScoreDeltaThreshold;
    }

    /// <summary>⭐ <c>CE-2165</c> V4 — the node's working state: scratch, lost on every exit; what outlives an exit is in <see cref="FiringPositionMemory"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VehicleFightFromCoverState
    {
        public VehiclePhase Phase;
        /// <summary>1 while a move this node issued runs · 1 = the withdraw mode resolved to Scoot · 1 while waiting for a hide.</summary>
        public byte Moving, Scoot, Waiting;
        public Vector3 HidePoint, FiringPoint;
        public EqsSensorHandle HideSensor, FiringSensor;
        /// <summary>What both sensors are pointed at.</summary>
        public Entity Threat;
        public long HeardId;
        public Vector3 HeardPoint;
        public long SensorId;
        public Vector3 SensorAt;
        /// <summary>When the phase's timer ends · when the vehicle reached the firing point · since when a dangerous threat has seen it
        /// without a break (0 = none now) · since when the node waits (sim s).</summary>
        public double PhaseUntil, ExposedAt, SeenSince, WaitingSince;
        /// <summary>The sim tick the exposure began (a heard launch is compared with <c>TargetMemory.LastSeenTick</c>).</summary>
        public uint ExposedTick;
        /// <summary>The exposure budget (s): how long it may be seen by a dangerous threat.</summary>
        public float Budget;
        /// <summary>Exposures from the current hide · exposures of this run (the random wait's salt).</summary>
        public int ExposuresHere, Exposures;
        /// <summary>The aimed fire · the gun kept on the threat while hidden.</summary>
        public EngageState Fire;
        public TrackTargetNodeParams Track;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2165</c> V4 — <c>VehicleFightFromCover</c>: a vehicle hides behind a building (a hull-sized hide, <see cref="FindVehicleHide"/>),
    /// keeps its gun on the threat, drives out FRONT FIRST to a firing point that sees him (<see cref="FindVehicleFiringPoint"/>), fires —
    /// the RPG man first, by danger to its own armour (V1) — and withdraws before the enemy's aim time runs out: REVERSE into the same hide
    /// (a tank) or SCOOT on to the next (a light vehicle). 📄 docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md §4.
    /// <para>The sibling of <see cref="PeekAndFireNodes"/>: the same steps (<see cref="EqsTacticsNodes.TopAim"/>, <see cref="PostureNodes.Keep"/>,
    /// <c>PostureNodes.Fire</c>, <see cref="FiringPositionMemory"/> heat, <see cref="PeekAndFireNodes.ClaimedByAFriend"/>) — its own phases.</para>
    /// </summary>
    public static class VehicleFightFromCoverNodes
    {
        /// <summary>The two sensor sites.</summary>
        public const int HideSite = 0x21650001, FiringSite = 0x21650002;

        /// <summary>Arrival radius of the node's moves (m) — a hull, not a man.</summary>
        public const float ArrivalRadius = 2f;

        /// <summary>A heard heavy weapon (a launch) within this of the threat's bearing cuts an exposure short (degrees).</summary>
        public const float LaunchBearingDegrees = 30f;

        /// <summary>The §4 default column.</summary>
        public static readonly VehicleFightFromCoverParams Defaults = new()
        {
            ExposeSeconds = 8f, WatchSeconds = 6f, HideSecondsMin = 4f, HideSecondsMax = 10f, RoundsPerExposure = 2,
            AimBudgetFactor = 0.7f, AtDangerMin = 0.5f, SearchRadius = 60f, FiringSearchRadius = 25f,
            NoCoverSeconds = 8f, MinRelocateMetres = 15f, ScoreDeltaThreshold = 0.05f, Speed = 5f,
        };

        /// <summary>The parameters with every zero replaced by its default; <c>ExposuresPerPosition</c> by the resolved mode.</summary>
        public static VehicleFightFromCoverParams Effective(in VehicleFightFromCoverParams a, bool scoot)
        {
            var d = Defaults;
            var e = a;
            if (e.ExposeSeconds <= 0f) e.ExposeSeconds = d.ExposeSeconds;
            if (e.WatchSeconds <= 0f) e.WatchSeconds = d.WatchSeconds;
            if (e.HideSecondsMin <= 0f) e.HideSecondsMin = d.HideSecondsMin;
            if (e.HideSecondsMax < e.HideSecondsMin) e.HideSecondsMax = Math.Max(e.HideSecondsMin, d.HideSecondsMax);
            if (e.RoundsPerExposure <= 0) e.RoundsPerExposure = d.RoundsPerExposure;
            if (e.ExposuresPerPosition <= 0) e.ExposuresPerPosition = scoot ? 1 : 2;
            if (e.AimBudgetFactor <= 0f) e.AimBudgetFactor = d.AimBudgetFactor;
            if (e.AtDangerMin <= 0f) e.AtDangerMin = d.AtDangerMin;
            if (e.SearchRadius <= 0f) e.SearchRadius = d.SearchRadius;
            if (e.FiringSearchRadius <= 0f) e.FiringSearchRadius = d.FiringSearchRadius;
            if (e.NoCoverSeconds <= 0f) e.NoCoverSeconds = d.NoCoverSeconds;
            if (e.MinRelocateMetres <= 0f) e.MinRelocateMetres = d.MinRelocateMetres;
            if (e.ScoreDeltaThreshold <= 0f) e.ScoreDeltaThreshold = d.ScoreDeltaThreshold;
            if (e.Speed <= 0f) e.Speed = d.Speed;
            return e;
        }

        /// <summary>The heat of firing points: each exposure adds 1; a point is burned after <c>ExposuresPerPosition</c> uses.</summary>
        public static HeatRules Rules(in VehicleFightFromCoverParams p) => new()
        {
            CoolHalfLifeSeconds = 60f, BurnHeat = p.ExposuresPerPosition, ReuseHeat = 0.5f * p.ExposuresPerPosition,
            HeatPenalty = 0.3f, MatchRadius = 3f,
        };

        /// <summary>D-J — the resolved withdraw mode: as authored, else from the hull (front ≥ 2 × side ⇒ reverse).</summary>
        public static bool Scoots(in VehicleFightFromCoverParams p, EntityRepository world, Entity self)
        {
            if (p.WithdrawMode != WithdrawMode.Auto) return p.WithdrawMode == WithdrawMode.Scoot;
            var hull = CombatTkb.PlatformOf(world, self);
            return hull == null || hull.ArmorFront < 2f * hull.ArmorSide;
        }

        /// <summary>
        /// Runs the cycle against the unit's top threat. Running while it has one; Success when nothing is remembered. ⭐ CE-2196 / CE-2197
        /// (hit location §9 S2-8 — backend's mobility and firepower kills): with no hide for <c>NoCoverSeconds</c>, or a hull that can no
        /// longer drive (<see cref="Immobile"/>), it FIGHTS FROM WHERE IT STANDS; a gun that can no longer fire (<see cref="Disarmed"/>)
        /// never exposes again — it withdraws to its hide and stays there; Failure only when it can do neither (or has no hide to hold).
        /// </summary>
        [SharedAiAction]
        public static NodeStatus VehicleFightFromCover(ref VehicleFightFromCoverParams authored, ref VehicleFightFromCoverState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<LocomotionChannel>(self) || !world.HasComponent<WeaponChannel>(self)) return NodeStatus.Failure;
            if (ws.Phase == VehiclePhase.Choose && ws.Exposures == 0 && ws.Moving == 0) ws.Scoot = (byte)(Scoots(in authored, world, self) ? 1 : 0);
            var p = Effective(in authored, ws.Scoot == 1);
            double now = Now(world);

            var was = new ThreatAim(ws.Threat, ws.HeardId, ws.HeardPoint);
            if (!EqsTacticsNodes.TopAim(world, self, in was, out var threat))
            {
                Release(ref ws, self, world);
                return NodeStatus.Success;   // nothing left to fight
            }
            KeepSensors(ref ws, in p, self, world, in was, in threat);

            ref var mem = ref UnitMemory.Ref<FiringPositionMemory>(world, self);
            // ⭐ CE-2197 — the hull's damage (backend's hit location S2: the running gear / engine, the turret / gun) decides what is left
            bool immobile = Immobile(world, self), disarmed = Disarmed(world, self);
            if (immobile && disarmed)
            {
                Release(ref ws, self, world);
                return NodeStatus.Failure;   // nothing it can do (the crew's bail-out is R-263's damage states)
            }
            if (immobile && ws.Phase != VehiclePhase.StandAndFight) BeginStand(ref ws, self, world);
            else if (disarmed && ws.Phase is VehiclePhase.Exposing or VehiclePhase.Engaging or VehiclePhase.Watching or VehiclePhase.StandAndFight)
                Withdraw(ref ws, in p, ref mem, self, world, now);   // a broken gun has no business in the open
            switch (ws.Phase)
            {
                case VehiclePhase.Choose:
                    if (!ChooseOrWait(ref ws, in p, ref mem, self, world, now, relocating: ws.Exposures > 0))
                    {
                        if (disarmed)
                        {
                            Release(ref ws, self, world);
                            return NodeStatus.Failure;   // no hide and no gun
                        }
                        BeginStand(ref ws, self, world);   // ⭐ CE-2196 — no hide: fight from where it stands
                    }
                    break;
                case VehiclePhase.StandAndFight: StandAndFight(ref ws, in p, ref mem, self, world, now, in threat, immobile); break;
                case VehiclePhase.MoveToHide:  MoveToHide(ref ws, in p, ref mem, self, world, now); break;
                case VehiclePhase.Hidden:      Hidden(ref ws, in p, ref mem, self, world, now, in threat); break;
                case VehiclePhase.Exposing:    Exposing(ref ws, in p, ref mem, self, world, now, in threat); break;
                case VehiclePhase.Engaging:    Engaging(ref ws, in p, ref mem, self, world, now, in threat); break;
                case VehiclePhase.Watching:    Watching(ref ws, in p, ref mem, self, world, now, in threat); break;
                case VehiclePhase.Withdrawing: Withdrawing(ref ws, in p, self, world, now); break;
            }
            Claim(ref ws, self, world, now);
            return NodeStatus.Running;
        }

        /// <summary>Leaving the node: its sensors go, its fire, its tracking and its move stop. ⭐ The position memory stays.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.VehicleFightFromCoverNodes.VehicleFightFromCover")]
        public static void Deactivate_VehicleFightFromCover(ref VehicleFightFromCoverParams p, ref VehicleFightFromCoverState ws, Entity self, EntityRepository world)
            => Release(ref ws, self, world);

        // ── the phases ──────────────────────────────────────────────────────────────────────────────

        private static bool ChooseOrWait(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, ref FiringPositionMemory mem,
                                         Entity self, EntityRepository world, double now, bool relocating)
        {
            if (PickBest(ws.HideSensor, ref mem, in p, world, self, now, relocating ? ws.HidePoint : (Vector3?)null, p.MinRelocateMetres, out var hide))
            {
                ws.Waiting = 0;
                GoToHide(ref ws, in p, self, world, hide);
                return true;
            }
            if (ws.Waiting != 1) { ws.Waiting = 1; ws.WaitingSince = now; }
            return now - ws.WaitingSince < p.NoCoverSeconds;
        }

        private static void GoToHide(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, Entity self, EntityRepository world, Vector3 hide)
        {
            ws.HidePoint = hide;
            ws.ExposuresHere = 0;
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, hide, p.Speed, ArrivalRadius) ? 1 : 0);
            ws.Phase = VehiclePhase.MoveToHide;
        }

        private static void MoveToHide(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, ref FiringPositionMemory mem,
                                       Entity self, EntityRepository world, double now)
        {
            var status = LocomotionMoveTo.Status(world, self);
            if (status == NodeStatus.Running) return;
            ws.Moving = 0;
            if (status == NodeStatus.Success) { EnterHidden(ref ws, in p, self, world, now); return; }
            // an unreachable hide: heat it so a second failure burns it (PeekAndFire's G2 rule)
            PositionHeat.Add(ref mem, ws.HidePoint, PeekAndFireNodes.UnreachableHeat * Rules(in p).BurnHeat, now, Rules(in p), exposure: false);
            ws.Phase = VehiclePhase.Choose;
        }

        private static void Hidden(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, ref FiringPositionMemory mem,
                                   Entity self, EntityRepository world, double now, in ThreatAim threat)
        {
            if (Disarmed(world, self)) return;   // ⭐ CE-2197 — a broken gun stays in its hide

            // T4 — the gun stays on the threat while hidden (an entity only: a heard point has nothing to track)
            if (!threat.IsPoint && !threat.Entity.IsNull)
            {
                if (ws.Track.Target != threat.Entity) TrackTargetNodes.Stop(ref ws.Track, self, world);
                ws.Track.Target = threat.Entity;
                TrackTargetNodes.TrackTarget(ref ws.Track, self, world);
            }

            if (ws.ExposuresHere >= p.ExposuresPerPosition) { ws.Phase = VehiclePhase.Choose; return; }   // used up: to another hide
            if (!AimAndFireExecutor.RoePermitsFire(world, self) || Reloading(world, self) || UnderFire(world, self, now - 3.0)) return;
            if (now < ws.PhaseUntil) return;

            if (!PickBest(ws.FiringSensor, ref mem, in p, world, self, now, null, 0f, out var firing))
            {
                // no firing point from this hide for NoCoverSeconds: it cannot be fought from — to the next hide
                if (ws.Waiting != 1) { ws.Waiting = 1; ws.WaitingSince = now; }
                else if (now - ws.WaitingSince >= p.NoCoverSeconds) { ws.Waiting = 0; ws.ExposuresHere = p.ExposuresPerPosition; }
                return;
            }
            ws.Waiting = 0;
            ws.FiringPoint = firing;
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, firing, p.Speed, ArrivalRadius) ? 1 : 0);   // T3 — front first
            ws.Phase = VehiclePhase.Exposing;
        }

        private static void Exposing(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, ref FiringPositionMemory mem,
                                     Entity self, EntityRepository world, double now, in ThreatAim threat)
        {
            var status = LocomotionMoveTo.Status(world, self);
            if (status == NodeStatus.Running) return;
            ws.Moving = 0;
            if (status != NodeStatus.Success)
            {
                PositionHeat.Add(ref mem, ws.FiringPoint, Rules(in p).BurnHeat, now, Rules(in p), exposure: false);   // never this one again
                Withdraw(ref ws, in p, ref mem, self, world, now);
                return;
            }
            // at the firing point: the exposure is real — count it, heat the point, set the budget
            ws.ExposedAt = now;
            ws.ExposedTick = world.SimulationTick;
            ws.SeenSince = 0;
            ws.ExposuresHere++;
            ws.Exposures++;
            PositionHeat.Add(ref mem, ws.FiringPoint, 1f, now, Rules(in p), exposure: true);
            ws.Budget = Budget(in p, world, self);
            if (!StartFire(ref ws, in p, self, world, in threat))
            {
                ws.PhaseUntil = now + p.WatchSeconds;
                ws.Phase = VehiclePhase.Watching;
            }
        }

        private static void Engaging(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, ref FiringPositionMemory mem,
                                     Entity self, EntityRepository world, double now, in ThreatAim threat)
        {
            bool seenByDanger = SeenByADangerousThreat(world, self, in p);
            if (!seenByDanger) ws.SeenSince = 0;
            else if (ws.SeenSince == 0) ws.SeenSince = now;

            ref readonly var channel = ref world.GetComponentRO<WeaponChannel>(self);
            // the aimed-fire executor's own answer (its status row): a guided round in flight holds the gunner (W-12c)
            bool guiding = channel.ActiveAction == CombatConstants.ActionIdAimAndFire && channel.Status == NodeStatus.Running
                           && world.IsComponentTypeRegistered<ActionStatus>() && world.HasComponent<ActionStatus>(self)
                           && world.GetComponentRO<ActionStatus>(self).Weapon.Reason == ActionReason.Guiding;
            bool done = channel.ActiveAction != CombatConstants.ActionIdAimAndFire || channel.Status != NodeStatus.Running;
            bool hit = FiredUponSince(world, self, ws.ExposedAt);
            bool overBudget = ws.SeenSince > 0 && now - ws.SeenSince >= ws.Budget;
            bool overTime = now - ws.ExposedAt >= p.ExposeSeconds;
            bool launch = LaunchHeard(world, self, ws.ExposedTick, in threat);
            // Guiding — a command missile needs its gunner: stay until it lands, unless hit
            if (hit || (!guiding && (done || overBudget || overTime || launch)))
                Withdraw(ref ws, in p, ref mem, self, world, now);
        }

        private static void Watching(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, ref FiringPositionMemory mem,
                                     Entity self, EntityRepository world, double now, in ThreatAim threat)
        {
            if (StartFire(ref ws, in p, self, world, in threat)) return;   // he showed himself
            if (now >= ws.PhaseUntil || FiredUponSince(world, self, ws.ExposedAt)) Withdraw(ref ws, in p, ref mem, self, world, now);
        }

        /// <summary>
        /// The exposure ends: REVERSE straight back into the hide (fire stops — the gun swings back to tracking), or SCOOT on to the
        /// next hide with the fire left running (a <c>FireOnMove</c> mount keeps shooting, the weapon brain holds the others, D-L).
        /// </summary>
        private static void Withdraw(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, ref FiringPositionMemory mem,
                                     Entity self, EntityRepository world, double now)
        {
            if (ws.Scoot == 1)
            {
                if (PickBest(ws.HideSensor, ref mem, in p, world, self, now, ws.HidePoint, p.MinRelocateMetres, out var next))
                {
                    GoToHide(ref ws, in p, self, world, next);   // forward, the fire still running
                    return;
                }
                PostureNodes.StopFiring(world, self, ref ws.Fire);
                ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, ws.HidePoint, p.Speed, ArrivalRadius) ? 1 : 0);   // nowhere new: back to the old one
                ws.Phase = VehiclePhase.Withdrawing;
                return;
            }
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, ws.HidePoint, p.Speed, ArrivalRadius, reverseAllowed: true) ? 1 : 0);
            ws.Phase = VehiclePhase.Withdrawing;
        }

        private static void Withdrawing(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, Entity self, EntityRepository world, double now)
        {
            var status = LocomotionMoveTo.Status(world, self);
            if (status == NodeStatus.Running) return;
            ws.Moving = 0;
            if (status == NodeStatus.Success) EnterHidden(ref ws, in p, self, world, now);
            else ws.Phase = VehiclePhase.Choose;
        }

        /// <summary>
        /// ⭐ CE-2196 / CE-2197 — no reachable hide: hold where it stands, the gun on the threat, firing whenever it SEES him (rounds as the
        /// weapon brain grants them). A hull that can still drive keeps asking for a hide and goes to it the moment one answers.
        /// </summary>
        private static void StandAndFight(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, ref FiringPositionMemory mem,
                                          Entity self, EntityRepository world, double now, in ThreatAim threat, bool immobile)
        {
            if (!immobile && PickBest(ws.HideSensor, ref mem, in p, world, self, now, null, 0f, out var hide))
            {
                PostureNodes.StopFiring(world, self, ref ws.Fire);
                GoToHide(ref ws, in p, self, world, hide);
                return;
            }
            var target = PeekAndFireNodes.LockTarget(world, self, in threat);
            if (!target.IsNull && SightNow.Sees(world, self, target))
            {
                TrackTargetNodes.Stop(ref ws.Track, self, world);
                PostureNodes.Fire(world, self, ref ws.Fire, p.FireCooldownSeconds, 0, target);
                return;
            }
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            if (!threat.IsPoint && !threat.Entity.IsNull)
            {
                if (ws.Track.Target != threat.Entity) TrackTargetNodes.Stop(ref ws.Track, self, world);
                ws.Track.Target = threat.Entity;
                TrackTargetNodes.TrackTarget(ref ws.Track, self, world);
            }
        }

        private static void BeginStand(ref VehicleFightFromCoverState ws, Entity self, EntityRepository world)
        {
            if (ws.Moving == 1) LocomotionMoveTo.Stop(world, self);
            ws.Moving = 0;
            ws.Waiting = 0;
            ws.Phase = VehiclePhase.StandAndFight;
        }

        /// <summary>⭐ CE-2197 — the hull can no longer drive: a mobility kill (<c>CanMove</c> stripped — the running gear or engine over its
        /// threshold, hit location §9 S2-8, or the type's <c>MobilityKill</c>), read on any node from the replicated part damage too.</summary>
        public static bool Immobile(EntityRepository world, Entity self)
            => !Capable(world, self, ActorCapabilities.CanMove) || Fdp.Toolkit.Physics.BodyParts.Wounds.Has(world, self, Fdp.Toolkit.Tkb.Domain.WoundEffect.Mobility);

        /// <summary>⭐ CE-2197 — the gun can no longer fire: a firepower kill (<c>CanShoot</c> stripped — the turret or gun over its threshold).</summary>
        public static bool Disarmed(EntityRepository world, Entity self)
            => !Capable(world, self, ActorCapabilities.CanShoot) || Fdp.Toolkit.Physics.BodyParts.Wounds.Has(world, self, Fdp.Toolkit.Tkb.Domain.WoundEffect.Firepower);

        private static bool Capable(EntityRepository world, Entity self, ActorCapabilities cap)
            => !world.IsComponentTypeRegistered<ActorCapabilityState>() || !world.HasComponent<ActorCapabilityState>(self)
               || (world.GetComponentRO<ActorCapabilityState>(self).Capabilities & cap) != 0;

        // ── the steps ───────────────────────────────────────────────────────────────────────────────

        private static void EnterHidden(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, Entity self, EntityRepository world, double now)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            var rng = SimRng.FromSim(self.Index, HideSite ^ (ws.Exposures * 7919), (float)now);
            ws.PhaseUntil = now + p.HideSecondsMin + rng.NextSingle() * (p.HideSecondsMax - p.HideSecondsMin);
            ws.Waiting = 0;
            ws.Phase = VehiclePhase.Hidden;
        }

        /// <summary>Aimed fire at the locked, SEEN target — its tracking stops first (one weapon channel); false = nothing seen.</summary>
        private static bool StartFire(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, Entity self, EntityRepository world, in ThreatAim threat)
        {
            var target = PeekAndFireNodes.LockTarget(world, self, in threat);
            if (target.IsNull || !SightNow.Sees(world, self, target)) return false;
            TrackTargetNodes.Stop(ref ws.Track, self, world);
            ws.Fire = default;
            PostureNodes.Fire(world, self, ref ws.Fire, p.FireCooldownSeconds, p.RoundsPerExposure, target);
            ws.Phase = VehiclePhase.Engaging;
            return true;
        }

        /// <summary>
        /// The exposure budget (§4): <c>ExposeSeconds</c>, cut to <c>AimBudgetFactor</c> × the aim time of the QUICKEST threat that is
        /// dangerous to me (<see cref="ThreatDanger.Of"/> ≥ <c>AtDangerMin</c>) and can see me now — if I see him, he sees me (R-246).
        /// </summary>
        internal static unsafe float Budget(in VehicleFightFromCoverParams p, EntityRepository world, Entity self)
        {
            float budget = p.ExposeSeconds;
            if (!world.HasComponent<TargetMemory>(self)) return budget;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            for (int i = 0; i < mem.Count; i++)
            {
                if (TargetMemory.IsAnonymous(in mem, i)) continue;
                var e = new Entity((ulong)mem.EntityIds[i]);
                if (!world.IsAlive(e) || ThreatDanger.Of(world, self, e) < p.AtDangerMin || !SightNow.Sees(world, self, e)) continue;
                budget = MathF.Min(budget, p.AimBudgetFactor * AimAndFireExecutor.AimSecondsFor(world, e, 0));
            }
            return budget;
        }

        /// <summary>A threat dangerous to me (<c>AtDangerMin</c>) is in sight now.</summary>
        private static unsafe bool SeenByADangerousThreat(EntityRepository world, Entity self, in VehicleFightFromCoverParams p)
        {
            if (!world.HasComponent<TargetMemory>(self)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            for (int i = 0; i < mem.Count; i++)
            {
                if (TargetMemory.IsAnonymous(in mem, i)) continue;
                var e = new Entity((ulong)mem.EntityIds[i]);
                if (world.IsAlive(e) && ThreatDanger.Of(world, self, e) >= p.AtDangerMin && SightNow.Sees(world, self, e)) return true;
            }
            return false;
        }

        /// <summary>T5 — a HEARD heavy weapon (a launch) since the exposure began, within <see cref="LaunchBearingDegrees"/> of the threat.
        /// ⚠ Only an ANONYMOUS slot: a shot heard inside a SEEN contact's circle refreshes that contact (<c>TargetMemory.HearContact</c>), and
        /// a seen dangerous shooter is the budget's business (<see cref="Budget"/>) — this is the launch from a window nobody saw.</summary>
        private static unsafe bool LaunchHeard(EntityRepository world, Entity self, uint sinceTick, in ThreatAim threat)
        {
            if (!world.HasComponent<TargetMemory>(self) || !world.HasComponent<SimTransform>(self)) return false;
            if (!EqsTacticsNodes.ThreatPosition(world, self, in threat, out var threatAt)) return false;
            var me = world.GetComponentRO<SimTransform>(self).Position;
            var toThreat = new Vector2(threatAt.X - me.X, threatAt.Y - me.Y);
            if (toThreat.LengthSquared() < 1e-4f) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            fixed (byte* cls = mem.SourceClass)
                for (int i = 0; i < mem.Count; i++)
                {
                    if (!TargetMemory.IsAnonymous(in mem, i) || cls[i] != (byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.HeavyWeapon) continue;
                    if (mem.LastSeenTick[i] < sinceTick) continue;
                    var toHeard = new Vector2(mem.PositionsX[i] - me.X, mem.PositionsY[i] - me.Y);
                    if (toHeard.LengthSquared() < 1e-4f) continue;
                    float cos = Vector2.Dot(Vector2.Normalize(toThreat), Vector2.Normalize(toHeard));
                    if (cos >= MathF.Cos(LaunchBearingDegrees * MathF.PI / 180f)) return true;
                }
            return false;
        }

        /// <summary>The best point of a sensor's answer by <c>score − heat penalty</c>: burned ones and a friend's claimed ones skipped; with
        /// <paramref name="awayFrom"/>, none nearer than <paramref name="minMetres"/> to it.</summary>
        private static bool PickBest(EqsSensorHandle sensor, ref FiringPositionMemory mem, in VehicleFightFromCoverParams p, EntityRepository world,
                                     Entity self, double now, Vector3? awayFrom, float minMetres, out Vector3 point)
        {
            point = default;
            if (!sensor.IsValid || !world.IsAlive(sensor.ChildId) || !world.HasComponent<EqsCognitiveBuffer>(sensor.ChildId)) return false;
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(sensor.ChildId);
            if (!buffer.IsReady || buffer.Count == 0) return false;
            var rules = Rules(in p);
            var span = buffer.GetSpanRO()[..buffer.Count];
            float best = float.NegativeInfinity;
            for (int i = 0; i < span.Length; i++)
            {
                var r = span[i];
                var at = new Vector3(r.PositionX, r.PositionY, r.PositionZ);
                if (awayFrom is { } from && Vector3.Distance(at, from) < minMetres) continue;
                if (PositionHeat.IsBurnedAt(ref mem, at, now, rules)) continue;
                if (PeekAndFireNodes.ClaimedByAFriend(world, self, at, now)) continue;
                float score = r.Score - PositionHeat.Penalty(ref mem, at, now, rules);
                if (score <= best) continue;   // ties keep the sensor's order
                best = score;
                point = at;
            }
            return !float.IsNegativeInfinity(best);
        }

        /// <summary>The hide sensor always; the firing-point sensor while hidden or about to expose — both at the freshest evidence (D14).</summary>
        private static void KeepSensors(ref VehicleFightFromCoverState ws, in VehicleFightFromCoverParams p, Entity self, EntityRepository world,
                                        in ThreatAim was, in ThreatAim threat)
        {
            var aim = PeekAndFireNodes.SensorAim(world, self, in threat);
            bool retarget = ws.SensorId == 0 || EqsTacticsNodes.Moved(new ThreatAim(Entity.Null, ws.SensorId, ws.SensorAt), in aim);
            var hide = new PostureSensorsParams { SearchRadius = p.SearchRadius, ScoreDeltaThreshold = p.ScoreDeltaThreshold, FactionFilter = p.FactionFilter };
            ws.HideSensor = PostureNodes.Keep(ws.HideSensor, world, self, HideSite, FindVehicleHide.BlueprintId, in hide, in aim, retarget);
            if (ws.Phase == VehiclePhase.Hidden)
            {
                var firing = hide;
                firing.SearchRadius = p.FiringSearchRadius;
                ws.FiringSensor = PostureNodes.Keep(ws.FiringSensor, world, self, FiringSite, FindVehicleFiringPoint.BlueprintId, in firing, in aim, retarget);
            }
            if (retarget) { ws.SensorId = aim.HeardId; ws.SensorAt = aim.Point; }
            if (EqsTacticsNodes.Moved(in was, in threat) || ws.HeardId != threat.HeardId) ws.HeardPoint = threat.Point;
            ws.Threat = threat.Entity;
            ws.HeardId = threat.HeardId;
        }

        /// <summary>The unit's claim on the hide (and firing point) it holds or is going to — a friend's query skips them.</summary>
        private static void Claim(ref VehicleFightFromCoverState ws, Entity self, EntityRepository world, double now)
        {
            if (ws.Phase == VehiclePhase.Choose || !world.IsComponentTypeRegistered<CoverClaim>()) return;
            var claim = new CoverClaim { Hide = ws.HidePoint, Peek = ws.FiringPoint, Until = now + PeekAndFireNodes.ClaimTtlSeconds };
            if (world.HasComponent<CoverClaim>(self)) world.GetComponentRW<CoverClaim>(self) = claim;
            else world.AddComponent(self, claim);
        }

        private static bool Reloading(EntityRepository world, Entity self)
            => Fdp.Toolkit.Combat.WeaponSystems.WeaponOf(world, self) is var gun && !gun.IsNull
               && Magazine.Reloading(world.GetComponentRO<WeaponState>(gun));   // ⭐ WS-4 — own gun or the controlled station weapon

        private static bool UnderFire(EntityRepository world, Entity self, double since) => FiredUponSince(world, self, since);

        private static bool FiredUponSince(EntityRepository world, Entity self, double since)
        {
            double window = Now(world) - since + 1e-6;
            return RecentSensesOf.WithinSince(world, self, SensorChange.NearMiss, window, since)
                || RecentSensesOf.WithinSince(world, self, SensorChange.Hit, window, since);
        }

        private static void Release(ref VehicleFightFromCoverState ws, Entity self, EntityRepository world)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            TrackTargetNodes.Stop(ref ws.Track, self, world);
            PostureNodes.Drop(world, ws.HideSensor);
            PostureNodes.Drop(world, ws.FiringSensor);
            if (ws.Moving == 1) LocomotionMoveTo.Stop(world, self);
            if (world.IsComponentTypeRegistered<CoverClaim>() && world.HasComponent<CoverClaim>(self))
                world.GetComponentRW<CoverClaim>(self).Until = 0d;
            ws = default;
        }

        private static double Now(EntityRepository world)
            => world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
    }
}
