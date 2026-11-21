using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-3488</c> (flame FL-4 B-2; 📄 docs/designs/weapons/DESIGN_Flame_Weapons.md §7c; F-3 "a building panel of a burning material: 2 s") — what a lit flame jet
    /// sets alight among the BUILDINGS: for each unit whose jet is lit (<see cref="FlameJets.TryGet"/>, replicated), each burnable wall panel
    /// (<see cref="BuildingFires.BurnablePanels"/>) whose nearest point to the jet's axis lies in the <see cref="FlameCone"/>, with a clear sight line from the shooter's
    /// eye to just short of it, adds exposure to its building; at <see cref="Ignition.BuildingSeconds"/> the building is set alight (a <see cref="BuildingFireEvent"/>, the
    /// shared <see cref="BuildingFires"/> rule). Registered where the building record's writer runs (SimHost's <c>DamageAssessmentModule</c>), so the event never has to
    /// cross a node. ⛔ Never in a replay. Exposure is this system's own buffer (cleared on a new world, not recorded — as <see cref="FlameSystem"/>'s, F-12).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class StructureFlameSystem : IEcsModuleSystem
    {
        /// <summary>How far short of the wall the sight line ends (m) — the wall's own thickness would hide itself.</summary>
        public const float ShortOfWallM = 0.4f;

        private struct Exposure { public float Seconds; public double Last; }

        [Fdp.Core.WorldDerived("per-building flame exposure, cleared on a new world; not recorded by design (DESIGN_Flame_Weapons F-12, B-2)")]
        private readonly Dictionary<int, Exposure> _exposure = new();
        private EntityQuery? _throwers;
        [Fdp.Core.WorldDerived("the world epoch last seen")]
        private int _epoch = -1;

        /// <summary>Buildings this node set alight (rails, diagnostics).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of buildings set alight")]
        public long Ignited { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<WeaponPosture>() || !repo.IsComponentTypeRegistered<WeaponAmmo>()) return;
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _epoch) || _throwers == null)
            {
                _throwers = repo.Query().With<WeaponPosture>().With<WeaponAmmo>().With<SimTransform>().Build();
                _exposure.Clear();
            }
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view) || deltaTime <= 0f) return;
            if (!repo.Bus.IsRegistered<BuildingFireEvent>() || !repo.HasSingletonManaged<TerrainWorld>() || repo.GetSingletonManaged<TerrainWorld>() is not { } terrain) return;
            var panels = BuildingFires.BurnablePanels(terrain);
            if (panels.Count == 0) return;
            double now = HitModel.Now(repo);
            Fdp.Toolkit.Spatial.Eqs.ILosService? sight = null;
            bool sightAsked = false;

            foreach (var thrower in _throwers)
            {
                if (!FlameJets.TryGet(repo, thrower, now, out var jet)) continue;
                var origin = FlameJets.Origin(repo, thrower, in jet);   // ⭐ CE-4634 DR-5 — from the mouth / nozzle when the type places one
                var toAim = (Vector3)jet.AimPoint - origin;
                if (toAim.LengthSquared() < 1e-6f) continue;
                var dir = Vector3.Normalize(toAim);
                int lastBuilding = -1;
                for (int k = 0; k < panels.Count; k++)
                {
                    var p = terrain.Panels[panels[k]];
                    if (p.Building == lastBuilding) continue;   // this building already gained this tick
                    var at = NearestToAxis(p, origin, dir);
                    if (!FlameCone.Contains(origin, dir, jet.RangeM, jet.HalfAngleRad, at)) continue;
                    var d = at - origin;
                    float len = d.Length();
                    var end = len > ShortOfWallM ? origin + d * ((len - ShortOfWallM) / len) : origin;
                    if (!sightAsked) { sight = Fdp.Toolkit.Spatial.Eqs.EqsTerrainSight.Sight(repo, null); sightAsked = true; }
                    if (sight != null && !sight.HasLineOfSight(origin, end)) continue;
                    lastBuilding = p.Building;
                    if (Expose(p.Building, deltaTime * jet.Intensity, now) < Ignition.BuildingSeconds) continue;
                    if (!BuildingFires.TryOf(terrain, p.Building, now, jet.Intensity, out var fire)) continue;
                    repo.Bus.Publish(fire);
                    _exposure.Remove(p.Building);   // lit: a later stay counts again (the record keeps one fire at a time)
                    Ignited++;
                }
            }
        }

        /// <summary>The point of panel <paramref name="p"/> nearest the jet's axis (on the ground plane), at the axis's height there, clamped into the wall's height.</summary>
        public static Vector3 NearestToAxis(TerrainWallPanel p, Vector3 origin, Vector3 dir)
        {
            var o = new Vector2(origin.X, origin.Y);
            var d2 = new Vector2(dir.X, dir.Y);
            float dl = d2.Length();
            var ab = p.B - p.A;
            float s = 0f;
            if (dl > 1e-6f && ab.LengthSquared() > 1e-9f)
            {
                var u = d2 / dl;
                var n = new Vector2(-u.Y, u.X);   // the axis's normal: the panel point whose offset from the axis is least
                float da = Vector2.Dot(p.A - o, n), db = Vector2.Dot(p.B - o, n);
                s = MathF.Abs(da - db) > 1e-6f ? Math.Clamp(da / (da - db), 0f, 1f) : 0f;
            }
            var q = p.A + ab * s;
            float along = dl > 1e-6f ? Vector2.Dot(q - o, d2 / dl) : 0f;
            float z = origin.Z + (dl > 1e-6f ? dir.Z / dl * along : 0f);
            return new Vector3(q, Math.Clamp(z, p.BaseZ + 0.1f, p.TopZ - 0.1f));
        }

        private float Expose(int building, float amount, double now)
        {
            _exposure.TryGetValue(building, out var e);
            if (now - e.Last > Ignition.ExposureGapSeconds) e.Seconds = 0f;   // a new stay
            e.Seconds += amount;
            e.Last = now;
            _exposure[building] = e;
            return e.Seconds;
        }
    }
}
