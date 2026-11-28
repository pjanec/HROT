using System;
using System.Globalization;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Hrot.Player.Tests.Gizmos
{
    /// <summary>
    /// ⭐ Simulated devices SD-S5b (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-119) — THE received-target cue helper every device HUD shares
    /// (<see cref="HudCue"/>; the binoculars now, the designator in SD-S5c): the offset is measured in the DEVICE's frame (left +, up +) — a turned device sees a
    /// target on the other side; its texts are culture-invariant (R-4221: run under cs-CZ, never a decimal or thousands comma).
    /// </summary>
    public sealed class HudCueTests
    {
        [Fact]
        public void SDS5b_TheOffset_IsInTheDevicesFrame_LeftAndUpPositive()
        {
            Assert.True(HudCue.Offset(Vector3.Zero, Quaternion.Identity, new Vector3(100f, 100f, 0f), out float b, out float e));
            Assert.Equal(45f, b, 3);                                                        // north-east of an east-facing device: 45° LEFT
            Assert.Equal(0f, e, 3);
            var north = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
            Assert.True(HudCue.Offset(Vector3.Zero, north, new Vector3(100f, 100f, 100f * MathF.Sqrt(2f)), out b, out e));
            Assert.Equal(-45f, b, 3);                                                       // the same point from a north-facing device: 45° RIGHT
            Assert.Equal(45f, e, 3);                                                        // and 45° up
            Assert.False(HudCue.Offset(Vector3.One, Quaternion.Identity, Vector3.One, out _, out _));
        }

        [Fact]
        public void SDS5b_TheTexts_AreCultureInvariant()
        {
            var was = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
                Assert.Equal("1 240 m", HudCue.RangeText(1239.6f).ToString());
                Assert.Equal("12 005 m", HudCue.RangeText(12005f).ToString());
                Assert.Equal("452 m", HudCue.RangeText(452.4f).ToString());
                Assert.Equal("14°", HudCue.OffsetText(-13.6f).ToString());
            }
            finally { CultureInfo.CurrentCulture = was; }
        }
    }
}
