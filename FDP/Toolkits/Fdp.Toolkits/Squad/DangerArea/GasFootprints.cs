using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Time;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// ⭐ <c>CE-3227</c> (civilians, docs/DESIGN_Civilians_And_Crowd_Control.md §7.3d-B G-2) — where a tear-gas cloud IS and where the wind
    /// will take it: a capsule from its centre now to its centre moved by the ground wind over <see cref="LookAheadSeconds"/>, its radius
    /// plus <see cref="MarginM"/>. Smoke is never one (it is cover, not danger).
    /// </summary>
    public readonly record struct GasFootprint(long CloudId, Vector2 From, Vector2 To, float Radius, float BaseZ, float TopZ)
    {
        /// <summary>Is <paramref name="p"/> inside the footprint (the capsule, within its height band)?</summary>
        public bool Contains(Vector3 p)
        {
            if (p.Z < BaseZ || p.Z > TopZ) return false;
            var q = new Vector2(p.X, p.Y);
            var ab = To - From;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-9f ? Math.Clamp(Vector2.Dot(q - From, ab) / len2, 0f, 1f) : 0f;
            return Vector2.DistanceSquared(q, From + ab * t) <= Radius * Radius;
        }
    }

    /// <summary>⭐ <c>CE-3227</c> — the gas footprints a node holds, for the danger-area solve.</summary>
    public static class GasFootprints
    {
        /// <summary>How far ahead the drift is predicted (s): a cover chosen now must still be clear when the unit gets there.</summary>
        public const float LookAheadSeconds = 15f;

        /// <summary>Added to the cloud's radius (m): a man at its edge is already in it.</summary>
        public const float MarginM = 2f;

        /// <summary>How far below the cloud's base / above its top a route point still counts (m) — a man walks on the ground under it.</summary>
        public const float BandM = 2f;

        /// <summary>
        /// Writes every live tear-gas cloud's footprint into <paramref name="into"/> and returns how many. A cloud still sitting on its
        /// venting canister (<see cref="AreaEffectCloud.SourceNetworkId"/> ≠ 0) does not drift, so its footprint is where it is.
        /// </summary>
        public static int Of(EntityRepository repo, Span<GasFootprint> into)
        {
            if (into.Length == 0 || !repo.IsComponentTypeRegistered<AreaEffectCloud>()) return 0;
            double now = SimClock.Of(repo).TotalTime;
            int n = 0;
            foreach (var e in repo.Query().With<AreaEffectCloud>().With<SimTransform>().Build())
            {
                ref readonly var c = ref repo.GetComponentRO<AreaEffectCloud>(e);
                if (c.Kind != CloudKind.TearGas || c.Radius <= 0f || c.IsOver(now)) continue;
                var p = repo.GetComponentRO<SimTransform>(e).Position;
                var from = new Vector2(p.X, p.Y);
                var to = c.SourceNetworkId != 0 ? from
                    // ⭐ CE-3258 — the SAME wind the cloud drifts on (AreaEffectClouds.DriftWind), so the prediction is the drift
                    : from + Fdp.Toolkit.Combat.AreaEffectClouds.DriftWind(repo, p, c.Height) * MathF.Min(LookAheadSeconds, (float)(c.BornAtSimSeconds + c.Seconds - now));
                long id = repo.IsComponentTypeRegistered<NetworkIdentity>() && repo.HasComponent<NetworkIdentity>(e)
                    ? repo.GetComponentRO<NetworkIdentity>(e).Value : e.Index;
                into[n++] = new GasFootprint(id, from, to, c.Radius + MarginM, p.Z - BandM, p.Z + MathF.Max(c.Height, 0.1f) + BandM);
                if (n == into.Length) break;
            }
            return n;
        }
    }
}
