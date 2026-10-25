using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Physics.Components;
using Hrot.MuscleCharacter.Animation.Components;

namespace Fdp.Toolkit.Physics.BodyParts
{
    /// <summary>What <see cref="BodyPartRay"/> found on one body.</summary>
    public enum BodyPartTest : byte
    {
        /// <summary>The segment does not reach the body's bounding sphere (tier ②) — most candidates stop here.</summary>
        SphereMiss,
        /// <summary>It reaches the sphere but enters no part.</summary>
        Miss,
        /// <summary>It enters a part.</summary>
        Hit,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1126</c> (docs/DESIGN_Entity_Control.md §8g F-4; hit location HL-2, §9) — ONE body's part test, the one a round makes and the
    /// one the FPS crosshair makes: a person by the <see cref="PersonBody"/> boxes of his pose, a ground vehicle or a placed weapon by its
    /// <see cref="VehicleBody"/> boxes posed by its turret and deployment. ⛔ It was inline in <c>RaycastSolverSystem</c>'s narrow phase, so
    /// the crosshair met a coarse box while the round met the parts — and "the crosshair is on his head" and "the round strikes his head"
    /// could disagree. Both now call this.
    /// </summary>
    public static class BodyPartRay
    {
        /// <summary>
        /// A PERSON's parts: the pose's bounding sphere, then its boxes, scaled by <paramref name="colliderHeight"/>
        /// (<see cref="PersonBody.ScaleOf"/> — 0 = the reference figure).
        /// </summary>
        public static BodyPartTest Person(EntityRepository world, Entity e, in SimTransform tf, float colliderHeight, Vector3 a, Vector3 b,
                                          out BodyPartHitResult hit)
        {
            hit = default;
            var pose = PersonBody.PoseOf(world, e);
            float scale = PersonBody.ScaleOf(colliderHeight);
            var (bc, br) = PersonBody.Bound(pose);
            var stance = LogicalStance.Of(world, e);
            int lean = pose == PersonPose.Downed ? 0 : Leans.SignOf(world, e, stance);   // ⭐ CE-3257 (BR-5, §13 LN-5)
            var sphereCentre = tf.Position + Vector3.Transform(bc * scale, tf.Rotation);
            float shift = lean != 0 ? Leans.ShiftOf(world, e) : 0f;   // ⭐ AQ104 G — his own depth
            if (!BodyPartHit.SegmentTouchesSphere(a, b, sphereCentre, br * scale + shift)) return BodyPartTest.SphereMiss;
            if (lean == 0)
                return BodyPartHit.Segment(PersonBody.Boxes(pose), tf.Position, tf.Rotation, scale, a, b, out hit) ? BodyPartTest.Hit : BodyPartTest.Miss;
            // ⭐ LN-5 — a LEANING man: the segment in his upright frame, clipped to the half he shows; the hit's t back on the original segment
            float eye = Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.EyeHeightFor(world, e, stance);
            if (!Leans.Upright(tf, lean, eye, ref a, ref b, out float t0, out float t1, shift)) return BodyPartTest.Miss;
            if (!BodyPartHit.Segment(PersonBody.Boxes(pose), tf.Position, tf.Rotation, scale, a, b, out hit)) return BodyPartTest.Miss;
            hit = hit with { T = t0 + hit.T * (t1 - t0) };
            return BodyPartTest.Hit;
        }

        /// <summary>
        /// A VEHICLE's (or a placed weapon's) parts: the bound sphere covering every turret pose, then the kit's boxes posed by
        /// <paramref name="turretPart"/>'s <c>TurretPose</c> (none = the kit's rest), the main hull box's engine third resolved
        /// (<see cref="VehicleBody.PartOf"/>). <paramref name="scratch"/> holds the posed boxes (<see cref="VehicleBody.MaxBoxes"/>).
        /// </summary>
        public static BodyPartTest Vehicle(EntityRepository world, Entity e, VehicleShape vehicle, in SimTransform tf, Entity turretPart,
                                           Vector3 a, Vector3 b, Span<BodyBox> scratch, out BodyPartHitResult hit)
        {
            hit = default;
            var size = VehicleBody.SizeOf(world, e, vehicle);
            float grow = MathF.Max(MathF.Max(size.X / vehicle.Size.X, size.Y / vehicle.Size.Y), 1f);
            var sphereCentre = tf.Position + Vector3.Transform(vehicle.BoundCentre, tf.Rotation);
            if (!BodyPartHit.SegmentTouchesSphere(a, b, sphereCentre, vehicle.BoundRadius * grow)) return BodyPartTest.SphereMiss;
            bool hasTurret = !turretPart.IsNull && world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.TurretPose>()
                             && world.HasComponent<Fdp.Toolkit.Combat.Components.TurretPose>(turretPart);
            var turretPose = hasTurret ? world.GetComponentRO<Fdp.Toolkit.Combat.Components.TurretPose>(turretPart) : default;
            // ⭐ CE-3214 (S2b-7, S2b-8) — a weapon system: how far it is deployed; with no turret part, laid at its kit's rest elevation
            float deployed = vehicle.Stowed != null ? WeaponDeployment.FractionOf(world, e) : 1f;
            float elevation = hasTurret ? turretPose.Elevation : vehicle.Kit.RestElevation;
            int nb = VehicleBody.Pose(vehicle, size, turretPose.Azimuth, elevation, deployed, scratch);
            if (!BodyPartHit.Segment(scratch[..nb], tf.Position, tf.Rotation, 1f, a, b, out hit, arcFaces: true)) return BodyPartTest.Miss;   // ⭐ CE-2217 — the armour arc
            hit = hit with { Part = VehicleBody.PartOf(vehicle, hit.Part, hit.LocalPoint) };
            return BodyPartTest.Hit;
        }

        /// <summary>
        /// The part of <paramref name="e"/> the segment <paramref name="a"/>→<paramref name="b"/> enters first — a person by his parts, a body
        /// with a vehicle shape by its kit (its main turret found here) — or false. <paramref name="hasParts"/> says whether the body HAS parts
        /// at all (false: the caller keeps its own coarse test).
        /// </summary>
        public static bool TryHit(EntityRepository world, Entity e, Vector3 a, Vector3 b, Span<BodyBox> scratch, out BodyPartHitResult hit, out bool hasParts)
        {
            hit = default;
            hasParts = false;
            if (!world.IsAlive(e) || !world.HasComponent<SimTransform>(e)) return false;
            var tf = world.GetComponentRO<SimTransform>(e);
            if (PhysicsColliderReaders.IsPerson(world, e))
            {
                hasParts = true;
                float height = world.IsComponentTypeRegistered<PhysicsCollider>() && world.HasComponent<PhysicsCollider>(e)
                    ? world.GetComponentRO<PhysicsCollider>(e).Height : 0f;
                return Person(world, e, tf, height, a, b, out hit) == BodyPartTest.Hit;
            }
            if (VehicleBody.Of(world, e) is not { } vehicle) return false;
            hasParts = true;
            var turret = world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>() ? TurretParts.Find(world, e, 0) : Entity.Null;
            return Vehicle(world, e, vehicle, tf, turret, a, b, scratch, out hit) == BodyPartTest.Hit;
        }
    }
}
