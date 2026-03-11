using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Components;
using Bagira.Map.Common.Replication.Utils;
using Fdp.Kernel;
using Fdp.Modules.Geographic;

namespace Bagira.Map.Common.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EntityAttributeCompiler"/>.
    /// </summary>
    public class EntityAttributeCompilerTests
    {
        // ── Stubs ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Trivial geographic transform stub: lat→Y, lon→X, alt→Z.
        /// Used so tests can assert on concrete Cartesian values without a real
        /// WGS84 projection.
        /// </summary>
        private sealed class StubGeoTransform : IGeographicTransform
        {
            public void SetOrigin(double lat, double lon, double alt) { }
            public Vector3 ToCartesian(double lat, double lon, double alt)
                => new Vector3((float)lon, (float)lat, (float)alt);
            public (double lat, double lon, double alt) ToGeodetic(Vector3 p)
                => (p.Y, p.X, p.Z);
        }

        private static readonly StubGeoTransform GeoTransform = new();

        // ── CompileOverrides — eaName ─────────────────────────────────────────

        [Fact]
        public void CompileOverrides_WithNameAttribute_ProducesIgEntityData()
        {
            var attrs = new[] {
                new EntityAttributePayload { _d = EntityAttribute.eaName, Name = "Bravo-7" }
            };

            var result = EntityAttributeCompiler.CompileOverrides(attrs, null);

            Assert.Single(result);
            var data = Assert.IsType<IgEntityData>(result[0]);
            Assert.Equal("Bravo-7", data.Name);
        }

        [Fact]
        public void CompileOverrides_WithNameAttribute_PreservesExistingAffiliationFromBase()
        {
            var baseComp = new IgEntityData { Name = "OldName", ForceId = ForceId.Hostile };
            var attrs = new[] {
                new EntityAttributePayload { _d = EntityAttribute.eaName, Name = "NewName" }
            };

            var result = EntityAttributeCompiler.CompileOverrides(attrs, new List<object> { baseComp });

            var data = Assert.IsType<IgEntityData>(result[0]);
            Assert.Equal("NewName", data.Name);
            Assert.Equal(ForceId.Hostile, data.ForceId);  // affiliation preserved
        }

        [Fact]
        public void CompileOverrides_WithNameAttribute_DoesNotDuplicateIgEntityData()
        {
            // If two hypothetical attributes both produce IgEntityData, we must have exactly one.
            var attrs = new[] {
                new EntityAttributePayload { _d = EntityAttribute.eaName, Name = "Tank-1" },
            };
            var baseComp = new IgEntityData { Name = "OldName" };

            var result = EntityAttributeCompiler.CompileOverrides(attrs, new List<object> { baseComp });

            int count = 0;
            foreach (var c in result)
                if (c is IgEntityData) count++;
            Assert.Equal(1, count);
        }

        // ── CompileOverrides — eaGeoPosition ─────────────────────────────────

        [Fact]
        public void CompileOverrides_WithGeoPositionAttribute_ProducesSimTransform()
        {
            var attrs = new[] {
                new EntityAttributePayload
                {
                    _d          = EntityAttribute.eaGeoPosition,
                    GeoPosition = new GeoPosition { Latitude = 10.0, Longitude = 20.0, Altitude = 100.0 }
                }
            };

            var result = EntityAttributeCompiler.CompileOverrides(attrs, null, GeoTransform);

            Assert.Single(result);
            var st = Assert.IsType<SimTransform>(result[0]);
            // StubGeoTransform: X=lon=20, Y=lat=10, Z=alt=100
            Assert.Equal(20f, st.Position.X, precision: 3);
            Assert.Equal(10f, st.Position.Y, precision: 3);
            Assert.Equal(100f, st.Position.Z, precision: 3);
        }

        [Fact]
        public void CompileOverrides_WithGeoPositionAttribute_PreservesExistingRotation()
        {
            var existingRotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
            var baseSt = new SimTransform { Position = Vector3.Zero, Rotation = existingRotation };
            var attrs = new[] {
                new EntityAttributePayload
                {
                    _d          = EntityAttribute.eaGeoPosition,
                    GeoPosition = new GeoPosition { Latitude = 5.0, Longitude = 10.0 }
                }
            };

            var result = EntityAttributeCompiler.CompileOverrides(attrs, new List<object> { baseSt }, GeoTransform);

            var st = Assert.IsType<SimTransform>(result[0]);
            Assert.Equal(existingRotation, st.Rotation);  // rotation preserved
        }

        [Fact]
        public void CompileOverrides_WithGeoPositionButNoGeoTransform_SkipsGeoEntry()
        {
            var attrs = new[] {
                new EntityAttributePayload
                {
                    _d          = EntityAttribute.eaGeoPosition,
                    GeoPosition = new GeoPosition { Latitude = 10.0, Longitude = 20.0 }
                }
            };

            // No geo transform: cannot convert, attribute should be skipped
            var result = EntityAttributeCompiler.CompileOverrides(attrs, null, geoTransform: null);

            Assert.Empty(result);
        }

        // ── CompileOverrides — null / empty inputs ────────────────────────────

        [Fact]
        public void CompileOverrides_NullAttributes_ReturnsEmpty()
        {
            var result = EntityAttributeCompiler.CompileOverrides(null, null);
            Assert.Empty(result);
        }

        [Fact]
        public void CompileOverrides_EmptyAttributes_ReturnsBaseComponentsCopied()
        {
            var base1 = new IgEntityData { Name = "KeepMe" };
            var result = EntityAttributeCompiler.CompileOverrides(
                Array.Empty<EntityAttributePayload>(),
                new List<object> { base1 });

            // No attributes — returns copy of base list unchanged
            Assert.Single(result);
        }

        // ── CompileOverrides — mixed attributes ───────────────────────────────

        [Fact]
        public void CompileOverrides_NameAndGeoAttributesTogether_ProducesTwoComponents()
        {
            var attrs = new[] {
                new EntityAttributePayload { _d = EntityAttribute.eaName, Name = "Alpha" },
                new EntityAttributePayload
                {
                    _d          = EntityAttribute.eaGeoPosition,
                    GeoPosition = new GeoPosition { Latitude = 1.0, Longitude = 2.0 }
                }
            };

            var result = EntityAttributeCompiler.CompileOverrides(attrs, null, GeoTransform);

            Assert.Equal(2, result.Count);
            Assert.Contains(result, c => c is IgEntityData);
            Assert.Contains(result, c => c is SimTransform);
        }

        // ── CompileFromWorld ──────────────────────────────────────────────────

        [Fact]
        public void CompileFromWorld_WithNameAttribute_ReadsExistingNameAndOverrides()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();
            world.SetManagedComponent(entity, new IgEntityData
            {
                Name    = "OldName",
                ForceId = ForceId.Friend,
            });

            var attrs = new[] {
                new EntityAttributePayload { _d = EntityAttribute.eaName, Name = "NewName" }
            };

            var result = EntityAttributeCompiler.CompileFromWorld(attrs, world, entity);

            var data = Assert.IsType<IgEntityData>(result[0]);
            Assert.Equal("NewName", data.Name);
            Assert.Equal(ForceId.Friend, data.ForceId); // other fields preserved from ECS
        }

        [Fact]
        public void CompileFromWorld_EntityWithNoIgEntityData_ProducesDefaultWithName()
        {
            using var world = CreateWorld();
            var entity = world.CreateEntity();

            var attrs = new[] {
                new EntityAttributePayload { _d = EntityAttribute.eaName, Name = "BrandNew" }
            };

            var result = EntityAttributeCompiler.CompileFromWorld(attrs, world, entity);

            var data = Assert.IsType<IgEntityData>(result[0]);
            Assert.Equal("BrandNew", data.Name);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<SimTransform>();
            world.RegisterManagedComponent<IgEntityData>();
            return world;
        }
    }
}
