using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;

namespace Fdp.Toolkit.Physics.BodyParts
{
    /// <summary>The person poses a round is tested against (HL-9: logical stance × weapon posture; no gait, no transition blend).</summary>
    public enum PersonPose : byte
    {
        StandingAtEase, StandingReady, StandingAimed,
        CrouchedAtEase, CrouchedReady, CrouchedAimed,
        Prone,
        CivilianStanding, CivilianCrouched, CivilianProne,
        /// <summary>Incapacitated or dead: lying on his back (the drawn figure's dead pose).</summary>
        Downed,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3209</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-1, HL-9) — a person's body as the rounds meet
    /// it: ten oriented boxes per pose (<see cref="PersonPart"/> order), in metres for a figure of <see cref="ReferenceHeight"/>, and one
    /// bounding sphere per pose. ⭐ The tables (<c>PersonBody.Tables.g.cs</c>) are GENERATED from the drawn block figure (ui's
    /// <c>BlockFigure</c>) and a rail in its suite fails the moment the two differ — so a round lands where the part is drawn. ⏭ S1c: the
    /// drawing reads its geometry from here (one description).
    /// </summary>
    public static partial class PersonBody
    {
        /// <summary>The height the tables are authored for (the drawn figure's).</summary>
        public const float ReferenceHeight = 1.8f;

        // ⚠ Lazy: Tables lives in the generated partial file, whose static initialiser may run AFTER this one.
        private static readonly Lazy<(Vector3 Centre, float Radius)[]> BoundsLazy = new(BuildBounds);
        private static readonly Lazy<float> MaxRadiusLazy = new(MaxRadius);

        /// <summary>The boxes of <paramref name="pose"/>.</summary>
        public static ReadOnlySpan<BodyBox> Boxes(PersonPose pose) => Tables[(int)pose];

        /// <summary>The bounding sphere of <paramref name="pose"/> (body frame, reference size) — tier ② (HL-2).</summary>
        public static (Vector3 Centre, float Radius) Bound(PersonPose pose) => BoundsLazy.Value[(int)pose];

        /// <summary>The largest bounding radius of any pose (reference size) — the broad phase's pad for a person.</summary>
        public static float MaxBoundRadius => MaxRadiusLazy.Value;

        /// <summary>The pose for a stance, an armed or unarmed man, his weapon posture, and whether he is down.</summary>
        public static PersonPose PoseOf(StanceId stance, bool armed, WeaponPostureKind posture, bool down)
        {
            if (down) return PersonPose.Downed;
            if (!armed)
                return stance switch { StanceId.Crouched => PersonPose.CivilianCrouched, StanceId.Prone => PersonPose.CivilianProne, _ => PersonPose.CivilianStanding };
            int p = System.Math.Clamp((int)posture, 0, 2);
            return stance switch
            {
                StanceId.Prone => PersonPose.Prone,
                StanceId.Crouched => (PersonPose)((int)PersonPose.CrouchedAtEase + p),
                _ => (PersonPose)((int)PersonPose.StandingAtEase + p),
            };
        }

        /// <summary>
        /// <paramref name="e"/>'s pose as this node knows it: the LOGICAL stance (<see cref="LogicalStance"/> — the one rule the hit model,
        /// the fire chain and perception share), armed when it carries a weapon, its weapon posture, down when incapacitated or dead.
        /// </summary>
        public static PersonPose PoseOf(EntityRepository world, Entity e)
        {
            var stance = LogicalStance.Of(world, e);
            bool armed = world.IsComponentTypeRegistered<WeaponState>() && world.HasComponent<WeaponState>(e)
                         && !Fdp.Toolkit.Combat.PersonHands.HandsOnly(world, e);   // ⭐ CE-3230 (H-3) — a civilian's hands are no rifle
            var posture = world.IsComponentTypeRegistered<WeaponPosture>() && world.HasComponent<WeaponPosture>(e)
                ? world.GetComponentRO<WeaponPosture>(e).Kind : WeaponPostureKind.AtEase;
            bool down = world.IsComponentTypeRegistered<Health>() && world.HasComponent<Health>(e)
                        && Injury.StateOf(world, e) is InjuryState.Incapacitated or InjuryState.Dead;
            return PoseOf(stance, armed, posture, down);
        }

        /// <summary>A person's scale against the reference figure: its collider height when it has one, else 1.</summary>
        public static float ScaleOf(float colliderHeight)
            => colliderHeight > 0.5f ? colliderHeight / ReferenceHeight : 1f;

        private static (Vector3, float)[] BuildBounds()
        {
            var bounds = new (Vector3, float)[Tables.Length];
            Span<Vector3> corners = stackalloc Vector3[8];
            for (int i = 0; i < Tables.Length; i++)
            {
                var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
                foreach (var box in Tables[i])
                {
                    Corners(box, corners);
                    foreach (var c in corners) { min = Vector3.Min(min, c); max = Vector3.Max(max, c); }
                }
                var centre = (min + max) * 0.5f;
                float r = 0f;
                foreach (var box in Tables[i])
                {
                    Corners(box, corners);
                    foreach (var c in corners) r = MathF.Max(r, Vector3.Distance(c, centre));
                }
                bounds[i] = (centre, r);
            }
            return bounds;
        }

        private static float MaxRadius()
        {
            float r = 0f;
            foreach (var (c, radius) in BoundsLazy.Value) r = MathF.Max(r, c.Length() + radius);   // from the body origin, so the grid point covers it
            return r;
        }

        private static void Corners(in BodyBox box, Span<Vector3> corners)
        {
            int k = 0;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                corners[k++] = box.Centre + Vector3.Transform(new Vector3(sx * box.Half.X, sy * box.Half.Y, sz * box.Half.Z), box.Rotation);
        }
    }
}
