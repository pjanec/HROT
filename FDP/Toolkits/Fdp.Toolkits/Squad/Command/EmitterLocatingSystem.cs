using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Radar;

namespace Fdp.Toolkit.Squad.Command
{
    /// <summary>
    /// ⭐⭐ <c>CE-4617</c> (B-5, RS-7; docs/designs/air-attack/DESIGN_Radar_SAM.md) — the side's DIRECTION FINDING, derived from every emitter's
    /// replicated <see cref="RadarEmission"/>: a radar radiating CONTINUOUSLY for <see cref="ContinuousSeconds"/> is located, and a SPOT
    /// (<see cref="ElementEquipment.AirDefence"/>, its <see cref="CommandReport.Radius"/> the location's error — <see cref="FirstRadiusM"/>
    /// shrinking to <see cref="BestRadiusM"/> while it keeps radiating) goes to the TOP commander (the one with no superior) of every other
    /// side, every <see cref="ResendSeconds"/> while it stays on. 🔒 User: <i>"could be sent to the highest level commander entity?"</i>
    /// <para>The report is the ordinary <see cref="SquadReportNotification"/> (marked synthetic — no staff sent it), from the commander's first
    /// subordinate, so the commander's <c>StaffInboxSystem</c> folds it into its <c>CommandPicture</c> like any SPOT. Raised once per cluster:
    /// on the node that runs the top commander's brain (<c>gateOnAuthority</c>). Switched off, the clock restarts (<see cref="RadarEmission.OnSince"/>).</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class EmitterLocatingSystem : IEcsModuleSystem
    {
        /// <summary>How long a radar must radiate without a break before it is located (s).</summary>
        public const float ContinuousSeconds = 30f;
        /// <summary>The first location's error (m), and the best it refines to (m), at <see cref="RefineMetresPerSecond"/>.</summary>
        public const float FirstRadiusM = 200f, BestRadiusM = 50f, RefineMetresPerSecond = 2.5f;
        /// <summary>A located radar is re-reported this often while it stays on (s).</summary>
        public const float ResendSeconds = 10f;

        private const int ErrorSalt = 0x4617;
        private readonly bool _gateOnAuthority;
        private EntityQuery? _emitters, _commanders;
        private EntityRepository? _queriesFor;
        private readonly Dictionary<Entity, SimInstant> _sent = new();
        private readonly List<Entity> _tops = new();

        public EmitterLocatingSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        /// <summary>The location's error after <paramref name="onSeconds"/> of continuous emission (m).</summary>
        public static float RadiusAfter(double onSeconds)
            => MathF.Max(BestRadiusM, FirstRadiusM - RefineMetresPerSecond * (float)Math.Max(0.0, onSeconds - ContinuousSeconds));

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<RadarEmission>() || !repo.IsComponentTypeRegistered<EntityInfo>()
                || !repo.IsComponentTypeRegistered<UnitRoster>() || !repo.Bus.IsRegisteredManaged<SquadReportNotification>()) return;
            if (!ReferenceEquals(_queriesFor, repo))
            {
                _emitters = repo.Query().With<RadarEmission>().With<SimTransform>().With<EntityInfo>().Build();
                _commanders = repo.Query().With<UnitRoster>().With<EntityInfo>().Build();
                _queriesFor = repo;
                _sent.Clear();
            }
            SimInstant now = repo.HasSingleton<GlobalTime>() ? (SimInstant)repo.GetSingleton<GlobalTime>().TotalTime : SimInstant.Zero;
            bool topsFound = false;
            foreach (var e in _emitters!)
            {
                var r = repo.GetComponentRO<RadarEmission>(e);
                if (!RadarEmissions.Radiates(repo, e, in r)) continue;
                double on = now - r.OnSince;
                if (on < ContinuousSeconds) continue;
                if (_sent.TryGetValue(e, out var last) && last >= r.OnSince && now - last < ResendSeconds) continue;
                if (!topsFound) { FindTops(repo); topsFound = true; }
                var force = repo.GetComponentRO<EntityInfo>(e).ForceId;
                var at = repo.GetComponentRO<SimTransform>(e).Position;
                float radius = RadiusAfter(on);
                // the located point: off the truth by half the error, in a direction fixed for this emission (seeded by the emitter and its OnSince)
                var rng = Fdp.Toolkit.Determinism.SimRng.FromSim(e.Index, ErrorSalt, (float)(double)r.OnSince);
                float angle = rng.NextSingle() * 2f * MathF.PI;
                var located = at + new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * (0.5f * radius);
                bool any = false;
                for (int i = 0; i < _tops.Count; i++)
                {
                    var top = _tops[i];
                    var side = repo.GetComponentRO<EntityInfo>(top).ForceId;
                    if (side == force || side == ForceId.Neutral) continue;
                    if (_gateOnAuthority && !(repo.HasComponent<BehaviorState>(top) && repo.HasAuthority<BehaviorState>(top))) continue;
                    repo.Bus.PublishManaged(new SquadReportNotification
                    {
                        FromUnit = SenderOf(repo, top), ToUnit = top, SentAt = now,
                        Report = new CommandReport
                        {
                            Kind = ReportKind.Spot, At = located, Radius = radius, Count = 1, Size = ElementSize.Team,
                            Activity = ElementActivity.Static, Equipment = ElementEquipment.AirDefence | ElementEquipment.Vehicle,
                            ObservedAt = now, Synthetic = 1,
                        },
                    });
                    any = true;
                }
                if (any || !_sent.ContainsKey(e)) _sent[e] = now;
            }
        }

        /// <summary>The commanders with no superior (the top of each chain).</summary>
        private void FindTops(EntityRepository repo)
        {
            _tops.Clear();
            bool subs = repo.IsComponentTypeRegistered<UnitSubordinate>();
            foreach (var c in _commanders!)
            {
                if (subs && repo.HasComponent<UnitSubordinate>(c))
                {
                    var up = repo.GetComponentRO<UnitSubordinate>(c).Commander;
                    if (!up.IsNull && repo.IsAlive(up)) continue;
                }
                _tops.Add(c);
            }
        }

        /// <summary>Who the SPOT is from: the commander's first live subordinate (a sender of its side), else itself.</summary>
        private static Entity SenderOf(EntityRepository repo, Entity top)
        {
            ref readonly var roster = ref repo.GetComponentRO<UnitRoster>(top);
            for (int i = 0; i < roster.Count; i++)
                if (!roster.SubordinateEntities[i].IsNull && repo.IsAlive(roster.SubordinateEntities[i])) return roster.SubordinateEntities[i];
            return top;
        }
    }
}
