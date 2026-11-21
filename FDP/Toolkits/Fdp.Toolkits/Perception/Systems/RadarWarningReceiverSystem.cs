using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Radar;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-4617</c> (B-4, RS-6 / RS-10 / RV-8; docs/designs/air-attack/DESIGN_Radar_SAM.md) — an aircraft's RADAR WARNING RECEIVER, on the
    /// node that runs its brain: DERIVED from every emitter's replicated <see cref="RadarEmission"/> — nobody writes another node's aircraft.
    /// <b>Tracked</b> when an emitter of another side holds it (<see cref="RadarEmission.TrackedTarget"/> is this aircraft, inside the emitter's
    /// range); else <b>searched</b> when an emitter of another side radiates with the aircraft inside its range and in its line of sight (the
    /// emitter's antenna = its sight height; beyond the terrain the world answers its own edge rule). Written as the aircraft's
    /// <see cref="RadarWarning"/> ONLY on a change (the level or the emitter) — the chunk version (and the recorder) moves only then.
    /// Copies <see cref="MissileWarningSensingSystem"/>'s place: the CGF pack's simulation list, before the brain tick.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class RadarWarningReceiverSystem : IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;
        private EntityQuery? _emitters, _aircraft;
        private EntityRepository? _queriesFor;
        [Fdp.Core.FrameScratch("the radiating radars of this run: cleared at the top and filled from this frame's world")]
        private readonly List<(Entity E, Vector3 Antenna, ForceId Force, float Range, Entity Tracked)> _on = new();

        public RadarWarningReceiverSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<RadarEmission>()
                || !repo.IsComponentTypeRegistered<RadarWarning>() || !repo.IsComponentTypeRegistered<EntityInfo>()) return;
            if (!ReferenceEquals(_queriesFor, repo))
            {
                _emitters = repo.Query().With<RadarEmission>().With<SimTransform>().With<EntityInfo>().Build();
                _aircraft = repo.Query().With<SimTransform>().With<EntityInfo>().Build();
                _queriesFor = repo;
            }
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0.0;

            _on.Clear();
            foreach (var e in _emitters!)
            {
                var r = repo.GetComponentRO<RadarEmission>(e);
                if (!RadarEmissions.Radiates(repo, e, in r) || r.RangeM <= 0f) continue;
                var at = repo.GetComponentRO<SimTransform>(e).Position;
                var antenna = at + new Vector3(0f, 0f, Fdp.Toolkit.Spatial.Eqs.EqsTerrainSight.Mount(repo, e).Standing);
                _on.Add((e, antenna, repo.GetComponentRO<EntityInfo>(e).ForceId, r.RangeM, r.Mode == RadarMode.Track ? r.TrackedTarget : Entity.Null));
            }

            Fdp.Toolkit.World.IWorldQuery? world = null;
            bool worldAsked = false;
            foreach (var a in _aircraft!)
            {
                if (!Fdp.Toolkit.Motion.Locomotions.IsAircraft(repo, a)) continue;
                if (_gateOnAuthority && !(repo.HasComponent<BehaviorState>(a) && repo.HasAuthority<BehaviorState>(a))) continue;
                bool had = repo.HasComponent<RadarWarning>(a);
                if (_on.Count == 0 && (!had || repo.GetComponentRO<RadarWarning>(a).Level == RadarWarningLevel.None)) continue;

                var me = repo.GetComponentRO<SimTransform>(a).Position;
                var force = repo.GetComponentRO<EntityInfo>(a).ForceId;
                var level = RadarWarningLevel.None;
                int from = -1;
                float nearest = float.MaxValue;
                for (int i = 0; i < _on.Count; i++)
                {
                    var o = _on[i];
                    if (o.Force == force || o.E == a) continue;
                    float d = Vector3.Distance(o.Antenna, me);
                    if (d > o.Range) continue;
                    if (o.Tracked == a) { if (level != RadarWarningLevel.Tracked || d < nearest) { level = RadarWarningLevel.Tracked; from = i; nearest = d; } continue; }
                    if (level == RadarWarningLevel.Tracked) continue;
                    if (!worldAsked) { world = Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.LiveWorldQuery(repo)(repo); worldAsked = true; }
                    if (world != null && world.SightBlocked(o.Antenna, me)) continue;
                    if (d < nearest) { level = RadarWarningLevel.Searched; from = i; nearest = d; }
                }

                var emitter = from >= 0 ? _on[from].E : Entity.Null;
                if (had)
                {
                    var was = repo.GetComponentRO<RadarWarning>(a);
                    if (was.Level == level && was.Emitter == emitter) continue;
                }
                else if (level == RadarWarningLevel.None) continue;
                var w = new RadarWarning { Level = level, Emitter = emitter, Since = (SimInstant)now };
                if (from >= 0)
                {
                    var src = _on[from].Antenna;
                    w.From = src;
                    float bearing = MathF.Atan2(src.X - me.X, src.Y - me.Y) * (180f / MathF.PI);
                    w.BearingDeg = bearing < 0f ? bearing + 360f : bearing;
                }
                if (had) repo.GetComponentRW<RadarWarning>(a) = w;
                else repo.AddComponent(a, w);
            }
        }
    }
}
