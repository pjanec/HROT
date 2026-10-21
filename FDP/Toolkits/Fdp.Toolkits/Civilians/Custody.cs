using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;

namespace Fdp.Toolkit.Civilians
{
    /// <summary>⭐ <c>CE-3256</c> (BR-4, CB-19) — what a soldier does to a man in his custody. ⚠ Member names unique (one IDL scope).</summary>
    public enum CustodyVerb : byte
    {
        CustodyNone = 0,
        /// <summary>Search him (3 s, within reach, while he complies): every weapon he hides is put down at his feet.</summary>
        Search = 1,
        /// <summary>Detain him: out of the fight, held.</summary>
        Detain = 2,
        /// <summary>Restrain him: hands tied — he cannot fire; he can still walk.</summary>
        Restrain = 3,
        /// <summary>Let him go: Detained and Restrained cleared.</summary>
        ReleaseCustody = 4,
    }

    /// <summary>
    /// ⭐ <c>CE-3256</c> (BR-4; docs/DESIGN_Entity_Interactions.md §7.6) — "<see cref="By"/> does <see cref="Verb"/> to <see cref="Man"/>". Raised
    /// on any node (<see cref="CustodyRules.Ask"/>); applied by <see cref="CustodySystem"/> where the man's passenger list is owned — the
    /// interaction transport carries it there (kind 13, <c>Custody</c>).
    /// </summary>
    [EventId(CombatConstants.CustodyEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct CustodyEvent
    {
        public Entity Man;
        public Entity By;
        public CustodyVerb Verb;
        /// <summary>Set by the transport's ingress (it is never sent back out).</summary>
        public bool IsRemote;
    }

    /// <summary>
    /// ⭐ <c>CE-3256</c> (BR-4, CU-1…CU-6) — a man in CUSTODY: detained, restrained, searched, being searched. On the man, Brain group,
    /// replicated as <c>dtCustody</c> (148): every node reads him out of the fight. Written only by <see cref="CustodySystem"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Custody)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Custody
    {
        /// <summary>1 = detained: out of the fight, held.</summary>
        public byte Detained;
        /// <summary>1 = restrained: hands tied, cannot fire (still walks).</summary>
        public byte Restrained;
        /// <summary>1 = he has been searched (his hidden weapons are on the ground).</summary>
        public byte Searched;
        /// <summary>1 while a search runs (ui draws it).</summary>
        public byte Searching;
        /// <summary>Who searches him now (this node's entity). ⛔ Not on the wire.</summary>
        public Entity SearchedBy;
        /// <summary>When the running search ends (sim s). ⛔ Not on the wire.</summary>
        public double SearchUntil;
    }

    /// <summary>⭐ <c>CE-3256</c> — the asks and the readers of custody.</summary>
    public static class CustodyRules
    {
        /// <summary>⭐ The <c>dtCustody</c> ordinal (148) — FDP cannot see the NED enum, so it is stated here as <c>Embarkation</c> states its own
        /// (a rail pins the two).</summary>
        public const long CustodyDescriptorOrdinal = 148;

        /// <summary>The ownership key of <c>dtCustody</c>.</summary>
        public static readonly long CustodyKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(CustodyDescriptorOrdinal, 0);

        /// <summary>CU-2 — does this node apply custody to <paramref name="man"/>: it owns his <c>dtCustody</c> (Brain group — the node that owns his
        /// passenger list too). With no network authority (one node), always.</summary>
        public static bool Owns(EntityRepository world, Entity man)
            => world.IsAlive(man) && Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(world, man, CustodyKey);

        /// <summary>CU-3 — how close the soldier must be (m).</summary>
        public const float Reach = 1.5f;
        /// <summary>CU-3 — how long a search takes (s).</summary>
        public const double SearchSeconds = 3.0;

        /// <summary><paramref name="by"/> asks to do <paramref name="verb"/> to <paramref name="man"/>; false when the event is not registered here.</summary>
        public static bool Ask(EntityRepository world, Entity man, Entity by, CustodyVerb verb)
        {
            if (!world.Bus.IsRegistered<CustodyEvent>() || man.IsNull || verb == CustodyVerb.CustodyNone) return false;
            world.Bus.Publish(new CustodyEvent { Man = man, By = by, Verb = verb });
            return true;
        }

        /// <summary>Is <paramref name="man"/> detained (on any node)?</summary>
        public static bool IsDetained(ISimulationView view, Entity man) => Read(view, man, out var c) && c.Detained != 0;

        /// <summary>Is <paramref name="man"/> restrained (on any node)?</summary>
        public static bool IsRestrained(ISimulationView view, Entity man) => Read(view, man, out var c) && c.Restrained != 0;

        /// <summary>CU-5 — in custody: detained or restrained (out of the fight).</summary>
        public static bool InCustody(ISimulationView view, Entity man) => Read(view, man, out var c) && (c.Detained != 0 || c.Restrained != 0);

        /// <summary>CU-4 — does <paramref name="man"/> comply (hands up / on head — <c>ThreatDanger.ShowsHandsUp</c>) or is he restrained?</summary>
        public static bool Complies(ISimulationView view, Entity man)
            => Fdp.Toolkit.Perception.ThreatDanger.ShowsHandsUp(view, man) || IsRestrained(view, man);

        /// <summary>CU-3 — is <paramref name="by"/> alive and within <see cref="Reach"/> of <paramref name="man"/>?</summary>
        public static bool InReach(EntityRepository world, Entity man, Entity by)
            => !by.IsNull && world.IsAlive(by) && world.IsAlive(man) && world.HasComponent<SimTransform>(by) && world.HasComponent<SimTransform>(man)
               && Vector3.DistanceSquared(world.GetComponentRO<SimTransform>(by).Position, world.GetComponentRO<SimTransform>(man).Position) <= Reach * Reach;

        private static bool Read(ISimulationView view, Entity man, out Custody c)
        {
            c = default;
            if (man.IsNull || (view is EntityRepository r && !r.IsComponentTypeRegistered<Custody>())) return false;
            if (!view.IsAlive(man) || !view.HasComponent<Custody>(man)) return false;
            c = view.GetComponentRO<Custody>(man);
            return true;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3256</c> (BR-4; DESIGN_Entity_Interactions §7.6) — applies <see cref="CustodyEvent"/> where the man's passenger list is owned
    /// (<see cref="CustodyRules.Owns"/>, CU-2) and times a running search. In the <c>Interactions</c> capability on every node.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class CustodySystem : IEcsModuleSystem
    {
        /// <summary>Asks refused here (not in reach, not complying, nothing to apply) — rails and diagnostics read it.</summary>
        public long Refused { get; private set; }
        /// <summary>Searches broken off (the soldier left, the man stopped complying).</summary>
        public long Aborted { get; private set; }
        /// <summary>Searches finished.</summary>
        public long Searched { get; private set; }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository world || !world.IsComponentTypeRegistered<Custody>()) return;
            double now = HitModel.Now(world);
            if (world.Bus.IsRegistered<CustodyEvent>())
            {
                var asks = world.ReadEvents<CustodyEvent>();
                for (int i = 0; i < asks.Length; i++)
                    if (!Apply(world, asks[i], now)) Refused++;
            }
            foreach (var man in world.Query().With<Custody>().Build())
            {
                ref var c = ref world.GetComponentRW<Custody>(man);
                if (c.Searching == 0) continue;
                if (!CustodyRules.Owns(world, man)) continue;
                if (!CustodyRules.InReach(world, man, c.SearchedBy) || !CustodyRules.Complies(world, man))
                {
                    c.Searching = 0; c.SearchedBy = Entity.Null; Aborted++;
                    continue;
                }
                if (now < c.SearchUntil) continue;
                RevealHidden(world, man);
                c.Searching = 0; c.SearchedBy = Entity.Null; c.Searched = 1;
                Searched++;
            }
        }

        private static bool Apply(EntityRepository world, in CustodyEvent ask, double now)
        {
            var man = ask.Man;
            if (!CustodyRules.Owns(world, man)) return false;
            if (!world.HasComponent<Custody>(man)) world.AddComponent(man, default(Custody));
            ref var c = ref world.GetComponentRW<Custody>(man);
            switch (ask.Verb)
            {
                case CustodyVerb.Search:
                    if (c.Searching != 0 || !CustodyRules.InReach(world, man, ask.By) || !CustodyRules.Complies(world, man)) return false;
                    c.Searching = 1; c.SearchedBy = ask.By; c.SearchUntil = now + CustodyRules.SearchSeconds;
                    return true;
                case CustodyVerb.Detain:
                    if (!CustodyRules.InReach(world, man, ask.By) || !CustodyRules.Complies(world, man)) return false;
                    c.Detained = 1;
                    return true;
                case CustodyVerb.Restrain:
                    if (!CustodyRules.InReach(world, man, ask.By) || !(CustodyRules.Complies(world, man) || c.Detained != 0)) return false;
                    c.Restrained = 1;
                    return true;
                case CustodyVerb.ReleaseCustody:
                    c.Detained = 0; c.Restrained = 0;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>CU-3 — every thing in a CONCEALED station of <paramref name="man"/> is put down at his feet (seen, nobody's).</summary>
        private static void RevealHidden(EntityRepository world, Entity man)
        {
            if (!world.IsComponentTypeRegistered<PassengerBuffer>() || !world.HasComponent<PassengerBuffer>(man)) return;
            var buffer = world.GetComponentRO<PassengerBuffer>(man);   // a copy: Disembark rewrites the list
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
            {
                var thing = buffer.Passengers[i];
                if (thing.IsNull || !CarryStations.IsConcealed(world, thing)) continue;
                Embarkation.Disembark(world, thing, man);
            }
        }
    }
}
