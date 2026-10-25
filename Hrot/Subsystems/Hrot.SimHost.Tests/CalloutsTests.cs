using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Squad;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2372</c> (AQ105, R-2080) — the squad calls: one policy (<see cref="Callouts.Say"/>: alone, hushed, cool-down, no repeats), the
    /// leader's command and the bound's "moving / covering" at an issue, and the moments engine code leaves behind (a throw, a reload, a new
    /// contact, a man down). 📄 docs/blueprints/Architect_Question_105_Squad_Callouts.md §4.
    /// </summary>
    public sealed class CalloutsTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        private double _t = 10.0;

        public CalloutsTests()
        {
            SimHostComponentRegistry.RegisterAll(_w);
            CognitiveComponentRegistry.RegisterAll(_w);
            if (!_w.IsComponentTypeRegistered<Utterance>()) _w.RegisterComponent<Utterance>();
            Clock(0.0);
        }

        public void Dispose() => _w.Dispose();

        private void Clock(double dt)
        {
            _t += dt;
            _w.SetSingleton(new GlobalTime { TotalTime = _t, DeltaTime = 0.016f, TimeScale = 1f, FrameNumber = (long)(_t * 60) });
        }

        private Entity Man(float x, ForceId force = ForceId.Friend)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new BehaviorState());
            _w.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            _w.AddComponent(e, new SimTransform { Position = new Vector3(x, 0f, 0f), Rotation = Quaternion.Identity });
            _w.AddComponent(e, new EntityInfo { Name = "m", ForceId = force });
            return e;
        }

        private UtteranceKind Said(Entity man) => Utterances.Of(_w, man, _t, out _);

        /// <summary>
        /// AQ105 B/C/E — the policy: a man alone stays silent; with a friend in earshot he calls; within 3 s he does not call again — but an
        /// urgent call (grenade) breaks the cool-down; a friend 5 m off who just called the same is not echoed; a man who holds his fire with
        /// nothing come at him is hushed; a man in an ambush is hushed. ✅ Red-proofs: the earshot test dropped ⇒ the lone man calls; the
        /// cool-down dropped ⇒ the second call is said; the repeat rule dropped ⇒ the echo is said.
        /// </summary>
        [Fact]
        public void CE2372_ThePolicy_Alone_CoolDown_Urgent_NoEcho_Hushed()
        {
            var lone = Man(0f);
            Assert.False(Callouts.Say(_w, lone, UtteranceKind.CallMoving));

            var a = Man(100f); var b = Man(105f);
            Assert.True(Callouts.Say(_w, a, UtteranceKind.CallMoving));
            Assert.Equal(UtteranceKind.CallMoving, Said(a));
            Clock(1.0);
            Assert.False(Callouts.Say(_w, a, UtteranceKind.CallReloading));   // cooling down
            Assert.True(Callouts.Say(_w, a, UtteranceKind.CallGrenade));      // urgent
            Assert.False(Callouts.Say(_w, b, UtteranceKind.CallGrenade));     // a friend 5 m off just called it

            var quiet = Man(200f); Man(205f);
            _w.AddComponent(quiet, new Roe { Fire = RoeFire.HoldFire, SetBy = BehaviorOrigin.Operator });
            Assert.False(Callouts.Say(_w, quiet, UtteranceKind.CallContact));

            var ambusher = Man(300f); Man(305f);
            _w.GetComponentRW<BehaviorState>(ambusher).ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.Ambush);
            Assert.False(Callouts.Say(_w, ambusher, UtteranceKind.CallContact));
        }

        /// <summary>A squad: a commander with these men (the first its leader), each under it.</summary>
        private (Entity Commander, SquadPlayContext Ctx) Squad(params Entity[] men)
        {
            var c = _w.CreateEntity();
            var roster = new UnitRoster();
            foreach (var m in men)
            {
                UnitRoster.Add(ref roster, m);
                _w.AddComponent(m, new UnitSubordinate { Commander = c });
            }
            _w.AddComponent(c, roster);
            _w.AddComponent(c, new SquadLeadership { Leader = men[0] });
            return (c, new SquadPlayContext(_w, c, _t, in roster));
        }

        /// <summary>
        /// AQ105 B — at an issue the leader calls the command (a bound: "move"), each mover "moving" and each suppressor "covering"; the same
        /// command at the next issue is not called again; a hold with no bound is acknowledged by the man nearest the leader. ✅ Red-proofs:
        /// the last-call memory dropped ⇒ "move" again; the bound test dropped ⇒ no "moving".
        /// </summary>
        [Fact]
        public void CE2372_AnIssue_TheLeaderCommands_TheBoundCallsMovingAndCovering_ThenAnAck()
        {
            var leader = Man(0f); var mover = Man(3f); var gun = Man(6f);
            var (_, ctx) = Squad(leader, mover, gun);
            byte last = 0;
            Span<byte> bound = stackalloc byte[] { (byte)MemberOrderKind.Hold, (byte)MemberOrderKind.AdvanceTo, (byte)MemberOrderKind.Suppress };
            Callouts.OnIssue(in ctx, bound, maneuver: 0, ref last);
            Assert.Equal(UtteranceKind.CmdMove, Said(leader));
            Assert.Equal(UtteranceKind.CallMoving, Said(mover));
            Assert.Equal(UtteranceKind.CallCovering, Said(gun));

            Clock(5.0);
            Callouts.OnIssue(in ctx, bound, maneuver: 0, ref last);
            Assert.Equal(UtteranceKind.UtterNone, Said(leader));   // the same command: not called again

            Clock(5.0);
            Span<byte> hold = stackalloc byte[] { (byte)MemberOrderKind.Hold, (byte)MemberOrderKind.Hold, (byte)MemberOrderKind.Hold };
            Callouts.OnIssue(in ctx, hold, maneuver: 0, ref last);
            Assert.Equal(UtteranceKind.CmdHold, Said(leader));
            Assert.Equal(UtteranceKind.CallAck, Said(mover));       // nearest the leader
            Assert.Equal(UtteranceKind.UtterNone, Said(gun));
        }

        /// <summary>
        /// AQ105 B — what engine code leaves behind: a mount starting to reload ⇒ "reloading"; the first look learns what he already knows
        /// silently; a squad member down ⇒ the live man nearest him calls "man down" about him. ✅ Red-proofs: the reload edge read as a level
        /// ⇒ called again every tick (a second call after the cool-down while still reloading); the nearest choice dropped ⇒ the far man calls.
        /// </summary>
        [Fact]
        public void CE2372_TheSystem_Reload_AndAManDown()
        {
            var a = Man(0f); var b = Man(4f); var far = Man(20f);
            var (commander, _) = Squad(a, b, far);
            _w.AddComponent(a, new Fdp.Toolkit.Combat.Components.WeaponAmmo { Count = 1 });
            var system = new CalloutSystem();
            system.Execute(_w, 0.016f);   // the first look
            Assert.Equal(UtteranceKind.UtterNone, Said(a));

            Clock(0.1);
            _w.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponAmmo>(a).Busy = 1;   // he starts reloading
            system.Execute(_w, 0.016f);
            Assert.Equal(UtteranceKind.CallReloading, Said(a));
            Clock(5.0);
            system.Execute(_w, 0.016f);
            Assert.Equal(UtteranceKind.UtterNone, Said(a));   // still reloading: one call per reload

            _w.Bus.Publish(new Fdp.Toolkit.Perception.Events.SensorChangedEvent { Unit = commander, What = Fdp.Toolkit.Perception.Events.SensorChange.SquadMemberDown, Target = a });
            _w.Bus.SwapBuffers();
            Clock(0.1);
            system.Execute(_w, 0.016f);
            Assert.Equal(UtteranceKind.CallManDown, Utterances.Of(_w, b, _t, out var about));
            Assert.Equal(a, about);
            Assert.Equal(UtteranceKind.UtterNone, Said(far));
        }
    }
}
