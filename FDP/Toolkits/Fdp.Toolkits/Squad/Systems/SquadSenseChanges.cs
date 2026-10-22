using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Squad.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-2283</c> slice M-1 (DA-21, SC-1…SC-5) — the four sensor changes a fighting position needs, raised where the squad's changes
    /// already are (<see cref="SquadCoordinationSystem"/>, after its merge) so a play READS them instead of counting them itself:
    /// <see cref="SensorChange.ContactInEngagementArea"/> (commander), <see cref="SensorChange.ContactOutsideSector"/>,
    /// <see cref="SensorChange.PositionUntenable"/> and <see cref="SensorChange.SniperFire"/> (the man; the sniper one on his commander too).
    /// The per-man memory (rounds in the window, the contact already answered) is held here, by the driver — ⚠ it restarts when authority
    /// moves to another node (the counts are seconds long). 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §14a.
    /// </summary>
    public sealed class SquadSenseChanges
    {
        /// <summary>SC-3 — a seen contact this close to a man's arc origin, outside his arc, flanks him (m).</summary>
        public const float FlankedMetres = 50f;
        /// <summary>SC-4 — this many rounds at a man within <see cref="UntenableSeconds"/> ⇒ untenable.</summary>
        public const int UntenableRounds = 3;
        public const float UntenableSeconds = 10f;
        /// <summary>SC-4 — a squad friend going down this close to a man makes his place untenable (m).</summary>
        public const float FriendDownMetres = 3f;
        /// <summary>SC-5 — nothing seen and no shot heard within this of the man ⇒ the round came from beyond: sniper fire (m).</summary>
        public const float SniperMetres = 250f;
        /// <summary>⭐ SC-8 (M-2) — the commander's WorldSurvey site for "where did that come from"; a point placed in a building by this many
        /// of the nine asked makes it fire from a structure; one ask per commander per <see cref="StructureAskSeconds"/>; an unanswered ask is
        /// dropped after <see cref="StructureTimeoutSeconds"/>; the source is looked for within <see cref="SourceMetres"/> of the man.</summary>
        public const int StructureSite = 0x22830001;
        public const int StructureVotes = 5;
        public const float StructureAskSeconds = 10f, StructureTimeoutSeconds = 5f, SourceMetres = 300f;

        private const int PoolCapacity = 16;
        /// <summary><c>Tkb.Domain.SoundSourceClass</c> SmallArms / HeavyWeapon — a shot, not footsteps or an engine.</summary>
        private const byte SmallArms = 4, HeavyWeapon = 5;

        private sealed class MemberSense
        {
            public double WindowStart = double.NegativeInfinity;
            public int Rounds;
            public long Flanker;
            public bool Down;
        }

        private sealed class UnitSense
        {
            public readonly HashSet<long> Inside = new();
            public readonly HashSet<long> Scratch = new();
            public bool HeardInside;
            /// <summary>SC-8 — the point survey asked (−∞ = none), where the fire came from, and when the last was asked.</summary>
            public double AskedAt = double.NegativeInfinity, LastAsk = double.NegativeInfinity;
            public Vector3 Source;
        }

        private readonly Dictionary<ulong, MemberSense> _members = new();
        private readonly Dictionary<ulong, UnitSense> _units = new();

        /// <summary>SC-2 / SC-3 / SC-4 (a friend down) — the per-squad changes, from the fresh pool and the roster.</summary>
        public void Squad(EntityRepository repo, Entity commander, double now)
        {
            ref readonly var squad = ref repo.GetComponentRO<SquadCognitiveState>(commander);
            int count = Math.Min(squad.Contacts.Count, PoolCapacity);
            var pool = MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), PoolCapacity)[..count];
            InEngagementArea(repo, commander, pool);
            Structure(repo, commander, now);
            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(commander);
            for (int i = 0; i < roster.Count && i < 16; i++)
            {
                var man = roster.SubordinateEntities[i];
                if (man.IsNull || !repo.IsAlive(man)) continue;
                var sense = Of(man);
                if (!Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, man))
                {
                    if (!sense.Down) { sense.Down = true; FriendDown(repo, in roster, man, now); }
                    continue;
                }
                sense.Down = false;
                OutsideSector(repo, man, sense, pool);
            }
        }

        /// <summary>SC-4 (rounds) and SC-5 — the per-man changes from last frame's Hit / NearMiss edges.</summary>
        public void Rounds(EntityRepository repo, bool gateOnAuthority, double now)
        {
            foreach (var evt in repo.ReadEvents<SensorChangedEvent>())
            {
                if (evt.What != SensorChange.Hit && evt.What != SensorChange.NearMiss) continue;
                var man = evt.Unit;
                if (!repo.IsAlive(man) || !Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, man)) continue;   // a bleeding man's own drops are no rounds
                var commander = repo.HasComponent<UnitSubordinate>(man) ? repo.GetComponentRO<UnitSubordinate>(man).Commander : Entity.Null;
                if (commander.IsNull || !repo.IsAlive(commander) || !repo.HasComponent<SquadCognitiveState>(commander)) continue;
                if (gateOnAuthority && (!repo.HasComponent<BehaviorState>(commander) || !repo.HasAuthority<BehaviorState>(commander))) continue;
                var sense = Of(man);
                if (now - sense.WindowStart > UntenableSeconds) { sense.WindowStart = now; sense.Rounds = 0; }
                if (++sense.Rounds >= UntenableRounds)
                {
                    sense.Rounds = 0;
                    sense.WindowStart = double.NegativeInfinity;
                    Publish(repo, man, SensorChange.PositionUntenable);
                }
                if (FromBeyond(repo, man))
                {
                    Publish(repo, man, SensorChange.SniperFire);
                    Publish(repo, commander, SensorChange.SniperFire);
                }
                AskStructure(repo, commander, man, now);
            }
        }

        /// <summary>SC-2 — every pooled contact newly inside the commander's Allow (its geometry), once; a heard one on the edge "none heard inside → one".</summary>
        private void InEngagementArea(EntityRepository repo, Entity commander, ReadOnlySpan<SquadContact> pool)
        {
            var unit = Of(commander.PackedValue);
            if (!EngagementAreas.TryAllow(repo, commander, out var area)) { unit.Inside.Clear(); unit.HeardInside = false; return; }
            unit.Scratch.Clear();
            bool heard = false;
            foreach (ref readonly var c in pool)
            {
                if (!EngagementAreas.Contains(repo, in area, new Vector3(c.PositionX, c.PositionY, c.PositionZ))) continue;
                if ((c.Flags & SquadContact.AnonymousFlag) != 0) { heard = true; continue; }
                unit.Scratch.Add(c.EntityId);
                if (!unit.Inside.Contains(c.EntityId))
                    repo.Bus.Publish(new SensorChangedEvent { Unit = commander, What = SensorChange.ContactInEngagementArea, Target = new Entity((ulong)c.EntityId) });
            }
            if (heard && !unit.HeardInside) Publish(repo, commander, SensorChange.ContactInEngagementArea);
            unit.HeardInside = heard;
            unit.Inside.Clear();
            unit.Inside.UnionWith(unit.Scratch);
        }

        /// <summary>SC-3 — a seen hostile within <see cref="FlankedMetres"/> of the man's arc origin and outside his arc; once per contact.</summary>
        private static void OutsideSector(EntityRepository repo, Entity man, MemberSense sense, ReadOnlySpan<SquadContact> pool)
        {
            if (!EngagementAreas.TryArc(repo, man, out var arc)) return;
            foreach (ref readonly var c in pool)
            {
                if (c.EntityId == sense.Flanker || !Flanks(repo, in arc, in c)) continue;
                sense.Flanker = c.EntityId;
                repo.Bus.Publish(new SensorChangedEvent { Unit = man, What = SensorChange.ContactOutsideSector, Target = new Entity((ulong)c.EntityId) });
                return;
            }
        }

        /// <summary>⭐ SC-3 — THE flank rule (the sensor raises on it; a play that must know WHICH contact flanked a man asks it again): a SEEN,
        /// living, hostile pooled contact within <see cref="FlankedMetres"/> of the arc's origin and outside the arc.</summary>
        public static bool Flanks(Fdp.ModuleHost.Abstractions.ISimulationView repo, in EngagementArea arc, in SquadContact c)
        {
            if ((c.Flags & SquadContact.AnonymousFlag) != 0) return false;
            var at = new Vector3(c.PositionX, c.PositionY, c.PositionZ);
            if (Vector3.Distance(at, arc.ArcOrigin) > FlankedMetres
                || EngagementAreas.InArc(arc.ArcOrigin, arc.LeftBearing, arc.RightBearing, arc.MinRange, arc.MaxRange, at)) return false;
            var contact = new Entity((ulong)c.EntityId);
            return repo.IsAlive(contact) && !ThreatDanger.IsOutOfTheFight(repo, contact) && !ThreatDanger.IsNeutral(repo, contact);
        }

        /// <summary>SC-4 — <paramref name="down"/> just went down: every squad friend still in the fight within <see cref="FriendDownMetres"/>.</summary>
        private static void FriendDown(EntityRepository repo, in UnitRoster roster, Entity down, double now)
        {
            if (!repo.HasComponent<SimTransform>(down)) return;
            var at = repo.GetComponentRO<SimTransform>(down).Position;
            for (int i = 0; i < roster.Count && i < 16; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || m == down || !repo.IsAlive(m) || !repo.HasComponent<SimTransform>(m)
                    || !Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, m)) continue;
                if (Vector3.Distance(repo.GetComponentRO<SimTransform>(m).Position, at) <= FriendDownMetres)
                    Publish(repo, m, SensorChange.PositionUntenable);
            }
        }

        /// <summary>SC-5 — nothing the man SEES within <see cref="SniperMetres"/>, and no shot he HEARD centred within it.</summary>
        private static unsafe bool FromBeyond(EntityRepository repo, Entity man)
        {
            if (!repo.HasComponent<SimTransform>(man) || !repo.HasComponent<TargetMemory>(man)) return false;
            var me = repo.GetComponentRO<SimTransform>(man).Position;
            ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(man);
            fixed (TargetMemory* m = &mem)
            {
                for (int i = 0; i < m->Count; i++)
                {
                    bool heard = m->Anonymous[i] != 0;
                    if (heard && m->SourceClass[i] != SmallArms && m->SourceClass[i] != HeavyWeapon) continue;
                    if (Vector2.Distance(new Vector2(m->PositionsX[i], m->PositionsY[i]), new Vector2(me.X, me.Y)) <= SniperMetres) return false;
                }
            }
            return true;
        }

        /// <summary>⭐ SC-8 — under fire: where the man places the shooter (the nearest heard SHOT within <see cref="SourceMetres"/>, its error
        /// circle; else the nearest seen contact) asked as ONE point survey — the circle's centre and eight points on its rim.</summary>
        private void AskStructure(EntityRepository repo, Entity commander, Entity man, double now)
        {
            var unit = Of(commander.PackedValue);
            if (now - unit.LastAsk < StructureAskSeconds || !Source(repo, man, out var at, out float radius)) return;
            var points = new List<Vector3>(9) { at };
            float r = MathF.Max(2f, radius);
            for (int k = 0; k < 8; k++) points.Add(at + new Vector3(MathF.Cos(k * MathF.PI / 4f) * r, MathF.Sin(k * MathF.PI / 4f) * r, 0f));
            var request = Fdp.Toolkit.Perception.Survey.WorldSurveyRequest.OfPoints(points);
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(repo, commander, StructureSite);
            if (child.IsNull) child = Fdp.Toolkit.Perception.Survey.WorldSurveyChildSensor.Ensure(repo, commander, StructureSite, request);
            else Fdp.Toolkit.Perception.Survey.WorldSurveyChildSensor.Configure(repo, child, request);
            if (child.IsNull) return;
            unit.AskedAt = unit.LastAsk = now;
            unit.Source = at;
        }

        /// <summary>⭐ SC-8 — the answer: ≥ <see cref="StructureVotes"/> of the nine points in one building ⇒
        /// <see cref="SensorChange.FiredOnFromStructure"/> on the commander and a CONTACT report up (the source, firing).</summary>
        private void Structure(EntityRepository repo, Entity commander, double now)
        {
            var unit = Of(commander.PackedValue);
            if (double.IsNegativeInfinity(unit.AskedAt)) return;
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(repo, commander, StructureSite);
            var read = Fdp.Toolkit.Perception.Survey.WorldSurveyChildSensor.Read(repo, child, now);
            if (!read.Ready || read.Payload == null || read.Payload.Kind != Fdp.Toolkit.Perception.Survey.WorldSurveyKind.PointSurvey)
            {
                if (now - unit.AskedAt > StructureTimeoutSeconds) unit.AskedAt = double.NegativeInfinity;
                return;
            }
            unit.AskedAt = double.NegativeInfinity;
            var votes = new Dictionary<int, int>();
            foreach (var pt in read.Payload.Points)
                if (pt.Building >= 0) votes[pt.Building] = votes.TryGetValue(pt.Building, out int v) ? v + 1 : 1;
            foreach (var (_, count) in votes)
            {
                if (count < StructureVotes) continue;
                Publish(repo, commander, SensorChange.FiredOnFromStructure);
                ReportUp(repo, commander, unit.Source, (float)now);
                return;
            }
        }

        /// <summary>The CONTACT the squad sends up: an element firing at <paramref name="at"/> (its staff delay applies).</summary>
        private static void ReportUp(EntityRepository repo, Entity commander, Vector3 at, float now)
        {
            if (!repo.IsComponentTypeRegistered<Command.ReportOutbox>() || !repo.HasComponent<Command.ReportOutbox>(commander)) return;
            Command.ReportQueue.Enqueue(repo, commander, new Command.CommandReport
            {
                Kind = Command.ReportKind.Contact, At = at, Count = 1, Size = Command.ElementSize.Individual,
                Activity = Command.ElementActivity.Firing, ObservedAt = now,
            }, now + Command.StaffComposerSystem.ReportDelaySeconds);
        }

        /// <summary>Where the man places the shooter: the nearest heard SHOT within <see cref="SourceMetres"/> (its circle), else the nearest
        /// seen contact.</summary>
        private static unsafe bool Source(EntityRepository repo, Entity man, out Vector3 at, out float radius)
        {
            at = default; radius = 0f;
            if (!repo.HasComponent<SimTransform>(man) || !repo.HasComponent<TargetMemory>(man)) return false;
            var me = repo.GetComponentRO<SimTransform>(man).Position;
            ref readonly var mem = ref repo.GetComponentRO<TargetMemory>(man);
            float bestHeard = SourceMetres, bestSeen = SourceMetres;
            Vector3 heardAt = default, seenAt = default;
            float heardRadius = 0f;
            fixed (TargetMemory* m = &mem)
            {
                for (int i = 0; i < m->Count; i++)
                {
                    var p = new Vector3(m->PositionsX[i], m->PositionsY[i], m->PositionsZ[i]);
                    float d = Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(me.X, me.Y));
                    if (m->Anonymous[i] != 0)
                    {
                        if ((m->SourceClass[i] == SmallArms || m->SourceClass[i] == HeavyWeapon) && d < bestHeard)
                        { bestHeard = d; heardAt = p; heardRadius = m->Radius[i]; }
                    }
                    else if (d < bestSeen) { bestSeen = d; seenAt = p; }
                }
            }
            if (bestHeard < SourceMetres) { at = heardAt; radius = heardRadius; return true; }
            if (bestSeen < SourceMetres) { at = seenAt; return true; }
            return false;
        }

        private MemberSense Of(Entity man)
        {
            if (!_members.TryGetValue(man.PackedValue, out var s)) _members[man.PackedValue] = s = new MemberSense();
            return s;
        }

        private UnitSense Of(ulong commander)
        {
            if (!_units.TryGetValue(commander, out var s)) _units[commander] = s = new UnitSense();
            return s;
        }

        private static void Publish(EntityRepository repo, Entity unit, SensorChange what)
            => repo.Bus.Publish(new SensorChangedEvent { Unit = unit, What = what });
    }
}
