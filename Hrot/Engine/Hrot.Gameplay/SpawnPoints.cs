using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Weather;
using Hrot.Common;

namespace Hrot.Gameplay;

/// <summary>
/// ⭐ Player Join S2-c (D-11, <c>docs/DESIGN_Player_Join.md</c> §10) — the FREE POINT a role's man spawns at: a random <see cref="SpawnArea"/> of the side, then up to <see cref="Tries"/> random points of its outline's
/// bounding box that pass the ONE area-entity containment (<see cref="WeatherField.AreaEntityContains"/> — the test <c>ItemSources</c> reads too), the ground height from the world (<c>WorldQuery</c>, the area's own Z when
/// the host has no terrain) and not within <see cref="ClearanceM"/> of any alive entity that has a position. ⛔ No navmesh here: the server (CGF) has none — SimHost's crowd snaps a created man's start onto its own.
/// ⭐ S3: with <c>only</c> the point comes from THAT area alone. A direct scan is fine — a spawn is a user action, never per frame.
/// </summary>
public static class SpawnPoints
{
    public const int Tries = 20;
    public const float ClearanceM = 3f;

    /// <summary>The refusal when the side has no area.</summary>
    public static string NoArea(ForceId side) => $"no spawn area for {side}";

    public const string NoFreePoint = "no free point";
    public const string NoSuchArea = "NoSuchArea";
    public const string WrongSide = "WrongSide";

    /// <summary>Picks a point; false with <paramref name="reason"/> when the side has no area or 20 tries found no free point inside it.</summary>
    public static bool Pick(EntityRepository world, ForceId side, Random rng, out Vector3 pos, out float yawDeg, out string reason, Entity? only = null)
    {
        pos = default; yawDeg = 0f; reason = "";
        var areas = new List<Entity>();
        if (only is { } named)
        {
            // ⭐ S3-c — the NAMED area only: it must be a spawn area with an outline (else NoSuchArea) of THIS side (else WrongSide)
            if (named.IsNull || !world.IsAlive(named) || !world.IsComponentTypeRegistered<SpawnArea>() || !world.HasComponent<SpawnArea>(named)
                || !world.IsComponentTypeRegistered<SimTransform>() || !world.HasComponent<SimTransform>(named) || !WeatherField.TryOutline(world, named, out _)) { reason = NoSuchArea; return false; }
            if (world.GetComponentRO<SpawnArea>(named).Side != side) { reason = WrongSide; return false; }
            areas.Add(named);
        }
        else if (world.IsComponentTypeRegistered<SpawnArea>() && world.IsComponentTypeRegistered<SimTransform>())
            foreach (var e in world.Query().With<SpawnArea>().With<SimTransform>().Build())
                if (world.IsAlive(e) && world.GetComponentRO<SpawnArea>(e).Side == side && WeatherField.TryOutline(world, e, out _)) areas.Add(e);
        if (areas.Count == 0) { reason = NoArea(side); return false; }

        var area = areas[rng.Next(areas.Count)];
        WeatherField.TryOutline(world, area, out var outline);
        var origin = world.GetComponentRO<SimTransform>(area).Position;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in outline.Points)
        {
            minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X); minY = MathF.Min(minY, p.Y); maxY = MathF.Max(maxY, p.Y);
        }
        float facing = world.GetComponentRO<SpawnArea>(area).FacingDeg;
        var terrain = Fdp.Toolkit.World.WorldQuery.Of(world);
        for (int i = 0; i < Tries; i++)
        {
            var p = new Vector2(origin.X + minX + (float)rng.NextDouble() * (maxX - minX), origin.Y + minY + (float)rng.NextDouble() * (maxY - minY));
            if (!WeatherField.AreaEntityContains(world, area, p)) continue;
            if (Crowded(world, p)) continue;
            float z = terrain?.GroundHeightAt(p.X, p.Y) ?? origin.Z;
            pos = new Vector3(p.X, p.Y, z); yawDeg = facing;
            return true;
        }
        reason = NoFreePoint;
        return false;
    }

    private static bool Crowded(EntityRepository world, Vector2 p)
    {
        float r2 = ClearanceM * ClearanceM;
        foreach (var e in world.Query().With<SimTransform>().Build())
        {
            if (!world.IsAlive(e) || WeatherField.TryOutline(world, e, out _)) continue;   // an area's own anchor is no one standing there
            var t = world.GetComponentRO<SimTransform>(e).Position;
            float dx = t.X - p.X, dy = t.Y - p.Y;
            if (dx * dx + dy * dy < r2) return true;
        }
        return false;
    }
}
