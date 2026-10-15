using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐ M1 / M2 — <see cref="ArmorModel"/>'s face and armour column, directly.
    /// 📄 <c>docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md</c> §2, §5.
    /// </summary>
    public class ArmorModelTests
    {
        // a hull facing east (+X)
        private static readonly SimTransform East = new() { Position = Vector3.Zero, Rotation = Quaternion.Identity };

        /// <summary>
        /// ⭐⭐ M1 (D-A) — the face from the way the round TRAVELS: level from ahead / beside / behind ⇒ Front / Side / Rear;
        /// descending 50° ⇒ Top; 40° ⇒ still the side; straight down ⇒ Top (⛔ it was Front: no horizontal bearing).
        /// </summary>
        [Fact]
        public void M1_TheFace_FromTheArrival()
        {
            Assert.Equal(Facing.Front, ArmorModel.FacingOfArrival(East, new Vector3(-1, 0, 0)));   // coming from +X
            Assert.Equal(Facing.Side,  ArmorModel.FacingOfArrival(East, new Vector3(0, -1, 0)));
            Assert.Equal(Facing.Rear,  ArmorModel.FacingOfArrival(East, new Vector3(1, 0, 0)));
            float rad50 = 50f * MathF.PI / 180f, rad40 = 40f * MathF.PI / 180f;
            Assert.Equal(Facing.Top,  ArmorModel.FacingOfArrival(East, new Vector3(0, -MathF.Cos(rad50), -MathF.Sin(rad50))));
            Assert.Equal(Facing.Side, ArmorModel.FacingOfArrival(East, new Vector3(0, -MathF.Cos(rad40), -MathF.Sin(rad40))));
            Assert.Equal(Facing.Top,  ArmorModel.FacingOfArrival(East, new Vector3(0, 0, -5)));
            Assert.Equal(Facing.Front, ArmorModel.FacingOfArrival(East, Vector3.Zero));            // unknown
            // FacingOf(target, from) is the same rule, from a source point: a burst above the hull meets its roof
            Assert.Equal(Facing.Top,  ArmorModel.FacingOf(East, new Vector3(0.5f, 0, 3f), aimHeight: 1.2f));
            Assert.Equal(Facing.Side, ArmorModel.FacingOf(East, new Vector3(0, 30, 0)));
        }

        /// <summary>⭐ M2 (D-B) — the armour column: kinetic by default; a shaped charge reads …Ce, 0 ⇒ the kinetic value; the
        /// roof is ArmorTop, 0 ⇒ ArmorRear; tandem reads the shaped-charge column (D-C, until ERA exists).</summary>
        [Fact]
        public void M2_TheArmourColumn_PerFace_AndPenetrator()
        {
            var p = new CombatPlatformDefDto { ArmorFront = 500, ArmorSide = 250, ArmorRear = 150, ArmorFrontCe = 800, ArmorTopCe = 90 };
            Assert.Equal(500f, ArmorModel.ArmourFor(p, Facing.Front));
            Assert.Equal(800f, ArmorModel.ArmourFor(p, Facing.Front, Penetrator.ShapedCharge));
            Assert.Equal(800f, ArmorModel.ArmourFor(p, Facing.Front, Penetrator.Tandem));
            Assert.Equal(250f, ArmorModel.ArmourFor(p, Facing.Side, Penetrator.ShapedCharge));   // no SideCe
            Assert.Equal(150f, ArmorModel.ArmourFor(p, Facing.Top));                              // no ArmorTop ⇒ rear
            Assert.Equal(90f,  ArmorModel.ArmourFor(p, Facing.Top, Penetrator.ShapedCharge));
            Assert.Equal(0f,   ArmorModel.ArmourFor(null, Facing.Top, Penetrator.ShapedCharge));
        }

        /// <summary>
        /// ⭐ R-2019 D-M2 (DESIGN_Armour_Hit_Model §7.5) — the share of the blast a target takes: a person (hull height 0) and a hull with no
        /// platform record take it all; a stated value is used (clamped to 0..1); −1 (unstated) derives it from the side armour,
        /// <c>1 − side / 40 mm</c> — a 5 mm pickup 0.875, an APC's 20 mm 0.5, 40 mm and more 0.
        /// </summary>
        [Fact]
        public void AE2_BlastSusceptibility_PersonAll_StatedValue_ElseFromTheSideArmour()
        {
            var stated = new CombatPlatformDefDto { ArmorSide = 350, BlastSusceptibility = 0.3f };
            Assert.Equal(1f, ArmorModel.BlastSusceptibility(stated, hullHeight: 0f));   // a person: the hull rule never applies
            Assert.Equal(1f, ArmorModel.BlastSusceptibility(null, hullHeight: 2f));
            Assert.Equal(0.3f, ArmorModel.BlastSusceptibility(stated, 2f));
            Assert.Equal(1f, ArmorModel.BlastSusceptibility(stated with { BlastSusceptibility = 7f }, 2f));
            Assert.Equal(-1f, new CombatPlatformDefDto().BlastSusceptibility);   // unstated by default
            Assert.Equal(0.875f, ArmorModel.BlastSusceptibility(new CombatPlatformDefDto { ArmorSide = 5 }, 2f), 4);
            Assert.Equal(0.5f, ArmorModel.BlastSusceptibility(new CombatPlatformDefDto { ArmorSide = 20 }, 2f), 4);
            Assert.Equal(0f, ArmorModel.BlastSusceptibility(new CombatPlatformDefDto { ArmorSide = 60 }, 2f));
        }
    }
}
