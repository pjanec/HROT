using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Squad;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2372</c> (AQ105 B/C/E, R-2080) — the ONE squad-call policy: every combat call goes through <see cref="Say"/>, which is silent
    /// when the man is HUSHED (hidden on purpose — <see cref="RestlessHolds.IsHiddenOnPurpose"/>; or HoldFire and nothing has come at him in
    /// <see cref="HushQuietSeconds"/>), when he is ALONE (no friend within <see cref="EarshotMetres"/>), within his <see cref="CoolDownSeconds"/>
    /// (an urgent call — grenade, man down — breaks it), or when a friend within <see cref="RepeatMetres"/> said the same just now.
    /// A shout is heard by the enemy (backend's voice, <c>Utterances.IsShouted</c>) and played by the IG (ui's lines per kind).
    /// 📄 docs/blueprints/Architect_Question_105_Squad_Callouts.md §4.
    /// </summary>
    public static class Callouts
    {
        public const float SpeakSeconds = 1.2f;
        public const double CoolDownSeconds = 3.0, RepeatSeconds = 2.0, HushQuietSeconds = 60.0;
        public const float EarshotMetres = 25f, RepeatMetres = 10f;

        /// <summary><paramref name="man"/> calls <paramref name="kind"/> (about <paramref name="target"/>) when the policy lets him. True when said.</summary>
        public static bool Say(EntityRepository world, Entity man, UtteranceKind kind, Entity target = default)
        {
            if (!world.IsComponentTypeRegistered<Utterance>() || !world.IsComponentTypeRegistered<CalloutState>()) return false;
            if (!world.IsAlive(man) || !world.HasComponent<SimTransform>(man) || !Fdp.Toolkit.Squad.CombatEffective.IsInTheFight(world, man)) return false;
            double now = OneManCommand.Now(world);
            var at = world.GetComponentRO<SimTransform>(man).Position;
            if (Hushed(world, man, now) || !UnderFire.FriendNear(world, man, at, EarshotMetres)) return false;
            if (!world.HasComponent<CalloutState>(man)) world.AddComponent(man, new CalloutState { Grenades = -1 });
            ref var st = ref world.GetComponentRW<CalloutState>(man);
            bool urgent = kind is UtteranceKind.CallGrenade or UtteranceKind.CallManDown;
            if (!urgent && st.LastAt > 0 && now - st.LastAt < CoolDownSeconds) return false;
            if (SaidNearby(world, man, at, kind, now)) return false;
            if (!Utterances.Say(world, man, kind, target, now, SpeakSeconds)) return false;
            st.LastAt = now;
            st.LastKind = kind;
            return true;
        }

        /// <summary>
        /// ⭐ AQ105 B — a squad play ISSUED its orders: the leader calls the command — fall back (a break-contact play), move (someone moves),
        /// suppress (someone suppresses), else hold — only when it differs from his last; in a bound each mover calls "moving" and each
        /// suppressor "covering"; otherwise the man nearest the leader acknowledges. <paramref name="kinds"/>[i] = member i's
        /// <see cref="MemberOrderKind"/> (255 = none).
        /// </summary>
        public static void OnIssue(in SquadPlayContext ctx, ReadOnlySpan<byte> kinds, ushort maneuver, ref byte lastCall)
        {
            var world = ctx.World;
            int movers = 0, suppressors = 0;
            foreach (var k in kinds)
            {
                if (k == (byte)MemberOrderKind.AdvanceTo) movers++;
                else if (k == (byte)MemberOrderKind.Suppress || k == (byte)MemberOrderKind.FireAtPoint) suppressors++;
            }
            var leader = world.HasComponent<SquadLeadership>(ctx.Commander) ? world.GetComponentRO<SquadLeadership>(ctx.Commander).Leader : Entity.Null;
            if (leader.IsNull || !Fdp.Toolkit.Squad.CombatEffective.IsInTheFight(world, leader)) leader = SquadManeuverRunner.FirstLive(in ctx);
            if (leader.IsNull) return;
            var command = maneuver == Fdp.Toolkit.Squad.Maneuvers.BreakContactManeuver.ManeuverKind ? UtteranceKind.CmdFallBack
                        : movers > 0 ? UtteranceKind.CmdMove : suppressors > 0 ? UtteranceKind.CallSuppress : UtteranceKind.CmdHold;
            bool commanded = (byte)command != lastCall && Say(world, leader, command);
            if (commanded) lastCall = (byte)command;
            bool bound = movers > 0 && suppressors > 0;
            Entity ack = Entity.Null;
            float ackD = float.MaxValue;
            var leaderAt = world.HasComponent<SimTransform>(leader) ? world.GetComponentRO<SimTransform>(leader).Position : default;
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] == byte.MaxValue || !ctx.Live(i, out var m) || m == leader) continue;
                if (bound && kinds[i] == (byte)MemberOrderKind.AdvanceTo) { Say(world, m, UtteranceKind.CallMoving); continue; }
                if (bound && (kinds[i] == (byte)MemberOrderKind.Suppress || kinds[i] == (byte)MemberOrderKind.FireAtPoint)) { Say(world, m, UtteranceKind.CallCovering); continue; }
                float d = Vector3.Distance(ctx.At(m), leaderAt);
                if (d < ackD) { ackD = d; ack = m; }
            }
            if (commanded && !bound && !ack.IsNull) Say(world, ack, UtteranceKind.CallAck);
        }

        /// <summary>AQ105 C — must he keep quiet: his task is a hidden one (ambush, trigger man, bomber, window duel), or he holds his fire and
        /// nothing has come at him (no near miss, no hit) in <see cref="HushQuietSeconds"/>.</summary>
        public static bool Hushed(EntityRepository world, Entity man, double now)
        {
            if (RestlessHolds.IsHiddenOnPurpose(world, man)) return true;
            if (!world.IsComponentTypeRegistered<Roe>() || !world.HasComponent<Roe>(man) || world.GetComponentRO<Roe>(man).Fire != RoeFire.HoldFire) return false;
            return !RecentSensesOf.Within(world, man, SensorChange.NearMiss, HushQuietSeconds)
                && !RecentSensesOf.Within(world, man, SensorChange.Hit, HushQuietSeconds);
        }

        /// <summary>AQ105 E — a friend within <see cref="RepeatMetres"/> is saying, or said within <see cref="RepeatSeconds"/>, the same call.</summary>
        private static bool SaidNearby(EntityRepository world, Entity man, Vector3 at, UtteranceKind kind, double now)
        {
            if (!world.HasComponent<EntityInfo>(man)) return false;
            var force = world.GetComponentRO<EntityInfo>(man).ForceId;
            foreach (var e in world.Query().With<Utterance>().With<SimTransform>().Build())
            {
                if (e == man) continue;
                ref readonly var u = ref world.GetComponentRO<Utterance>(e);
                if (u.Kind != kind || u.Until + RepeatSeconds - SpeakSeconds < now) continue;
                if (!world.HasComponent<EntityInfo>(e) || world.GetComponentRO<EntityInfo>(e).ForceId != force) continue;
                if (Vector3.Distance(world.GetComponentRO<SimTransform>(e).Position, at) <= RepeatMetres) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2372</c> (AQ105 B) — the calls whose moment happens in engine code (the staff's SPOT, the weapon brain's throw, the reload):
    /// behaviors cannot be called from there (FDP does not reference Hrot), so this brain-node pass watches what those systems leave and calls
    /// through <see cref="Callouts.Say"/>: a NEW identified, armed enemy in his memory ⇒ CallContact (the contact as target); his throwable
    /// mount's grenades falling ⇒ CallFragOut; a mount starting to reload ⇒ CallReloading; a squad member down ⇒ the live man nearest him
    /// calls CallManDown. Only men under a commander (a team) call.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class CalloutSystem : IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;
        private EntityQuery? _men;

        public CalloutSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository world || !world.IsComponentTypeRegistered<CalloutState>() || !world.IsComponentTypeRegistered<UnitSubordinate>()) return;
            if (world.Bus.IsRegistered<SensorChangedEvent>())
                foreach (var evt in world.ReadEvents<SensorChangedEvent>())
                    if (evt.What == SensorChange.SquadMemberDown) ManDown(world, evt.Unit, evt.Target);

            _men ??= world.Query().With<UnitSubordinate>().With<SimTransform>().With<BehaviorState>().Build();
            foreach (var man in _men)
            {
                if (_gateOnAuthority && !world.HasAuthority<BehaviorState>(man)) continue;
                if (world.GetComponentRO<UnitSubordinate>(man).Commander.IsNull) continue;
                if (!world.HasComponent<CalloutState>(man)) world.AddComponent(man, new CalloutState { Grenades = -1 });
                Watch(world, man);
            }
        }

        private static unsafe void Watch(EntityRepository world, Entity man)
        {
            ref var st = ref world.GetComponentRW<CalloutState>(man);
            int grenades = Grenades(world, man);
            bool reloading = world.HasComponent<WeaponAmmo>(man) && world.GetComponentRO<WeaponAmmo>(man).Busy != 0;
            var contact = NewContact(world, man, ref st);
            bool seeded = st.Seeded != 0;
            bool threw = seeded && st.Grenades > grenades;
            bool startedReload = seeded && reloading && st.WasReloading == 0;
            st.Grenades = grenades;
            st.WasReloading = reloading ? (byte)1 : (byte)0;
            st.Seeded = 1;
            if (threw) Callouts.Say(world, man, UtteranceKind.CallFragOut);
            else if (!contact.IsNull) Callouts.Say(world, man, UtteranceKind.CallContact, contact);
            else if (startedReload) Callouts.Say(world, man, UtteranceKind.CallReloading);
        }

        /// <summary>The first identified, armed, non-neutral enemy in his memory (seen now) that he has not called yet; remembered either way.</summary>
        private static unsafe Entity NewContact(EntityRepository world, Entity man, ref CalloutState st)
        {
            if (!world.HasComponent<TargetMemory>(man)) return Entity.Null;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(man);
            for (int i = 0; i < mem.Count; i++)
            {
                if (TargetMemory.IsAnonymous(in mem, i) || st.Called(mem.EntityIds[i])) continue;
                var e = new Entity((ulong)mem.EntityIds[i]);
                if (!world.IsAlive(e) || ThreatDanger.IsNeutral(world, e) || ThreatDanger.IsOutOfTheFight(world, e)) continue;
                if (!SightNow.Sees(world, man, e)) continue;
                st.Remember(mem.EntityIds[i]);
                return st.Seeded != 0 ? e : Entity.Null;   // the first look only learns what is already known
            }
            return Entity.Null;
        }

        private static int Grenades(EntityRepository world, Entity man)
            => Fdp.Toolkit.Combat.HiddenTargetThrow.ThrowableMount(world, man, out var mount, out _, out _, out _)
               && world.HasComponent<WeaponState>(mount) ? world.GetComponentRO<WeaponState>(mount).Ammo : 0;

        /// <summary>A squad member down: the live member nearest him calls it.</summary>
        private static void ManDown(EntityRepository world, Entity commander, Entity down)
        {
            if (!world.HasComponent<UnitRoster>(commander) || !world.HasComponent<SimTransform>(down)) return;
            var at = world.GetComponentRO<SimTransform>(down).Position;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(commander);
            Entity best = Entity.Null;
            float bestD = float.MaxValue;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || m == down || !world.IsAlive(m) || !world.HasComponent<SimTransform>(m) || !Fdp.Toolkit.Squad.CombatEffective.IsInTheFight(world, m)) continue;
                float d = Vector3.Distance(world.GetComponentRO<SimTransform>(m).Position, at);
                if (d < bestD) { bestD = d; best = m; }
            }
            if (!best.IsNull) Callouts.Say(world, best, UtteranceKind.CallManDown, down);
        }
    }
}
