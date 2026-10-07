using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ Tuning T-4 — <c>GET /combat/shots?last=&amp;shooter=&amp;target=</c> (📄 docs/DESIGN_Terrain_Combat_Tuning.md §4): the last rounds
    /// with the inputs they were fired with and why each ended — read from the <see cref="ShotLog"/> the combat systems write, never
    /// recomputed. Served where the fire chain runs (SimHost, Editor); a world that never fired says so.
    /// </summary>
    internal static class CombatReport
    {
        public static JsonNode Shots(EntityRepository world, NetworkEntityMap? map, int last, long? shooter, long? target)
        {
            var log = ShotLog.Peek(world);
            if (log == null)
                return new JsonObject { ["count"] = 0, ["shots"] = new JsonArray(), ["note"] = "no round has been fired on this node (the fire chain runs where combat is composed — SimHost / Editor)" };
            long? Id(Entity e) => map != null && map.TryGetNetworkId(e, out var id) ? id : null;
            var arr = new JsonArray();
            foreach (var s in log.Recent(ShotLog.Capacity))
            {
                if (arr.Count >= last) break;
                if (shooter.HasValue && Id(s.Shooter) != shooter) continue;
                if (target.HasValue && Id(s.Target) != target) continue;
                var crossings = new JsonArray();
                foreach (var c in s.Crossings)
                    crossings.Add(new JsonObject
                    {
                        ["kind"] = c.Kind, ["label"] = c.Label, ["material"] = c.Material, ["resistanceMmRha"] = c.ResistanceMmRha,
                        ["roundPenetrationMm"] = c.RoundPenetrationMm, ["chance"] = c.Chance, ["passed"] = c.Passed, ["at"] = V(c.At),
                    });
                arr.Add(new JsonObject
                {
                    ["seq"] = s.Seq, ["tick"] = s.Tick, ["endTick"] = s.Outcome == ShotOutcome.InFlight ? null : s.EndTick,
                    ["shooter"] = Id(s.Shooter), ["target"] = Id(s.Target), ["weaponIndex"] = s.WeaponIndex,
                    ["muzzle"] = V(s.Muzzle), ["aim"] = V(s.Aim),
                    ["ordinal"] = s.Ordinal, ["sigmaRad"] = s.Sigma, ["deflectionRad"] = s.Deflection,
                    ["penetration"] = s.Penetration, ["penetrationSource"] = s.PenetrationSource, ["damage"] = s.Damage,
                    ["outcome"] = s.Outcome.ToString(), ["end"] = s.EndPoint is { } p ? V(p) : null,
                    ["hit"] = s.Outcome == ShotOutcome.Hit ? Id(s.HitEntity) : null,
                    ["arrivingDamage"] = s.Outcome == ShotOutcome.Hit ? s.ArrivingDamage : null,
                    ["arrivingPenetration"] = s.Outcome == ShotOutcome.Hit ? s.ArrivingPenetration : null,
                    ["crossings"] = crossings,
                });
            }
            return new JsonObject { ["count"] = log.Count, ["returned"] = arr.Count, ["shots"] = arr };
        }

        /// <summary>
        /// ⭐ Tuning T-4 — <c>GET /perception/los?observer=&amp;target=</c>: the line of sight between two units as THIS node's perception
        /// decides it — eye and aim heights for the stances used, every terrain crossing with its transmittance, and any entity in
        /// the way. A dry run of the same <see cref="Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy"/> perception composes
        /// (<c>ForLiveWorld</c>), so it cannot disagree with it.
        /// </summary>
        public static JsonNode Los(EntityRepository world, NetworkEntityMap? map, long observer, long target)
        {
            if (map == null || !map.TryGetEntity(observer, out var o) || !world.IsAlive(o))
                return new JsonObject { ["error"] = $"observer {observer} is not a live entity on this node" };
            if (!map.TryGetEntity(target, out var t) || !world.IsAlive(t))
                return new JsonObject { ["error"] = $"target {target} is not a live entity on this node" };
            var strategy = Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.ForLiveWorld(world);
            strategy.BeginBatch(world);
            var x = strategy.Explain(world, o, t);
            var points = new JsonArray();
            foreach (var pt in x.Points)
            {
                var crossed = new JsonArray();
                if (pt.Terrain is { } tr)
                    foreach (var c in tr.Crossed)
                        crossed.Add(new JsonObject
                        {
                            ["along"] = c.Along, ["kind"] = c.Kind, ["label"] = c.Label, ["material"] = c.Material,
                            ["transmittance"] = c.Transmittance, ["building"] = c.Building, ["storey"] = c.Storey,
                        });
                points.Add(new JsonObject
                {
                    ["height"] = pt.Height, ["aim"] = V(pt.Aim), ["clear"] = pt.Clear, ["verdict"] = pt.Verdict,
                    ["terrainTransmittance"] = pt.Terrain?.Transmittance, ["crossed"] = crossed,
                    ["blockingEntity"] = pt.BlockingEntity is { } b ? (map.TryGetNetworkId(b, out var bid) ? bid : (long?)null) : null,
                });
            }
            return new JsonObject
            {
                ["observer"] = observer, ["target"] = target, ["visible"] = x.Visible, ["verdict"] = x.Verdict,
                ["eye"] = V(x.Eye), ["eyeHeight"] = x.EyeHeight,
                ["observerStance"] = x.ObserverStance.ToString(), ["targetStance"] = x.TargetStance.ToString(),
                ["threshold"] = Fdp.Toolkit.Terrain.TerrainWorld.SightThreshold, ["points"] = points,
                ["note"] = "The target is SEEN when ANY body point's line is clear (buildings Stage 4). Stances are the LOGICAL stance (the brain's StanceIntent); on a cluster SimHost it arrives with the stance wire (CE-2121 slice 2) — until then Standing.",
            };
        }

        private static JsonArray V(Vector3 v) => new(v.X, v.Y, v.Z);
    }
}
