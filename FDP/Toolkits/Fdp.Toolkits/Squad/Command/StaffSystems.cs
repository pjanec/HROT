using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Squad.Command
{
    /// <summary>
    /// ⭐⭐ <c>CE-2293</c> CC-C (CC-5, CC-8) — the SENDING half of a commander's staff: composes the standing reports nobody has to ask for
    /// and flushes the outbox when due. Runs on the brain node that owns the commander, before the brain tick.
    /// <list type="bullet">
    /// <item><b>PROGRESS</b> when the commander's TASK ends (<see cref="BehaviorFinishedEvent"/>, not a reaction or the SOP): Set on success,
    /// TaskFailed otherwise — so <c>SquadMarch</c>, unchanged, reports SET (CC-5).</item>
    /// <item><b>SPOT</b> from the squad's merged pool (<see cref="SquadCognitiveState"/>): the pool's contacts as ONE element (CC-7: one
    /// SPOT, not one per contact), re-sent only when it is new, changes size class or moves &gt; <see cref="SpotMoveMetres"/>; a SPOT with
    /// Count 0 when the pool empties (lost).</item>
    /// <item><b>SITREP</b> at the superior's requested rate (<see cref="ReportingRequirements.SitrepEverySeconds"/>): strength as a colour
    /// from <see cref="CombatEffective"/>, the play's phase.</item>
    /// <item><b>Flush</b>: a queued report whose time has come goes to <see cref="UnitSubordinate.Commander"/> — unless the outbox is muted
    /// (CC-15, lost comms) or Silent (CC-8: only a CONTACT goes).</item>
    /// </list>
    /// 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §3, §5, §8 CC-C.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class StaffComposerSystem : IEcsModuleSystem
    {
        /// <summary>How long a composed report waits before it goes (R-2031: ~3 s, a leader's report).</summary>
        public const float ReportDelaySeconds = 3f;
        /// <summary>CB-37 — an element that moved this far is news.</summary>
        public const float SpotMoveMetres = 25f;
        /// <summary>⭐ CE-2294 (CB-33, CB-34) — a still-held element is SPOTted again this often, so the superior's picture keeps it
        /// fresh while the squad still sees it (and a stale one means it is really gone) — the engaged heartbeat.</summary>
        public const float SpotRefreshSeconds = 15f;
        /// <summary>⭐ CE-2309 (HQ-1) — a launch heard within this of the last one SPOTted, this soon, is the same one.</summary>
        public const float LaunchSameMetres = 150f, LaunchSameSeconds = 30f;
        /// <summary>⭐ CE-2309 (HQ-2) — a gun that elevates this far is an air-defence gun (the SOPs' air-defence row's figure).</summary>
        public const float AirDefenceElevationDeg = 80f;
        /// <summary>⭐ CE-2310 (HQ-3, HQ-4) — an element observed this recently is fresh enough to relay or to ask fire on (s).</summary>
        public const float SupportFreshSeconds = 60f;
        /// <summary>⭐ CE-2310 (HQ-3) — an element within this of one already relayed (same flag) is the same one (m).</summary>
        public const float RelaySameMetres = 150f;
        /// <summary>⭐ CE-4686 ① (HQ-3b) — a commander of units relays at most this many GROUND element groups up (the freshest), so a superior's
        /// eight element slots never fill with one sender's infantry (HQ-3's reason for relaying only the support flags).</summary>
        public const int MaxGroundRelays = 2;
        /// <summary>⭐ CE-4686 ① (HQ-3b) — picture elements this close are ONE group (several squads reporting one enemy); the fold's own merge distance.</summary>
        public const float GroundGroupMetres = 50f;
        /// <summary>⭐ CE-2310 (HQ-4) — fire support is asked again only this much later, or this far from the last ask.</summary>
        public const float SupportAgainSeconds = 120f, SupportAgainMetres = 300f;
        /// <summary>⭐ CE-2310 (HQ-5) — reinforcement is asked at most this often (s), with an element this near (m).</summary>
        public const float ReinforceAgainSeconds = 180f, ReinforceContactMetres = 300f, ReinforceFreshSeconds = 30f;

        private readonly bool _gateOnAuthority;
        private EntityQuery? _commanders;
        // ⭐ CE-4518 (X-3) — this frame's heard launches, SORTED by the commander's index: a commander reads only its own run (binary search),
        //   instead of every unit walking every launch (O(units × launches)), and a phase thread reads it without a lock. Grown, never shrunk.
        [Fdp.Core.FrameScratch("scratch — cleared at the top of each run and filled from this frame's world")]
        private (Entity Commander, SoundContactEvent Heard)[] _launches = new (Entity, SoundContactEvent)[16];
        [Fdp.Core.FrameScratch("count of launches collected this run: reset to 0 at line 97")]
        private int _launchCount;
        [Fdp.Core.FrameScratch("the units of this run: refilled from index 0, read only below this run's count")]
        private Entity[] _units = new Entity[64];
        [Fdp.Core.FrameScratch("per-unit sent counts: zeroed after being summed (line 144) within the same run")]
        private int[] _sent = new int[64];


        /// <param name="gateOnAuthority">the pack's execution gate (as <c>SquadCoordinationSystem</c>): only commanders whose brain this
        /// node owns.</param>
        private EntityQuery? _stochastic;

        public StaffComposerSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        /// <summary>Reports sent since this system was built (a test / diagnostics probe).</summary>
        public int SentCount { get; private set; }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<ReportOutbox>() || !repo.IsComponentTypeRegistered<UnitSubordinate>()
                || !repo.IsComponentTypeRegistered<UnitRoster>() || !repo.Bus.IsRegisteredManaged<SquadReportNotification>()) return;
            SimInstant now = CommandPictureOps.Now(repo);

            // PROGRESS on the end of a commander's TASK (CC-5)
            if (repo.Bus.IsRegistered<BehaviorFinishedEvent>())
                foreach (var fin in repo.ReadEvents<BehaviorFinishedEvent>())
                {
                    if (fin.Origin == BehaviorOrigin.Reaction || fin.Origin == BehaviorOrigin.Sop) continue;
                    if (!Sends(repo, fin.Entity)) continue;
                    var progress = fin.Result == Fbt.NodeStatus.Success ? ProgressKind.Set : ProgressKind.TaskFailed;
                    ReportQueue.Enqueue(repo, fin.Entity, new CommandReport
                    {
                        Kind = ReportKind.Progress, Subtype = (byte)progress, At = PositionOf(repo, fin.Entity), ObservedAt = now,
                    }, now + ReportDelaySeconds);
                }

            // ⭐ CE-2309 (HQ-1) — this frame's heard launches, by the listener's commander
            _launchCount = 0;
            if (repo.Bus.IsRegistered<SoundContactEvent>())
                foreach (var heard in repo.ReadEvents<SoundContactEvent>())
                    if (IsLaunch(repo, in heard, out var commander))
                    {
                        if (_launchCount == _launches.Length) Array.Resize(ref _launches, _launches.Length * 2);
                        _launches[_launchCount++] = (commander, heard);
                    }
            // ⭐ stable by commander (insertion sort: a handful a frame, and Array.Sort is not stable — a commander's launches keep the bus order)
            for (int i = 1; i < _launchCount; i++)
            {
                var x = _launches[i]; int j = i - 1;
                while (j >= 0 && _launches[j].Commander.Index > x.Commander.Index) { _launches[j + 1] = _launches[j]; j--; }
                _launches[j + 1] = x;
            }

            // ⭐ CE-4518 (X-3) — the serial step: who sends, and their outbox exists (a structural add, never inside the phase)
            _commanders ??= repo.Query().With<UnitRoster>().With<UnitSubordinate>().WithOwnedWhen<BehaviorState>(_gateOnAuthority).Build();
            int n = 0;
            foreach (var unit in _commanders)
            {
                if (!Sends(repo, unit)) continue;
                if (!repo.HasComponent<ReportOutbox>(unit)) repo.AddComponent(unit, new ReportOutbox());
                if (n == _units.Length) { Array.Resize(ref _units, n * 2); Array.Resize(ref _sent, n * 2); }
                _units[n++] = unit;
            }
            // ⭐ agg-behaviors ST-0 (design §23.7) — a STOCHASTIC unit has no roster, yet it is a commander's subordinate that must report (SITREP, PROGRESS Set,
            //   its refusal's TaskFailed): without this its outbox is never flushed and its commander waits to its deadline
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Aggregate.AggregateUnit>())
            {
                _stochastic ??= repo.Query().With<Fdp.Toolkit.Aggregate.AggregateUnit>().With<UnitSubordinate>().WithOwnedWhen<BehaviorState>(_gateOnAuthority).Build();
                foreach (var unit in _stochastic)
                {
                    if (repo.HasComponent<UnitRoster>(unit) || !Sends(repo, unit)) continue;   // a rostered one is in the walk above
                    if (!repo.HasComponent<ReportOutbox>(unit)) repo.AddComponent(unit, new ReportOutbox());
                    if (n == _units.Length) { Array.Resize(ref _units, n * 2); Array.Resize(ref _sent, n * 2); }
                    _units[n++] = unit;
                }
            }
            if (n == 0) return;
            var job = new ComposeJob { System = this, Repo = repo, Now = now };
            // ⭐ The per-entity rule (docs/DESIGN_Parallel_Simulation.md §18): a commander writes only ITS OWN ReportOutbox; it reads its squad's
            //   picture, its members and its contacts (nothing this phase writes); its reports go up as managed events, which a phase defers into
            //   its batch (FdpEventBus, N-10) and plays back in batch order ⇒ the superiors receive them in the serial order.
            if (Fdp.Core.Jobs.ParallelEntities.Enabled) Fdp.Core.Jobs.ParallelEntities.Run(repo, _units, n, in job);
            else for (int i = 0; i < n; i++) job.Execute(_units[i], i, null!);
            int sent = 0;
            for (int i = 0; i < n; i++) { sent += _sent[i]; _sent[i] = 0; }
            SentCount += sent;
        }

        /// <summary>⭐ CE-4518 — one commander's whole compose (SPOT, launch, support, SITREP, casevac, flush): its own outbox only.</summary>
        private struct ComposeJob : Fdp.Core.Jobs.IEntityBatchJob, Fdp.Core.Jobs.ICoarseJob, Fdp.Core.Jobs.IStableReads
        {
            /// <summary>⭐ CE-4518 — what a SPOT reads on a CONTACT (another unit) that nothing writes during the staff phase: is it a vehicle
            /// (<c>VehicleParams</c>, via <c>Locomotions.Of</c>), can it fire at aircraft (its controlled weapons: <c>PassengerBuffer</c>,
            /// <c>CarrierStations</c>). 📐 The read-check survey (<c>HROT_READ_CHECK=2</c>, 1 000 men, 4 threads) named exactly these three.</summary>
            public void DeclareStableReads(Fdp.Core.Jobs.PhaseReadSet reads) => reads
                .Unmanaged<global::CarKinem.Core.VehicleParams>()
                .Unmanaged<Fdp.Toolkit.Behavior.Components.PassengerBuffer>()
                .Unmanaged<Fdp.Toolkit.Behavior.Components.CarrierStations>()
                // ⭐ CE-3450 (D-5) — a CASEVAC compose asks whether a member is still in the fight (Injury.IsInTheFight): his Health and
                //   InjuryThresholds, written by the damage systems, never in this phase. 📐 The survey on city-battle named both
                .Unmanaged<Fdp.Toolkit.Combat.Components.Health>()
                .Unmanaged<Fdp.Toolkit.Combat.Components.InjuryThresholds>()
                // ⭐ agg-behaviors ST-0 — a commander's SITREP reads its STOCHASTIC member's slots (UnitStrength.Sum): written by phase B (ST-3), never in this phase
                .Unmanaged<Fdp.Toolkit.Aggregate.AggregateUnit>()
                .Unmanaged<Fdp.Toolkit.Aggregate.UnitSlotTable>()
                .Unmanaged<Fdp.Toolkit.Aggregate.UnitHoldings>();   // ⭐ ST-3 — a stochastic unit's ammunition share (UnitAmmunition.TryShare) reads its holdings
            /// <summary>⭐ CE-4517 — a job is a whole commander's compose: 4 a batch (~100 commanders ⇒ ~25 batches, not 4).</summary>
            public int BatchSize => 4;
            public StaffComposerSystem System;
            public EntityRepository Repo;
            public SimInstant Now;

            public void Execute(Entity unit, int index, Fdp.Core.Jobs.BatchContext ctx)
            {
                var repo = Repo; var now = Now;
                ComposeSpot(repo, unit, now);
                System.ComposeLaunch(repo, unit, now);
                ComposeSupport(repo, unit, now);
                ComposeSitrep(repo, unit, now);
                ComposeCasevac(repo, unit, now);
                System._sent[index] = Flush(repo, unit, now);
            }

            public void ApplyDeferred(EntityRepository repo, in Fdp.Core.Jobs.Deferred d) { }
        }

        /// <summary>A commander that has a live superior to report to.</summary>
        private static bool Sends(EntityRepository repo, Entity unit)
        {
            if (!repo.IsAlive(unit) || !repo.HasComponent<UnitSubordinate>(unit)) return false;
            if (!repo.HasComponent<UnitRoster>(unit) && !Fdp.Toolkit.Aggregate.AggregateUnits.IsStochastic(repo, unit)) return false;   // ⭐ ST-0: a stochastic unit has slots, not a roster
            var superior = repo.GetComponentRO<UnitSubordinate>(unit).Commander;
            return !superior.IsNull && repo.IsAlive(superior);
        }

        private static Vector3 PositionOf(EntityRepository repo, Entity e)
            => repo.HasComponent<SimTransform>(e) ? repo.GetComponentRO<SimTransform>(e).Position : Fdp.Core.WorldPoint.Zero;

        /// <summary>SALUTE's S from a head count.</summary>
        public static ElementSize SizeOf(int count)
            => count <= 0 ? ElementSize.SizeUnknown : count == 1 ? ElementSize.Individual : count <= 4 ? ElementSize.Team
             : count <= 12 ? ElementSize.SquadSize : ElementSize.PlatoonSize;

        private static void ComposeSpot(EntityRepository repo, Entity unit, SimInstant now)
        {
            if (!repo.HasComponent<SquadCognitiveState>(unit)) return;
            ref readonly var squad = ref repo.GetComponentRO<SquadCognitiveState>(unit);
            var pool = squad.Contacts.Contacts;
            // ⭐ CE-2294 (R-214, CE-3080) — only contacts still IN THE FIGHT are an element: a killed or incapacitated man stays in memory
            //   by design (every threat reader filters him, ThreatDanger), and a neutral is no enemy (CV-8). 📌 Measured live in
            //   cc-platoon-bound: three dead enemies kept a count-3 SPOT going forever, so the platoon never stopped holding.
            Span<int> live = stackalloc int[16];   // the pool holds at most 16 (SquadContactPool)
            int n = 0;
            for (int i = 0; i < squad.Contacts.Count && n < live.Length; i++)
            {
                if (!SquadContacts.IsEnemy(repo, in pool[i])) continue;   // ⭐ CE-2353 — the one squad "enemy"
                // ⭐ CE-4666 (D-8, user-approved) — heard only FROM an opening or a corner: where to LOOK, not where the enemy IS. 📌 Measured in
                //   pc-defend-house: a shot 125 m off, heard through the reserve's own window, was SPOTted AT its post, and the platoon turned its
                //   line to face that "flank" (DA-60), away from the attack
                if ((pool[i].Flags & SquadContact.HeardThroughFlag) != 0) continue;
                live[n++] = i;
            }
            var sum = Vector3.Zero;
            var equipment = ElementEquipment.NoEquipment;
            for (int k = 0; k < n; k++)
            {
                var c = pool[live[k]];
                sum += new Vector3(c.PositionX, c.PositionY, c.PositionZ);
                if (c.EntityId > 0) equipment |= EquipmentOf(repo, new Entity((ulong)c.EntityId));   // ⭐ CE-2309 (HQ-2) — identified only
            }
            // ⭐ The parallel experiment, E-4 (M-1: ReportOutbox 94 % of its dirty chunks unchanged) — read first; writable only to send.
            ref readonly var outbox = ref repo.GetComponentRO<ReportOutbox>(unit);
            if (n == 0)
            {
                if (outbox.SpotSent == 0) return;
                repo.GetComponentRW<ReportOutbox>(unit).SpotSent = 0;   // the element is lost: say so (Count 0)
                ReportQueue.Enqueue(repo, unit, new CommandReport { Kind = ReportKind.Spot, At = outbox.SpotAt, ObservedAt = now }, now + ReportDelaySeconds);
                return;
            }
            var centre = sum / n;
            float radius = 0f;
            for (int k = 0; k < n; k++)
            {
                var c = pool[live[k]];
                radius = MathF.Max(radius, Vector3.Distance(centre, new Vector3(c.PositionX, c.PositionY, c.PositionZ)));
            }
            var size = SizeOf(n);
            if (outbox.SpotSent != 0 && outbox.SpotSize == size && outbox.SpotEquipment == equipment && Vector3.Distance(outbox.SpotAt, centre) <= SpotMoveMetres
                && now - outbox.SpotSentAt < SpotRefreshSeconds) return;
            ref var sent = ref repo.GetComponentRW<ReportOutbox>(unit);
            sent.SpotSent = 1;
            sent.SpotSentAt = now;
            sent.SpotAt = centre;
            sent.SpotSize = size;
            sent.SpotEquipment = equipment;
            ReportQueue.Enqueue(repo, unit, new CommandReport
            {
                Kind = ReportKind.Spot, At = centre, Radius = radius, Count = (byte)Math.Min(n, 255), Size = size, Equipment = equipment, ObservedAt = now,
            }, now + ReportDelaySeconds);
        }

        /// <summary>⭐ CE-2309 (HQ-2) — what an IDENTIFIED contact is, for a SPOT: a vehicle; a weapon that can fire at aircraft.</summary>
        public static ElementEquipment EquipmentOf(EntityRepository repo, Entity contact)
        {
            if (!repo.IsAlive(contact)) return ElementEquipment.NoEquipment;
            var e = ElementEquipment.NoEquipment;
            if (Fdp.Toolkit.Spatial.Eqs.VehicleCover.IsVehicle(repo, contact)) e |= ElementEquipment.Vehicle;
            if (Fdp.Toolkit.Combat.AirDefenceWeapons.CanEngageAir(repo, contact, AirDefenceElevationDeg)) e |= ElementEquipment.AirDefence;
            return e;
        }

        /// <summary>⭐ CE-2309 (HQ-1) — a heard sound is a LAUNCH: a mortar's or a gun's shot, as the listener's ear TOLD it (Q95 A / E1, R-3024 —
        /// the family a Regular-or-better ear names), heard by a man of a commander. ⛔ No distance test: CE-2309's "≥ 1 km" guessed the weapon
        /// from the range and is retired; an ear below Regular tells no family (0 = "could not tell") and reports no launch.</summary>
        private static bool IsLaunch(EntityRepository repo, in SoundContactEvent heard, out Entity commander)
        {
            commander = Entity.Null;
            if (heard.Kind != (byte)Fdp.Toolkit.Perception.Signatures.SoundKind.Shot
                || (heard.Family != (byte)Fdp.Toolkit.Tkb.Domain.SoundFamily.Mortar && heard.Family != (byte)Fdp.Toolkit.Tkb.Domain.SoundFamily.Gun))
                return false;
            if (heard.Observer.IsNull || !repo.IsAlive(heard.Observer) || !repo.HasComponent<UnitSubordinate>(heard.Observer)) return false;
            commander = repo.GetComponentRO<UnitSubordinate>(heard.Observer).Commander;
            return !commander.IsNull;
        }

        /// <summary>⭐ CE-2309 (HQ-1) — a launch one of the unit's men heard is SPOTted: Count 1, its estimate and radius, IndirectFire; the
        /// same launch (within <see cref="LaunchSameMetres"/>, <see cref="LaunchSameSeconds"/>) once.</summary>
        private void ComposeLaunch(EntityRepository repo, Entity unit, SimInstant now)
        {
            // ⭐ CE-4518 — the unit's run of the sorted launches (lower bound by index, then while the commander is this unit)
            int lo = 0, hi = _launchCount;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (_launches[mid].Commander.Index < unit.Index) lo = mid + 1; else hi = mid; }
            for (int i = lo; i < _launchCount && _launches[i].Commander.Index == unit.Index; i++)
            {
                var (commander, heard) = _launches[i];
                if (!commander.Equals(unit)) continue;
                var at = new Vector3(heard.X, heard.Y, heard.Z);
                ref var outbox = ref repo.GetComponentRW<ReportOutbox>(unit);
                if (outbox.LaunchSentAt > 0f && now - outbox.LaunchSentAt < LaunchSameSeconds && Vector3.Distance(outbox.LaunchAt, at) <= LaunchSameMetres)
                    continue;
                outbox.LaunchAt = at;
                outbox.LaunchSentAt = (SimInstant)Math.Max(now, 1e-3);
                ReportQueue.Enqueue(repo, unit, new CommandReport
                {
                    Kind = ReportKind.Spot, At = at, Radius = heard.Radius, Count = 1, Size = ElementSize.Individual,
                    Equipment = ElementEquipment.IndirectFire, ObservedAt = now,
                }, now + ReportDelaySeconds);
            }
        }

        /// <summary>
        /// ⭐ CE-2310 (HQ-3…HQ-5) — what a commander with a picture sends up for SUPPORT: it relays the elements its superior acts on
        /// (IndirectFire, AirDefence — once each, keeping their observation time); asks for FIRE SUPPORT on a fresh IndirectFire element
        /// (once per <see cref="SupportAgainSeconds"/> within <see cref="SupportAgainMetres"/>); asks to be REINFORCED when its strength is Red
        /// or worse with an element near (once per <see cref="ReinforceAgainSeconds"/>).
        /// </summary>
        private static void ComposeSupport(EntityRepository repo, Entity unit, SimInstant now)
        {
            if (!repo.IsComponentTypeRegistered<CommandPicture>() || !repo.HasComponent<CommandPicture>(unit)) return;
            ref readonly var picture = ref repo.GetComponentRO<CommandPicture>(unit);
            var centre = CentreOf(repo, unit);
            bool asked = false, near = false;
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                var e = picture.Elements.Items[i];
                if (e.Count == 0) continue;
                float age = now - e.ObservedAt;
                if (age <= ReinforceFreshSeconds && Vector2.Distance(new Vector2(e.At.X, e.At.Y), new Vector2(centre.X, centre.Y)) <= ReinforceContactMetres)
                    near = true;
                if (age > SupportFreshSeconds) continue;
                foreach (var flag in s_relayed)
                    if ((e.Equipment & flag) != 0) Relay(repo, unit, in e, flag, now);
                if (!asked && (e.Equipment & ElementEquipment.IndirectFire) != 0)
                {
                    asked = true;
                    ref var outbox = ref repo.GetComponentRW<ReportOutbox>(unit);
                    if (outbox.SupportAskedTime > 0f && now - outbox.SupportAskedTime < SupportAgainSeconds
                        && Vector3.Distance(outbox.SupportAskedAt, e.At) <= SupportAgainMetres) continue;
                    outbox.SupportAskedAt = e.At;
                    outbox.SupportAskedTime = (SimInstant)Math.Max(now, 1e-3);
                    ReportQueue.Enqueue(repo, unit, new CommandReport
                    {
                        Kind = ReportKind.Request, Subtype = (byte)RequestKind.FireSupport, At = e.At, Count = 1, ObservedAt = e.ObservedAt,
                    }, now + ReportDelaySeconds);
                }
            }
            RelayGround(repo, unit, in picture, now);   // ⭐ CE-4686 ① (HQ-3b)
            var strength = StrengthOf(repo, unit);
            if (!near || (strength != StatusColour.Red && strength != StatusColour.Black)) return;
            ref var box = ref repo.GetComponentRW<ReportOutbox>(unit);
            if (box.ReinforceAskedTime > 0f && now - box.ReinforceAskedTime < ReinforceAgainSeconds) return;
            box.ReinforceAskedTime = (SimInstant)Math.Max(now, 1e-3);
            ReportQueue.Enqueue(repo, unit, new CommandReport
            {
                Kind = ReportKind.Request, Subtype = (byte)RequestKind.Reinforce, At = centre, Count = 1, Strength = strength, ObservedAt = now,
            }, now + ReportDelaySeconds);
        }

        [Fdp.Core.NotState("static readonly constant array")]
        private static readonly ElementEquipment[] s_relayed = { ElementEquipment.IndirectFire, ElementEquipment.AirDefence };

        /// <summary>
        /// ⭐⭐ <c>CE-4686</c> ① (HQ-3b; user-authorised cross-lane edit, `2026-11-24`: "feel free to give the aggregated unit behavior to the backend")
        /// — a commander of UNITS (its roster holds commanders or stochastic units: a platoon over squads, a company over platoons) has no eyes of its
        /// own, so its superior learns of the enemy only if it RELAYS what its subordinates reported. 📌 Measured on <c>llm-company-aggregated</c>
        /// (CE-4685): the company never heard of Red's squad — HQ-3 relays only IndirectFire / AirDefence. ⭐ Bounded, as HQ-3 asked: its picture's
        /// fresh (<see cref="SupportFreshSeconds"/>) unflagged elements are grouped (<see cref="GroundGroupMetres"/>; the freshest element speaks
        /// for the group, with its OWN observation time, so the superior ages it as the sender does), at most <see cref="MaxGroundRelays"/> groups,
        /// the freshest; one is re-sent when it moved <see cref="SpotMoveMetres"/> or every <see cref="SpotRefreshSeconds"/>. ⛔ Never its own
        /// invention: only what is in its picture. 0 B: stack spans, no LINQ.
        /// </summary>
        private static void RelayGround(EntityRepository repo, Entity unit, in CommandPicture picture, SimInstant now)
        {
            if (!CommandsUnits(repo, unit)) return;
            int n = picture.Elements.Count;
            if (n == 0) return;
            Span<int> groupOf = stackalloc int[CommandPicture.MaxElements];
            Span<int> head = stackalloc int[CommandPicture.MaxElements];   // per group: its freshest element
            Span<byte> count = stackalloc byte[CommandPicture.MaxElements];
            int groups = 0;
            for (int i = 0; i < n; i++)
            {
                groupOf[i] = -1;
                var e = picture.Elements.Items[i];
                if (e.Count == 0 || now - e.ObservedAt > SupportFreshSeconds) continue;
                if ((e.Equipment & (ElementEquipment.IndirectFire | ElementEquipment.AirDefence)) != 0) continue;   // HQ-3 relays those
                int g = -1;
                for (int j = 0; j < i && g < 0; j++)
                    if (groupOf[j] >= 0 && Vector3.Distance(picture.Elements.Items[j].At, e.At) <= GroundGroupMetres) g = groupOf[j];
                if (g < 0) { g = groups++; head[g] = i; count[g] = 0; }
                else if (e.ObservedAt > picture.Elements.Items[head[g]].ObservedAt) head[g] = i;
                groupOf[i] = g;
                if (e.Count > count[g]) count[g] = e.Count;
            }
            for (int sent = 0; sent < MaxGroundRelays && groups > 0; sent++)
            {
                int best = -1;   // the freshest group not yet taken
                for (int g = 0; g < groups; g++)
                    if (head[g] >= 0 && (best < 0 || picture.Elements.Items[head[g]].ObservedAt > picture.Elements.Items[head[best]].ObservedAt)) best = g;
                if (best < 0) break;
                var e = picture.Elements.Items[head[best]];
                head[best] = -1;
                ref var outbox = ref repo.GetComponentRW<ReportOutbox>(unit);
                ref var list = ref outbox.RelayedGround;
                int known = -1;
                for (int i = 0; i < list.Count; i++)
                    if (Vector3.Distance(list.Items[i].At, e.At) <= GroundGroupMetres) { known = i; break; }
                if (known >= 0 && Vector3.Distance(list.Items[known].At, e.At) <= SpotMoveMetres && now - list.Items[known].SentAt < SpotRefreshSeconds) continue;
                var mark = new RelayedElement { At = e.At, SentAt = (SimInstant)Math.Max(now, 1e-3) };
                if (known >= 0) list.Items[known] = mark;
                else
                {
                    if (list.Count == RelayedElementList.Capacity)
                    {
                        for (int i = 1; i < list.Count; i++) list.Items[i - 1] = list.Items[i];
                        list.Count--;
                    }
                    list.Items[list.Count++] = mark;
                }
                ReportQueue.Enqueue(repo, unit, new CommandReport
                {
                    Kind = ReportKind.Spot, At = e.At, Radius = e.Radius, Count = count[best], Size = e.Size, Activity = e.Activity,
                    Equipment = e.Equipment, ObservedAt = e.ObservedAt,
                }, now + ReportDelaySeconds);
            }
        }

        /// <summary>⭐ CE-4686 ① — the commander commands UNITS (a live roster member is itself a commander, or a stochastic unit): no eyes of its own.</summary>
        public static bool CommandsUnits(EntityRepository repo, Entity unit)
        {
            if (!repo.HasComponent<UnitRoster>(unit)) return false;
            var roster = repo.GetComponentRO<UnitRoster>(unit);
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || !repo.IsAlive(m)) continue;
                if (repo.HasComponent<UnitRoster>(m) || Fdp.Toolkit.Aggregate.AggregateUnits.IsStochastic(repo, m)) return true;
            }
            return false;
        }

        /// <summary>⭐ CE-2310 (HQ-3) — SPOT <paramref name="e"/> up, once per element place and flag.</summary>
        private static void Relay(EntityRepository repo, Entity unit, in ReportedElement e, ElementEquipment flag, SimInstant now)
        {
            ref var outbox = ref repo.GetComponentRW<ReportOutbox>(unit);
            ref var list = ref outbox.Relayed;
            for (int i = 0; i < list.Count; i++)
                if (list.Items[i].Flag == flag && Vector3.Distance(list.Items[i].At, e.At) <= RelaySameMetres) return;
            if (list.Count == RelayedElementList.Capacity)
            {
                for (int i = 1; i < list.Count; i++) list.Items[i - 1] = list.Items[i];
                list.Count--;
            }
            list.Items[list.Count++] = new RelayedElement { At = e.At, Flag = flag, SentAt = (SimInstant)Math.Max(now, 1e-3) };
            ReportQueue.Enqueue(repo, unit, new CommandReport
            {
                Kind = ReportKind.Spot, At = e.At, Radius = e.Radius, Count = e.Count, Size = e.Size, Activity = e.Activity,
                Equipment = e.Equipment, ObservedAt = e.ObservedAt,
            }, now + ReportDelaySeconds);
        }

        /// <summary>Where a unit is: its own position, else the middle of its live members'.</summary>
        public static Vector3 CentreOf(EntityRepository repo, Entity unit)
        {
            if (repo.HasComponent<SimTransform>(unit)) return repo.GetComponentRO<SimTransform>(unit).Position;
            if (!repo.HasComponent<UnitRoster>(unit)) return Vector3.Zero;
            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(unit);
            var sum = Vector3.Zero; int n = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || !repo.IsAlive(m) || !repo.HasComponent<SimTransform>(m)) continue;
                sum += repo.GetComponentRO<SimTransform>(m).Position; n++;
            }
            return n > 0 ? sum / n : Vector3.Zero;
        }

        private static void ComposeSitrep(EntityRepository repo, Entity unit, SimInstant now)
        {
            float every = repo.IsComponentTypeRegistered<ReportingRequirements>() && repo.HasComponent<ReportingRequirements>(unit)
                ? repo.GetComponentRO<ReportingRequirements>(unit).SitrepEverySeconds : 0f;
            if (every <= 0f) return;
            ref var outbox = ref repo.GetComponentRW<ReportOutbox>(unit);
            if (outbox.LastSitrepAt > 0f && now - outbox.LastSitrepAt < every) return;
            outbox.LastSitrepAt = (SimInstant)Math.Max(now, 1e-3);
            ushort phase = repo.HasComponent<SquadCognitiveState>(unit) ? repo.GetComponentRO<SquadCognitiveState>(unit).PhaseId : (ushort)0;
            ReportQueue.Enqueue(repo, unit, new CommandReport
            {
                Kind = ReportKind.Sitrep, At = PositionOf(repo, unit), Strength = StrengthOf(repo, unit), Ammo = AmmoOf(repo, unit), Phase = phase, ObservedAt = now,
            }, now + ReportDelaySeconds);
        }

        /// <summary>⭐ <c>CE-2305</c> RQ-1, RQ-5 — the 9-line as a standing procedure: a member down and ALIVE (incapacitated) is asked for
        /// once — REQUEST Casevac at where he lies, the man as its subject. A man back in the fight frees his slot's bit; a dead man is not
        /// asked for (and keeps it). The squad's own buddy aid goes on regardless; the answer takes him from there.</summary>
        private static void ComposeCasevac(EntityRepository repo, Entity unit, SimInstant now)
        {
            if (!repo.HasComponent<UnitRoster>(unit)) return;   // ⭐ ST-0 — a stochastic unit has no men to lie wounded (its slots are not asked for)
            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(unit);
            int n = Math.Min(roster.Count, 16);
            ushort asked = repo.GetComponentRO<ReportOutbox>(unit).CasevacAsked;
            for (int i = 0; i < n; i++)
            {
                var man = roster.SubordinateEntities[i];
                ushort bit = (ushort)(1 << i);
                if (man.IsNull || !repo.IsAlive(man)) continue;
                if (Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, man)) { asked &= (ushort)~bit; continue; }
                if ((asked & bit) != 0 || !Fdp.Toolkit.Combat.Injury.IsIncapacitated(repo, man)) continue;
                asked |= bit;
                ReportQueue.Enqueue(repo, unit, new CommandReport
                {
                    Kind = ReportKind.Request, Subtype = (byte)RequestKind.Casevac, At = PositionOf(repo, man), Count = 1, ObservedAt = now,
                }, now + ReportDelaySeconds, man);
            }
            repo.GetComponentRW<ReportOutbox>(unit).CasevacAsked = asked;
        }

        /// <summary>The share of its members fighting at or above which a unit is GREEN / at or above which it is AMBER (below: RED while still combat
        /// effective). ⭐ ONE place for the thresholds: <see cref="StrengthOf"/> and a reader that judges a unit against its own earlier strength
        /// (<c>PlatoonDefend</c>'s fall-back, CE-3361 ②) both read them.</summary>
        public const float GreenFraction = 0.9f, AmberFraction = 0.7f;

        /// <summary>A unit's strength as a colour: GREEN ≥ 90 % of its members fighting, AMBER ≥ 70 %, RED while still combat
        /// effective, BLACK once it is not (CB-37).</summary>
        /// <remarks>⭐ <c>CE-4613</c> (R-D4) — through the one strength seam, <see cref="UnitStrength"/>: in MEN at every echelon (a platoon by its men, not
        /// its squads); for a squad of soldiers the same share as before.</remarks>
        public static StatusColour StrengthOf(ISimulationView view, Entity unit)
        {
            if (!view.HasComponent<UnitRoster>(unit) && !Fdp.Toolkit.Aggregate.AggregateUnits.IsStochastic(view, unit)) return StatusColour.ColourUnknown;   // ⭐ ST-0 ① — a stochastic unit has slots, not a roster
            return UnitStrength.ColourOf(view, unit);
        }

        /// <summary>⭐ <c>CE-4610</c> — the ammunition share at or above which a unit's SITREP ammo is GREEN / AMBER; below AMBER with rounds left RED,
        /// none BLACK (R-2095, lane decision: a unit fights on at half its basic load; a quarter is a resupply call).</summary>
        public const float AmmoGreenFraction = 0.5f, AmmoAmberFraction = 0.25f;

        /// <summary>⭐ ST-3 (R-8314, lane decision) — a stochastic unit's weight in a parent's ammunition sum: its share counts as this many "rounds" of capacity
        /// (a unit has no round count of its own, only a share; alone, its colour is exactly the share's).</summary>
        public const int StochasticAmmoWeight = 1000;

        /// <summary>⭐ <c>CE-4610</c> — a unit's ammunition as a colour (the SITREP's <see cref="CommandReport.Ammo"/>): the rounds its men carry
        /// (<see cref="Fdp.Toolkit.Combat.Components.WeaponAmmo"/>, every mount with a capacity) over their capacity, down the roster;
        /// unknown when no man carries a counted weapon. Allocation-free.</summary>
        public static StatusColour AmmoOf(ISimulationView view, Entity unit)
        {
            long have = 0, max = 0;
            Rounds(view, unit, 0, ref have, ref max);
            if (max <= 0) return StatusColour.ColourUnknown;
            float f = (float)have / max;
            return have <= 0 ? StatusColour.Black : f >= AmmoGreenFraction ? StatusColour.Green : f >= AmmoAmberFraction ? StatusColour.Amber : StatusColour.Red;
        }

        /// <summary>⭐ <c>CE-2436</c> AU-2 (A-8) — the rounds a unit's fighting men carry (<paramref name="max"/>: their capacity; 0 = no counted
        /// weapon). The base of fire reads it to know it has really FIRED. Allocation-free.</summary>
        public static long RoundsOf(ISimulationView view, Entity unit, out long max)
        {
            long have = 0; max = 0;
            Rounds(view, unit, 0, ref have, ref max);
            return have;
        }

        private static void Rounds(ISimulationView view, Entity e, int depth, ref long have, ref long max)
        {
            if (e.IsNull || !view.IsAlive(e) || depth > CombatEffective.MaxDepth) return;
            // ⭐ agg-behaviors ST-3 (R-8314; design §23.5) — a STOCHASTIC unit carries no counted weapon, but agg-backend DERIVES its ammunition share from the
            //   TKB's basic load (UnitAmmunition.TryShare, CE-8014): it adds StochasticAmmoWeight rounds of capacity and that share of them; Unknown (adds nothing) when it cannot be known
            if (Fdp.Toolkit.Aggregate.AggregateUnits.IsStochastic(view, e))
            {
                if (view is EntityRepository repo && Fdp.Toolkit.Aggregate.UnitAmmunition.TryShare(repo, e, out float share))
                {
                    have += (long)MathF.Round(share * StochasticAmmoWeight); max += StochasticAmmoWeight;
                }
                return;
            }
            if (view.HasComponent<UnitRoster>(e))
            {
                ref readonly var roster = ref view.GetComponentRO<UnitRoster>(e);
                for (int i = 0; i < roster.Count; i++) Rounds(view, roster.SubordinateEntities[i], depth + 1, ref have, ref max);
                return;
            }
            if (!view.HasComponent<Fdp.Toolkit.Combat.Components.WeaponAmmo>(e) || !CombatEffective.IsInTheFight(view, e)) return;
            ref readonly var a = ref view.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponAmmo>(e);
            Add(a.Count > 0, a.Ammo0, a.Max0, ref have, ref max); Add(a.Count > 1, a.Ammo1, a.Max1, ref have, ref max);
            Add(a.Count > 2, a.Ammo2, a.Max2, ref have, ref max); Add(a.Count > 3, a.Ammo3, a.Max3, ref have, ref max);
        }

        private static void Add(bool mounted, int ammo, int capacity, ref long have, ref long max)
        {
            if (!mounted || capacity <= 0) return;
            have += Math.Clamp(ammo, 0, capacity); max += capacity;
        }

        /// <returns>How many reports went (CE-4518: summed by the caller — the phase writes no shared counter).</returns>
        private static int Flush(EntityRepository repo, Entity unit, SimInstant now)
        {
            ref readonly var queued = ref repo.GetComponentRO<ReportOutbox>(unit);   // E-4 — read first; writable only to send
            if (queued.Queued.Count == 0) return 0;
            ref var outbox = ref repo.GetComponentRW<ReportOutbox>(unit);
            int sent = 0;
            var superior = repo.GetComponentRO<UnitSubordinate>(unit).Commander;
            bool muted = outbox.MutedUntil > now;
            int keep = 0;
            for (int i = 0; i < outbox.Queued.Count; i++)
            {
                var q = outbox.Queued.Items[i];
                bool goes = q.DueAt <= now && !muted
                         && (outbox.Posture != ReportingPosture.Silent || q.Report.Kind == ReportKind.Contact);
                if (!goes) { outbox.Queued.Items[keep++] = q; continue; }
                repo.Bus.PublishManaged(new SquadReportNotification { FromUnit = unit, ToUnit = superior, Subject = q.Subject, Report = q.Report, SentAt = now });
                sent++;
            }
            outbox.Queued.Count = keep;
            return sent;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2293</c> CC-C (CC-6, CB-44) — the RECEIVING half: folds each <see cref="SquadReportNotification"/> (this node's own, or
    /// one the BR-3 ingress raised from another node — CE-3250) addressed to a
    /// commander whose brain is here into its <see cref="CommandPicture"/> and raises <see cref="SensorChange.ReportReceived"/> on it (the
    /// SOP's edge). Behaviours read the picture, never the reports. Newest per sender wins (CB-32 — a late older <c>Seq</c> is dropped,
    /// a <c>Synthetic</c> fake always taken, CC-14).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class StaffInboxSystem : IEcsModuleSystem
    {
        /// <summary>A sender's new SPOT replaces its element within this many metres; farther, it is another element.</summary>
        public const float SameElementMetres = 50f;

        private readonly bool _gateOnAuthority;
        private readonly bool _requireOwnedBrain;

        public StaffInboxSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        private StaffInboxSystem(bool gateOnAuthority, bool requireOwnedBrain)
        {
            _gateOnAuthority = gateOnAuthority;
            _requireOwnedBrain = requireOwnedBrain;
        }

        /// <summary>
        /// ⭐ <c>CE-2301</c> ① (R-2058) — the inbox of a node with no brain (SimHost, IG): it LOGS every report and folds only for a
        /// commander whose <see cref="BehaviorState"/> is here AND owned here — so on such a node it never folds, and a replicated
        /// <see cref="CommandPicture"/> keeps its one writer (the fold would otherwise add a local picture, <c>EnsureRow</c>).
        /// 📄 DESIGN_Command_Chain_Example.md §8b.
        /// </summary>
        public static StaffInboxSystem ForNodeWithoutBrain() => new(gateOnAuthority: true, requireOwnedBrain: true);

        /// <summary>Reports folded / dropped as stale since this system was built (a test / diagnostics probe).</summary>
        public int FoldedCount { get; private set; }
        public int DroppedCount { get; private set; }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.Bus.IsRegisteredManaged<SquadReportNotification>()) return;
            // ⭐ CE-2301 ① (R-2058) — the log is every node's; only the fold needs the picture (§8b)
            bool canFold = repo.IsComponentTypeRegistered<CommandPicture>();
            bool brainHere = repo.IsComponentTypeRegistered<BehaviorState>();
            SimInstant now = CommandPictureOps.Now(repo);
            SquadReportLog? log = null;
            foreach (var n in repo.ReadManagedEvents<SquadReportNotification>())
            {
                // ⭐ CE-2295 (PC-11) — every report this node sees goes into its log, with what the inbox did with it
                log ??= SquadReportLog.For(repo);
                var entry = new SquadReportLogEntry
                {
                    SeenAt = now, FromUnit = n.FromUnit, ToUnit = n.ToUnit, Report = n.Report, OriginNodeId = n.OriginNodeId,
                    Verdict = "not-owned-here",
                };
                log.Add(entry);
                var fate = ReportFate.NotOwnedHere;
                if (canFold && !n.ToUnit.IsNull && repo.IsAlive(n.ToUnit) && !n.FromUnit.IsNull
                    && !(_requireOwnedBrain && !(brainHere && repo.HasComponent<BehaviorState>(n.ToUnit) && repo.HasAuthority<BehaviorState>(n.ToUnit)))
                    && !(_gateOnAuthority && brainHere && repo.HasComponent<BehaviorState>(n.ToUnit) && !repo.HasAuthority<BehaviorState>(n.ToUnit)))
                {
                    if (Fold(repo, n.FromUnit, n.ToUnit, n.Report, now, n.Subject)) { FoldedCount++; entry.Verdict = "folded"; fate = ReportFate.Folded; }
                    else { DroppedCount++; entry.Verdict = "stale"; fate = ReportFate.Stale; }
                }
                // ⭐ CE-1319 (BV-57) — beside the node-wide log, the commander's OWN history: to him and from him (a no-op on a soldier)
                CommandHistoryOf.Report(repo, n.FromUnit, n.ToUnit, n.Report, now, fate);
            }
        }

        /// <summary>What a report does to the addressee's picture, NOW (the inbox and the HTTP fake share it). <c>false</c> = dropped.
        /// ⭐ <c>CE-2305</c> RQ-2 — a REQUEST keeps <paramref name="subject"/> (the notification's) as its network id.</summary>
        public static bool Fold(EntityRepository repo, Entity fromUnit, Entity toUnit, CommandReport r, SimInstant now, Entity subject = default)
        {
            int from = CommandPictureOps.NetIdOf(repo, fromUnit);
            int row = CommandPictureOps.EnsureRow(repo, toUnit, from);
            if (!repo.HasComponent<CommandPicture>(toUnit)) return false;
            ref var picture = ref repo.GetComponentRW<CommandPicture>(toUnit);
            if (row >= 0)
            {
                ref var sub = ref picture.Subordinates.Items[row];
                if (r.Synthetic == 0 && r.Seq != 0 && r.Seq <= sub.LastSeq) return false;   // CB-32: a late older report
                if (r.Synthetic == 0) sub.LastSeq = Math.Max(sub.LastSeq, r.Seq);
                sub.LastReportAt = (SimInstant)Math.Max(now, 1e-3);
                switch (r.Kind)
                {
                    case ReportKind.Progress:
                        if (r.ObservedAt >= sub.OrderedAt) sub.Progress = (ProgressKind)r.Subtype;   // CE-2294: older = the last order's
                        break;
                    case ReportKind.Sitrep:   sub.Strength = r.Strength; sub.Ammo = r.Ammo; sub.Phase = r.Phase; sub.LastSitrepAt = sub.LastReportAt; break;
                    case ReportKind.Lace:     sub.Ammo = r.Ammo; break;
                }
            }
            switch (r.Kind)
            {
                case ReportKind.Spot:
                case ReportKind.Contact:
                    FoldElement(ref picture, from, r, now);
                    break;
                case ReportKind.Request:
                    if (picture.Requests.Count == CommandPicture.MaxRequests) RemoveAt(ref picture.Requests, 0);
                    picture.Requests.Items[picture.Requests.Count++] = new PendingRequest
                    {
                        FromNetId = from, Kind = (RequestKind)r.Subtype, At = r.At, ReceivedAt = now, Seq = r.Seq,
                        SubjectNetId = subject.IsNull ? 0 : CommandPictureOps.NetIdOf(repo, subject),
                        Round = (RequestKind)r.Subtype == RequestKind.FireSupport && (r.Equipment & ElementEquipment.Smoke) != 0
                            ? FireSupportRound.Smoke : FireSupportRound.HighExplosive,   // ⭐ CE-7025
                        Rounds = (RequestKind)r.Subtype == RequestKind.FireSupport ? r.Count : (byte)0,   // ⭐ CE-2436 AU-3 (A-5) — a sustained mission
                    };
                    break;
            }
            if (repo.Bus.IsRegistered<SensorChangedEvent>())
                repo.Bus.Publish(new SensorChangedEvent { Unit = toUnit, What = SensorChange.ReportReceived });
            return true;
        }

        private static void FoldElement(ref CommandPicture picture, int from, in CommandReport r, SimInstant now)
        {
            int same = -1;
            float best = SameElementMetres;
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.FromNetId != from) continue;
                float d = Vector3.Distance(e.At, r.At);
                if (d <= best) { best = d; same = i; }
            }
            if (r.Count == 0)                               // lost: the sender's element there goes
            {
                if (same >= 0) RemoveAt(ref picture.Elements, same);
                return;
            }
            var element = new ReportedElement
            {
                At = r.At, Radius = r.Radius, Count = r.Count, Size = r.Size, Activity = r.Activity, Equipment = r.Equipment,
                FromNetId = from, ObservedAt = r.ObservedAt, ReceivedAt = now, Seq = r.Seq, Synthetic = r.Synthetic,
            };
            if (same >= 0) { picture.Elements.Items[same] = element; return; }
            if (picture.Elements.Count == CommandPicture.MaxElements)   // full: the oldest observation goes
            {
                // ⭐ CE-4686 ① (HQ-3b) — an unflagged (ground) element goes before any IndirectFire / AirDefence one: the relayed infantry never pushes
                //   out what the support HQ acts on (HQ-3's reason). A new flagged element may still replace the oldest of all
                const ElementEquipment support = ElementEquipment.IndirectFire | ElementEquipment.AirDefence;
                int oldest = -1;
                for (int pass = 0; pass < 2 && oldest < 0; pass++)
                    for (int i = 0; i < picture.Elements.Count; i++)
                    {
                        bool flagged = (picture.Elements.Items[i].Equipment & support) != 0;
                        if (pass == 0 && flagged) continue;
                        if (oldest < 0 || picture.Elements.Items[i].ObservedAt < picture.Elements.Items[oldest].ObservedAt) oldest = i;
                    }
                if ((r.Equipment & support) == 0 && (picture.Elements.Items[oldest].Equipment & support) != 0) return;   // a ground report never evicts support
                RemoveAt(ref picture.Elements, oldest);
            }
            picture.Elements.Items[picture.Elements.Count++] = element;
        }

        private static void RemoveAt(ref ReportedElementList list, int index)
        {
            for (int i = index + 1; i < list.Count; i++) list.Items[i - 1] = list.Items[i];
            list.Count--;
        }

        private static void RemoveAt(ref PendingRequestList list, int index)
        {
            for (int i = index + 1; i < list.Count; i++) list.Items[i - 1] = list.Items[i];
            list.Count--;
        }
    }
}
