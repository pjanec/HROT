using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Rendering;
using Fdp.Toolkit.Vis2D.Shapes;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Shapes
{
    public class DefaultEntityShapeLibraryTests
    {
        // ── GetShape by explicit name ─────────────────────────────────────────

        [Fact]
        public void GetShape_ByName_GroundVehicle_ReturnsGroundVehicleProfile()
        {
            var lib = new DefaultEntityShapeLibrary();
            var shape = lib.GetShape(DefaultEntityShapeLibrary.GroundVehicle, default);
            Assert.Equal(DefaultEntityShapeLibrary.GroundVehicle, shape.Name);
        }

        [Fact]
        public void GetShape_ByName_Humanoid_ReturnsHumanoidProfile()
        {
            var lib = new DefaultEntityShapeLibrary();
            var shape = lib.GetShape(DefaultEntityShapeLibrary.Humanoid, default);
            Assert.Equal(DefaultEntityShapeLibrary.Humanoid, shape.Name);
        }

        [Fact]
        public void GetShape_ByName_FixedWing_ReturnsFixedWingProfile()
        {
            var lib = new DefaultEntityShapeLibrary();
            var shape = lib.GetShape(DefaultEntityShapeLibrary.FixedWing, default);
            Assert.Equal(DefaultEntityShapeLibrary.FixedWing, shape.Name);
        }

        [Fact]
        public void GetShape_ByName_RotaryWing_ReturnsRotaryWingProfile()
        {
            var lib = new DefaultEntityShapeLibrary();
            var shape = lib.GetShape(DefaultEntityShapeLibrary.RotaryWing, default);
            Assert.Equal(DefaultEntityShapeLibrary.RotaryWing, shape.Name);
        }

        // ── GetShape custom registration ──────────────────────────────────────

        [Fact]
        public void Register_CustomShape_RetrievableByName()
        {
            var lib = new DefaultEntityShapeLibrary();
            var custom = new EntityShapeProfile
            {
                Name = "boat",
                Elements = new[]
                {
                    new PolylineDefinition
                    {
                        LocalVertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0) }
                    }
                }
            };

            lib.Register(custom);

            var retrieved = lib.GetShape("boat", default);
            Assert.Equal("boat", retrieved.Name);
        }

        [Fact]
        public void Register_CustomShape_NullName_Overrides_DoesNotThrow()
        {
            var lib = new DefaultEntityShapeLibrary();
            // Null/empty name → DIS fallback used; no exception expected.
            var shape = lib.GetShape(null, default);
            Assert.NotNull(shape);
        }

        // ── DIS type fallback ─────────────────────────────────────────────────

        [Theory]
        [InlineData(1, 1, 0, DefaultEntityShapeLibrary.GroundVehicle)] // Platform, Land
        [InlineData(1, 3, 0, DefaultEntityShapeLibrary.GroundVehicle)] // Platform, Surface
        [InlineData(3, 0, 0, DefaultEntityShapeLibrary.Humanoid)]      // Lifeform
        [InlineData(1, 2, 5, DefaultEntityShapeLibrary.FixedWing)]     // Air, category < 20
        [InlineData(1, 2, 20, DefaultEntityShapeLibrary.RotaryWing)]   // Air, category >= 20
        [InlineData(1, 2, 19, DefaultEntityShapeLibrary.FixedWing)]    // Air, category == 19
        [InlineData(0, 0, 0, DefaultEntityShapeLibrary.GroundVehicle)] // Unknown -> GroundVehicle
        public void GetShape_NullName_FallsBackToDisType(
            byte kind, byte domain, byte category, string expectedName)
        {
            var lib = new DefaultEntityShapeLibrary();
            var disType = new DISEntityType { Kind = kind, Domain = domain, Category = category };

            var shape = lib.GetShape(null, disType);

            Assert.Equal(expectedName, shape.Name);
        }

        [Fact]
        public void GetShape_EmptyName_FallsBackToDisType()
        {
            var lib = new DefaultEntityShapeLibrary();
            // Kind=3 = Lifeform → Humanoid
            var disType = new DISEntityType { Kind = 3 };

            var shape = lib.GetShape(string.Empty, disType);

            Assert.Equal(DefaultEntityShapeLibrary.Humanoid, shape.Name);
        }

        // ── Profile structure ─────────────────────────────────────────────────

        [Fact]
        public void GroundVehicle_Profile_HasAtLeastOneElement()
        {
            var lib = new DefaultEntityShapeLibrary();
            var shape = lib.GetShape(DefaultEntityShapeLibrary.GroundVehicle, default);
            Assert.NotEmpty(shape.Elements);
        }

        [Fact]
        public void FixedWing_Profile_HasAtLeastOneElement()
        {
            var lib = new DefaultEntityShapeLibrary();
            var shape = lib.GetShape(DefaultEntityShapeLibrary.FixedWing, default);
            Assert.NotEmpty(shape.Elements);
        }
    }

    public class EntityShapeProfileTests
    {
        [Fact]
        public void Profile_Name_StoresName()
        {
            var profile = new EntityShapeProfile { Name = "test", Elements = Array.Empty<PolylineDefinition>() };
            Assert.Equal("test", profile.Name);
        }

        [Fact]
        public void Profile_Elements_StoresElements()
        {
            var elem = new PolylineDefinition { LocalVertices = new[] { Vector3.Zero, Vector3.UnitX } };
            var profile = new EntityShapeProfile { Name = "test", Elements = new[] { elem } };
            Assert.Single(profile.Elements);
        }
    }

    public class PolylineDefinitionTests
    {
        [Fact]
        public void PolylineDef_StoresVertices()
        {
            var verts = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0) };
            var pd = new PolylineDefinition { LocalVertices = verts };
            Assert.Equal(2, pd.LocalVertices.Length);
        }

        [Fact]
        public void PolylineDef_ShowWhen_DefaultIsNone()
        {
            var pd = new PolylineDefinition { LocalVertices = new[] { Vector3.Zero } };
            Assert.Equal(EntityShapeCondition.None, pd.ShowWhen);
        }

        [Fact]
        public void PolylineDef_HideWhen_DefaultIsNone()
        {
            var pd = new PolylineDefinition { LocalVertices = new[] { Vector3.Zero } };
            Assert.Equal(EntityShapeCondition.None, pd.HideWhen);
        }
    }

    public class PerspectiveShapeRendererProjectVertexTests
    {
        private const float Tolerance = 0.001f;

        // ── Identity rotation, zero exaggeration ──────────────────────────────

        [Fact]
        public void ProjectVertex_IdentityRotation_ZeroExaggeration_CentreVertex_ReturnsWorldPos()
        {
            // A vertex at (0, 0, 0) in normalized space should map to worldPos.
            var result = PerspectiveShapeRenderer.ProjectVertex(
                normalizedPos: Vector3.Zero,
                worldPos: new Vector2(100, 200),
                rotation: Quaternion.Identity,
                L: 10f,
                W: 5f,
                exaggeration: 0f);

            Assert.Equal(100f, result.X, Tolerance);
            Assert.Equal(200f, result.Y, Tolerance);
        }

        [Fact]
        public void ProjectVertex_IdentityRotation_ZeroExaggeration_ForwardVertex_OffsetsByLength()
        {
            // A vertex at (0.5, 0, 0) normalized → X = 0.5 * L = 5m forward.
            var result = PerspectiveShapeRenderer.ProjectVertex(
                normalizedPos: new Vector3(0.5f, 0, 0),
                worldPos: Vector2.Zero,
                rotation: Quaternion.Identity,
                L: 10f,
                W: 5f,
                exaggeration: 0f);

            Assert.Equal(5f, result.X, Tolerance);
            Assert.Equal(0f, result.Y, Tolerance);
        }

        [Fact]
        public void ProjectVertex_IdentityRotation_ZeroExaggeration_SideVertex_OffsetsByWidth()
        {
            // A vertex at (0, 0.5, 0) normalized → Y = 0.5 * W = 2.5m lateral.
            var result = PerspectiveShapeRenderer.ProjectVertex(
                normalizedPos: new Vector3(0, 0.5f, 0),
                worldPos: Vector2.Zero,
                rotation: Quaternion.Identity,
                L: 10f,
                W: 5f,
                exaggeration: 0f);

            Assert.Equal(0f, result.X, Tolerance);
            Assert.Equal(2.5f, result.Y, Tolerance);
        }

        // ── Exaggeration effect ───────────────────────────────────────────────

        [Fact]
        public void ProjectVertex_PositiveZ_WithExaggeration_ExpandsProjectedOffset()
        {
            // Vertex at (0.5, 0, 0.5) in normalized space.
            // With identity rotation, Z stays positive after rotation.
            // scale = 1 + rotated.Z * exaggeration = 1 + (0.5 * 10) * 0.1 = 1.5
            // X = (0.5 * L) * scale = 5 * 1.5 = 7.5
            var result = PerspectiveShapeRenderer.ProjectVertex(
                normalizedPos: new Vector3(0.5f, 0, 0.5f),
                worldPos: Vector2.Zero,
                rotation: Quaternion.Identity,
                L: 10f,
                W: 5f,
                exaggeration: 0.1f);

            // rotated.Z after identity = 0.5 * L = 5.0; scale = 1 + 5 * 0.1 = 1.5
            // projected X = 5 * 1.5 = 7.5
            Assert.Equal(7.5f, result.X, Tolerance);
        }

        [Fact]
        public void ProjectVertex_ZeroZ_ExaggerationHasNoEffect()
        {
            // A flat vertex at Z=0 is unaffected by any exaggeration coefficient.
            var withExaggeration = PerspectiveShapeRenderer.ProjectVertex(
                normalizedPos: new Vector3(0.5f, 0, 0),
                worldPos: Vector2.Zero,
                rotation: Quaternion.Identity,
                L: 10f, W: 5f, exaggeration: 0.5f);

            var withoutExaggeration = PerspectiveShapeRenderer.ProjectVertex(
                normalizedPos: new Vector3(0.5f, 0, 0),
                worldPos: Vector2.Zero,
                rotation: Quaternion.Identity,
                L: 10f, W: 5f, exaggeration: 0f);

            Assert.Equal(withoutExaggeration.X, withExaggeration.X, Tolerance);
            Assert.Equal(withoutExaggeration.Y, withExaggeration.Y, Tolerance);
        }

        // ── Scale multiplier ──────────────────────────────────────────────────

        [Fact]
        public void ProjectVertex_ScaleMultiplier_ScalesEffectiveDimensions()
        {
            // With scaleMultiplier=2, the projected offset should be doubled.
            var normal = PerspectiveShapeRenderer.ProjectVertex(
                normalizedPos: new Vector3(0.5f, 0, 0),
                worldPos: Vector2.Zero,
                rotation: Quaternion.Identity,
                L: 10f, W: 5f, exaggeration: 0f,
                scaleMultiplier: 1f);

            var scaled = PerspectiveShapeRenderer.ProjectVertex(
                normalizedPos: new Vector3(0.5f, 0, 0),
                worldPos: Vector2.Zero,
                rotation: Quaternion.Identity,
                L: 10f, W: 5f, exaggeration: 0f,
                scaleMultiplier: 2f);

            Assert.Equal(normal.X * 2f, scaled.X, Tolerance);
        }

        // ── 180-degree rotation ───────────────────────────────────────────────

        [Fact]
        public void ProjectVertex_180DegRotationAroundZ_InvertsXY()
        {
            // Rotate 180 degrees around Z axis → (x, y) → (-x, -y).
            var rot180 = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI);

            var result = PerspectiveShapeRenderer.ProjectVertex(
                normalizedPos: new Vector3(0.5f, 0, 0),
                worldPos: Vector2.Zero,
                rotation: rot180,
                L: 10f, W: 5f, exaggeration: 0f);

            // X = -5, Y ≈ 0
            Assert.Equal(-5f, result.X, Tolerance);
            Assert.Equal(0f, result.Y, Tolerance);
        }
    }
}
