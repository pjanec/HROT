using System;
using System.Numerics;
using System.Text.Json;
using Fdp.Toolkit.Tkb.Attributes;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests.Domain
{
    /// <summary>
    /// ⭐ CE-1041 — the body's geometry relative to its reference point, and where a body resting on its gear sits.
    /// 📄 docs/DESIGN_Body_Geometry_And_Ground_Contact.md G1–G3.
    /// </summary>
    public class BodyGeometryTests
    {
        /// <summary>An F-16-like tricycle: nose 3.4 m ahead, mains 0.6 m behind and 1.18 m out, tyres 1.9 m below the CG.</summary>
        private static BodyGeometryDto Tricycle() => new()
        {
            ReferencePoint = ReferencePointKind.CentreOfGravity,
            Length = 15f, Width = 10f, Height = 4.9f,
            GroundContacts =
            {
                new GroundContactPoint { Name = "nose", X = 3.4f, Y = 0f, Z = -1.9f, WheelRadius = 0.24f, Retractable = true },
                new GroundContactPoint { Name = "main left", X = -0.6f, Y = 1.18f, Z = -1.9f, WheelRadius = 0.36f, Retractable = true },
                new GroundContactPoint { Name = "main right", X = -0.6f, Y = -1.18f, Z = -1.9f, WheelRadius = 0.36f, Retractable = true },
            },
        };

        [Fact]
        public void TheDescriptor_IsNamedBodyGeometry()
            => Assert.Equal("Body.Geometry",
                Assert.Single(typeof(BodyGeometryDto).GetCustomAttributes(typeof(TkbDescriptorAttribute), false)) is TkbDescriptorAttribute a
                    ? a.HierarchicalName : null);

        [Fact]
        public void ATkbFile_AuthorsTheGeometry_AsTheGeneratedParserReadsIt()
        {
            // ⭐ The generated parser deserialises with FdpJsonOptionsRegistry.DefaultRelaxed (TkbDescriptorGenerator.cs:125).
            const string json = """
            { "ReferencePoint": "CentreOfGravity", "Length": 29.79, "Width": 40.41, "Height": 11.66,
              "BodyCentreX": 1.5, "BodyCentreZ": 2.83,
              "GroundContacts": [ { "Name": "nose", "X": 9.4, "Z": -3.0, "WheelRadius": 0.5, "Retractable": true } ] }
            """;
            var dto = JsonSerializer.Deserialize<BodyGeometryDto>(json, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
            Assert.Equal(ReferencePointKind.CentreOfGravity, dto.ReferencePoint);
            Assert.Equal(40.41f, dto.Width);
            var nose = Assert.Single(dto.GroundContacts);
            Assert.Equal(9.4f, nose.X);
            Assert.True(nose.Retractable);
            Assert.Equal(GroundContactKind.Wheel, nose.Kind);
        }

        [Fact]
        public void OnLevelGround_TheCgSits_TheGearHeight_AboveIt_Level()
        {
            var g = Tricycle();
            Assert.Equal(1.9f, BodyGeometry.RestingHeight(g), 3);
            var pose = BodyGeometry.RestingPose(g, new Vector2(100, 50), 0.7f, (_, _) => 12f);
            Assert.Equal(13.9f, pose.Z, 3);
            Assert.Equal(0f, pose.Pitch, 4);
            Assert.Equal(0f, pose.Roll, 4);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(1.1f)]
        [InlineData(-2.3f)]
        public void OnASlope_TheBodyPitchesAndRolls_WithTheGround_WhicheverWayItFaces(float yaw)
        {
            // Ground rising 10 % to the north (R-248 — never flat).
            static float Ground(float x, float y) => 0.1f * y;
            var g = Tricycle();
            var pose = BodyGeometry.RestingPose(g, new Vector2(0, 0), yaw, Ground);

            // Facing north (yaw π/2) is nose-up by the full slope; facing east, the left (north) side is up — tan follows the heading.
            Assert.InRange(MathF.Abs(MathF.Tan(pose.Pitch) - 0.1f * MathF.Sin(yaw)), 0f, 1e-3f);
            Assert.InRange(MathF.Abs(MathF.Tan(pose.Roll) - 0.1f * MathF.Cos(yaw)), 0f, 1e-3f);
            Assert.InRange(pose.Z, 1.9f, 1.92f);   // the ground under the CG is 0 here: gear height / cos(tilt)
        }

        [Fact]
        public void TheRestingPose_PutsEveryWheelOnTheGround()
        {
            static float Ground(float x, float y) => 3f + 0.08f * x - 0.05f * y;   // a tilted plane
            var g = Tricycle();
            float yaw = 0.4f;
            var pose = BodyGeometry.RestingPose(g, new Vector2(20, -7), yaw, Ground);
            var rot = BodyGeometry.Rotation(yaw, pose);
            foreach (var c in g.GroundContacts)
            {
                var w = new Vector3(20, -7, pose.Z) + Vector3.Transform(new Vector3(c.X, c.Y, c.Z), rot);
                Assert.InRange(w.Z - Ground(w.X, w.Y), -0.03f, 0.03f);
            }
        }

        [Fact]
        public void AGroundReferencedType_SitsOnTheGround_WhateverItsContacts()
        {
            var g = Tricycle() with { ReferencePoint = ReferencePointKind.GroundCentre };
            Assert.Equal(0f, BodyGeometry.RestingHeight(g));
            Assert.Equal(5f, BodyGeometry.RestingPose(g, Vector2.Zero, 0f, (_, _) => 5f).Z);
        }
    }
}
