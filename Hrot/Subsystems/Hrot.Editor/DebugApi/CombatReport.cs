using System.Collections.Generic;
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
                    ["hitPart"] = s.HitPart, ["hitFace"] = s.HitFace,   // ⭐ CE-3209 — the body part met and the face entered
                    // ⭐ CE-2180 — the trip from the brain: raised (brain's synced sim time, null when unknown) → fired here
                    ["raisedAt"] = s.RaisedAt > 0d ? s.RaisedAt : null, ["firedAt"] = s.FiredAt,
                    ["tripSeconds"] = s.RaisedAt > 0d ? s.FiredAt - s.RaisedAt : null, ["caughtUpSeconds"] = s.CaughtUpSeconds, ["owedFrames"] = s.OwedFrames, ["caughtUpFrames"] = s.CaughtUpFrames, ["frameDt"] = s.FrameDt,
                    ["crossings"] = crossings,
                });
            }
            return new JsonObject { ["count"] = log.Count, ["returned"] = arr.Count, ["shots"] = arr };
        }

        /// <summary>
        /// ⭐ Buildings Stage 6 (<c>CE-1032</c>, W-10) — <c>GET /combat/detonations?last=</c>: the last bursts with an area effect, as
        /// <c>AreaEffectSystem</c> decided them — the warhead and where its numbers came from, every entity in reach with its stance,
        /// fragment exposure, blast barrier/shadow and the damage each effect did, and the doors breached. Read from the
        /// <see cref="DetonationLog"/>, never recomputed.
        /// </summary>
        public static JsonNode Detonations(EntityRepository world, NetworkEntityMap? map, int last)
        {
            var log = DetonationLog.Peek(world);
            if (log == null)
                return new JsonObject { ["count"] = 0, ["detonations"] = new JsonArray(), ["note"] = "no warhead has burst on this node (the area effect runs where damage is assessed — SimHost / Editor)" };
            long? Id(Entity e) => map != null && map.TryGetNetworkId(e, out var id) ? id : null;
            var arr = new JsonArray();
            foreach (var d in log.Recent(last))
            {
                var effects = new JsonArray();
                foreach (var x in d.Effects)
                    effects.Add(new JsonObject
                    {
                        ["entity"] = Id(x.Entity), ["entityIndex"] = x.Entity.Index, ["stance"] = x.Stance, ["bodyPoints"] = x.BodyPoints,
                        ["distance"] = x.Distance,
                        ["fragmentExposure"] = x.FragmentExposure, ["fragmentFalloff"] = x.FragmentFalloff,
                        ["fragmentArmourChance"] = x.FragmentArmourChance, ["fragmentDamage"] = x.FragmentDamage,
                        ["blastFalloff"] = x.BlastFalloff, ["blastBarrier"] = x.BlastBarrier, ["blastShadow"] = x.BlastShadow,
                        ["blastSusceptibility"] = x.BlastSusceptibility,   // ⭐ R-2019 D-M2
                        ["fragmentHits"] = x.FragmentHits, ["target"] = x.Target.ToString(),   // ⭐ CE-3203
                        ["blastDamage"] = x.BlastDamage, ["totalDamage"] = x.TotalDamage, ["shieldedBy"] = x.ShieldedBy,
                        ["at"] = V(x.At),   // ⭐ CE-3117 — where it stood
                    });
                // ⭐ CE-3117 — every fragment ray the map draws: the body point, how much got through, the first obstacle (or null)
                var rays = new JsonArray();
                foreach (var r in d.Rays)
                    rays.Add(new JsonObject
                    {
                        ["to"] = V(r.To), ["transmission"] = r.Transmission, ["stopAt"] = r.StopAt is { } at ? V(at) : null,
                    });
                var doors = new JsonArray();
                foreach (var k in d.DoorsBreached) doors.Add((JsonNode)k!);
                arr.Add(new JsonObject
                {
                    ["seq"] = d.Seq, ["tick"] = d.Tick, ["shooter"] = Id(d.Shooter), ["struck"] = Id(d.Struck), ["burst"] = V(d.Burst),
                    ["ammo"] = d.Ammo, ["warhead"] = d.Warhead, ["warheadSource"] = d.WarheadSource,
                    ["fragmentRadius"] = d.FragmentRadius, ["blastInjuryRadius"] = d.BlastInjuryRadius,
                    ["effects"] = effects, ["doorsBreached"] = doors, ["rays"] = rays,
                });
            }
            return new JsonObject { ["count"] = log.Count, ["returned"] = arr.Count, ["detonations"] = arr };
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
                if (pt.Crossed is { } list)   // ⭐ CE-1035 Q2 — the world query's crossings (along = metres, as before)
                    foreach (var c in list)
                        crossed.Add(new JsonObject
                        {
                            ["along"] = c.T * Vector3.Distance(x.Eye, pt.Aim), ["kind"] = c.Kind, ["label"] = c.Label, ["material"] = c.Material,
                            ["transmittance"] = c.Loss, ["building"] = c.Building, ["storey"] = c.Storey,
                        });
                points.Add(new JsonObject
                {
                    ["height"] = pt.Height, ["aim"] = V(pt.Aim), ["clear"] = pt.Clear, ["verdict"] = pt.Verdict,
                    ["terrainTransmittance"] = pt.TerrainTransmittance, ["crossed"] = crossed,
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

        /// <summary>
        /// ⭐ AQ106 L (R-3050) — <c>GET /perception/hearing?listener=&amp;seconds=</c>: every sound near a listener in the last seconds as THIS
        /// node's hearing sensor judges it — heard, or why not, the straight line's loss, the way round (through which doorways and corners) and
        /// where it seems to come from. A dry run of the sensor's own ears and verdict (<c>HearingExplain</c>), so it cannot disagree with it.
        /// </summary>
        public static JsonNode Hearing(EntityRepository world, NetworkEntityMap? map, long listener, double seconds)
        {
            if (map == null || !map.TryGetEntity(listener, out var u) || !world.IsAlive(u))
                return new JsonObject { ["error"] = $"listener {listener} is not a live entity on this node" };
            var list = new List<Fdp.Toolkit.Perception.Sensors.HeardSoundExplanation>();
            if (!Fdp.Toolkit.Perception.Sensors.HearingExplain.TryExplain(world, u, seconds, list, out var why))
                return new JsonObject { ["error"] = why };
            var sounds = new JsonArray();
            int heard = 0;
            foreach (var x in list)
            {
                var v = x.Verdict;
                if (v.Reason == Fdp.Toolkit.Perception.Sensors.HearingReason.Heard) heard++;
                var route = new JsonArray();
                foreach (var p in x.Waypoints) route.Add(V(p));
                long? src = map.TryGetNetworkId(new Entity((int)(x.Source & 0xFFFFFFFF), (ushort)(x.Source >> 32)), out var id) ? id : null;
                sounds.Add(new JsonObject
                {
                    ["source"] = src, ["kind"] = x.Voice ? "Voice" : x.Kind.ToString(),
                    ["class"] = ((Fdp.Toolkit.Tkb.Domain.SoundSourceClass)x.Class).ToString(), ["at"] = V(x.At), ["ageSeconds"] = x.Age,
                    ["carry"] = x.Carry, ["distance"] = v.Distance, ["effectiveCarry"] = v.EffectiveCarry,
                    ["verdict"] = v.Reason.ToString(), ["via"] = v.Route.Via.ToString(), ["heardFrom"] = V(v.Route.HeardFrom),
                    ["straightDb"] = v.Route.StraightDb, ["crossings"] = v.Route.Crossings, ["pathMetres"] = v.Route.PathMetres,
                    ["pathDb"] = v.Route.PathDb, ["openings"] = v.Route.Openings, ["corners"] = v.Route.Corners, ["route"] = route,
                });
            }
            return new JsonObject
            {
                ["listener"] = listener, ["seconds"] = seconds, ["count"] = list.Count, ["heard"] = heard, ["sounds"] = sounds,
                ["note"] = "A dry run of the hearing sensor (AQ106 L): verdict Heard | BeyondItsCarry | BeyondHisEar | WallsOrCorners | HisOwnSide | Silent. via Straight = through the walls (a halving each); Openings = through doors / windows / stairs; AroundBuildings = round the street corners. heardFrom is where it seems to come from (the last opening or corner).",
            };
        }

        private static JsonArray V(Vector3 v) => new(v.X, v.Y, v.Z);
    }
}
