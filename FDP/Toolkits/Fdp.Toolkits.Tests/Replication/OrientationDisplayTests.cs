using System;
using System.Linq;
using System.Numerics;
using CycloneDDS.Schema.Formatting;
using Fdp.Core;
using Fdp.Modules.Geographic.Systems;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// The text <c>ddsmonitor</c> shows for a position wire's orientation (<see cref="OrientationDisplay"/>, used by BOTH
    /// <c>OrientationQuat</c> (NED) and <c>BdcOrientationQuat</c> (BDC)) — AQ107 D, as-built addendum 2026-10-28.
    /// 📐 Convention under test = <see cref="SimTransformBridgeSystem"/>'s: H compass (0 north, clockwise), P positive nose-up, R positive right-wing-down;
    /// the identity (an all-zero wire struct) faces EAST, so it reads <c>H 090.0</c>. Each expected string was computed independently
    /// (a Python re-derivation of the quaternion maths), not copied from the code's output.
    /// </summary>
    public class OrientationDisplayTests
    {
        /// <summary>The wire form of a pose given as compass heading / pitch (nose-up +) / roll (right-wing-down +), degrees.</summary>
        private static string Shown(float headingDeg, float pitchUpDeg, float rollDeg)
        {
            const float D2R = MathF.PI / 180f;
            Quaternion q = SimMath.FromYawPitchRoll((90f - headingDeg) * D2R, -pitchUpDeg * D2R, rollDeg * D2R);
            QuaternionWireCodec.Encode(q, out float x, out float y, out float z);
            return OrientationDisplay.Format(x, y, z);
        }

        [Fact]
        public void AnAllZeroStruct_IsTheIdentity_FacingEastLevel()
            => Assert.Equal("HPR [090.0, 0.0, 0.0] deg, Quat [0.000, 0.000, 0.000, 1.000]", OrientationDisplay.Format(0f, 0f, 0f));

        [Fact]
        public void Heading45_Level()
            => Assert.Equal("HPR [045.0, 0.0, 0.0] deg, Quat [0.000, 0.000, 0.383, 0.924]", Shown(45f, 0f, 0f));

        [Fact]
        public void APitchedAndSlightlyRolledPose_ShowsNoseUpPositive_AndNegativeRollForLeftWingDown()
            => Assert.Equal("HPR [045.0, 3.2, -1.0] deg, Quat [0.003, -0.029, 0.382, 0.924]", Shown(45f, 3.2f, -1.0f));

        [Fact]
        public void ARolledPose_ShowsRollRightWingDownPositive_AtHeadingNorth()
            => Assert.Equal("HPR [000.0, 0.0, 30.0] deg, Quat [0.183, 0.183, 0.683, 0.683]", Shown(0f, 0f, 30f));

        [Fact]
        public void ANoseDownWestwardPose_ShowsNegativePitch_AndAWrappedHeading()
            => Assert.Equal("HPR [300.0, -20.0, 0.0] deg, Quat [-0.168, 0.045, 0.951, 0.255]", Shown(300f, -20f, 0f));

        [Fact]
        public void ANoseStraightUp_ShowsPitch90_NoNaN_HeadingZero()
        {
            // CE-4068: the gimbal case. Heading is undefined for a vertical nose and shows 0; pitch is exactly 90; nothing is NaN.
            Quaternion up = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f);
            QuaternionWireCodec.Encode(up, out float x, out float y, out float z);
            string s = OrientationDisplay.Format(x, y, z);
            Assert.Equal("HPR [000.0, 90.0, 0.0] deg, Quat [0.000, -0.707, 0.000, 0.707]", s);
            Assert.DoesNotContain("NaN", s);
        }

        [Fact]
        public void ANoseStraightDown_ShowsPitchMinus90_NoNaN_HeadingZero()
        {
            Quaternion down = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
            QuaternionWireCodec.Encode(down, out float x, out float y, out float z);
            string s = OrientationDisplay.Format(x, y, z);
            Assert.Equal("HPR [000.0, -90.0, 0.0] deg, Quat [0.000, 0.707, 0.000, 0.707]", s);
            Assert.DoesNotContain("NaN", s);
        }

        [Fact]
        public void ANegativeWQuaternion_IsCanonicalisedByTheEncoder_SoItDisplaysAsItsPositiveTwin()
        {
            const float D2R = MathF.PI / 180f;
            Quaternion q = SimMath.FromYawPitchRoll(45f * D2R, -3.2f * D2R, -1.0f * D2R);
            var negated = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);   // the SAME rotation with w < 0
            QuaternionWireCodec.Encode(negated, out float x, out float y, out float z);
            Assert.Equal("HPR [045.0, 3.2, -1.0] deg, Quat [0.003, -0.029, 0.382, 0.924]", OrientationDisplay.Format(x, y, z));
        }

        [Fact]
        public void TinyNegativeAnglesAndComponents_NeverPrintAsMinusZero()
        {
            string s = Shown(10f, 0f, -0.004f);   // roll -0.004 deg rounds to 0.0; x and y are about -3e-5
            Assert.DoesNotContain("-0.0", s);
            Assert.StartsWith("HPR [010.0, 0.0, 0.0] deg, Quat [0.000, 0.000, 0.643, 0.766]", s);
        }

        [Fact]
        public void AHeadingThatRoundsUpTo360_WrapsToZero()
            => Assert.StartsWith("HPR [000.0,", Shown(359.97f, 0f, 0f));

        [Fact]
        public void ANonFiniteWireValue_DisplaysTheIdentity_NotNaN()
            => Assert.Equal("HPR [090.0, 0.0, 0.0] deg, Quat [0.000, 0.000, 0.000, 1.000]", OrientationDisplay.Format(float.NaN, 0f, 0f));

        [Theory]
        [InlineData(0f, 0f, 0f)]
        [InlineData(0.003f, -0.029f, 0.382f)]
        [InlineData(0f, -0.7071068f, 0f)]
        public void TheTokensConcatenateToExactlyTheText_AndNumbersAreNumberTokens(float x, float y, float z)
        {
            FormattedToken[] tokens = OrientationDisplay.Tokens(x, y, z).ToArray();
            Assert.Equal(OrientationDisplay.Format(x, y, z), string.Concat(tokens.Select(t => t.Text)));
            Assert.Equal(7, tokens.Count(t => t.Type == TokenType.Number));   // H, P, R, qx, qy, qz, qw
            Assert.All(tokens.Where(t => t.Type == TokenType.Number),
                t => Assert.True(double.TryParse(t.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _), t.Text));
            Assert.Equal(new[] { "HPR", "deg", "Quat" }, tokens.Where(t => t.Type == TokenType.Keyword).Select(t => t.Text));
        }
    }
}
