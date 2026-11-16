using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8007</c> (ST-3, C-2 … C-5) — the constants of the attrition step. ⭐ The stress constants are THE soldier's
    /// (<c>StressSystem</c> / <c>Stresses</c>) — one model (RV-12); the cover, exposure and suppression factors are CALIBRATED (RV-17), named.
    /// </summary>
    public static class StochasticCombat
    {
        /// <summary>The attrition step (s of sim time) — C-2.</summary>
        public const float StepSeconds = 1f;
        /// <summary>THE soldier's stress constants (<c>Hrot.AI.Behaviors.Brains.StressSystem</c>): a hit, a friend down, a near miss, the
        /// calming per second, the quiet that doubles it, the break (the default nerve).</summary>
        public const float StressHit = 0.25f, StressFriendDown = 0.2f, StressNearMiss = 0.08f, CalmPerSecond = 0.03f;
        public const double QuietSeconds = 30.0;
        public const float FriendDownMetres = 15f;
        public const float BreakLevel = Fdp.Toolkit.Behavior.Components.TemperamentOf.DefaultNerve;
        /// <summary>CALIBRATED — the share of incoming λ a slot takes by the unit's prepared level (P-5): none, hasty, prepared, fortified.</summary>
        public static float CoverFactor(PreparedLevel p) => p switch
        {
            PreparedLevel.Hasty => 0.7f, PreparedLevel.Prepared => 0.45f, PreparedLevel.Fortified => 0.25f, _ => 1f,
        };
        /// <summary>CALIBRATED — mounted men are under armour: this share of the personnel λ reaches them.</summary>
        public const float MountedExposure = 0.2f;
        /// <summary>CALIBRATED — a fully suppressed firer delivers this share of its fire (σ × 2 under fire, the rate cut — C-4).</summary>
        public const float SuppressedFireFloor = 0.4f;
        /// <summary>CALIBRATED — rounds per exposed man over the 5 s window that suppress the whole unit (C-4).</summary>
        public const float SuppressingRoundsPerMan = 6f;
        /// <summary>The expected rounds per man near-missed (missed rounds land near a man in this share).</summary>
        public const float NearMissShare = 0.25f;

        /// <summary>Is the step due at <paramref name="now"/> after <paramref name="last"/>?</summary>
        internal static bool Due(SimInstant now, SimInstant last) => (double)now - (double)last >= StepSeconds - 1e-6;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8007</c> (ST-3, C-2 phase A, RV-4) — the FIRER's half of the attrition step, a per-entity phase every
    /// <see cref="StochasticCombat.StepSeconds"/>: each stochastic unit splits its live systems' effective fire over its REPORTED contacts in
    /// range (its own <see cref="UnitDetections"/>) by suitability × P(hit), and writes ONLY its own <see cref="FireAllocation"/> (and its
    /// <c>Activity.Firing</c>). ⭐ It spends no ammunition here: phase B spends the firer's own from its own lines, so A writes one component.
    /// The targets' slot table, holdings and position are read as declared stable reads (nothing in this phase writes them). 0 B steady.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class StochasticFirePhase : IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;
        private EntityQuery? _units;
        private EntityRepository? _queriesFor;
        private Entity[] _firers = new Entity[32];
        private SimInstant _last = (SimInstant)(-1.0);

        public StochasticFirePhase(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            var table = KillerVictimTable.Current;
            if (table == null || view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AggregateUnit>()
                || !repo.IsComponentTypeRegistered<FireAllocation>() || !repo.IsComponentTypeRegistered<UnitDetections>()) return;
            SimInstant now = repo.HasSingleton<GlobalTime>() ? (SimInstant)repo.GetSingleton<GlobalTime>().TotalTime : SimInstant.Zero;
            if (!StochasticCombat.Due(now, _last)) return;
            _last = now;
            if (!ReferenceEquals(_queriesFor, repo))
            {
                _units = repo.Query().With<AggregateUnit>().With<UnitSlotTable>().With<UnitDetections>().With<SimTransform>()
                    .WithOwnedWhen<AggregateUnit>(_gateOnAuthority).Build();
                _queriesFor = repo;
            }
            int n = 0;
            foreach (var e in _units!)
            {
                if (repo.GetComponentRO<AggregateUnit>(e).Effective != AggregateResolution.Stochastic) continue;
                if (!repo.HasComponent<FireAllocation>(e)) repo.AddComponent(e, new FireAllocation());   // serial: the structural add
                if (n == _firers.Length) Array.Resize(ref _firers, n * 2);
                _firers[n++] = e;
            }
            if (n == 0) return;
            var job = new Job { Repo = repo, Now = now, Table = table };
            if (Fdp.Core.Jobs.ParallelEntities.Enabled) Fdp.Core.Jobs.ParallelEntities.Run(repo, _firers, n, in job);
            else for (int i = 0; i < n; i++) job.Execute(_firers[i], i, null!);
        }

        private struct Job : Fdp.Core.Jobs.IEntityBatchJob, Fdp.Core.Jobs.IStableReads
        {
            public EntityRepository Repo;
            public SimInstant Now;
            public KillerVictimTable Table;

            /// <summary>The TARGETS' strength and position: nothing in phase A writes them.</summary>
            public void DeclareStableReads(Fdp.Core.Jobs.PhaseReadSet reads) => reads
                .Unmanaged<UnitSlotTable>().Unmanaged<UnitHoldings>().Unmanaged<SimTransform>();

            public void ApplyDeferred(EntityRepository repo, in Fdp.Core.Jobs.Deferred d) { }

            public void Execute(Entity self, int index, Fdp.Core.Jobs.BatchContext ctx) => Allocate(Repo, self, Now, Table);
        }

        /// <summary>One firer's allocation (the test seam).</summary>
        internal static void Allocate(EntityRepository repo, Entity self, SimInstant now, KillerVictimTable table)
        {
            ref var alloc = ref repo.GetComponentRW<FireAllocation>(self);
            alloc.Step = now;
            alloc.Count = 0;
            ref var u = ref repo.GetComponentRW<AggregateUnit>(self);
            ref readonly var slots = ref repo.GetComponentRO<UnitSlotTable>(self);
            var holdings = repo.HasComponent<UnitHoldings>(self) ? repo.GetComponentRO<UnitHoldings>(self) : default;
            ref readonly var det = ref repo.GetComponentRO<UnitDetections>(self);
            var at = (Vector3)repo.GetComponentRO<SimTransform>(self).Position;
            float fire = 1f - (1f - StochasticCombat.SuppressedFireFloor) * Math.Clamp(u.SuppressedShare, 0f, 1f);
            float dt = StochasticCombat.StepSeconds;
            bool fired = false;

            // the personal weapons: each up slot with rounds left fires its role's weapon
            for (int r = 1; r < 16; r++)
            {
                var weapon = table.PersonalWeapon((SlotRole)r);
                if ((long)weapon == 0) continue;
                int shooters = 0;
                for (int i = 0; i < slots.Count; i++)
                    if ((int)slots.Slots[i].Role == r && slots.IsUp(i) && slots.Slots[i].Ammo > 0
                        && (float)slots.Slots[i].Stress < StochasticCombat.BreakLevel) shooters++;
                if (shooters == 0) continue;
                fired |= Split(repo, ref alloc, table, weapon, shooters * fire * dt, FireAllocation.PersonalSource, in det, at);
            }
            // the held systems (crew-served weapons, the vehicles' guns): operational ones with ammunition
            for (int h = 0; h < holdings.Count && h < 254; h++)
            {
                ref readonly var x = ref holdings.Items[h];
                if (x.Kind == HoldingKind.Sensor || x.Operational == 0 || x.Ammo == 0) continue;
                fired |= Split(repo, ref alloc, table, x.Type, x.Operational * fire * dt, (byte)(h + 1), in det, at);
            }
            if (fired) { u.Activity |= UnitActivity.Firing; u.LastFiredAt = now; }
            else u.Activity &= ~UnitActivity.Firing;
        }

        /// <summary>Splits <paramref name="systemSeconds"/> of one weapon type's effective fire over the reported contacts and their classes,
        /// by suitability × P(hit) (C-2, RV-4). Lines aggregate per (target, class, source).</summary>
        private static bool Split(EntityRepository repo, ref FireAllocation alloc, KillerVictimTable table, TkbTypeId weapon, float systemSeconds,
                                  byte source, in UnitDetections det, Vector3 at)
        {
            float total = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int k = 0; k < det.Count; k++)
                {
                    ref readonly var pair = ref det.Pairs[k];
                    var t = pair.Target;
                    if (pair.Reported == 0 || !repo.IsAlive(t) || !repo.HasComponent<UnitSlotTable>(t)) continue;
                    var tAt = (Vector3)repo.GetComponentRO<SimTransform>(t).Position;
                    float range = Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(tAt.X, tAt.Y));
                    // the men
                    if (repo.GetComponentRO<UnitSlotTable>(t).Present() > 0 && table.TryGet(weapon, KillerVictimTable.Personnel, out var em))
                        total = Line(ref alloc, pass, total, systemSeconds, t, FireAllocation.PersonnelClass, source, em, range);
                    // the vehicles
                    if (repo.HasComponent<UnitHoldings>(t))
                    {
                        ref readonly var th = ref repo.GetComponentRO<UnitHoldings>(t);
                        for (int h = 0; h < th.Count && h < 254; h++)
                            if (th.Items[h].Kind == HoldingKind.Vehicle && th.Items[h].Operational > 0 && table.TryGet(weapon, th.Items[h].Type, out var ev))
                                total = Line(ref alloc, pass, total, systemSeconds, t, (byte)(h + 1), source, ev, range);
                    }
                }
                if (total <= 0f) return false;
            }
            return true;
        }

        /// <summary>Pass 0 sums the weights; pass 1 writes each option's share: hits = systems·s × rate × P(hit) × share.</summary>
        private static float Line(ref FireAllocation alloc, int pass, float total, float systemSeconds, Entity t, byte cls, byte source, KvEntry e, float range)
        {
            float p = e.PHitAt(range);
            float w = e.Suitability * p;
            if (w <= 0f) return total;
            if (pass == 0) return total + w;
            float rounds = systemSeconds * e.EffectiveRate * (w / total);
            if (rounds <= 0f) return total;
            int i = 0;
            for (; i < alloc.Count; i++)
                if (alloc.Lines[i].Target == t && alloc.Lines[i].VictimClass == cls && alloc.Lines[i].Source == source) break;
            if (i == alloc.Count)
            {
                if (alloc.Count >= FireAllocation.Capacity) return total;
                alloc.Lines[i] = new FireLine { Target = t, VictimClass = cls, Source = source };
                alloc.Count++;
            }
            ref var line = ref alloc.Lines[i];
            float hits = rounds * p;
            // the damage per hit, weighted by hits when two weapon types share a line (they never do: the source separates them)
            line.DamagePerHit = line.Hits + hits <= 0f ? e.DamagePerHit : (line.DamagePerHit * line.Hits + e.DamagePerHit * hits) / (line.Hits + hits);
            line.Hits += hits;
            line.Rounds += rounds;
            return total;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8007</c> (ST-3, C-2 phase B, C-3, C-4, C-5) — the VICTIM's half, a per-entity phase right after
    /// <see cref="StochasticFirePhase"/>: each stochastic unit reads every enemy <see cref="FireAllocation"/> of THIS step naming it (a declared
    /// stable read — phase B writes none), × its cover (P-5) and posture ⇒ λ per class; the DRAW per exposed slot p = 1 − exp(−λ / n), seeded
    /// (<c>SimRng</c>, the run seed — C-8); each hit lands on a slot weighted by exposure and takes the line's damage from its health (wounds
    /// stay); stress per slot with the soldier's constants; the suppressed share over the last 5 s; vehicles K- / M-killed. ⭐ It also spends
    /// the unit's OWN ammunition from its own allocation's lines. Writes only itself. 0 B steady.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class StochasticLossPhase : IEcsModuleSystem
    {
        private const int SaltDraw = 0x8007, SaltVehicle = 0x8008;
        private readonly bool _gateOnAuthority;
        private EntityQuery? _units;
        private EntityRepository? _queriesFor;
        private Entity[] _victims = new Entity[32];
        private Entity[] _all = new Entity[32];
        private SimInstant _last = (SimInstant)(-1.0);

        public StochasticLossPhase(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (KillerVictimTable.Current == null || view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AggregateUnit>()
                || !repo.IsComponentTypeRegistered<FireAllocation>()) return;
            SimInstant now = repo.HasSingleton<GlobalTime>() ? (SimInstant)repo.GetSingleton<GlobalTime>().TotalTime : SimInstant.Zero;
            if (!StochasticCombat.Due(now, _last)) return;
            _last = now;
            if (!ReferenceEquals(_queriesFor, repo))
            {
                _units = repo.Query().With<AggregateUnit>().With<UnitSlotTable>().With<FireAllocation>().Build();
                _queriesFor = repo;
            }
            // C-8 — the run's seed: the world's recorded singleton, else the process's (read, never written here)
            uint seed = repo.HasSingleton<RunSeed>() ? repo.GetSingleton<RunSeed>().Value : Determinism.SimRng.RunSeed;
            int all = 0, n = 0;
            foreach (var e in _units!)
            {
                if (repo.GetComponentRO<AggregateUnit>(e).Effective != AggregateResolution.Stochastic) continue;
                if (all == _all.Length) Array.Resize(ref _all, all * 2);
                _all[all++] = e;
                if (_gateOnAuthority && !repo.HasAuthority<AggregateUnit>(e)) continue;
                if (n == _victims.Length) Array.Resize(ref _victims, n * 2);
                _victims[n++] = e;
            }
            if (n == 0) return;
            var job = new Job { Repo = repo, Now = now, All = _all, AllCount = all, Seed = seed };
            if (Fdp.Core.Jobs.ParallelEntities.Enabled) Fdp.Core.Jobs.ParallelEntities.Run(repo, _victims, n, in job);
            else for (int i = 0; i < n; i++) job.Execute(_victims[i], i, null!);
        }

        private struct Job : Fdp.Core.Jobs.IEntityBatchJob, Fdp.Core.Jobs.IStableReads
        {
            public EntityRepository Repo;
            public SimInstant Now;
            public Entity[] All;
            public int AllCount;
            public uint Seed;

            /// <summary>The FIRERS' allocations: phase B writes none.</summary>
            public void DeclareStableReads(Fdp.Core.Jobs.PhaseReadSet reads) => reads.Unmanaged<FireAllocation>();

            public void ApplyDeferred(EntityRepository repo, in Fdp.Core.Jobs.Deferred d) { }

            public void Execute(Entity self, int index, Fdp.Core.Jobs.BatchContext ctx) => Resolve(Repo, self, Now, All, AllCount, Seed);
        }

        /// <summary>One victim's step (the test seam).</summary>
        internal static void Resolve(EntityRepository repo, Entity self, SimInstant now, Entity[] all, int allCount, uint seed)
        {
            ref var u = ref repo.GetComponentRW<AggregateUnit>(self);
            ref var slots = ref repo.GetComponentRW<UnitSlotTable>(self);
            bool hasHoldings = repo.HasComponent<UnitHoldings>(self);
            float dt = StochasticCombat.StepSeconds;

            // ① spend this unit's OWN ammunition, from its own lines (phase A wrote them this step)
            ref readonly var mine = ref repo.GetComponentRO<FireAllocation>(self);
            if (mine.Step == now) Spend(repo, self, ref slots, hasHoldings, in mine);

            // ② the incoming fire of this step, per class
            Span<float> lambda = stackalloc float[UnitHoldings.Capacity + 1];
            Span<float> damage = stackalloc float[UnitHoldings.Capacity + 1];
            lambda.Clear(); damage.Clear();
            float rounds = 0f;
            for (int j = 0; j < allCount; j++)
            {
                var f = all[j];
                if (f == self || !repo.IsAlive(f) || !repo.HasComponent<FireAllocation>(f)) continue;
                ref readonly var a = ref repo.GetComponentRO<FireAllocation>(f);
                if (a.Step != now) continue;
                for (int i = 0; i < a.Count; i++)
                {
                    ref readonly var line = ref a.Lines[i];
                    if (line.Target != self || line.VictimClass > UnitHoldings.Capacity) continue;
                    float l0 = lambda[line.VictimClass];
                    lambda[line.VictimClass] = l0 + line.Hits;
                    damage[line.VictimClass] = l0 + line.Hits <= 0f ? line.DamagePerHit : (damage[line.VictimClass] * l0 + line.DamagePerHit * line.Hits) / (l0 + line.Hits);
                    rounds += line.Rounds;
                }
            }

            // ③ the men: cover × posture, then the seeded draw per exposed slot
            int exposed = slots.Present();
            float cover = StochasticCombat.CoverFactor(u.Prepared) * (u.Posture == UnitPosture.Mounted ? StochasticCombat.MountedExposure : 1f);
            float lam = lambda[FireAllocation.PersonnelClass] * cover;
            var rng = Fdp.Toolkit.Determinism.SimRng.FromSim(self.Index, SaltDraw, (float)(double)now, seed);
            int downNow = 0;
            if (lam > 0f && exposed > 0)
            {
                float p = 1f - MathF.Exp(-lam / exposed);
                int hits = 0;
                for (int i = 0; i < exposed; i++) if (rng.NextSingle() < p) hits++;
                for (int h = 0; h < hits; h++)
                {
                    int s = PickExposed(in slots, ref rng);
                    if (s < 0) break;
                    bool wasUp = slots.IsUp(s);
                    float hp = (float)slots.Slots[s].Health - damage[FireAllocation.PersonnelClass];
                    slots.Slots[s].Health = (Half)MathF.Max(0f, hp);
                    slots.Slots[s].Stress = (Half)MathF.Min(1f, (float)slots.Slots[s].Stress + StochasticCombat.StressHit);
                    if (wasUp && !slots.IsUp(s)) downNow++;
                }
            }

            // ④ stress for the rest: friends down near them, near misses, then the calming (THE soldier's constants)
            float neighbours = Math.Clamp(2f * StochasticCombat.FriendDownMetres / MathF.Max(u.Width, 1f), 0.1f, 1f);
            float perMan = exposed > 0 ? rounds / exposed : 0f;
            float nearMissed = Math.Clamp(perMan * StochasticCombat.NearMissShare, 0f, 1f);
            bool inCover = u.Prepared != PreparedLevel.Unprepared;
            float calm = StochasticCombat.CalmPerSecond * dt * (inCover ? 2f : 1f)
                         * ((double)now - (double)u.LastContactAt > StochasticCombat.QuietSeconds ? 2f : 1f);
            for (int i = 0; i < slots.Count; i++)
            {
                if (!slots.IsUp(i)) continue;
                float s = (float)slots.Slots[i].Stress;
                s += downNow * StochasticCombat.StressFriendDown * neighbours;
                s += nearMissed * StochasticCombat.StressNearMiss * (inCover ? 0.5f : 1f);
                if (rounds <= 0f) s -= calm;
                slots.Slots[i].Stress = (Half)Math.Clamp(s, 0f, 1f);
            }

            // ⑤ the suppressed share: rounds per exposed man over the last 5 steps (C-4)
            int head = u.IncomingHead % AggregateUnit.SuppressionWindowSteps;
            u.IncomingRounds[head] = perMan;
            u.IncomingHead = (byte)((head + 1) % AggregateUnit.SuppressionWindowSteps);
            float window = 0f;
            for (int i = 0; i < AggregateUnit.SuppressionWindowSteps; i++) window += u.IncomingRounds[i];
            u.SuppressedShare = Math.Clamp(window / StochasticCombat.SuppressingRoundsPerMan, 0f, 1f);
            if (rounds > 0f) u.LastContactAt = now;
            u.LastStepAt = now;

            // ⑥ the vehicles: per operational vehicle p = 1 − exp(−λ / n); a hit K-kills or M-kills by the line's shares
            if (!hasHoldings) return;
            ref var hold = ref repo.GetComponentRW<UnitHoldings>(self);
            var vr = Fdp.Toolkit.Determinism.SimRng.FromSim(self.Index, SaltVehicle, (float)(double)now, seed);
            var table = KillerVictimTable.Current;
            for (int h = 0; h < hold.Count; h++)
            {
                float lv = lambda[h + 1];
                ref var x = ref hold.Items[h];
                if (lv <= 0f || x.Kind != HoldingKind.Vehicle || x.Operational == 0) continue;
                float pv = 1f - MathF.Exp(-lv / x.Operational);
                int n = x.Operational;
                float kShare = Math.Clamp(damage[h + 1] > 0f ? KillShareOf(x.Type, table) : 0f, 0f, 1f);
                float mShare = Math.Clamp(MobilityShareOf(x.Type, table), 0f, 1f);
                for (int v = 0; v < n; v++)
                {
                    if (vr.NextSingle() >= pv) continue;
                    float roll = vr.NextSingle();
                    if (roll < kShare) { if (x.Operational > 0) x.Operational--; if (x.Immobile > x.Operational) x.Immobile = (byte)x.Operational; }
                    else if (roll < kShare + mShare && x.Immobile < x.Operational) x.Immobile++;
                }
            }
        }

        /// <summary>The K-kill share of a hit on <paramref name="type"/> — the table's (any weapon's row; the hit's own weapon is folded in
        /// λ already). ⚠ CALIBRATED fallback when the table holds none.</summary>
        private static float KillShareOf(TkbTypeId type, KillerVictimTable? table)
            => KillerVictimGenerator.KKillOfPenetrating;

        private static float MobilityShareOf(TkbTypeId type, KillerVictimTable? table)
            => KillerVictimGenerator.MKillOfPenetrating;

        /// <summary>The slot a hit lands on: a seeded draw over the UP slots weighted by exposure (a crewman under armour 0.5, the rest 1).</summary>
        private static int PickExposed(in UnitSlotTable t, ref Fdp.Toolkit.Determinism.SimRng rng)
        {
            float sum = 0f;
            for (int i = 0; i < t.Count; i++) if (t.IsUp(i)) sum += Exposure(t.Slots[i].Role);
            if (sum <= 0f) return -1;
            float x = rng.NextSingle() * sum;
            for (int i = 0; i < t.Count; i++)
            {
                if (!t.IsUp(i)) continue;
                x -= Exposure(t.Slots[i].Role);
                if (x < 0f) return i;
            }
            for (int i = t.Count - 1; i >= 0; i--) if (t.IsUp(i)) return i;
            return -1;
        }

        /// <summary>CALIBRATED exposure weight of a role.</summary>
        private static float Exposure(SlotRole r) => r == SlotRole.Crew ? 0.5f : 1f;

        /// <summary>① — the unit's own rounds: personal lines spread over its up slots of a firing role, held lines from that holding.</summary>
        private static void Spend(EntityRepository repo, Entity self, ref UnitSlotTable slots, bool hasHoldings, in FireAllocation mine)
        {
            float personal = 0f;
            for (int i = 0; i < mine.Count; i++)
            {
                ref readonly var line = ref mine.Lines[i];
                if (line.Source == FireAllocation.PersonalSource) { personal += line.Rounds; continue; }
                if (!hasHoldings) continue;
                ref var hold = ref repo.GetComponentRW<UnitHoldings>(self);
                int h = line.Source - 1;
                if (h < hold.Count) hold.Items[h].Ammo = (uint)Math.Max(0, (long)hold.Items[h].Ammo - (long)MathF.Ceiling(line.Rounds));
            }
            if (personal <= 0f) return;
            int shooters = 0;
            for (int i = 0; i < slots.Count; i++) if (slots.IsUp(i) && slots.Slots[i].Ammo > 0) shooters++;
            if (shooters == 0) return;
            int each = (int)MathF.Ceiling(personal / shooters);
            for (int i = 0; i < slots.Count; i++)
                if (slots.IsUp(i) && slots.Slots[i].Ammo > 0) slots.Slots[i].Ammo = (ushort)Math.Max(0, slots.Slots[i].Ammo - each);
        }
    }
}
