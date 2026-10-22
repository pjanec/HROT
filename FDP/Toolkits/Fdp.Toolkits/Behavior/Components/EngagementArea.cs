using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Behavior.Components
{
    [InlineArray(EngagementArea.MaxNoFire)]
    public struct NoFireAreaSlots { private long _e0; }

    /// <summary>The NoFire areas' network ids (a fixed-list wrapper, CC-16: the entity dump prints it as an array).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NoFireAreaList
    {
        public int Count;
        public NoFireAreaSlots Items;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2282</c> DA-L (DA-20, DA-24, DA-26…DA-30) — a SECTOR, on a man or on a commander. <b>Allow</b> (what he may CHOOSE to
    /// shoot): an arc from <see cref="ArcOrigin"/> between two compass bearings and two ranges, and / or an area entity
    /// (<see cref="AllowArea"/>) — the union; none of them set = everything. <b>NoFire</b> (where a shot may NEVER end): area entities owned
    /// by THIS unit, honoured by every man under it. Read through <see cref="EngagementAreas"/> ONLY. Written only by
    /// <see cref="EngagementAreaSystem"/> from <see cref="SetEngagementAreaEvent"/> (the ROE twin). Areas are NETWORK ids (the
    /// <c>PirArea</c> precedent). Compass bearings: 0 = north (+Y), 90 = east (+X), the arc runs clockwise from left to right.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §13, §13a, §13c.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.EngagementArea)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct EngagementArea
    {
        public const int MaxNoFire = 4;

        /// <summary>1 = the arc below is set.</summary>
        public byte ArcSet;
        public Vector3 ArcOrigin;
        public float LeftBearing;
        public float RightBearing;
        public float MinRange;
        /// <summary>0 = no limit.</summary>
        public float MaxRange;
        /// <summary>An area entity's network id the man may shoot into (0 = none).</summary>
        public long AllowArea;
        /// <summary>Where he watches (compass degrees), when <see cref="WatchSet"/> — for the eyes (LD-3).</summary>
        public float WatchBearing;
        public byte WatchSet;
        public NoFireAreaList NoFire;
        /// <summary>Who set it — the origin gate, as <see cref="Roe"/>'s.</summary>
        public BehaviorOrigin SetBy;
    }

    /// <summary>
    /// ⭐ <c>CE-2282</c> DA-26 — a superior sets a man's (or a commander's) sector: the <c>SetRoeEvent</c> twin (CC-2's shape, R-2044).
    /// It REPLACES the whole set; all empty WITHDRAWS it. Applied by <see cref="EngagementAreaSystem"/>.
    /// </summary>
    [EventId(BehaviorConstants.EventId_SetEngagementArea)]
    [StructLayout(LayoutKind.Sequential)]
    public struct SetEngagementAreaEvent
    {
        public Entity Entity;
        /// <summary>Who orders it; unmarked reads as Operator.</summary>
        public BehaviorOrigin Origin;
        public byte ArcSet;
        public Vector3 ArcOrigin;
        public float LeftBearing;
        public float RightBearing;
        public float MinRange;
        public float MaxRange;
        public long AllowArea;
        public float WatchBearing;
        public byte WatchSet;
        public NoFireAreaList NoFire;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2282</c> DA-L — the ONE reader of sectors. <see cref="Allows"/> decides what a man may CHOOSE to shoot (the four fire
    /// picks); <see cref="IsNoFire"/> what he may never hit (the gates). Tiers (DA-29): a man's own <c>Allow</c> counts from Regular up,
    /// a commander's (the squad level) from Militia up, Untrained none; <c>NoFire</c> for everyone, always. Self-defence (DA-28): a
    /// contact within <see cref="SelfDefenceMetres"/> while he was hit or near-missed within <see cref="SelfDefenceSeconds"/>.
    /// </summary>
    public static class EngagementAreas
    {
        public const float SelfDefenceMetres = 50f;
        public const float SelfDefenceSeconds = 5f;

        /// <summary>May <paramref name="man"/> CHOOSE to shoot at <paramref name="point"/>: inside his own Allow (Regular+), inside his
        /// direct commander's Allow (Militia+), or in self-defence. No sector = yes.</summary>
        public static bool Allows(ISimulationView view, Entity man, Vector3 point)
        {
            if (!Registered(view)) return true;
            var tier = ProficiencyOf.Tier(view, man);
            if (tier <= ProficiencyTier.Untrained) return true;
            bool limited = false;
            if (tier >= ProficiencyTier.Regular && TryGet(view, man, out var own) && HasAllow(in own))
            {
                limited = true;
                if (InAllow(view, in own, point)) return true;
            }
            var commander = CommanderOf(view, man);
            if (!commander.IsNull && TryGet(view, commander, out var unit) && HasAllow(in unit))
            {
                limited = true;
                if (InAllow(view, in unit, point)) return true;
            }
            return !limited || SelfDefence(view, man, point);
        }

        /// <summary>An entity target: <see cref="Allows"/> at its position (an entity with no position is allowed).</summary>
        public static bool Allows(ISimulationView view, Entity man, Entity target)
            => !TryPosition(view, target, out var p) || Allows(view, man, p);

        /// <summary>The squad level (DA-24): may a COMMANDER's unit choose <paramref name="point"/> — inside its own Allow (Militia+).
        /// What <c>TopContact</c> asks for its members' Suppress orders.</summary>
        public static bool AllowsUnit(ISimulationView view, Entity commander, Vector3 point)
        {
            if (!Registered(view) || !TryGet(view, commander, out var a) || !HasAllow(in a)) return true;
            if (ProficiencyOf.Tier(view, commander) <= ProficiencyTier.Untrained) return true;
            return InAllow(view, in a, point);
        }

        /// <summary>⭐ <c>CE-2275</c> D-1 (DA-36) — the one point a unit's Allow is ABOUT: an area's centre (its outline's mean), else the arc's
        /// middle bearing at the middle of its range band (100 m when it has no limit). <c>false</c> = the unit has no Allow. What the squad
        /// defence aims its post queries and its men's arcs at. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8c.</summary>
        public static bool TryAllowPoint(ISimulationView view, Entity unit, out Vector3 point)
        {
            point = default;
            if (!Registered(view) || !TryGet(view, unit, out var a)) return false;
            if (a.AllowArea != 0 && view is EntityRepository repo)
            {
                var area = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(repo, a.AllowArea);
                if (!area.IsNull && repo.IsAlive(area) && Fdp.Toolkit.Weather.WeatherField.TryOutline(repo, area, out var outline)
                    && TryPosition(repo, area, out var origin) && outline.Points.Count > 0)
                {
                    var sum = Vector2.Zero;
                    foreach (var p in outline.Points) sum += p;
                    sum /= outline.Points.Count;
                    point = new Vector3(origin.X + sum.X, origin.Y + sum.Y, origin.Z);
                    return true;
                }
            }
            if (a.ArcSet == 0) return false;
            float middle = (a.LeftBearing + Norm(a.RightBearing - a.LeftBearing) * 0.5f) * (MathF.PI / 180f);
            float range = a.MaxRange > 0f ? (a.MinRange + a.MaxRange) * 0.5f : 100f;
            point = a.ArcOrigin + new Vector3(MathF.Sin(middle) * range, MathF.Cos(middle) * range, 0f);
            return true;
        }

        /// <summary>⭐ <c>CE-2302</c> (DA-32) — does <paramref name="man"/> hold a sector a SUPERIOR set (his current play's)?</summary>
        public static bool HasSuperiorSet(ISimulationView view, Entity man)
        {
            if (!Registered(view) || !TryGet(view, man, out var a)) return false;
            return a.SetBy == BehaviorOrigin.Superior && (a.ArcSet != 0 || a.AllowArea != 0 || a.WatchSet != 0 || a.NoFire.Count > 0);
        }

        /// <summary>⭐ <c>CE-2302</c> (DA-32) — withdraw <paramref name="man"/>'s sector if a Superior set it: an all-empty
        /// <see cref="SetEngagementAreaEvent"/> at origin Superior (the gate keeps an Operator's). Called when the play gives him an
        /// order without an arc and when the commander's run that gave it ends (<see cref="SubordinateOrders.Release"/>).</summary>
        public static void WithdrawSuperior(EntityRepository repo, Entity man)
        {
            if (!repo.Bus.IsRegistered<SetEngagementAreaEvent>() || !repo.IsAlive(man) || !HasSuperiorSet(repo, man)) return;
            repo.Bus.Publish(new SetEngagementAreaEvent { Entity = man, Origin = BehaviorOrigin.Superior });
        }

        /// <summary>Does a shot from <paramref name="shooter"/> ending at <paramref name="point"/> end inside a NoFire area of his own or
        /// of any commander up his chain (≤ 8 levels)? Always honoured — self-defence never lifts it.</summary>
        public static bool IsNoFire(ISimulationView view, Entity shooter, Vector3 point)
        {
            if (!Registered(view) || view is not EntityRepository repo) return false;
            var at = shooter;
            for (int depth = 0; depth <= RoeOf.MaxChainDepth && !at.IsNull; depth++)
            {
                if (TryGet(view, at, out var a))
                    for (int i = 0; i < a.NoFire.Count && i < EngagementArea.MaxNoFire; i++)
                        if (InArea(repo, a.NoFire.Items[i], point)) return true;
                at = CommanderOf(view, at);
            }
            return false;
        }

        /// <summary>An entity target: <see cref="IsNoFire"/> at its position.</summary>
        public static bool IsNoFire(ISimulationView view, Entity shooter, Entity target)
            => TryPosition(view, target, out var p) && IsNoFire(view, shooter, p);

        /// <summary>⭐ <c>CE-2283</c> (SC-2) — <paramref name="unit"/>'s sector when it has an Allow (an arc or an area); test a point against
        /// it with <see cref="Contains"/> (geometry only — no tier, no self-defence).</summary>
        public static bool TryAllow(ISimulationView view, Entity unit, out EngagementArea a)
        {
            a = default;
            return Registered(view) && TryGet(view, unit, out a) && HasAllow(in a);
        }

        /// <summary>⭐ <c>CE-2283</c> (SC-3) — <paramref name="man"/>'s own arc (its origin and bounds); false when he has none.</summary>
        public static bool TryArc(ISimulationView view, Entity man, out EngagementArea arc)
        {
            arc = default;
            return Registered(view) && TryGet(view, man, out arc) && arc.ArcSet != 0;
        }

        /// <summary>Is <paramref name="point"/> inside the arc (compass bearings, ranges) — exposed for the planner and the rails.</summary>
        public static bool InArc(Vector3 origin, float left, float right, float minRange, float maxRange, Vector3 point)
        {
            float dx = point.X - origin.X, dy = point.Y - origin.Y;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            if (d < minRange || (maxRange > 0f && d > maxRange)) return false;
            if (d < 1e-3f || MathF.Abs(right - left) >= 360f) return true;
            float bearing = Norm(MathF.Atan2(dx, dy) * (180f / MathF.PI));
            float span = Norm(right - left);
            return Norm(bearing - left) <= span;
        }

        /// <summary>⭐ <c>CE-2276</c> E-1 (DA-53) — is <paramref name="point"/> inside the Allow of a sector VALUE (its arc and / or its area):
        /// the platoon's defence plan holds its engagement area as one. No Allow = false. No tier, no self-defence — geometry only.</summary>
        public static bool Contains(ISimulationView view, in EngagementArea a, Vector3 point) => HasAllow(in a) && InAllow(view, in a, point);

        private static bool SelfDefence(ISimulationView view, Entity man, Vector3 point)
        {
            if (!RecentSensesOf.Within(view, man, SensorChange.Hit, SelfDefenceSeconds)
                && !RecentSensesOf.Within(view, man, SensorChange.NearMiss, SelfDefenceSeconds)) return false;
            return TryPosition(view, man, out var me) && Vector2.Distance(new Vector2(me.X, me.Y), new Vector2(point.X, point.Y)) <= SelfDefenceMetres;
        }

        private static bool HasAllow(in EngagementArea a) => a.ArcSet != 0 || a.AllowArea != 0;

        private static bool InAllow(ISimulationView view, in EngagementArea a, Vector3 point)
        {
            if (a.ArcSet != 0 && InArc(a.ArcOrigin, a.LeftBearing, a.RightBearing, a.MinRange, a.MaxRange, point)) return true;
            return a.AllowArea != 0 && view is EntityRepository repo && InArea(repo, a.AllowArea, point);
        }

        private static bool InArea(EntityRepository repo, long areaNetId, Vector3 point)
        {
            if (areaNetId == 0) return false;
            var area = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(repo, areaNetId);
            if (area.IsNull || !repo.IsAlive(area) || !Fdp.Toolkit.Weather.WeatherField.TryOutline(repo, area, out var outline)) return false;
            if (!TryPosition(repo, area, out var origin)) return false;
            return Fdp.Toolkit.Terrain.PolygonMath.Contains(outline.Points, new Vector2(point.X - origin.X, point.Y - origin.Y));
        }

        private static bool Registered(ISimulationView view)
            => view is not EntityRepository repo || repo.IsComponentTypeRegistered<EngagementArea>();

        private static bool TryGet(ISimulationView view, Entity e, out EngagementArea a)
        {
            a = default;
            if (e.IsNull || !view.IsAlive(e) || !view.HasComponent<EngagementArea>(e)) return false;
            a = view.GetComponentRO<EngagementArea>(e);
            return true;
        }

        private static bool TryPosition(ISimulationView view, Entity e, out Vector3 p)
        {
            p = default;
            if (e.IsNull || !view.IsAlive(e) || !view.HasComponent<SimTransform>(e)) return false;
            p = view.GetComponentRO<SimTransform>(e).Position;
            return true;
        }

        private static Entity CommanderOf(ISimulationView view, Entity e)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<UnitSubordinate>()) return Entity.Null;
            if (!view.HasComponent<UnitSubordinate>(e)) return Entity.Null;
            var commander = view.GetComponentRO<UnitSubordinate>(e).Commander;
            return !commander.IsNull && commander != e && view.IsAlive(commander) ? commander : Entity.Null;
        }

        private static float Norm(float deg)
        {
            deg %= 360f;
            return deg < 0f ? deg + 360f : deg;
        }
    }
}
