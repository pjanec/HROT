using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐ An ORIENTATION IN THE WORLD FRAME — local ENU, body-forward = +X; what <c>SimTransform.Rotation</c> holds.
    /// 📄 <c>docs/DESIGN_Scenario_Rules.md</c> §9.8.10 SR-142.
    /// <para>
    /// Memory and JSON are <see cref="Quaternion"/>'s exactly (four floats; <c>[x, y, z, w]</c> through the options' own
    /// <see cref="Quaternion"/> handling) and the conversions are implicit both ways, so recordings and files are byte-identical. A
    /// body-LOCAL rotation (a turret's, a bone's) stays a <see cref="Quaternion"/> — the type is what tells a tool to draw a heading arrow.
    /// </para>
    /// <para>
    /// ⭐ THE compass convention lives HERE, once: heading 0 = north, 90 = east, clockwise, [0, 360); pitch positive = nose up; roll
    /// positive = right wing down. <c>SimTransformBridgeSystem</c> (the wire translators') and <c>OrientationDisplay</c> (ddsmonitor's
    /// text) route to it — moved down from the toolkit so this type can own it without a second copy (S0 as-built).
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(WorldRotationJsonConverter))]
    [DebuggerDisplay("{ToString(),nq}")]
    public struct WorldRotation : IEquatable<WorldRotation>
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public WorldRotation(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public WorldRotation(Quaternion q) { X = q.X; Y = q.Y; Z = q.Z; W = q.W; }

        /// <summary>Facing east, level (the identity — body-forward +X is east).</summary>
        public static WorldRotation Identity => Quaternion.Identity;

        public static implicit operator Quaternion(WorldRotation r) => new(r.X, r.Y, r.Z, r.W);
        public static implicit operator WorldRotation(Quaternion q) => new(q.X, q.Y, q.Z, q.W);

        /// <summary>Two world rotations composed (a <see cref="Quaternion"/>: the product is a turn, not necessarily a pose).</summary>
        public static Quaternion operator *(WorldRotation a, WorldRotation b) => (Quaternion)a * (Quaternion)b;
        /// <summary>Mixed with a bare <see cref="Quaternion"/> (a body-local or not-yet-typed rotation): composes as the quaternions do — declared so it is not ambiguous.</summary>
        public static Quaternion operator *(WorldRotation a, Quaternion b) => (Quaternion)a * b;
        /// <inheritdoc cref="op_Multiply(WorldRotation, Quaternion)"/>
        public static Quaternion operator *(Quaternion a, WorldRotation b) => a * (Quaternion)b;

        /// <summary>The compass heading, degrees [0, 360) — 0 = north, clockwise.</summary>
        public readonly float HeadingDeg => ToHeadingDeg(this);

        /// <summary>The level rotation facing <paramref name="headingDeg"/> (compass degrees).</summary>
        public static WorldRotation FromHeadingDeg(float headingDeg) => HeadingDegToQuaternion(headingDeg);

        /// <summary>This rotation's pitch (nose up positive) and roll (right wing down positive), degrees.</summary>
        public readonly (float PitchDeg, float RollDeg) PitchRollDeg
        {
            get { ToPitchRollDeg(this, out float p, out float r); return (p, r); }
        }

        // ── the convention (the bodies SimTransformBridgeSystem used to hold; it routes here now) ─────────────────────────────

        /// <summary>
        /// Compass heading degrees [0, 360) of a world rotation (body-forward +X; X = east, Y = north; 0 = north, 90 = east,
        /// clockwise). A nose pointing (nearly) straight up or down has no heading: 0.
        /// </summary>
        public static float ToHeadingDeg(Quaternion rotation)
        {
            // Degenerate guard: when the nose points nearly straight up/down, the body-forward axis projects to ~zero in the XY (ENU)
            // plane and compass heading is undefined (gimbal lock) → fall back to 0. Non-degenerate headings are unchanged.
            Vector3 fwd = Vector3.Transform(Vector3.UnitX, rotation);
            if (new Vector2(fwd.X, fwd.Y).LengthSquared() < 1e-6f)
                return 0f;

            var (yawDeg, _, _) = SimMath.ToYawPitchRollDeg(rotation);
            return (90f - yawDeg + 360f) % 360f;
        }

        /// <summary>Pitch (positive = nose up) and roll (positive = right wing down) of a world rotation, degrees.</summary>
        public static void ToPitchRollDeg(Quaternion rotation, out float pitchDeg, out float rollDeg)
        {
            // SimMath's pitch convention is nose-DOWN-positive; the world / wire convention is nose-UP-positive — negate.
            var (_, p, r) = SimMath.ToYawPitchRollDeg(rotation);
            pitchDeg = -p;
            rollDeg = r;
        }

        /// <summary>The level rotation for a compass heading — the inverse of <see cref="ToHeadingDeg"/>.</summary>
        public static Quaternion HeadingDegToQuaternion(float headingDeg)
        {
            // heading = (90 - mathYaw_deg + 360) % 360  →  mathYaw_rad = (90 - heading) * π/180
            float mathYawRad = (90f - headingDeg) * (MathF.PI / 180f);
            return SimMath.FromYaw(mathYawRad);
        }

        /// <summary>The full orientation from heading / pitch / roll (the exact inverse of <see cref="ToHeadingDeg"/> + <see cref="ToPitchRollDeg"/>).</summary>
        public static Quaternion EulerDegToQuaternion(float headingDeg, float pitchDeg, float rollDeg)
        {
            const float Deg = MathF.PI / 180f;
            return SimMath.FromYawPitchRoll((90f - headingDeg) * Deg, -pitchDeg * Deg, rollDeg * Deg);
        }

        /// <summary>A heading-only change: the new heading, <paramref name="current"/>'s own pitch and roll kept (a never-set rotation: level).</summary>
        public static Quaternion WithHeadingDeg(Quaternion current, float headingDeg)
        {
            if (current.LengthSquared() < 1e-6f) return HeadingDegToQuaternion(headingDeg);
            ToPitchRollDeg(Quaternion.Normalize(current), out float pitchDeg, out float rollDeg);
            return EulerDegToQuaternion(headingDeg, pitchDeg, rollDeg);
        }

        public readonly bool Equals(WorldRotation other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        public override readonly bool Equals(object? obj) => obj is WorldRotation r && Equals(r);
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        public static bool operator ==(WorldRotation a, WorldRotation b) => a.Equals(b);
        public static bool operator !=(WorldRotation a, WorldRotation b) => !a.Equals(b);

        /// <summary>The ONE human text of an orientation: <c>HPR [045.0, 3.2, -1.0] deg, Quat [x, y, z, w]</c> (<see cref="OrientationText"/>).</summary>
        public override readonly string ToString() => OrientationText.Format(this);
    }

    /// <summary><see cref="WorldRotation"/> exactly as <see cref="Quaternion"/> (the options' own handling; see <see cref="WorldPointJsonConverter"/>).</summary>
    public sealed class WorldRotationJsonConverter : JsonConverter<WorldRotation>
    {
        public override WorldRotation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonSerializer.Deserialize<Quaternion>(ref reader, options);

        public override void Write(Utf8JsonWriter writer, WorldRotation value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, (Quaternion)value, options);
    }

    /// <summary>What a token of <see cref="OrientationText"/> is (a display colours by it).</summary>
    public enum OrientationTokenKind : byte { Keyword, Punctuation, Number }

    /// <summary>
    /// ⭐ The ONE human-readable rendering of a world orientation — <c>HPR [045.0, 3.2, -1.0] deg, Quat [0.010, 0.030, 0.380, 0.924]</c>:
    /// heading (3 integer digits, one decimal), pitch and roll (one decimal), then the quaternion (3 decimals), invariant culture. The
    /// identity reads <c>HPR [090.0, 0.0, 0.0]</c> (facing east, level). ddsmonitor's <c>OrientationDisplay</c> colours these tokens;
    /// <see cref="WorldRotation.ToString"/> joins them. ⛔ Display only — no second copy of the text anywhere.
    /// </summary>
    public static class OrientationText
    {
        public static string Format(Quaternion q)
        {
            var sb = new StringBuilder(64);
            foreach (var (text, _) in Tokens(q)) sb.Append(text);
            return sb.ToString();
        }

        public static IEnumerable<(string Text, OrientationTokenKind Kind)> Tokens(Quaternion q)
        {
            float heading = WorldRotation.ToHeadingDeg(q);
            WorldRotation.ToPitchRollDeg(q, out float pitch, out float roll);

            // A vertical nose (CE-4068) has no heading, and roll is then only a numerically arbitrary share of the same turn: show H and
            // R as 0 and let the Quat part carry the pose (the same gate as ToHeadingDeg's own guard).
            Vector3 forward = Vector3.Transform(Vector3.UnitX, q);
            if (new Vector2(forward.X, forward.Y).LengthSquared() < 1e-6f)
                roll = 0f;

            // Round BEFORE the wrap check so 359.96 prints as 000.0, not 360.0.
            heading = MathF.Round(heading * 10f) / 10f;
            if (heading >= 360f || heading < 0f) heading = 0f;

            yield return ("HPR", OrientationTokenKind.Keyword);
            yield return (" [", OrientationTokenKind.Punctuation);
            yield return Number(heading, "000.0");
            yield return (", ", OrientationTokenKind.Punctuation);
            yield return Number(pitch, "0.0");
            yield return (", ", OrientationTokenKind.Punctuation);
            yield return Number(roll, "0.0");
            yield return ("] ", OrientationTokenKind.Punctuation);
            yield return ("deg", OrientationTokenKind.Keyword);
            yield return (", ", OrientationTokenKind.Punctuation);
            yield return ("Quat", OrientationTokenKind.Keyword);
            yield return (" [", OrientationTokenKind.Punctuation);
            yield return Number(q.X, "0.000");
            yield return (", ", OrientationTokenKind.Punctuation);
            yield return Number(q.Y, "0.000");
            yield return (", ", OrientationTokenKind.Punctuation);
            yield return Number(q.Z, "0.000");
            yield return (", ", OrientationTokenKind.Punctuation);
            yield return Number(q.W, "0.000");
            yield return ("]", OrientationTokenKind.Punctuation);
        }

        private static (string, OrientationTokenKind) Number(float value, string format)
        {
            string s = value.ToString(format, CultureInfo.InvariantCulture);
            // A value that rounds to zero must not print as "-0.0" / "-0.000".
            if (s.Length > 1 && s[0] == '-' && s.AsSpan(1).IndexOfAnyExcept('0', '.') < 0)
                s = s.Substring(1);
            return (s, OrientationTokenKind.Number);
        }
    }
}
