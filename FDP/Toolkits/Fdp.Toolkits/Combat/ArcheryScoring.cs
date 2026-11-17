using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-4649</c> (bows BW-11, docs/designs/weapons/DESIGN_Bows_And_Crossbows.md) — the ring an arrow scores on an archery target (<see cref="ArcheryTargetDto"/>): the distance of
    /// the hit from the scoring face's centre, in rings of <c>RingWidthM</c>. ⭐ The ONE rule (the contest scenario, a rail and any future game-score hook call it).
    /// ⚠ The target is a PROP with no body parts, so the hit's place is the round's STRIKE point on the target (the detonation's hit point) — not a <c>HitMarks</c> entry.
    /// </summary>
    public static class ArcheryScoring
    {
        /// <summary>The ring of a hit <paramref name="distanceM"/> from the centre of <paramref name="face"/>: <c>Rings</c> at the centre, one less per ring width, 0 beyond the rim (a miss on the boss).</summary>
        public static int RingOf(float distanceM, ArcheryTargetDto face)
        {
            if (distanceM < 0f || face.RingWidthM <= 0f || face.Rings <= 0) return 0;
            int ring = face.Rings - (int)MathF.Floor(distanceM / face.RingWidthM);
            return Math.Max(0, ring);
        }

        /// <summary>The scoring face's centre in the world: the target's position, <see cref="ArcheryTargetDto.FaceCentreZ"/> up.</summary>
        public static Vector3 CentreOf(Vector3 targetPosition, ArcheryTargetDto face) => targetPosition + new Vector3(0f, 0f, face.FaceCentreZ);

        /// <summary>The scoring face of <paramref name="target"/>'s type, or null when it is no archery target (or its type is unknown).</summary>
        public static ArcheryTargetDto? FaceOf(EntityRepository world, Entity target)
            => CombatTkb.TryGetTemplate(world, target, out var t) ? t.GetDescriptor<ArcheryTargetDto>() : null;

        /// <summary>The ring an arrow striking <paramref name="target"/> at <paramref name="hit"/> scores; −1 when the target is no archery target.</summary>
        public static int RingAt(EntityRepository world, Entity target, Vector3 hit)
        {
            if (target.IsNull || !world.IsAlive(target) || !world.HasComponent<SimTransform>(target)) return -1;
            if (FaceOf(world, target) is not { } face) return -1;
            return RingOf(Vector3.Distance(hit, CentreOf(world.GetComponentRO<SimTransform>(target).Position, face)), face);
        }
    }
}
