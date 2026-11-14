using System;
using Fdp.Core;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using CycloneDDS.Schema.Formatting;
using Fdp.Modules.Geographic.Systems;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// The ONE human-readable rendering of a position wire's orientation (<c>OrientationQuat</c> on the NED stack) — what <c>ddsmonitor</c> shows, while the wire still carries the quaternion's vector part
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

        /// <summary>The same text as <see cref="Format"/>, split into the tokens <c>ddsmonitor</c>'s detail panel colours — ⭐ S0: the
        /// tokens are <see cref="OrientationText"/>'s (Fdp.Core, the ONE text, also <see cref="WorldRotation"/>'s <c>ToString</c>); this maps their kinds.</summary>
        public static IEnumerable<FormattedToken> Tokens(float x, float y, float z)
        {
            Quaternion q = QuaternionWireCodec.Decode(x, y, z);   // all-zero => identity, never NaN
            foreach (var (text, kind) in OrientationText.Tokens(q))
                yield return new FormattedToken(text, kind switch
                {
                    OrientationTokenKind.Keyword     => TokenType.Keyword,
                    OrientationTokenKind.Punctuation => TokenType.Punctuation,
                    _                                => TokenType.Number,
                });
        }
    }
}
