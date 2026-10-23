using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-1262</c> (AV-1) — what one map canvas shows this frame: the mode, the camera (with its vertical field of view), the 2-D view
/// rectangle, the possessed unit and what its crosshair rests on. 📄 docs/DESIGN_Agent_Shared_View.md §6a.
/// </summary>
public sealed record ViewSnapshot(
    string Mode,
    Vector3 Eye,
    Vector3 Forward,
    float YawDeg,
    float PitchDeg,
    float Distance,
    float FovyDeg,
    Vector2 Viewport,
    Vector2? Rect2DMin,
    Vector2? Rect2DMax,
    Entity Possessed,
    long AimTarget,
    float? ZoomFovDeg,
    double PublishedAtSeconds);

/// <summary>
/// ⭐ <c>CE-1262</c> (AV-1) — the per-frame view publish, keyed by the WORLD the canvas draws, so two canvases in one process (the editor
/// map and the replay browser's) never overwrite each other and the debug API reads the camera of the world it serves. Written by
/// <see cref="MapStatsOverlay"/> each drawn frame (every map host composes it), read from any thread.
/// </summary>
public static class ViewTelemetry
{
    private sealed class Box { public ViewSnapshot? Value; }
    private static readonly ConditionalWeakTable<EntityRepository, Box> ByWorld = new();

    public static void Publish(EntityRepository world, ViewSnapshot snapshot)
    {
        var box = ByWorld.GetOrCreateValue(world);
        System.Threading.Volatile.Write(ref box.Value, snapshot);
    }

    /// <summary>The last view drawn of <paramref name="world"/>, or null when no canvas draws it (a headless host).</summary>
    public static ViewSnapshot? Of(EntityRepository world)
        => ByWorld.TryGetValue(world, out var box) ? System.Threading.Volatile.Read(ref box.Value) : null;
}

/// <summary>One unit in view.</summary>
public sealed record InViewUnit(Entity Entity, long NetworkId, string? Name, float Distance, Vector2 Screen);

/// <summary>
/// ⭐ <c>CE-1262</c> (AV-2) — which units a view shows: in 2-D the published view rectangle (the per-entity <see cref="CullingState"/> only
/// where no rectangle was published); in 3-D / first person a frustum test from the published camera (Z up), nearest first, with the
/// screen point. ⚠ Measured live `2026-10-23`: <see cref="CullingState"/> is a draw-skip hint that reads VISIBLE wherever its host's
/// viewport is unset (CE-131) — on the editor it listed units 300 px below the screen, so it may not decide what the operator sees.
/// Pure; run on the main thread, per request only.
/// </summary>
public static class ViewReader
{
    public static IReadOnlyList<InViewUnit> InView(EntityRepository world, ViewSnapshot view, int limit = 50)
    {
        var list = new List<InViewUnit>();
        if (!world.IsComponentTypeRegistered<SimTransform>() || !world.IsComponentTypeRegistered<NetworkIdentity>()) return list;
        bool culling = view.Mode == "2D" && world.IsComponentTypeRegistered<CullingState>();
        bool names = world.IsComponentTypeRegistered<EntityInfo>();

        foreach (var e in world.Query().With<SimTransform>().With<NetworkIdentity>().Build())
        {
            var p = world.GetComponentRO<SimTransform>(e).Position;
            float distance;
            Vector2 screen;
            if (view.Mode == "2D")
            {
                if (view.Rect2DMin is { } min && view.Rect2DMax is { } max)
                { if (p.X < min.X || p.X > max.X || p.Y < min.Y || p.Y > max.Y) continue; }
                else if (culling && world.HasComponent<CullingState>(e)) { if (!world.GetComponentRO<CullingState>(e).IsVisible) continue; }
                else continue;
                distance = Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(view.Eye.X, view.Eye.Y));
                screen = view.Rect2DMin is { } lo && view.Rect2DMax is { } hi && hi.X > lo.X && hi.Y > lo.Y
                    ? new Vector2((p.X - lo.X) / (hi.X - lo.X) * view.Viewport.X, (hi.Y - p.Y) / (hi.Y - lo.Y) * view.Viewport.Y)
                    : default;
            }
            else if (!TryProject(view, p, out distance, out screen)) continue;

            string? name = names && world.HasComponent<EntityInfo>(e) && !world.GetComponentRO<EntityInfo>(e).Name.IsEmpty
                ? world.GetComponentRO<EntityInfo>(e).Name.ToString() : null;
            list.Add(new InViewUnit(e, world.GetComponentRO<NetworkIdentity>(e).Value, name, distance, screen));
        }
        list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        if (list.Count > limit) list.RemoveRange(limit, list.Count - limit);
        return list;
    }

    /// <summary>Is <paramref name="point"/> inside the 3-D view's frustum? Its distance from the eye and its screen point.</summary>
    public static bool TryProject(ViewSnapshot view, Vector3 point, out float distance, out Vector2 screen)
    {
        screen = default;
        var d = point - view.Eye;
        distance = d.Length();
        var forward = view.Forward.LengthSquared() > 1e-6f ? Vector3.Normalize(view.Forward) : Vector3.UnitY;
        float along = Vector3.Dot(d, forward);
        if (along <= 0.1f) return false;   // behind (or at) the eye
        var right = Vector3.Cross(forward, Vector3.UnitZ);
        right = right.LengthSquared() > 1e-6f ? Vector3.Normalize(right) : Vector3.UnitX;   // looking straight down
        var up = Vector3.Cross(right, forward);
        float tanV = MathF.Tan(view.FovyDeg * MathF.PI / 360f);
        float aspect = view.Viewport.Y > 0 ? view.Viewport.X / view.Viewport.Y : 16f / 9f;
        float ndcX = Vector3.Dot(d, right) / (along * tanV * aspect);
        float ndcY = Vector3.Dot(d, up) / (along * tanV);
        if (MathF.Abs(ndcX) > 1f || MathF.Abs(ndcY) > 1f) return false;
        screen = new Vector2((ndcX + 1f) * 0.5f * view.Viewport.X, (1f - ndcY) * 0.5f * view.Viewport.Y);
        return true;
    }
}
