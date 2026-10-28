using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using CycloneDDS.Schema.Formatting;
using Fdp.Modules.Geographic.Systems;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// The ONE human-readable rendering of a position wire's orientation (<c>OrientationQuat</c> on the NED stack,
    /// <c>BdcOrientationQuat</c> on BDC) — what <c>ddsmonitor</c> shows, while the wire still carries the quaternion's vector part
    /// (<see cref="QuaternionWireCodec"/>, <c>docs/blueprints/Architect_Question_107_NED_Wire_Frame_And_Names.md</c> D).
    ///
    /// <para><b>Shape</b>: <c>HPR [045.0, 3.2, -1.0] deg, Quat [0.010, 0.030, 0.380, 0.924]</c> — heading (3 integer digits, one decimal),
    /// pitch and roll (one decimal), then the quaternion x, y, z and the <i>implied</i> w (3 decimals), invariant culture.</para>
    ///
    /// <para><b>Convention</b> (⭐ NOT new — it is <see cref="SimTransformBridgeSystem"/>'s, the one the DIS/aerospace wire consumers and the
    /// editor's compass gizmo already use, itself built on <c>SimMath.ToYawPitchRollDeg</c>): the decoded rotation is the ECS
    /// <c>SimTransform.Rotation</c> in local ENU, body-forward = +X. <b>H</b> = compass heading, 0 = north, 90 = east, clockwise, [0, 360)
    /// (an exactly vertical nose has no heading and no separable roll — both shown as 0, the Quat part carries the pose); <b>P</b> = pitch, positive = nose up; <b>R</b> = roll, positive = right
    /// wing down. The identity (an all-zero wire struct) therefore reads <c>HPR [090.0, 0.0, 0.0]</c> — facing east, level.</para>
    ///
    /// <para>⛔ Display only: a second copy of this text, or any Euler maths here, would drift from the editor. JSON capture never calls it
    /// (it serialises the raw fields).</para>
    /// </summary>
    public static class OrientationDisplay
    {
        /// <summary>The display text for the wire vector part <c>(x, y, z)</c>.</summary>
        public static string Format(float x, float y, float z)
        {
            var sb = new StringBuilder(64);
            foreach (FormattedToken token in Tokens(x, y, z))
                sb.Append(token.Text);
            return sb.ToString();
        }

        /// <summary>The same text as <see cref="Format"/>, split into the tokens <c>ddsmonitor</c>'s detail panel colours.</summary>
        public static IEnumerable<FormattedToken> Tokens(float x, float y, float z)
        {
            Quaternion q = QuaternionWireCodec.Decode(x, y, z);   // all-zero => identity, never NaN

            float heading = SimTransformBridgeSystem.RotationToHeadingDeg(q);
            SimTransformBridgeSystem.RotationToPitchRollDeg(q, out float pitch, out float roll);

            // A vertical nose (CE-4068) has no heading, and roll is then only a numerically arbitrary share of the same turn
            // (it came out 180 for a clean "straight up"): show H and R as 0 and let the Quat part carry the pose.
            // ⚠ Same gate as SimTransformBridgeSystem.RotationToHeadingDeg's own guard (forward projected on the ENU plane ~ 0).
            Vector3 forward = Vector3.Transform(Vector3.UnitX, q);
            if (new Vector2(forward.X, forward.Y).LengthSquared() < 1e-6f)
                roll = 0f;

            // Round BEFORE the wrap check so 359.96 prints as 000.0, not 360.0.
            heading = MathF.Round(heading * 10f) / 10f;
            if (heading >= 360f || heading < 0f) heading = 0f;

            yield return new FormattedToken("HPR", TokenType.Keyword);
            yield return new FormattedToken(" [", TokenType.Punctuation);
            yield return Number(heading, "000.0");
            yield return new FormattedToken(", ", TokenType.Punctuation);
            yield return Number(pitch, "0.0");
            yield return new FormattedToken(", ", TokenType.Punctuation);
            yield return Number(roll, "0.0");
            yield return new FormattedToken("] ", TokenType.Punctuation);
            yield return new FormattedToken("deg", TokenType.Keyword);
            yield return new FormattedToken(", ", TokenType.Punctuation);
            yield return new FormattedToken("Quat", TokenType.Keyword);
            yield return new FormattedToken(" [", TokenType.Punctuation);
            yield return Number(q.X, "0.000");
            yield return new FormattedToken(", ", TokenType.Punctuation);
            yield return Number(q.Y, "0.000");
            yield return new FormattedToken(", ", TokenType.Punctuation);
            yield return Number(q.Z, "0.000");
            yield return new FormattedToken(", ", TokenType.Punctuation);
            yield return Number(q.W, "0.000");
            yield return new FormattedToken("]", TokenType.Punctuation);
        }

        private static FormattedToken Number(float value, string format)
        {
            string s = value.ToString(format, CultureInfo.InvariantCulture);
            // A value that rounds to zero must not print as "-0.0" / "-0.000".
            if (s.Length > 1 && s[0] == '-' && s.AsSpan(1).IndexOfAnyExcept('0', '.') < 0)
                s = s.Substring(1);
            return new FormattedToken(s, TokenType.Number);
        }
    }
}
