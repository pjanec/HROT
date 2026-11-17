using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Brains;
using Hrot.AI.Behaviors.Orders;
using Hrot.CGF.Systems;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐⭐ agg-behaviors <c>ST-3</c> — the stochastic unit's ATTACK play (B-4, B-6, B-8), the neutral Attack order's Position, the two-payload routing of the increment-2 sites (R-8315), and
    /// the SPOT the staff composes from a stochastic unit's own contact pool (B-7). The unit under test is the fixture's commander made stochastic; the enemy is a second stochastic unit of the other force; the
    /// real sensing and attrition phases (<c>StochasticUnitSensorSystem</c>, <c>StochasticFirePhase</c>, <c>StochasticLossPhase</c>) fight it with agg-backend's killer–victim table.</summary>
    public partial class SquadManeuverTests
    {
        private static readonly System.Text.Json.JsonSerializerOptions AggJson = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;

        /// <summary>A stochastic attacker (the fixture's commander, 48 slots, Friend) at the origin and a stochastic defender (<paramref name="defenderSlots"/> slots, Hostile) <paramref name="range"/> m north.</summary>
        private sealed class Fight : IDisposable
        {
            public readonly Squad S; public readonly EntityRepository Repo; public readonly Entity A, D;
            public readonly StochasticUnitMovementSystem Move = new();
            public readonly StochasticUnitSensorSystem Sensor = new();
            public readonly StochasticFirePhase Fire = new();
            public readonly StochasticLossPhase Loss = new();
            private readonly KillerVictimTable? _was;
            public int Time;

            public Fight(float range = 300f, int attackerSlots = 48, int defenderSlots = 12, ushort defenderAmmo = 210, ushort attackerAmmo = 210, float fireRate = 0.5f)
            {
                _was = KillerVictimTable.Current;
                KillerVictimTable.Current = AggregateTestUnits.FightTable(fireRate);
                var (s, _, repo, a) = StochasticMover(slots: attackerSlots);
                S = s; Repo = repo; A = a;
                AggregateTestUnits.Make(repo, a, slots: attackerSlots, up: attackerSlots, ammoEach: attackerAmmo);
                Repo.AddComponent(a, new EntityInfo { ForceId = ForceId.Friend });
                Repo.AddComponent(a, new UnitDetections());
                Repo.GetComponentRW<AggregateUnit>(a).Width = 60f;
                D = repo.CreateEntity();
                Repo.AddComponent(D, new SimTransform { Position = new Vector3(0f, range, 0f), Rotation = Quaternion.Identity });
                AggregateTestUnits.Make(repo, D, slots: defenderSlots, up: defenderSlots, ammoEach: defenderAmmo);
                Repo.GetComponentRW<AggregateUnit>(D).Width = 60f;
                Repo.AddComponent(D, new EntityInfo { ForceId = ForceId.Hostile });
                Repo.AddComponent(D, new UnitDetections());
                Repo.AddComponent(D, new SquadCognitiveState());
            }

            /// <summary>One second of the world: the brain frame, the follower, then sensing → fire → losses (the unit phases of a node).</summary>
            public void Second()
            {
                Time++;
                S.Pause(1.0 - 0.016);
                S.Tick();
                Move.Execute(Repo, 1f);
                Sensor.Execute(Repo, 1f); Fire.Execute(Repo, 1f); Loss.Execute(Repo, 1f);
            }

            /// <summary>Seconds until the play's end event shows (it lives one frame), at most <paramref name="max"/>.</summary>
            public bool UntilFinished(int max, out Fbt.NodeStatus result) { result = default; for (int i = 0; i < max; i++) { Second(); if (S.Finished(out result)) return true; } return false; }

            public void Seconds(int n) { for (int i = 0; i < n; i++) Second(); }
            public int Health(Entity e) { var t = Repo.GetComponentRO<UnitSlotTable>(e); float h = 0; for (int i = 0; i < t.Count; i++) h += (float)t.Slots[i].Health; return (int)h; }
            public int Rounds(Entity e) { var t = Repo.GetComponentRO<UnitSlotTable>(e); int r = 0; for (int i = 0; i < t.Count; i++) r += t.Slots[i].Ammo; return r; }
            public float DistanceToDefender() => Vector2.Distance(new Vector2(Where(Repo, A).X, Where(Repo, A).Y), new Vector2(Where(Repo, D).X, Where(Repo, D).Y));
            public void Dispose() { KillerVictimTable.Current = _was; S.Repo.Dispose(); }
        }

        private static string AttackJson(Vector3 position, bool holdBack = false, float searchRadius = 0f)
            => System.Text.Json.JsonSerializer.Serialize(new AttackOrder { Position = position, HoldBack = holdBack, SearchRadius = searchRadius }, AggJson);

        /// <summary>⭐⭐⭐ <c>ST-3 ACCEPTANCE</c> (design §19 B-4; R-8312, R-8313; a DIRECT order — the company-routed one is the routing rail below) — a stochastic platoon (48 slots) ordered <c>Attack</c> on a stochastic
        /// defender's position: it APPROACHES in line (not a column) while the attrition phases already trade fire, reports PROGRESS PhaseLine on reaching the 100 m assault distance, closes in the Assault
        /// formation, and ends either <c>ObjectiveSecure</c> (Success — the defender no longer STANDING) or <c>TaskFailed</c> (Failure — the attacker itself no longer standing): the slot tables agree with the
        /// verdict, and the health fell on BOTH sides. ✅ Red-proofs: the play never reaching the assault (stuck at the approach) ⇒ no PhaseLine ⇒ red · success judged without reading the defender's slots ⇒ the
        /// consistency assertion red · the SOP breaking the attacker's contact under fire (the old Defend-only spare) ⇒ the unit retreats ⇒ red.</summary>
        [Fact]
        public void ST3_Acceptance_AStochasticPlatoon_OrderedAttack_ApproachesFiresAssaults_AndTheOutcomeAgreesWithTheSlots()
        {
            using var f = new Fight(range: 300f, attackerSlots: 48, defenderSlots: 12);
            var (s, repo, a, d) = (f.S, f.Repo, f.A, f.D);
            SpawnWithTheTemplatesSop(s, a);                                                                  // the SOP of the table: an attacker under fire must press on (R-8317)
            int a0 = f.Health(a), d0 = f.Health(d);
            s.Order(AttackJson(Where(repo, d)), ResolutionRoutingTable.Plays.Attack);
            bool approachedInLine = false, firedWhileApproaching = false, assaultFormation = false;
            double phaseLineRange = -1;
            for (int i = 0; i < 2400 && !s.Finished(out _); i++)
            {
                f.Second();
                var au = repo.GetComponentRO<AggregateUnit>(a);
                if (au.Move == UnitMoveState.OnRoute && au.Formation == UnitFormation.Line) approachedInLine = true;
                if (au.Move == UnitMoveState.OnRoute && (au.Activity & UnitActivity.Firing) != 0 && Reports(repo, a, ProgressKind.PhaseLine) == 0) firedWhileApproaching = true;
                if (au.Formation == UnitFormation.Assault) assaultFormation = true;
                if (phaseLineRange < 0 && Reports(repo, a, ProgressKind.PhaseLine) > 0) phaseLineRange = f.DistanceToDefender();
            }
            Assert.True(s.Finished(out var result), "the attack ended (a decisive fight)");
            Assert.True(approachedInLine, "it approached as a deployed LINE");
            Assert.True(firedWhileApproaching, "it fired on its way in (phase A fires moving or not — finding F-8)");
            Assert.InRange(phaseLineRange, StochasticUnitAttackBehavior.AssaultDistanceMetres - 25f, StochasticUnitAttackBehavior.AssaultDistanceMetres + 25f);   // reported at the assault distance
            Assert.Equal(1, Reports(repo, a, ProgressKind.PhaseLine));
            Assert.True(assaultFormation, "it closed in the Assault formation");
            Assert.True(f.Health(a) < a0 && f.Health(d) < d0, $"slot health fell on BOTH sides: attacker {a0}→{f.Health(a)}, defender {d0}→{f.Health(d)}");
            bool attackerStanding = CombatEffective.IsStanding(repo, a), defenderStanding = CombatEffective.IsStanding(repo, d);
            if (result == Fbt.NodeStatus.Success)
            {
                Assert.Equal(1, Reports(repo, a, ProgressKind.ObjectiveSecure));
                Assert.Equal(0, Reports(repo, a, ProgressKind.TaskFailed));
                Assert.False(defenderStanding, "ObjectiveSecure ⇒ the defender is no longer standing");
                Assert.True(attackerStanding);
            }
            else
            {
                Assert.Equal(1, Reports(repo, a, ProgressKind.TaskFailed));
                Assert.Equal(0, Reports(repo, a, ProgressKind.ObjectiveSecure));
                Assert.False(attackerStanding, "TaskFailed ⇒ the attacker itself is no longer standing");
            }
        }

        /// <summary>⭐ <c>ST-3</c> (B-4, B-8, R-8303 <c>HoldBack</c>; ⭐ <c>CE-8302</c>, R-8341) — SUPPORT BY FIRE: <c>HoldBack</c> on a POSITION (the commander's support position — here the company's
        /// <see cref="CompanyAttackBehavior.SupportStandOffMetres"/>, 250 m short of the enemy) moves the unit ONTO that position and HOLDS there — it never closes on the enemy (the distance never falls below
        /// the stand-off), reports Set once in position (no PhaseLine: it has no assault line), keeps FIRING (its rounds fall, the defender's health falls) and never ends by itself. 📌 F-15: a HoldBack on
        /// the enemy's posts held 100 m from them — on the enemy line — and the base of fire died before its Set, so the assault was never ordered. ✅ Red-proof: the approach back at the assault distance
        /// short of the position (<c>HoldOn</c> ignored) ⇒ it holds 350 m out ⇒ red.</summary>
        [Fact]
        public void ST3_HoldBack_StandsOnItsSupportPosition_FiresThere_NeverClosesAndNeverEnds()
        {
            using var f = new Fight(range: 500f, attackerSlots: 12, defenderSlots: 120, defenderAmmo: 0, attackerAmmo: 5000, fireRate: 0.02f);     // an unarmed, large defender: the base of fire is never broken
            var (s, repo, a, d) = (f.S, f.Repo, f.A, f.D);
            int d0 = f.Health(d);
            float standOff = CompanyAttackBehavior.SupportStandOffMetres;
            var support = Where(repo, d) - new Vector3(0f, standOff, 0f);                                     // the support position, between the unit and the enemy
            s.Order(AttackJson(support, holdBack: true), ResolutionRoutingTable.Plays.Attack);
            for (int i = 0; i < 1500 && Reports(repo, a, ProgressKind.Set) == 0; i++) f.Second();
            Assert.Equal(1, Reports(repo, a, ProgressKind.Set));
            Assert.Equal(0, Reports(repo, a, ProgressKind.PhaseLine));
            float atSet = f.DistanceToDefender();
            Assert.InRange(atSet, standOff - 25f, standOff + 25f);
            int rounds = f.Rounds(a);
            f.Seconds(180);
            Assert.True(f.DistanceToDefender() > standOff - 25f, "it never closes: " + f.DistanceToDefender());
            Assert.False(repo.GetComponentRO<AggregateUnit>(a).Formation == UnitFormation.Assault);
            Assert.True(f.Rounds(a) < rounds, "it keeps firing (rounds spent)");
            Assert.True(f.Health(d) < d0, "…and hitting");
            Assert.False(s.Finished(out _), "support by fire never ends by itself");
            Assert.Equal(0, Reports(repo, a, ProgressKind.ObjectiveSecure));
            Assert.Equal(ResolutionRoutingTable.Plays.Attack, s.TaskName);
        }

        /// <summary>⭐ <c>ST-3</c> (R-8313) — the objective is NOT secured while an enemy still STANDS within the search radius: a strong, unarmed defender that the weak attacker cannot break is reached, and the
        /// attack goes on (no ObjectiveSecure, not finished). ✅ Red-proof: success judged without reading the defender's slots (<c>EnemyStandingNear</c> ignored) ⇒ it ends Success ⇒ red.</summary>
        [Fact]
        public void ST3_TheObjectiveIsNotSecured_WhileAnEnemyStillStandsOnIt()
        {
            using var f = new Fight(range: 300f, attackerSlots: 12, defenderSlots: 120, defenderAmmo: 0, attackerAmmo: 5000, fireRate: 0.002f);
            var (s, repo, a, d) = (f.S, f.Repo, f.A, f.D);
            s.Order(AttackJson(Where(repo, d)), ResolutionRoutingTable.Plays.Attack);
            bool ended = false;
            for (int i = 0; i < 700 && !ended; i++) { f.Second(); ended = s.Finished(out _); }
            Assert.True(f.DistanceToDefender() < 40f, "it closed to the objective: " + f.DistanceToDefender());
            Assert.True(CombatEffective.IsStanding(repo, d), "the defender still stands");
            Assert.False(ended, "…so the attack is not over");
            Assert.Equal(0, Reports(repo, a, ProgressKind.ObjectiveSecure));
        }

        /// <summary>⭐ <c>ST-3</c> — an Attack on a TARGET entity (no Position) takes the target's place as the objective, and a stochastic attacker that is itself no longer standing at the start fails at once
        /// (TaskFailed, Failure); an attack with neither a target nor a position fails too. ✅ Red-proof: the target's position not read ⇒ the unit stays ⇒ red.</summary>
        [Fact]
        public void ST3_Attack_OnATargetEntity_TakesItsPlace_AndABrokenAttackerOrNothingToAttack_Fails()
        {
            using var f = new Fight(range: 400f, defenderSlots: 12);
            var (s, repo, a, d) = (f.S, f.Repo, f.A, f.D);
            repo.AddComponent(d, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 7001 });
            s.Order("{\"Target\":" + 7001 + "}", ResolutionRoutingTable.Plays.Attack);
            f.Seconds(10);
            Assert.Equal(UnitMoveState.OnRoute, MoveOf(repo, a));
            Assert.True(((Vector3)repo.GetComponentRO<AggregateUnit>(a).Destination).Y > 250f, "marching on the target: " + (Vector3)repo.GetComponentRO<AggregateUnit>(a).Destination);

            using var g = new Fight(range: 400f);
            g.S.Order("{}", ResolutionRoutingTable.Plays.Attack);                                              // neither a target nor a position
            Assert.True(g.UntilFinished(6, out var r) && r == Fbt.NodeStatus.Failure);
            Assert.Equal(1, Reports(g.Repo, g.A, ProgressKind.TaskFailed));

            using var h = new Fight(range: 400f);
            AggregateTestUnits.Make(h.Repo, h.A, slots: 48, up: 0);                                            // wiped out
            h.S.Order(AttackJson(Where(h.Repo, h.D)), ResolutionRoutingTable.Plays.Attack);
            Assert.True(h.UntilFinished(6, out var r2) && r2 == Fbt.NodeStatus.Failure);
            Assert.Equal(1, Reports(h.Repo, h.A, ProgressKind.TaskFailed));
        }

        /// <summary>⭐ <c>ST-3</c> (R-8317) — the SOP's UNDER-FIRE row spares an ATTACKER as it spares a defender: incoming rounds do not turn the assault into a break of contact; a Move still does.
        /// ✅ Red-proof: <c>HoldsItsGround</c> back to "Defend only" ⇒ the attacker's play is replaced by BreakContact ⇒ red.</summary>
        [Fact]
        public void ST3_AnAttackingUnit_UnderFire_PressesOn_ButAMarchingOneStillBreaksContact()
        {
            var (s, mv, repo, u) = StochasticMover(at: Vector3.Zero);
            SpawnWithTheTemplatesSop(s, u);
            s.Order(AttackJson(new Vector3(0f, 900f, 0f)), ResolutionRoutingTable.Plays.Attack);
            Seconds(s, mv, 10);
            Contact(s, new Vector3(0f, 500f, 0f));
            for (int i = 0; i < 6; i++) { IncomingFire(repo, u, 0.5f); MoverSecond(s, mv); }
            Assert.True(StochasticUnitSop.UnderFire(repo.GetComponentRO<AggregateUnit>(u)));
            Assert.Equal(ResolutionRoutingTable.Plays.Attack, s.TaskName);
            Assert.True(StochasticUnitSop.HoldsItsGround(OrderKind.Attack) && StochasticUnitSop.HoldsItsGround(OrderKind.Defend));
            Assert.False(StochasticUnitSop.HoldsItsGround(OrderKind.Move));
        }

        /// <summary>⭐ <c>ST-3</c> (0 B) — the attack's STEADY tick (the approach, the assault's watch, the standing-enemy walk over its contacts) allocates nothing. ✅ Red-proof: a <c>new List</c> in
        /// <c>EnemyStandingNear</c> ⇒ red.</summary>
        [Fact]
        public void ST3_TheAttacksSteadyTick_AllocatesNothing()
        {
            using var f = new Fight(range: 300f);
            var (repo, a, d) = (f.Repo, f.A, f.D);
            f.Sensor.Execute(repo, 1f);
            var p = new StochasticAttackParams { Objective = Where(repo, d), SearchRadius = 75f, Phase = 2, Start = Vector3.Zero };
            Assert.Equal(Fbt.NodeStatus.Running, StochasticUnitAttackBehavior.Attack(ref p, a, repo));       // orders the unit
            repo.GetComponentRW<AggregateUnit>(a).Move = UnitMoveState.OnRoute;
            for (int i = 0; i < 5; i++) StochasticUnitAttackBehavior.Attack(ref p, a, repo);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) { StochasticUnitAttackBehavior.Attack(ref p, a, repo); StochasticUnitAttackBehavior.EnemyStandingNear(repo, a, in p); }
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // ── B-7: the stochastic unit's contacts reach its commander as SPOT ─────────────────────────────────────────────────────────

        /// <summary>⭐ <c>ST-3</c> (B-7) — what a stochastic unit's OWN sensing writes into its squad pool (<c>StochasticUnitSensorSystem</c>, N-2) reaches its commander as a SPOT through the staff composer (no
        /// roster needed — ST-0's walk), with the contact's position and size; and once the contact is a stochastic unit that is wiped out it is no longer an element (the composer's enemy filter reads the slots —
        /// <c>ThreatDanger.IsOutOfTheFight</c>'s stochastic arm), so the commander is told the element is LOST (Count 0). ✅ Red-proof: the stochastic arm of <c>IsOutOfTheFight</c> removed ⇒ the dead enemy stays a
        /// SPOT ⇒ red.</summary>
        [Fact]
        public void ST3_AStochasticUnitsContacts_ReachItsCommander_AsSpot_AndAWipedOutOneIsLost()
        {
            using var f = new Fight(range: 250f, defenderSlots: 12);
            var (s, repo, a, d) = (f.S, f.Repo, f.A, f.D);
            var commander = repo.CreateEntity();
            repo.AddComponent(commander, new UnitEntity());
            repo.AddComponent(commander, new UnitRoster());
            repo.AddComponent(a, new UnitSubordinate { Commander = commander });
            repo.AddComponent(a, new UnitEntity());
            var composer = new StaffComposerSystem();
            for (int i = 0; i < 40 && repo.GetComponentRO<SquadCognitiveState>(a).Contacts.Count == 0; i++) f.Second();
            Assert.True(repo.GetComponentRO<SquadCognitiveState>(a).Contacts.Count > 0, "the sensing wrote the contact into the unit's own squad pool");
            composer.Execute(repo, 0.016f);
            var queued = repo.GetComponentRO<ReportOutbox>(a).Queued;
            bool spot = false;
            for (int i = 0; i < queued.Count; i++)
                if (queued.Items[i].Report.Kind == ReportKind.Spot && queued.Items[i].Report.Count > 0)
                {
                    spot = true;
                    Assert.InRange(Vector2.Distance(new Vector2(queued.Items[i].Report.At.X, queued.Items[i].Report.At.Y), new Vector2(0f, 250f)), 0f, 5f);
                }
            Assert.True(spot, "a SPOT of the enemy unit is queued for the commander");
            AggregateTestUnits.Make(repo, d, slots: 12, up: 0);                                                 // the enemy is wiped out
            f.S.Pause(20); f.Second(); composer.Execute(repo, 0.016f);
            var q2 = repo.GetComponentRO<ReportOutbox>(a).Queued;
            bool lost = false;
            for (int i = 0; i < q2.Count; i++) if (q2.Items[i].Report.Kind == ReportKind.Spot && q2.Items[i].Report.Count == 0) lost = true;
            Assert.True(lost, "the wiped-out enemy is no longer a SPOT: the element is reported lost");
        }

        // ── increment 2 (R-8303, R-8315): the flipped sites send Order(kind) to a stochastic member, the unchanged play to a physical one ───────

        private static Fdp.Toolkit.Behavior.BehaviorRegistry AggProductionRegistry()
        {
            var r = new Fdp.Toolkit.Behavior.BehaviorRegistry();
            Hrot.CGF.Configuration.CgfBehaviorSetup.LoadFromAiAssembly(r);
            return r;
        }

        private static T LastOrder<T>(Squad s, Entity to, string intent)
            => System.Text.Json.JsonSerializer.Deserialize<T>(s.Orders.Last(o => o.Entity.Equals(to) && o.IntentId == intent).JsonParams, AggJson);

        private static int OrdersOf(Squad s, Entity to, string intent) => s.Orders.Count(o => o.Entity.Equals(to) && o.IntentId == intent);

        private static bool NoRoutedOrder(Squad s, Entity to) => !s.Orders.Any(o => o.Entity.Equals(to) && o.IntentId.StartsWith(Hrot.Map.Definitions.Behavior.Orders.OrderAttribute.IntentPrefix, StringComparison.Ordinal));

        private static void MakeStochastic(EntityRepository repo, Entity e, int slots = 12) => AggregateTestUnits.Make(repo, e, slots: slots, up: slots);

        /// <summary>⭐⭐ <c>ST-3</c> (R-8315) — <c>CompanyAttack</c>: its assault platoon (slot 0) is a STOCHASTIC unit ⇒ <c>Order.Attack</c> with the neutral <see cref="AttackOrder"/> on the objective (and its search
        /// radius); its base of fire (slot 1) ⇒ <c>Order.SupportByFire</c> with <c>HoldBack</c> on the support position (CE-8302: 250 m short); the physical reserve still gets <c>PlatoonDefend</c> (no routed order at all). A fully physical company
        /// still orders <c>PlatoonAttack</c> / <c>PlatoonSupportByFire</c> with their own json. ✅ Red-proof: the CompanyAttack sites back on <c>Assign</c> ⇒ no <c>Order.*</c> to the stochastic platoons ⇒ red.</summary>
        [Fact]
        public void ST3_CompanyAttack_OrdersAStochasticAssault_AndBaseOfFire_ByKind_AndAPhysicalOneByItsPlay()
        {
            var c = new Company();
            MakeStochastic(c.S.Repo, c.First); MakeStochastic(c.S.Repo, c.Second);
            Vector3 o1 = new(0f, 600f, 0f);
            CompanyAttack(c, 0f, o1);
            c.Ticks(6);
            var support = LastOrder<AttackOrder>(c.S, c.Second, "Order.SupportByFire");
            Assert.True(support.HoldBack, "the base of fire does not close");
            // ⭐ CE-8302 — on the SUPPORT POSITION (the physical base of fire's point, 250 m short of the objective), never on the objective
            float standOff = Vector2.Distance(new Vector2(support.Position.Local.X, support.Position.Local.Y), new Vector2(o1.X, o1.Y));
            Assert.True(MathF.Abs(standOff - CompanyAttackBehavior.SupportStandOffMetres) < 1f, "the base of fire stands 250 m short: " + standOff);
            Assert.Equal(0, c.OrdersTo(c.Second, BehaviorNames.PlatoonSupportByFire));
            Assert.Equal(0, OrdersOf(c.S, c.First, "Order.Attack"));                                              // the assault waits for the base of fire
            c.Set(c.Second); c.Ticks(5);
            var attack = LastOrder<AttackOrder>(c.S, c.First, "Order.Attack");
            Assert.True(Near(attack.Position, o1), "the assault attacks objective 1");
            Assert.Equal(CompanyAttackBehavior.DefaultRadius, attack.SearchRadius);
            Assert.False(attack.HoldBack);
            Assert.Equal(0, c.OrdersTo(c.First, BehaviorNames.PlatoonAttack));
            Assert.True(c.OrdersTo(c.Third, BehaviorNames.PlatoonDefend) > 0 && NoRoutedOrder(c.S, c.Third), "the physical reserve keeps its play");

            var p = new Company();                                                                                 // the all-physical company: unchanged
            CompanyAttack(p, 0f, o1);
            p.Ticks(6); p.Set(p.Second); p.Ticks(5);
            Assert.True(p.OrdersTo(p.Second, BehaviorNames.PlatoonSupportByFire) > 0 && p.OrdersTo(p.First, BehaviorNames.PlatoonAttack) > 0);
            Assert.True(NoRoutedOrder(p.S, p.First) && NoRoutedOrder(p.S, p.Second));
        }

        /// <summary>⭐⭐⭐ <c>ST-3 ACCEPTANCE, COMPANY-ROUTED</c> (design §23.5 "As built — ST-3" acceptance row; R-8315, CE-8302 / R-8341; CE-8305) — a PHYSICAL company's
        /// <c>CompanyAttack</c> over one STOCHASTIC and one PHYSICAL platoon, end to end through the receiver's resolution (<see cref="TacticalIntentResolutionSystem"/>, the routing
        /// table): ① the stochastic platoon as the BASE OF FIRE is ordered <c>Order.SupportByFire</c> (HoldBack, on the company's support position 250 m short), RUNS
        /// <c>StochasticUnitAttack</c>, walks onto that position and reports Set there; its Set orders the physical ASSAULT platoon <c>PlatoonAttack</c>, which RUNS it. ② the roles
        /// swapped: the stochastic platoon as the ASSAULT runs <c>StochasticUnitAttack</c> on the objective (no HoldBack), the physical base of fire runs <c>PlatoonSupportByFire</c>.
        /// ⭐ Neither crossed: the stochastic member never ran (or was ordered) a physical play, the physical member never ran the stochastic play nor got an <c>Order.*</c>.
        /// ✅ Red-proof: <c>CommandOrders.Route</c> sending the PHYSICAL payload/play to every member ⇒ the stochastic member runs a physical play ⇒ red.</summary>
        [Fact]
        public void ST3_Acceptance_APhysicalCompanyAttack_OverAStochasticAndAPhysicalPlatoon_EachRunsItsOwnPlay_NoneCrossed()
        {
            int stochasticPlay = BehaviorHash.FromName(ResolutionRoutingTable.Plays.Attack);
            int[] physicalPlays = { BehaviorHash.FromName(BehaviorNames.PlatoonAttack), BehaviorHash.FromName(BehaviorNames.PlatoonSupportByFire) };
            foreach (bool stochasticIsTheBaseOfFire in new[] { true, false })
            {
                var c = new Company();
                var repo = c.S.Repo;
                Entity sto = stochasticIsTheBaseOfFire ? c.Second : c.First, phy = stochasticIsTheBaseOfFire ? c.First : c.Second;
                AggregateTestUnits.Make(repo, sto, slots: 30, up: 30);
                repo.AddComponent(sto, new UnitEntity());
                repo.AddComponent(sto, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 6101 });
                repo.SetDisType(sto, UnitDis.Land(UnitArm.Infantry, UnitEchelon.Platoon, 225));
                repo.SetDisType(phy, UnitDis.Land(UnitArm.Infantry, UnitEchelon.Platoon, 225));
                foreach (var m in new[] { sto, phy }) { repo.AddComponent(m, new BehaviorState()); repo.SetAuthority<BehaviorState>(m, true); }
                var composer = new StaffComposerSystem();
                var resolver = new TacticalIntentResolutionSystem(new TacticalIntentMapperRegistry(), c.S.Registry);
                var mover = new StochasticUnitMovementSystem();
                int seen = 0;
                bool stoRanPhysical = false, phyRanStochastic = false;
                void Frame()
                {
                    c.S.Pause(1.0 - 0.016);                                                                       // a second of sim time a frame: the follower and the staff's delays move
                    c.Ticks(1);
                    mover.Execute(repo, 1f);
                    composer.Execute(repo, 0.016f);
                    repo.FlushCommandBuffers();
                    var all = c.S.Orders;
                    for (; seen < all.Count; seen++)                                                              // every member's orders through ITS receiver's resolution
                        if (all[seen].Entity.Equals(sto) || all[seen].Entity.Equals(phy)) { repo.Bus.PublishManaged(all[seen]); repo.Bus.SwapBuffers(); resolver.Execute(repo, 0.016f); }
                    int hs = repo.GetComponentRO<BehaviorState>(sto).ActiveBehaviorHash, hp = repo.GetComponentRO<BehaviorState>(phy).ActiveBehaviorHash;
                    if (Array.IndexOf(physicalPlays, hs) >= 0) stoRanPhysical = true;
                    if (hp == stochasticPlay) phyRanStochastic = true;
                }

                Vector3 o1 = new(0f, 400f, 0f);
                CompanyAttack(c, 0f, o1);
                if (stochasticIsTheBaseOfFire)
                {
                    for (int i = 0; i < 8; i++) Frame();
                    var support = LastOrder<AttackOrder>(c.S, sto, "Order.SupportByFire");
                    Assert.True(support.HoldBack, "① the stochastic base of fire is ordered by KIND, HoldBack");
                    float standOff = Vector2.Distance(new Vector2(support.Position.Local.X, support.Position.Local.Y), new Vector2(o1.X, o1.Y));
                    Assert.True(MathF.Abs(standOff - CompanyAttackBehavior.SupportStandOffMetres) < 1f, "① on the company's support position (CE-8302): " + standOff);
                    Assert.Equal(stochasticPlay, repo.GetComponentRO<BehaviorState>(sto).ActiveBehaviorHash);       // its receiver runs the stochastic play
                    Assert.Equal(0, c.OrdersTo(phy, BehaviorNames.PlatoonAttack));                                   // the assault waits for the base of fire's Set
                    for (int i = 0; i < 900 && c.OrdersTo(phy, BehaviorNames.PlatoonAttack) == 0; i++) Frame();      // it walks onto the support position, reports Set, the company orders the assault
                    Assert.True(c.OrdersTo(phy, BehaviorNames.PlatoonAttack) > 0, "① the stochastic base of fire's own Set ordered the physical assault");
                    var at = Where(repo, sto);
                    Assert.True(Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(support.Position.Local.X, support.Position.Local.Y)) < 30f, $"① it stands on its support position: {at}");
                    Frame(); Frame();
                    Assert.Equal(BehaviorHash.FromName(BehaviorNames.PlatoonAttack), repo.GetComponentRO<BehaviorState>(phy).ActiveBehaviorHash);   // the physical member RUNS its play
                }
                else
                {
                    for (int i = 0; i < 8; i++) Frame();
                    Assert.True(c.OrdersTo(phy, BehaviorNames.PlatoonSupportByFire) > 0, "② the physical base of fire gets its play");
                    Assert.Equal(BehaviorHash.FromName(BehaviorNames.PlatoonSupportByFire), repo.GetComponentRO<BehaviorState>(phy).ActiveBehaviorHash);
                    c.Set(phy);
                    for (int i = 0; i < 8 && OrdersOf(c.S, sto, "Order.Attack") == 0; i++) Frame();
                    var attack = LastOrder<AttackOrder>(c.S, sto, "Order.Attack");
                    Assert.False(attack.HoldBack, "② the stochastic ASSAULT closes");
                    Assert.True(Near(attack.Position, o1), "② on the objective");
                    Frame(); Frame();
                    Assert.Equal(stochasticPlay, repo.GetComponentRO<BehaviorState>(sto).ActiveBehaviorHash);
                }
                // ⭐ none crossed — by the orders AND by what each receiver ran
                Assert.Equal(0, c.OrdersTo(sto, BehaviorNames.PlatoonAttack) + c.OrdersTo(sto, BehaviorNames.PlatoonSupportByFire));
                Assert.True(NoRoutedOrder(c.S, phy), "the physical member never got an Order.*");
                Assert.False(stoRanPhysical, "the stochastic member never ran a physical play");
                Assert.False(phyRanStochastic, "the physical member never ran the stochastic play");
            }
        }

        /// <summary>⭐ <c>ST-3</c> (R-8315) — <c>CompanyDefend</c>'s counter-attack: the strong reserve is a STOCHASTIC platoon ⇒ <c>Order.Attack</c> (a position attack on the lost block's point); its Set ⇒ it defends the
        /// block by kind (<c>Order.Defend</c>, as before). ✅ Red-proof: the counter-attack back on <c>Assign(PlatoonAttack)</c> ⇒ no <c>Order.Attack</c> ⇒ red.</summary>
        [Fact]
        public void ST3_CompanyDefend_TheCounterAttackOfAStochasticReserve_IsOrderedByKind()
        {
            var c = new Company();
            MakeStochastic(c.S.Repo, c.Third);
            c.S.Order("{\"Position\":" + Pt(Vector3.Zero) + ",\"Threat\":" + Pt(new Vector3(0f, 500f, 0f)) + "}", BehaviorNames.CompanyDefend);
            c.Ticks(6);
            var b1 = c.Last<PlatoonDefendParamsJsonDto>(c.First, BehaviorNames.PlatoonDefend);
            c.Failed(c.First); c.Ticks(6);
            Assert.Equal(1, OrdersOf(c.S, c.Third, "Order.Attack"));
            Assert.Equal(0, c.OrdersTo(c.Third, BehaviorNames.PlatoonAttack));
            Assert.True(Near(LastOrder<AttackOrder>(c.S, c.Third, "Order.Attack").Position, b1.Position), "the reserve attacks the lost block");
            c.Set(c.Third); c.Ticks(6);
            Assert.True(OrdersOf(c.S, c.Third, "Order.Defend") > 0, "then it holds the block, by kind");
            Assert.Equal(1, OrdersOf(c.S, c.Third, "Order.Attack"));
        }

        /// <summary>⭐⭐ <c>ST-3</c> (R-8315) — <c>PlatoonAttack</c>'s squad orders: a stochastic SUPPORT squad is ordered <c>Order.SupportByFire</c> (HoldBack, on the support point — CE-8302), a stochastic ASSAULT squad
        /// <c>Order.FireAndMovement</c> in contact / <c>Order.Bound</c> before it (an <see cref="AttackOrder"/> on the objective); the physical squad of the same platoon keeps its play. ✅ Red-proof: the arms back on <c>Assign</c> ⇒ red.</summary>
        [Fact]
        public void ST3_PlatoonAttack_OrdersStochasticSquads_ByKind_PerArm()
        {
            foreach (bool reported in new[] { true, false })
            {
                var p = new Platoon(squads: 3);
                MakeStochastic(p.S.Repo, p.Alpha); MakeStochastic(p.S.Repo, p.Bravo);
                var o = new Vector3(0f, 200f, 0f);
                if (reported) { p.ReportSpot(p.Bravo, o, 3); p.Ticks(2); }
                Attack(p, o);
                p.Ticks(3); p.S.Pause(PlatoonAttackBehavior.SurveyTimeoutSeconds + 0.5); p.Ticks(4);
                if (reported)
                {
                    var support = LastOrder<AttackOrder>(p.S, p.Bravo, "Order.SupportByFire");
                    Assert.True(support.HoldBack);
                    // ⭐ CE-8302 — on the platoon's SUPPORT POINT (where its physical squad stands, 120 m short), never on the reported post
                    float standOff = Vector2.Distance(new Vector2(support.Position.Local.X, support.Position.Local.Y), new Vector2(o.X, o.Y));
                    Assert.True(MathF.Abs(standOff - PlatoonAttackBehavior.SupportStandOffMetres) < 1f, "on the support point, 120 m short: " + standOff);
                    Assert.Equal(0, p.OrdersTo(p.Bravo, BehaviorNames.SupportByFire));
                }
                p.ReportSet(p.Bravo); p.Ticks(4);
                string kind = reported ? "Order.FireAndMovement" : "Order.Bound";
                var assault = LastOrder<AttackOrder>(p.S, p.Alpha, kind);
                Assert.False(assault.HoldBack);
                Assert.Equal(0, p.OrdersTo(p.Alpha, reported ? BehaviorNames.FireAndMovement : BehaviorNames.BoundingOverwatch));
                Assert.True(NoRoutedOrder(p.S, p.Charlie), "the physical reserve is never routed");
            }
        }

        /// <summary>⭐ <c>ST-3</c> (R-8315) — <c>PlatoonAttack</c> on a BUILDING: a stochastic assault squad is ordered <c>Order.ClearBuilding</c> (an <see cref="AttackOrder"/> on the building). ✅ Red-proof: the arm
        /// back on <c>Assign(ClearBuilding)</c> ⇒ red.</summary>
        [Fact]
        public void ST3_PlatoonAttack_AStochasticAssaultOnABuilding_IsOrderedClearBuildingByKind()
        {
            var p = new Platoon(squads: 3);
            MakeStochastic(p.S.Repo, p.Alpha);
            var house = new Vector3(0f, 150f, 0f);
            p.ReportSpot(p.Bravo, house + new Vector3(0f, 50f, 0f), 2);
            p.ReportSpot(p.Alpha, house + new Vector3(2f, 1f, 0f), 2);
            p.Ticks(2);
            Attack(p, new Vector3(0f, 175f, 0f), radius: 80f);
            p.Ticks(3);
            AnswerArea(p, (7, new Vector2(house.X, house.Y)));
            p.Ticks(4);
            p.ReportSet(p.Bravo); p.Ticks(4);
            p.ReportSet(p.Alpha); p.Ticks(4);
            var clear = LastOrder<AttackOrder>(p.S, p.Alpha, "Order.ClearBuilding");
            Assert.True(Vector3.Distance(clear.Position, new Vector3(house.X, house.Y, 0f)) < 5f, "on the building " + clear.Position);
            Assert.Equal(0, p.OrdersTo(p.Alpha, BehaviorNames.ClearBuilding));
        }

        /// <summary>⭐ <c>ST-3</c> (R-8315; CE-8302) — a platoon's <c>PlatoonSupportByFire</c>: a stochastic squad gets <c>Order.SupportByFire</c> (<see cref="AttackOrder"/> on its support point, HoldBack), a physical one
        /// <c>SupportByFire</c> with the squad's own json. ✅ Red-proof: the site back on <c>Assign</c> ⇒ red.</summary>
        [Fact]
        public void ST3_PlatoonSupportByFire_OrdersAStochasticSquadByKind_AndAPhysicalOneByItsPlay()
        {
            var p = new Platoon(squads: 2);
            MakeStochastic(p.S.Repo, p.Alpha);
            var post = new Vector3(0f, 300f, 0f);
            p.S.Order(System.Text.Json.JsonSerializer.Serialize(new PlatoonSupportByFireParamsJsonDto { Position = new Vector3(0f, 100f, 0f), Post1 = post }, AggJson), BehaviorNames.PlatoonSupportByFire);
            p.Ticks(4);
            var a = LastOrder<AttackOrder>(p.S, p.Alpha, "Order.SupportByFire");
            Assert.True(a.HoldBack);
            // ⭐ CE-8302 — on ITS support point (the platoon's position ± the spread, as the physical squad's), never on the post
            Assert.True(Vector2.Distance(new(a.Position.Local.X, a.Position.Local.Y), new(0f, 100f)) <= PlatoonSupportByFireBehavior.SpreadMetres, "on its support point " + a.Position);
            Assert.Equal(0, p.OrdersTo(p.Alpha, BehaviorNames.SupportByFire));
            Assert.True(p.OrdersTo(p.Bravo, BehaviorNames.SupportByFire) > 0 && NoRoutedOrder(p.S, p.Bravo));
        }

        /// <summary>⭐ <c>ST-3</c> (R-8315, finding F-6) — <c>UrbanMovement</c>'s march: a stochastic squad is ordered <c>Order.March</c> with a <see cref="MoveOrder"/> (the kind's neutral DTO — the March row
        /// resolves to <c>StochasticUnitMove</c>, which reads it), the physical squads <c>SquadMarch</c> as before. ✅ Red-proof: the site back on <c>Assign(SquadMarch)</c> ⇒ the strict <c>MoveOrder</c> parse of a
        /// <c>SquadMarch</c> json refuses ⇒ red.</summary>
        [Fact]
        public void ST3_UrbanMovement_OrdersAStochasticSquadsMarchAsAMoveOrder_AndAPhysicalOneBySquadMarch()
        {
            var p = new Platoon(squads: 3);
            p.S.Teleport(p.Alpha, Vector3.Zero); p.S.Teleport(p.Bravo, new Vector3(0f, -30f, 0f)); p.S.Teleport(p.Charlie, new Vector3(0f, -60f, 0f));
            MakeStochastic(p.S.Repo, p.Bravo, 9);
            p.S.Order("{\"Legs\":[[0,100,0],[100,100,0]],\"Spacing\":30}", BehaviorNames.UrbanMovement);
            p.Ticks(6);
            var move = LastOrder<MoveOrder>(p.S, p.Bravo, "Order.March");
            Assert.True(move.Speed >= 0f && move.ArrivalRadius > 0f);
            Assert.Equal(0, p.OrdersTo(p.Bravo, BehaviorNames.SquadMarch));
            Assert.True(p.OrdersTo(p.Alpha, BehaviorNames.SquadMarch) > 0 && NoRoutedOrder(p.S, p.Alpha), "the lead (physical) keeps SquadMarch");
            // the strict neutral parse is what the March row's play does: it must accept the json
            Assert.Empty(Fdp.Toolkit.Behavior.BehaviorParamsValidation.Validate(AggProductionRegistry(), ResolutionRoutingTable.Plays.Move,
                p.S.Orders.Last(o => o.Entity.Equals(p.Bravo) && o.IntentId == "Order.March").JsonParams));
        }

        /// <summary>⭐ <c>ST-3</c> (R-8315) — the two-payload <c>Route</c> and its <c>StillRoutedRunning</c> / <c>GaveRouted</c> keys: a stochastic member gets the NEUTRAL json under <c>Order.&lt;kind&gt;</c>, anyone
        /// else the PHYSICAL json under the play, byte for byte; a recognised unchanged re-send is not sent twice. ✅ Red-proof: <c>Route</c> sending the neutral json to a physical member ⇒ red.</summary>
        [Fact]
        public void ST3_RouteWithTwoPayloads_SendsTheNeutralJsonToAStochasticMember_AndThePhysicalJsonToAnyoneElse()
        {
            var s = new Squad(members: 2);
            var stochastic = s.Members[0]; var physical = s.Members[1];
            MakeStochastic(s.Repo, stochastic);
            CommandOrders.Route(s.Repo, s.Commander, stochastic, OrderKind.Attack, "{\"neutral\":1}", "PlayX", "{\"physical\":1}", newOrder: true);
            CommandOrders.Route(s.Repo, s.Commander, physical, OrderKind.Attack, "{\"neutral\":1}", "PlayX", "{\"physical\":1}", newOrder: true);
            s.Tick();
            Assert.Equal("{\"neutral\":1}", s.Orders.Single(o => o.Entity.Equals(stochastic)).JsonParams);
            Assert.Equal("Order.Attack", s.Orders.Single(o => o.Entity.Equals(stochastic)).IntentId);
            Assert.Equal("{\"physical\":1}", s.Orders.Single(o => o.Entity.Equals(physical)).JsonParams);
            Assert.Equal("PlayX", s.Orders.Single(o => o.Entity.Equals(physical)).IntentId);
            Assert.True(CommandOrders.GaveRouted(s.Repo, s.Commander, stochastic, OrderKind.Attack, "{\"neutral\":1}", "PlayX", "{\"physical\":1}") || Fdp.Toolkit.Behavior.Components.BehaviorOwnedParts.OwnerOf(s.Repo, s.Commander) == 0);
        }
    }
}
