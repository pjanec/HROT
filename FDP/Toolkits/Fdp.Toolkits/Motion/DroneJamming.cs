using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐⭐ <c>CE-7014</c> (City Battle §11 R-2) — the jammer bubbles of one world, rebuilt every frame in the brains' SERIAL pre-pass
    /// (<c>BrainPhase.PrepareToolkitFrame</c>, as <c>PartIndex</c>) so a drone's brain in the parallel phase only READS it (R-4212: no lazy index
    /// mid-tick). A jammer is a live entity whose TKB type carries <see cref="JammerDto"/>. <see cref="Jammed"/>: is a drone inside the bubble
    /// of a jammer of ANOTHER force (its own side's jammer spares it). No allocation once warm (the arrays grow, the type cache fills once).
    /// </summary>
    public sealed class DroneJamming
    {
        private static readonly ConditionalWeakTable<EntityRepository, DroneJamming> s_of = new();

        private readonly Dictionary<long, float> _radiusOfType = new();   // the TKB type's JammerDto radius; 0 = not a jammer
        private EntityQuery? _typed;
        private Vector3[] _at = new Vector3[4];
        private float[] _radius = new float[4];
        private ForceId[] _force = new ForceId[4];

        /// <summary>Jammers live this frame.</summary>
        public int Count { get; private set; }

        public static DroneJamming Of(EntityRepository repo) => s_of.GetValue(repo, static _ => new DroneJamming());

        /// <summary>The serial pre-pass: every live jammer's position, radius and force, this frame.</summary>
        public static void PrepareFrame(EntityRepository repo) => Of(repo).Rebuild(repo);

        private void Rebuild(EntityRepository repo)
        {
            Count = 0;
            if (!repo.IsComponentTypeRegistered<TkbIdentity>() || !repo.HasSingletonManaged<ITkbDatabase>()
                || repo.GetSingletonManaged<ITkbDatabase>() is not { } db) return;
            _typed ??= repo.Query().With<TkbIdentity>().With<SimTransform>().Build();
            bool health = repo.IsComponentTypeRegistered<Health>();
            bool info = repo.IsComponentTypeRegistered<EntityInfo>();
            foreach (var e in _typed)
            {
                long type = (long)repo.GetComponentRO<TkbIdentity>(e).TkbType;
                if (!_radiusOfType.TryGetValue(type, out float r))
                {
                    r = db.TryGetByType(type, out var t) && t?.GetDescriptor<JammerDto>() is { } j ? MathF.Max(j.RadiusM, 0f) : 0f;
                    _radiusOfType[type] = r;
                }
                if (r <= 0f) continue;
                if (health && repo.HasComponent<Health>(e) && repo.GetComponentRO<Health>(e).Current <= 0f) continue;   // a dead jammer jams nothing
                if (Count == _at.Length)
                {
                    Array.Resize(ref _at, Count * 2); Array.Resize(ref _radius, Count * 2); Array.Resize(ref _force, Count * 2);
                }
                _at[Count] = repo.GetComponentRO<SimTransform>(e).Position;
                _radius[Count] = r;
                _force[Count] = info && repo.HasComponent<EntityInfo>(e) ? repo.GetComponentRO<EntityInfo>(e).ForceId : ForceId.Neutral;
                Count++;
            }
        }

        /// <summary>True when <paramref name="drone"/>, at <paramref name="at"/>, is inside the bubble of a jammer of another force.</summary>
        public static bool Jammed(EntityRepository repo, Entity drone, Vector3 at)
        {
            var j = Of(repo);
            if (j.Count == 0) return false;
            var own = repo.IsComponentTypeRegistered<EntityInfo>() && repo.HasComponent<EntityInfo>(drone) ? repo.GetComponentRO<EntityInfo>(drone).ForceId : ForceId.Neutral;
            for (int i = 0; i < j.Count; i++)
                if (j._force[i] != own && Vector3.DistanceSquared(j._at[i], at) <= j._radius[i] * j._radius[i]) return true;
            return false;
        }
    }
}
