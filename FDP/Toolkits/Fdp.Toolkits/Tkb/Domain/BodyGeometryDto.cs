using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>What an entity's position (<c>SimTransform.Position</c>) IS for a type.</summary>
    public enum ReferencePointKind : byte
    {
        /// <summary>The centre of the footprint, on the ground — ground vehicles and people today.</summary>
        GroundCentre = 0,

        /// <summary>The centre of gravity — aircraft. The body rests on its <see cref="BodyGeometryDto.GroundContacts"/>.</summary>
        CentreOfGravity = 1,
    }

    /// <summary>How a ground-contact point touches the ground.</summary>
    public enum GroundContactKind : byte
    {
        Wheel = 0,
        /// <summary>One end of a skid (a helicopter's runner); the two ends on one side make the skid.</summary>
        Skid = 1,
    }

    /// <summary>
    /// ⭐ One point where the body touches the ground — a wheel's tyre bottom or a skid end — in metres, BODY axes, from the type's
    /// reference point: x forward, y left, z up (below a centre of gravity, so z &lt; 0).
    /// </summary>
    public record GroundContactPoint
    {
        /// <summary>Human label ("nose", "main left", "tail").</summary>
        public string Name { get; init; } = "";

        [EditUnit("m")] public float X { get; init; }
        [EditUnit("m")] public float Y { get; init; }
        [EditUnit("m")] public float Z { get; init; }

        public GroundContactKind Kind { get; init; } = GroundContactKind.Wheel;

        /// <summary>True when the gear folds away in flight.</summary>
        public bool Retractable { get; init; }

        /// <summary>Tyre radius (m); 0 for a skid.</summary>
        [EditUnit("m")] public float WheelRadius { get; init; }

        /// <summary>Length of the leg above the wheel centre / skid (m) — what a drawing shows; 0 ⇒ 1 m.</summary>
        [EditUnit("m")] public float StrutLength { get; init; }
    }

    /// <summary>
    /// ⭐ CE-1041 — the BODY's geometry relative to its reference point, engine-neutral (TKB descriptor <c>"Body.Geometry"</c>).
    /// 📄 docs/DESIGN_Body_Geometry_And_Ground_Contact.md. 🔒 User, 2026-10-10: <i>"the aircraft model need to 'know' where their
    /// landing gear is relative to their center of gravity (reference point) so that we can simulate the landing."</i>
    /// <para>⛔ Not <see cref="VehicleParametersDto"/> (it gives the type CAR kinematics) and not <see cref="StrideRenderModelDefDto"/>
    /// (engine-specific by its own rule) — so an aircraft carries its size here.</para>
    /// </summary>
    [TkbDescriptor("Body.Geometry")]
    public record BodyGeometryDto
    {
        /// <summary>What the entity's position is for this type.</summary>
        public ReferencePointKind ReferencePoint { get; init; } = ReferencePointKind.GroundCentre;

        /// <summary>Overall length (m), nose to tail (rotor tip to tail rotor for a helicopter).</summary>
        [EditUnit("m")] public float Length { get; init; }

        /// <summary>Overall width (m) — the wingspan / main-rotor diameter for an aircraft.</summary>
        [EditUnit("m")] public float Width { get; init; }

        /// <summary>Overall height (m), ground to the highest point with the gear down.</summary>
        [EditUnit("m")] public float Height { get; init; }

        /// <summary>The centre of the Length × Width × Height box from the reference point (m, body axes).</summary>
        [EditUnit("m")] public float BodyCentreX { get; init; }
        [EditUnit("m")] public float BodyCentreY { get; init; }
        [EditUnit("m")] public float BodyCentreZ { get; init; }

        /// <summary>Where the body touches the ground (gear down).</summary>
        public List<GroundContactPoint> GroundContacts { get; init; } = new();
    }

    /// <summary>Where a body resting on its contacts sits: the reference point's height, and its pitch (nose up +) and roll
    /// (left side up +), radians.</summary>
    public readonly record struct RestingPose(float Z, float Pitch, float Roll);

    /// <summary>
    /// ⭐ CE-1041 — the one implementation of "where does this body sit on the ground" (<c>DESIGN_Body_Geometry_And_Ground_Contact.md</c>
    /// G3): for a lander, a parked aircraft, a ground clamp, and the 3-D map. Pure; the ground comes in as a function (R-252 — never
    /// the stand-in terrain).
    /// </summary>
    public static class BodyGeometry
    {
        /// <summary>Height of the reference point above LEVEL ground when resting on every contact (0 without contacts, or for a
        /// ground-referenced type).</summary>
        public static float RestingHeight(BodyGeometryDto geometry)
        {
            if (geometry is null) throw new ArgumentNullException(nameof(geometry));
            if (geometry.ReferencePoint == ReferencePointKind.GroundCentre || geometry.GroundContacts.Count == 0) return 0f;
            float lowest = float.MaxValue;
            foreach (var c in geometry.GroundContacts) lowest = MathF.Min(lowest, c.Z);
            return -lowest;
        }

        /// <summary>
        /// The pose of a body resting on the ground at <paramref name="xy"/> facing <paramref name="yaw"/> (radians from east,
        /// counter-clockwise): a plane through the ground heights under the contacts gives the pitch and roll, and the reference
        /// point sits <see cref="RestingHeight"/> above that plane along its normal. Fewer than three contacts (or a
        /// ground-referenced type) ⇒ level, on the ground under the reference point.
        /// </summary>
        public static RestingPose RestingPose(BodyGeometryDto geometry, Vector2 xy, float yaw, Func<float, float, float> groundHeightAt)
        {
            if (geometry is null) throw new ArgumentNullException(nameof(geometry));
            if (groundHeightAt is null) throw new ArgumentNullException(nameof(groundHeightAt));
            float h = RestingHeight(geometry);
            var contacts = geometry.GroundContacts;
            if (geometry.ReferencePoint == ReferencePointKind.GroundCentre || contacts.Count < 3)
                return new RestingPose(groundHeightAt(xy.X, xy.Y) + h, 0f, 0f);

            // Least-squares plane g = a + b·u + c·v in BODY-horizontal axes (u forward, v left) through the ground under each contact.
            float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
            double su = 0, sv = 0, sg = 0, suu = 0, svv = 0, suv = 0, sug = 0, svg = 0;
            int n = contacts.Count;
            foreach (var c in contacts)
            {
                float wx = xy.X + c.X * cos - c.Y * sin, wy = xy.Y + c.X * sin + c.Y * cos;
                double g = groundHeightAt(wx, wy);
                su += c.X; sv += c.Y; sg += g; suu += c.X * c.X; svv += c.Y * c.Y; suv += c.X * c.Y; sug += c.X * g; svg += c.Y * g;
            }
            // Solve the 3×3 normal equations by Cramer's rule.
            double[,] m = { { n, su, sv }, { su, suu, suv }, { sv, suv, svv } };
            double det = Det(m);
            if (Math.Abs(det) < 1e-9)
                return new RestingPose(groundHeightAt(xy.X, xy.Y) + h, 0f, 0f);   // collinear contacts: no plane
            double a = Det(new double[,] { { sg, su, sv }, { sug, suu, suv }, { svg, suv, svv } }) / det;
            double b = Det(new double[,] { { n, sg, sv }, { su, sug, suv }, { sv, svg, svv } }) / det;
            double cc = Det(new double[,] { { n, su, sg }, { su, suu, sug }, { sv, suv, svg } }) / det;

            float pitch = MathF.Atan((float)b);          // ground rising ahead ⇒ nose up
            float roll = MathF.Atan((float)cc);          // ground rising to the left ⇒ left side up
            // The reference point sits h above the plane along the plane's normal; for these slopes its height over the plane
            // at the reference XY is h / cos(tilt).
            float tilt = MathF.Atan(MathF.Sqrt((float)(b * b + cc * cc)));
            return new RestingPose((float)a + h / MathF.Cos(tilt), pitch, roll);
        }

        /// <summary>The rotation of a resting pose: yaw about up, then pitch (nose up), then roll (left side up), body axes.</summary>
        public static Quaternion Rotation(float yaw, RestingPose pose)
            => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw)
               * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -pose.Pitch)
               * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pose.Roll);

        private static double Det(double[,] m)
            => m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
             - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
             + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
    }
}
