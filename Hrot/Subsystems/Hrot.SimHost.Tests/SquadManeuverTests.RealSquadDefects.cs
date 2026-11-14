using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ <c>CE-4605</c>…<c>CE-4608</c> (aggregate squad A-0) — real-squad defects found analysing the aggregate squad; each rail is
    /// red-proved by reverting the fix's key line. 📄 docs/designs/aggregate-units/DESIGN_Aggregate_Squad.md §6.</summary>
    public partial class SquadManeuverTests
    {
        /// <summary>⭐ <c>CE-4605</c> — nobody sees the threat and the two LAST slots are dead: the split falls back to the live men by rank,
        /// so the assault element is non-empty and holds only live men (the slot split put both survivors in the base of fire ⇒ nobody
        /// assaulted ⇒ <c>PhaseComplete</c> passed on <c>!any</c> and the play reported Success). ✅ Red-proof: the helper replaced by the
        /// slot split ⇒ the assault is empty.</summary>
        [Fact]
        public void CE4605_SuppressAndManeuver_NobodySeesIt_DeadInTheLastSlots_TheLiveMenStillAssault()
        {
            var s = new Squad(members: 4);
            Down(s, s.Members[2], 0f); Down(s, s.Members[3], 0f);                  // dead
            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0);                  // nobody sees him
            s.Order("{}", BehaviorNames.SuppressAndManeuver);
            for (int i = 0; i < 4; i++) s.Tick();
            Assert.Equal(SuppressAndManeuverManeuver.PhaseSuppressing, s.Phase);
            var squad = s.Repo.GetComponentRO<SquadCognitiveState>(s.Commander);
            Assert.Equal(SuppressAndManeuverManeuver.ElementBaseOfFire, squad.Elements.MemberElements[0]);
            Assert.Equal(SuppressAndManeuverManeuver.ElementAssault, squad.Elements.MemberElements[1]);
            Assert.False(s.Finished(out _), "no assault, no Success");
        }

        /// <summary>⭐ <c>CE-4605</c> — the dead in the FIRST slots, everyone live sees him: the assault holds live men only (never a corpse).</summary>
        [Fact]
        public void CE4605_SuppressAndManeuver_DeadInTheFirstSlots_TheAssaultHoldsOnlyLiveMen()
        {
            var s = new Squad(members: 4);
            Down(s, s.Members[0], 0f); Down(s, s.Members[1], 0f);
            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b1100);
            s.Order("{}", BehaviorNames.SuppressAndManeuver);
            for (int i = 0; i < 4; i++) s.Tick();
            var squad = s.Repo.GetComponentRO<SquadCognitiveState>(s.Commander);
            Assert.Equal(SuppressAndManeuverManeuver.ElementBaseOfFire, squad.Elements.MemberElements[2]);
            Assert.Equal(SuppressAndManeuverManeuver.ElementAssault, squad.Elements.MemberElements[3]);
        }

        /// <summary>⭐ <c>CE-4606</c> — a two-man squad with one down: the survivor is the leader, and he is the only one who can carry, so he is the
        /// medic (<c>MedicFor</c> skipped the leader ⇒ Start failed, Rescue was re-picked, and the squad ran with no orders until the casualty
        /// died). ✅ Red-proof: the leader fallback removed ⇒ no care starts.</summary>
        [Fact]
        public void CE4606_CasualtyCare_TwoManSquad_OneDown_TheSurvivingLeaderIsTheMedic()
        {
            var s = new Squad(members: 2);
            foreach (var m in s.Members)
                s.Repo.AddComponent(m, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 100, MaxAmmo = 100, MagazineSize = 30, MagazineRounds = 30 });
            Down(s, s.Members[1], 25f);                                              // alive, incapacitated
            var lead = new SquadLeadership { Leader = s.Members[0] };
            if (s.Repo.HasComponent<SquadLeadership>(s.Commander)) s.Repo.SetComponent(s.Commander, lead); else s.Repo.AddComponent(s.Commander, lead);
            var enemy = Enemy(s, new Vector3(0f, 80f, 0f));
            s.Pool(enemy, new Vector3(0f, 80f, 0f), reporters: 1);
            s.Order("{}", BehaviorNames.CasualtyCare);
            s.Tick(); s.Tick(); s.Tick();
            Assert.Equal(CasualtyCareManeuver.ManeuverKind, s.Kind);                 // the care started
            Assert.Equal(CasualtyCareManeuver.PhaseSuppress, s.Phase);
        }

        /// <summary>⭐ <c>CE-4607</c> — a squad whose ONLY man is down: nobody can carry him or report Set, so the remount does not wait for it, no
        /// <c>SquadRecover</c> / <c>SquadEmbark</c> is ordered to it, and the convoy carries on and finishes (it waited for the 120 s on-foot
        /// fallback and then for a Set that never came). ✅ Red-proof: <c>Mounted</c> back on the old live test ⇒ the vehicles never carry on.</summary>
        [Fact]
        public void CE4607_PlatoonConvoy_ASquadWhoseOnlyManIsDown_IsNotWaitedFor_AndTheConvoyFinishes()
        {
            var p = new Platoon();
            var down = p.S.Repo.CreateEntity();
            p.S.Repo.AddComponent(down, new SimTransform { Position = new Vector3(0f, 40f, 0f), Rotation = Quaternion.Identity });
            p.S.Repo.AddComponent(down, new UnitSubordinate { Commander = p.Alpha });
            p.S.Repo.AddComponent(down, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 7001 });
            var roster = new UnitRoster();
            UnitRoster.Add(ref roster, down);
            p.S.Repo.AddComponent(p.Alpha, roster);
            p.S.Repo.AddComponent(p.Alpha, new UnitEntity());
            var apc = ConvoyVehicle(p.S, new Vector3(0f, 40f, 0f), 9001, 0, down);
            var ifv = ConvoyVehicle(p.S, new Vector3(0f, 15f, 0f), 9002, 0, p.Bravo);
            ConvoyRoute(p.S, 9811, new Vector3(0f, 30f, 0f), new Vector3(0f, 380f, 0f));
            Convoy(p);
            p.Ticks(4);

            Embarkation.Disembark(p.S.Repo, down);
            Down(p.S, down, 20f);                                                                                         // the squad's only man, down
            Embarkation.Disembark(p.S.Repo, p.Bravo);                                                                      // Bravo got out: the halt
            p.Ticks(3);
            p.ReportSet(p.Bravo);
            p.Ticks(3);
            Assert.Equal(1, p.OrdersTo(p.Bravo, BehaviorNames.SquadEmbark));                                              // the remount began
            Assert.Equal((0, 0), (p.OrdersTo(p.Alpha, BehaviorNames.SquadRecover), p.OrdersTo(p.Alpha, BehaviorNames.SquadEmbark)));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(p.S.Repo, p.Bravo, ifv));
            p.Ticks(3);
            Assert.Equal((2, 2), (p.OrdersTo(apc, BehaviorNames.FollowRoute), p.OrdersTo(ifv, BehaviorNames.FollowRoute)));   // carried on: Alpha not waited for

            p.S.Repo.GetComponentRW<SimTransform>(apc).Position = new Vector3(0f, 375f, 0f);
            p.S.Repo.GetComponentRW<SimTransform>(ifv).Position = new Vector3(2f, 370f, 0f);
            Fbt.NodeStatus? result = null;
            for (int i = 0; i < 12 && result == null; i++) { p.Tick(); if (p.S.Finished(out var r)) result = r; }
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }
    }
}
