using System.Globalization;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-7021</c> … <c>CE-7024</c>, <c>CE-7005</c> (City Battle §6, §11 C-1, C-4, §12 ②) — the COMPANY echelon over PUPPET PLATOONS: the
    /// company commander runs on the drills' one-frame fixture, and its platoons are stand-ins whose reports (Set, TaskFailed, SITREP colour)
    /// the rail publishes — the puppet bench. ⭐ Minutes of a company's battle in a few hundred frames: <see cref="Company.Over"/> moves sim
    /// time on without ticking every frame of it.
    /// </summary>
    public partial class SquadManeuverTests
    {
        /// <summary>⭐ <c>CE-7022</c> — the puppet bench: a body-less company over N puppet platoons (slot 0 First, 1 Second, 2 Third, 3 Fourth).</summary>
        private sealed class Company
        {
            public readonly Platoon P;
            public Company(int platoons = 3) => P = new Platoon(squads: platoons);
            public Squad S => P.S;
            public Entity First => P.S.Members[0];
            public Entity Second => P.S.Members[1];
            public Entity Third => P.S.Members[2];
            public void Ticks(int n) => P.Ticks(n);
            /// <summary>Sim time passes (<paramref name="seconds"/>), then a few frames run — the company sees the time gone by.</summary>
            public void Over(double seconds) { P.S.Pause(seconds); P.Ticks(3); }
            public void Set(Entity platoon) => P.ReportSet(platoon);
            public void Failed(Entity platoon) => P.Report(platoon, new CommandReport { Kind = ReportKind.Progress, Subtype = (byte)ProgressKind.TaskFailed });
            public void Colour(Entity platoon, StatusColour c) => P.Report(platoon, new CommandReport { Kind = ReportKind.Sitrep, Strength = c });
            public int OrdersTo(Entity platoon, string play) => P.OrdersTo(platoon, play);
            public T Last<T>(Entity platoon, string play) => OrderTo<T>(P, platoon, play);
            public Fbt.NodeStatus? RunToEnd(int frames = 10)
            {
                for (int i = 0; i < frames; i++) { P.Tick(); if (P.S.Finished(out var r)) return r; }
                return null;
            }
        }

        private static string Pt(Vector3 v) => "[" + v.X.ToString(CultureInfo.InvariantCulture) + "," + v.Y.ToString(CultureInfo.InvariantCulture) + ",0]";

        private static void CompanyAttack(Company c, float deadline, params Vector3[] objectives)
        {
            var parts = objectives.Select((o, i) => "\"Objective" + (i + 1) + "\":" + Pt(o)).ToList();
            if (deadline > 0f) parts.Add("\"DeadlineSeconds\":" + deadline.ToString(CultureInfo.InvariantCulture));
            c.S.Order("{" + string.Join(",", parts) + "}", BehaviorNames.CompanyAttack);
        }

        private static bool Near(Vector3 a, Vector3 b, float m = 1f) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y)) <= m;

        /// <summary>
        /// ⭐⭐ <c>CE-7023</c> (C-1b) — two objectives in order: the BASE OF FIRE (slot 1) defends 250 m short of objective 1 with its arc on it,
        /// the RESERVE (slot 2) 400 m short, the ASSAULT (slot 0) waits; the base of fire's Set ⇒ the assault platoon attacks objective 1
        /// (<c>PlatoonAttack</c> on its area); its Set ⇒ objective 2 — the base of fire moves up (250 m short of objective 2, on the line from 1);
        /// the second Set ⇒ every platoon consolidates on objective 2; all Set ⇒ the run succeeds.
        /// ✅ Red-proofs: <see cref="CompanyAttackBehavior.SupportSet"/> always true ⇒ the assault ordered at once · the next objective's points
        /// not recomputed ⇒ the base of fire stays short of objective 1.
        /// </summary>
        [Fact]
        public void CE7023_CompanyAttack_BaseOfFire_ThenAssault_ObjectiveByObjective_ThenConsolidates()
        {
            var c = new Company();
            Vector3 o1 = new(0f, 600f, 0f), o2 = new(0f, 1000f, 0f);
            CompanyAttack(c, 0f, o1, o2);
            c.Ticks(6);
            var start = StaffComposerSystem.CentreOf(c.S.Repo, c.S.Commander);
            var dir = Vector2.Normalize(new Vector2(start.X - o1.X, start.Y - o1.Y));
            var support = c.Last<PlatoonDefendParamsJsonDto>(c.Second, BehaviorNames.PlatoonDefend);
            Assert.True(Near(support.Position, o1 + new Vector3(dir * CompanyAttackBehavior.SupportStandOffMetres, 0f)), $"base of fire at {(Vector3)support.Position}");
            Assert.True(support.MaxRange > 0f, "the base of fire has an arc on the objective");
            Assert.True(Near(c.Last<PlatoonDefendParamsJsonDto>(c.Third, BehaviorNames.PlatoonDefend).Position,
                             o1 + new Vector3(dir * CompanyAttackBehavior.ReserveStandOffMetres, 0f)), "the reserve behind");
            Assert.Equal(0, c.OrdersTo(c.First, BehaviorNames.PlatoonAttack));   // waits for the base of fire

            c.Set(c.Second);
            c.Ticks(5);
            var attack = c.Last<PlatoonAttackParamsJsonDto>(c.First, BehaviorNames.PlatoonAttack);
            Assert.True(Near(attack.Position, o1), "the assault attacks objective 1");
            Assert.Equal(CompanyAttackBehavior.DefaultRadius, attack.SearchRadius);

            c.Set(c.First);
            c.Ticks(5);
            Assert.True(Near(c.Last<PlatoonDefendParamsJsonDto>(c.Second, BehaviorNames.PlatoonDefend).Position,
                             o2 + new Vector3(0f, -CompanyAttackBehavior.SupportStandOffMetres, 0f)), "the base of fire moved up, short of objective 2");
            c.Set(c.Second);
            c.Ticks(5);
            Assert.True(Near(c.Last<PlatoonAttackParamsJsonDto>(c.First, BehaviorNames.PlatoonAttack).Position, o2), "the assault attacks objective 2");

            c.Set(c.First);
            c.Ticks(5);
            foreach (var platoon in new[] { c.First, c.Second, c.Third })
                Assert.True(Near(c.Last<PlatoonDefendParamsJsonDto>(platoon, BehaviorNames.PlatoonDefend).Position, o2), "consolidates on objective 2");
            Assert.False(c.S.Finished(out _));
            c.Set(c.First); c.Set(c.Second); c.Set(c.Third);
            Assert.Equal(Fbt.NodeStatus.Success, c.RunToEnd());
        }

        /// <summary>⭐ <c>CE-7023</c> (C-1b) — a FAILED assault (its <c>PlatoonAttack</c> broke off: TaskFailed) is RELIEVED by the fresh reserve, which
        /// attacks the same objective; the spent platoon joins the base of fire. ✅ Red-proof: <see cref="CompanyAttackBehavior.Relieve"/> a no-op ⇒
        /// the reserve never attacks.</summary>
        [Fact]
        public void CE7023_AFailedAssault_IsRelievedByTheReserve()
        {
            var c = new Company();
            var o = new Vector3(0f, 600f, 0f);
            CompanyAttack(c, 0f, o);
            c.Ticks(6);
            c.Set(c.Second); c.Ticks(5);
            Assert.Equal(1, c.OrdersTo(c.First, BehaviorNames.PlatoonAttack));
            c.Failed(c.First);
            c.Ticks(6);
            Assert.True(Near(c.Last<PlatoonAttackParamsJsonDto>(c.Third, BehaviorNames.PlatoonAttack).Position, o), "the reserve attacks");
            Assert.True(c.OrdersTo(c.First, BehaviorNames.PlatoonDefend) > 0, "the spent assault defends (supports)");
        }

        /// <summary>⭐ <c>CE-7023</c> (C-1b) — two platoons, the assault BLACK and no reserve: the company breaks off — every platoon defends the start
        /// — and once both are Set there the run FAILS. ✅ Red-proof: the abort dropped ⇒ no defend order at the start.</summary>
        [Fact]
        public void CE7023_ABlackAssaultWithNoReserve_BreaksOff_AndTheRunFails()
        {
            var c = new Company(platoons: 2);
            var o = new Vector3(0f, 600f, 0f);
            CompanyAttack(c, 0f, o);
            c.Ticks(6);
            var start = StaffComposerSystem.CentreOf(c.S.Repo, c.S.Commander);
            c.Set(c.Second); c.Ticks(5);
            c.Colour(c.First, StatusColour.Black);
            c.Ticks(6);
            Assert.True(Near(c.Last<PlatoonDefendParamsJsonDto>(c.First, BehaviorNames.PlatoonDefend).Position, start), "back on the start");
            Assert.True(Near(c.Last<PlatoonDefendParamsJsonDto>(c.Second, BehaviorNames.PlatoonDefend).Position, start), "back on the start");
            c.Set(c.First); c.Set(c.Second);
            Assert.Equal(Fbt.NodeStatus.Failure, c.RunToEnd());
        }

        /// <summary>
        /// ⭐⭐ <c>CE-7005</c> (C-4, §3a tempo — "never wait") — an objective NOT taken: at half its deadline the company asks its headquarters
        /// for FIRE SUPPORT on it (a REQUEST in its outbox), at the whole deadline it COMMITS THE RESERVE beside the assault, and at 1.5 × it
        /// BYPASSES it for the next objective. ✅ Red-proofs: <c>Tempo</c> not called ⇒ no request, no commit, no bypass.
        /// </summary>
        [Fact]
        public void CE7005_TheDeadline_AsksForFires_ThenCommitsTheReserve_ThenBypasses()
        {
            var c = new Company();
            Vector3 o1 = new(0f, 600f, 0f), o2 = new(0f, 1000f, 0f);
            CompanyAttack(c, 100f, o1, o2);
            c.Ticks(6);
            c.Set(c.Second); c.Ticks(5);
            Assert.Equal(1, c.OrdersTo(c.First, BehaviorNames.PlatoonAttack));
            Assert.False(OutboxHasRequest(c, RequestKind.FireSupport));

            c.Over(55);
            Assert.True(OutboxHasRequest(c, RequestKind.FireSupport), "fires asked at half the deadline");
            Assert.Equal(0, c.OrdersTo(c.Third, BehaviorNames.PlatoonAttack));

            c.Over(50);
            Assert.True(Near(c.Last<PlatoonAttackParamsJsonDto>(c.Third, BehaviorNames.PlatoonAttack).Position, o1), "the reserve committed at the deadline");

            c.Over(55);
            Assert.True(Near(c.Last<PlatoonDefendParamsJsonDto>(c.Second, BehaviorNames.PlatoonDefend).Position,
                             o2 + new Vector3(0f, -CompanyAttackBehavior.SupportStandOffMetres, 0f)), "objective 1 bypassed: the base of fire moves on to objective 2");
        }

        /// <summary>The company's own outbox holds a REQUEST of <paramref name="kind"/> not yet sent.</summary>
        private static bool OutboxHasRequest(Company c, RequestKind kind)
        {
            if (!c.S.Repo.HasComponent<ReportOutbox>(c.S.Commander)) return false;
            var q = c.S.Repo.GetComponentRO<ReportOutbox>(c.S.Commander).Queued;
            for (int i = 0; i < q.Count; i++)
                if (q.Items[i].Report.Kind == ReportKind.Request && q.Items[i].Report.Subtype == (byte)kind) return true;
            return false;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-7024</c> (C-1c) — the defence: two BLOCKS across a 300 m line facing the threat (north), each with an arc on it and its own
        /// fall-back 250 m behind, the third platoon the RESERVE 125 m behind the middle; one block failing ⇒ the reserve goes in just behind it;
        /// the second block failing too (half the line) ⇒ the whole company falls back 250 m. ✅ Red-proofs: <c>Reinforce</c> a no-op ⇒ the reserve
        /// stays · <c>HalfTheBlocksFailing</c> always false ⇒ no fall-back.
        /// </summary>
        [Fact]
        public void CE7024_CompanyDefend_BlocksAcrossTheLine_TheReserveReinforces_HalfFailing_FallsBack()
        {
            var c = new Company();
            var at = new Vector3(0f, 0f, 0f);
            c.S.Order("{\"Position\":" + Pt(at) + ",\"Threat\":" + Pt(new Vector3(0f, 500f, 0f)) + "}", BehaviorNames.CompanyDefend);
            c.Ticks(6);
            var b1 = c.Last<PlatoonDefendParamsJsonDto>(c.First, BehaviorNames.PlatoonDefend);
            var b2 = c.Last<PlatoonDefendParamsJsonDto>(c.Second, BehaviorNames.PlatoonDefend);
            var reserve = c.Last<PlatoonDefendParamsJsonDto>(c.Third, BehaviorNames.PlatoonDefend);
            Assert.Equal(150f, Vector3.Distance(b1.Position, b2.Position), 1);                                  // spread over the frontage
            Assert.True(Near(b1.Position, new Vector3(75f, 0f, 0f)) || Near(b1.Position, new Vector3(-75f, 0f, 0f)), $"block 1 at {(Vector3)b1.Position}");
            Assert.True(Near(reserve.Position, new Vector3(0f, -125f, 0f)), "the reserve behind the middle");
            Assert.True(Near(b1.FallBackTo, (Vector3)b1.Position + new Vector3(0f, -CompanyDefendBehavior.DefaultDepth, 0f)), "its own fall-back behind");
            Assert.NotEqual(0f, (float)b1.Right - (float)b1.Left);                                              // an arc on the threat

            c.Colour(c.First, StatusColour.Black);
            c.Ticks(6);
            Assert.True(Near(c.Last<PlatoonDefendParamsJsonDto>(c.Third, BehaviorNames.PlatoonDefend).Position, (Vector3)b1.Position + new Vector3(0f, -40f, 0f)),
                        "the reserve reinforces the failing block");
            Assert.Equal(1, c.OrdersTo(c.First, BehaviorNames.PlatoonDefend));                                     // no fall-back yet: the reserve holds it

            c.Failed(c.Second);
            c.Ticks(6);
            Assert.True(Near(c.Last<PlatoonDefendParamsJsonDto>(c.Second, BehaviorNames.PlatoonDefend).Position, (Vector3)b2.Position + new Vector3(0f, -250f, 0f)),
                        "half the line failing: the company falls back");
        }
    }
}
