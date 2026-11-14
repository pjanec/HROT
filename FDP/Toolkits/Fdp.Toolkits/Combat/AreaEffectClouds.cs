using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Time;

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ <c>CE-3219</c> part 3 — what a cloud is made of (docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b).</summary>
    public enum CloudKind : byte
    {
        None = 0,
        /// <summary>Irritates every person inside (an Irritant hit each second) and thins sight.</summary>
        TearGas = 1,
        /// <summary>Blocks sight; harmless.</summary>
        Smoke = 2,
    }

    /// <summary>
    /// ⭐ <c>CE-3219</c> part 3 (civilians B-1, R-3014; docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b) — a gas or smoke cloud: a vertical
    /// cylinder of <see cref="Radius"/> × <see cref="Height"/> standing on the entity's <c>SimTransform</c> position. A REPLICATED entity its
    /// creator owns (<c>dtAreaEffectCloud</c>, 144): the creator drifts it on the wind and ends it (<see cref="AreaEffectCloudSystem"/>); every
    /// node reads it for sight (<see cref="AreaEffectClouds.Of"/>, folded in at the world-query seam) and draws it. Asked for by
    /// <see cref="AreaEffectClouds.Request"/> (the entity-creation request on the FDP bus).
    /// <para>⛔ No <c>Lifespan</c>: <c>LifespanSystem</c> destroys on every node that runs it; the cloud's own born/seconds let the owner
    /// alone end it, and let a renderer fade it.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AreaEffectCloud)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct AreaEffectCloud
    {
        public CloudKind Kind;
        /// <summary>Horizontal radius (m).</summary>
        public float Radius;
        /// <summary>Height above its base (m).</summary>
        public float Height;
        /// <summary>The share of light a line keeps crossing it while dense (0 = opaque); thins to 1 over its last third.</summary>
        public float SightTransmittance;
        /// <summary>How long one Irritant hit lasts (s) — the pulse is each second, so ≥ 1 keeps a man irritated while he stays in.</summary>
        public SimDuration IrritantSeconds;
        /// <summary>The sim time it was made (<c>ISimClock.TotalTime</c>).</summary>
        public SimInstant BornAtSimSeconds;
        /// <summary>How long it lives (s).</summary>
        public SimDuration Seconds;

        /// <summary>⭐ <c>CE-3236</c> (B-14, §7.15 VC-4) — the venting canister it sits on (its network id; APPENDED): while that lives the
        /// cloud does not drift; once it is known gone (picked up, burnt out) the owner sets 0 and the cloud drifts free. 0 = free.</summary>
        public int SourceNetworkId;

        /// <summary>⭐ <c>R-3039</c> S1 — the OWNER's step clock (APPENDED; ⛔ not on the wire — the translator copies named fields): the sim time of
        /// its last step, valid once <see cref="Stepped"/> is 1. Was a field of <c>AreaEffectCloudSystem</c>: a checkpoint restored into a fresh
        /// system lost it and the cloud stepped at once, drifting on the wind for its whole age. 📄 docs/DESIGN_System_State_Inventory.md §6 #3.</summary>
        public SimInstant LastStepSimSeconds;
        /// <summary>1 once the owner has stepped it (the first step comes at once).</summary>
        public byte Stepped;
        /// <summary>1 once the owner asked the lifecycle to end it (a networked cloud's teardown, asked once).</summary>
        public byte Ending;

        /// <summary>⭐ <c>CE-4155</c> (SR-82; APPENDED, on the wire — <c>dtAreaEffectCloud</c>) — the network id of who threw the round that made
        /// it (the detonation's <c>Shooter</c>); each pulse's <c>DamageAssessedEvent.Source</c> is that unit, resolved on the node that owns the cloud.
        /// 0 = unknown.</summary>
        public int CreatorNetworkId;

        /// <summary>⭐ <c>CE-4155</c> (SR-82; APPENDED, on the wire) — the network id of the FIRST thrower when the round was thrown back
        /// (<c>DetonationNotification.OriginalShooter</c>); 0 = the creator threw it first. Each pulse's <c>OriginalSource</c>.</summary>
        public int OriginalCreatorNetworkId;

        /// <summary>True once its time is up.</summary>
        public readonly bool IsOver(double now) => now >= BornAtSimSeconds + Seconds;

        /// <summary>0 when made, 1 when gone (clamped) — what a renderer fades on.</summary>
        public readonly float Fraction(double now) => Seconds <= 0f ? 1f : (float)Math.Clamp((now - BornAtSimSeconds) / Seconds, 0.0, 1.0);

        /// <summary>The transmittance now: dense for two thirds of its life, then thinning linearly to clear.</summary>
        public readonly float TransmittanceAt(double now)
        {
            float f = Fraction(now);
            const float thinFrom = 2f / 3f;
            return f <= thinFrom ? SightTransmittance : SightTransmittance + (1f - SightTransmittance) * ((f - thinFrom) / (1f - thinFrom));
        }
    }

    /// <summary>⭐ <c>CE-3219</c> part 3 — one cloud as a sight query sees it (a snapshot row).</summary>
    public readonly record struct CloudCylinder(Vector2 Centre, float BaseZ, float TopZ, float Radius, float Transmittance);

    /// <summary>
    /// ⭐ <c>CE-3219</c> part 3 — the clouds of one view, for sight: the <c>DoorStates</c> shape (R-219: a background view's snapshot, never a live
    /// value; R-220: per thread, the previous table handed back while nothing changed). Immutable once returned.
    /// </summary>
    public sealed class CloudTable
    {
        private readonly CloudCylinder[] _clouds;
        private CloudTable(CloudCylinder[] clouds) => _clouds = clouds;

        public int Count => _clouds.Length;
        public CloudCylinder this[int i] => _clouds[i];

        /// <summary>The light a straight line keeps after crossing every cloud it passes through (1 = none crossed).</summary>
        public float Transmittance(Vector3 from, Vector3 to)
        {
            float t = 1f;
            for (int i = 0; i < _clouds.Length; i++)
                if (Crosses(in _clouds[i], from, to, out _)) t *= _clouds[i].Transmittance;
            return t;
        }

        /// <summary>True when the segment passes through <paramref name="c"/>'s cylinder; <paramref name="along"/> is where it enters (0..1).</summary>
        public static bool Crosses(in CloudCylinder c, Vector3 from, Vector3 to, out float along)
        {
            along = 0f;
            var a = new Vector2(from.X, from.Y) - c.Centre;
            var d = new Vector2(to.X - from.X, to.Y - from.Y);
            float r2 = c.Radius * c.Radius;
            float t0, t1;
            float dd = Vector2.Dot(d, d);
            if (dd < 1e-9f)
            {
                if (Vector2.Dot(a, a) > r2) return false;
                t0 = 0f; t1 = 1f;
            }
            else
            {
                float b = Vector2.Dot(a, d), cc = Vector2.Dot(a, a) - r2;
                float disc = b * b - dd * cc;
                if (disc < 0f) return false;
                float sq = MathF.Sqrt(disc);
                t0 = MathF.Max((-b - sq) / dd, 0f);
                t1 = MathF.Min((-b + sq) / dd, 1f);
                if (t0 > t1) return false;
            }
            float z0 = from.Z + (to.Z - from.Z) * t0, z1 = from.Z + (to.Z - from.Z) * t1;
            if (MathF.Max(z0, z1) < c.BaseZ || MathF.Min(z0, z1) > c.TopZ) return false;
            along = t0;
            return true;
        }

        [ThreadStatic] private static List<CloudCylinder>? t_scratch;
        [ThreadStatic] private static CloudTable? t_last;

        // ⭐ CE-2414 — the answer this thread built THIS frame (null included) is handed back while: the same world, frame and epoch
        //   (FrameStamp), the same clock, no cloud ADDED since (EntityRepository.ComponentsAddedOf), and every cloud it read still alive with
        //   the SAME cloud values and position (compared here — clouds drift every frame, so a table-write check would never hold).
        //   📐 It walked every entity per sight / fire query, clouds or none: ~6.5 % of the SimHost thread on the CE-2408 stress world.
        [ThreadStatic] private static Fdp.Toolkit.Replication.Utilities.FrameStamp t_stamp;
        [ThreadStatic] private static bool t_held;
        [ThreadStatic] private static CloudTable? t_answer;
        [ThreadStatic] private static double t_now;
        [ThreadStatic] private static long t_added;
        [ThreadStatic] private static List<(Entity E, AreaEffectCloud Cloud, Vector3 At)>? t_read;

        internal static CloudTable? Build(ISimulationView view)
        {
            double now = SimClock.Of(view).TotalTime;
            if (view is EntityRepository live && t_held && now == t_now && t_stamp.HoldsFrame(live)
                && live.ComponentsAddedOf(ComponentType<AreaEffectCloud>.ID) == t_added && Unchanged(live))
                return t_answer;
            var list = t_scratch ??= new List<CloudCylinder>();
            var read = t_read ??= new List<(Entity, AreaEffectCloud, Vector3)>();
            list.Clear();
            read.Clear();
            foreach (var e in view.Query().With<AreaEffectCloud>().With<SimTransform>().Build())
            {
                var c = view.GetComponentRO<AreaEffectCloud>(e);
                var p = view.GetComponentRO<SimTransform>(e).Position;
                read.Add((e, c, p));
                if (c.Radius <= 0f || c.IsOver(now)) continue;
                list.Add(new CloudCylinder(new Vector2(p.X, p.Y), p.Z, p.Z + MathF.Max(c.Height, 0.1f), c.Radius, c.TransmittanceAt(now)));
            }
            CloudTable? answer;
            if (list.Count == 0) answer = null;
            else if (t_last is { } last && last._clouds.AsSpan().SequenceEqual(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list))) answer = last;
            else answer = t_last = new CloudTable(list.ToArray());
            if (view is EntityRepository built)
            {
                t_stamp.Take(built); t_held = true; t_now = now; t_answer = answer;
                t_added = built.ComponentsAddedOf(ComponentType<AreaEffectCloud>.ID);
            }
            else t_held = false;
            return answer;
        }

        private static bool Unchanged(EntityRepository repo)
        {
            var read = t_read!;
            for (int k = 0; k < read.Count; k++)
            {
                var (e, cloud, at) = read[k];
                if (!repo.IsAlive(e) || !repo.HasComponent<AreaEffectCloud>(e) || !repo.HasComponent<SimTransform>(e)) return false;
                if (repo.GetComponentRO<SimTransform>(e).Position != at) return false;
                var now = repo.GetComponentRO<AreaEffectCloud>(e);
                if (!System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<AreaEffectCloud>(in now))
                        .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<AreaEffectCloud>(in cloud)))) return false;
            }
            return true;
        }
    }

    /// <summary>⭐ <c>CE-3219</c> part 3 — the one reader of <see cref="AreaEffectCloud"/> for sight, and the one request helper.</summary>
    public static class AreaEffectClouds
    {
        /// <summary>
        /// ⭐ <c>CE-3258</c> — THE wind a cloud drifts on: the wind at its MIDDLE (<paramref name="position"/> + half its
        /// <paramref name="height"/>) over the ground it lies on (its own Z — R-2037: no terrain read on its owner, CGF). The drift
        /// (<see cref="Systems.AreaEffectCloudSystem"/>) and the danger footprint's prediction (<c>GasFootprints</c>) both read it, so the
        /// prediction is the drift. ⛔ The wind at its base: under a scenario wind (a level above the ground, a log profile to zero at the
        /// ground) a cloud lying on the ground never drifted — only the single-level steady wind moved it.
        /// </summary>
        public static Vector2 DriftWind(EntityRepository repo, Vector3 position, float height)
            => Fdp.Toolkit.Weather.WeatherField.WindAt(repo, position + new Vector3(0f, 0f, MathF.Max(height, 0f) * 0.5f), position.Z);

        /// <summary>The clouds <paramref name="view"/> holds, or null when it holds none (the common case: no cost to a query).</summary>
        public static CloudTable? Of(ISimulationView view)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<AreaEffectCloud>()) return null;
            long t0 = Fdp.Toolkit.Diagnostics.QueryTimings.Begin();   // ⭐ CE-3427
            var t = CloudTable.Build(view);
            Fdp.Toolkit.Diagnostics.QueryTimings.End(S_Of, t0);
            return t;
        }
        private static readonly int S_Of = Fdp.Toolkit.Diagnostics.QueryTimings.Slot("clouds.CloudTable.Build");

        /// <summary>⭐ The TKB type of a cloud entity (bare: what the cloud is travels on <see cref="AreaEffectCloud"/>, per instance).</summary>
        public const long TkbType = 7202;

        /// <summary>
        /// Asks for a cloud at <paramref name="at"/>: THE entity-creation request (<see cref="EntityCreationRequest"/>, <c>CE-3228</c>)
        /// published on the FDP bus with owner 0 — the system picks the owner (the default processor, then the role-group grants), as for any
        /// other author. A node with no request system (a replay) reads nothing, so nothing is made there.
        /// </summary>
        public static Guid Request(EntityRepository repo, Vector3 at, in AreaEffectCloud cloud)
        {
            var id = Guid.NewGuid();
            repo.Bus.PublishManaged(new EntityCreationRequest
            {
                RequestId = id,
                OwnerAppInstanceId = 0,             // the system picks
                TkbType = (Fdp.Core.TkbTypeId)(TkbType),
                InitType = ReliableInitType.None,   // a burst's cloud waits for no peer
                InitialComponents = new List<object>
                {
                    new SimTransform { Position = at, Rotation = Quaternion.Identity },
                    cloud,
                },
            });
            return id;
        }

        /// <summary>True when <paramref name="point"/> is inside the cloud standing at <paramref name="position"/>.</summary>
        public static bool Contains(in AreaEffectCloud cloud, Vector3 position, Vector3 point)
            => Vector2.DistanceSquared(new Vector2(point.X, point.Y), new Vector2(position.X, position.Y)) <= cloud.Radius * cloud.Radius
               && point.Z >= position.Z - 0.5f && point.Z <= position.Z + MathF.Max(cloud.Height, 0.1f);
    }
}
