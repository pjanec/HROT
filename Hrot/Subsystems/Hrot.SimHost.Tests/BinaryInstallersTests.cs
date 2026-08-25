using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.SimHost.Installers;
using Fdp.Toolkit.Replication.Patching;
using Hrot.Map.Common.Replication;
using Fdp.Core;
using Fdp.Modules.Geographic;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for P4T1 (<see cref="EntityDataAttributeInstaller"/>),
    /// P4T2 (<see cref="SimTransformAttributeInstaller">) and P4T3
    /// (<see cref="AttributeCompilerFactory.BuildBinaryInterpreter"/>).
    ///
    /// All tests use <see cref="ListPatchContext"/> for component access and
    /// purpose-built stubs where authority denial or call-counting is required.
    /// </summary>
    public class BinaryInstallersTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        // ⭐⭐ AX-005a — these build the FDP-INTERNAL record, not the DDS `AttributeRecord`. 📌 R-134: the
        //    installers no longer speak a network type, so neither do their rails. The DDS shape is
        //    exercised where it belongs — at the boundary, in AttributeRecordConversionTests.
        private static EntityAttributeChange StringRecord(ushort id, string value) =>
            new EntityAttributeChange { AttributeId = id, Value = AttributeValue.FromString(value) };

        private static EntityAttributeChange Int32Record(ushort id, int value) =>
            new EntityAttributeChange { AttributeId = id, Value = AttributeValue.FromInt(value) };

        private static EntityAttributeChange Float64Record(ushort id, double value) =>
            new EntityAttributeChange { AttributeId = id, Value = AttributeValue.FromDouble(value) };

        // ── EntityDataAttributeInstaller — P4T1 ───────────────────────────────

        [Fact]
        public void EntityData_Name_WrittenToComponent()
        {
            var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new EntityDataAttributeInstaller())
                .Build();

            var listCtx = new ListPatchContext(null);
            var ctx     = interpreter.CreateContext(listCtx);
            interpreter.Apply(ctx, new[] { StringRecord(AttributeIds.Name, "Bravo") });

            var data = listCtx.GetUnmanagedComponent<Fdp.Core.EntityInfo>();
            Assert.Equal("Bravo", data.Name.ToString());
        }

        [Fact]
        public void EntityData_Affiliation_StringVariant_WrittenToComponent()
        {
            var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new EntityDataAttributeInstaller())
                .Build();

            var listCtx = new ListPatchContext(null);
            var ctx     = interpreter.CreateContext(listCtx);
            interpreter.Apply(ctx, new[] { StringRecord(AttributeIds.Affiliation, "FORCE_FRIENDLY") });

            ref var data0 = ref listCtx.GetUnmanagedComponent<Fdp.Core.EntityInfo>();
            Assert.Equal(ForceId.Friend, data0.ForceId);
        }

        [Fact]
        public void EntityData_Affiliation_IntVariant_WrittenToComponent()
        {
            // Use the FORCE_FRIENDLY enum int value to avoid calling the internal factory method.
            int friendlyInt = (int)eForceIdentifier.FORCE_FRIENDLY;
            var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new EntityDataAttributeInstaller())
                .Build();

            var listCtx = new ListPatchContext(null);
            var ctx     = interpreter.CreateContext(listCtx);
            interpreter.Apply(ctx, new[] { Int32Record(AttributeIds.Affiliation, friendlyInt) });

            ref var data3 = ref listCtx.GetUnmanagedComponent<Fdp.Core.EntityInfo>();
            Assert.Equal(ForceId.Friend, data3.ForceId);
        }

        [Fact]
        public void EntityData_AuthorityGuard_NoWriteWhenDenied()
        {
            var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new EntityDataAttributeInstaller())
                .Build();

            var denyCtx  = new DenyAuthPatchContext();
            var binaryCtx = interpreter.CreateContext(denyCtx);
            interpreter.Apply(binaryCtx, new[] { StringRecord(AttributeIds.Name, "Should Not Apply") });

            // The deny context should have received no unmanaged-component access.
            Assert.Equal(0, denyCtx.GetManagedComponentCallCount);
        }

        [Fact]
        public void EntityData_DescriptorDirtyBit_SetAfterNameWrite()
        {
            var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new EntityDataAttributeInstaller())
                .Build();

            var listCtx = new ListPatchContext(null);
            var ctx     = interpreter.CreateContext(listCtx);
            interpreter.Apply(ctx, new[] { StringRecord(AttributeIds.Name, "Alpha") });

            // dtEntityInfo ordinal must be set in DirtyDescriptorMask.
            long entityInfoOrdinal = (long)EDescriptorType.dtEntityInfo;
            Assert.NotEqual(0UL, ctx.DirtyDescriptorMask & (1UL << (int)entityInfoOrdinal));
        }

        // ── SimTransformAttributeInstaller — P4T2 ────────────────────────────

        [Fact]
        public void SimTransform_FullUpdate_CartesianPositionWritten()
        {
            var geo         = new FactoryTestGeoTransform(); // lat→Y, lon→X, alt→Z
            var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new SimTransformAttributeInstaller(geo))
                .Build();

            var listCtx = new ListPatchContext(null);
            var ctx     = interpreter.CreateContext(listCtx);
            interpreter.Apply(ctx, new[]
            {
                Float64Record(AttributeIds.GeoLat, 32.0),
                Float64Record(AttributeIds.GeoLon, 34.0),
                Float64Record(AttributeIds.GeoAlt, 100.0),
            });

            // FactoryTestGeoTransform.ToCartesian → new Vector3((float)lon, (float)lat, (float)alt)
            ref SimTransform st = ref listCtx.GetUnmanagedComponent<SimTransform>();
            Assert.Equal(new Vector3(34f, 32f, 100f), st.Position);
        }

        [Fact]
        public void SimTransform_PartialUpdate_PreFillsMissingCoords()
        {
            // Seed with a known existing position: lat=50, lon=30, alt=0
            // via the trivial transform: position = (lon, lat, alt) = (30, 50, 0)
            var seedTransform = new SimTransform { Position = new Vector3(30f, 50f, 0f) };
            var listCtx = new ListPatchContext(new List<object> { seedTransform });

            var geo         = new FactoryTestGeoTransform(); // lat→Y, lon→X, alt→Z
            var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new SimTransformAttributeInstaller(geo))
                .Build();

            var ctx = interpreter.CreateContext(listCtx);
            // Only update latitude to 32; longitude and altitude should be preserved.
            interpreter.Apply(ctx, new[] { Float64Record(AttributeIds.GeoLat, 32.0) });

            // Expected: ToCartesian(32, 30, 0) = Vector3(30, 32, 0)
            ref SimTransform st = ref listCtx.GetUnmanagedComponent<SimTransform>();
            Assert.Equal(new Vector3(30f, 32f, 0f), st.Position);
        }

        [Fact]
        public void SimTransform_MultipleRecordsSameApply_ToCartesianCalledOnce()
        {
            var geo         = new CountingGeoTransform();
            var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new SimTransformAttributeInstaller(geo))
                .Build();

            var listCtx = new ListPatchContext(null);
            var ctx     = interpreter.CreateContext(listCtx);
            interpreter.Apply(ctx, new[]
            {
                Float64Record(AttributeIds.GeoLat, 10.0),
                Float64Record(AttributeIds.GeoLon, 20.0),
                Float64Record(AttributeIds.GeoAlt, 30.0),
            });

            Assert.Equal(1, geo.ToCartesianCallCount);
        }

        [Fact]
        public void SimTransform_AuthorityGuard_NoWriteWhenDenied()
        {
            var geo          = new CountingGeoTransform();
            var interpreter  = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new SimTransformAttributeInstaller(geo))
                .Build();

            var denyCtx   = new DenyAuthPatchContext();
            var binaryCtx = interpreter.CreateContext(denyCtx);
            interpreter.Apply(binaryCtx, new[] { Float64Record(AttributeIds.GeoLat, 10.0) });

            Assert.Equal(0, geo.ToCartesianCallCount);
        }

        [Fact]
        public void SimTransform_DescriptorDirtyBit_SetAfterPositionFlush()
        {
            var geo         = new FactoryTestGeoTransform();
            var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new SimTransformAttributeInstaller(geo))
                .Build();

            var listCtx = new ListPatchContext(null);
            var ctx     = interpreter.CreateContext(listCtx);
            interpreter.Apply(ctx, new[] { Float64Record(AttributeIds.GeoLat, 32.0) });

            long geoOrdinal = (long)EDescriptorType.dtWorldPos;
            Assert.NotEqual(0UL, ctx.DirtyDescriptorMask & (1UL << (int)geoOrdinal));
        }

        // ── BuildBinaryInterpreter — P4T3 ─────────────────────────────────────

        [Fact]
        public void BuildBinaryInterpreter_WithGeoTransform_AppliesNameAndPosition()
        {
            var geo         = new FactoryTestGeoTransform();
            var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter(geo);

            var listCtx = new ListPatchContext(null);
            var ctx     = interpreter.CreateContext(listCtx);
            interpreter.Apply(ctx, new[]
            {
                StringRecord(AttributeIds.Name,    "Delta"),
                Float64Record(AttributeIds.GeoLat, 32.0),
                Float64Record(AttributeIds.GeoLon, 34.0),
                Float64Record(AttributeIds.GeoAlt, 100.0),
            });

            var data2 = listCtx.GetUnmanagedComponent<Fdp.Core.EntityInfo>();
            Assert.Equal("Delta", data2.Name.ToString());

            ref SimTransform st = ref listCtx.GetUnmanagedComponent<SimTransform>();
            Assert.Equal(new Vector3(34f, 32f, 100f), st.Position);
        }

        [Fact]
        public void BuildBinaryInterpreter_NoGeoTransform_PositionHandlerAbsent()
        {
            // When geoTransform is null, no SimTransformAttributeInstaller is added.
            // Sending GeoLat records should be silently ignored (no exception).
            var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter(null);

            var listCtx = new ListPatchContext(null);
            var ctx     = interpreter.CreateContext(listCtx);
            var ex = Record.Exception(() =>
                interpreter.Apply(ctx, new[] { Float64Record(AttributeIds.GeoLat, 32.0) }));

            Assert.Null(ex);
        }

        // ── BuildEdgeCompiler — P2T2 ──────────────────────────────────────────

        [Fact]
        public void BuildEdgeCompiler_Name_EmitsNameRecord()
        {
            var compiler = AttributeCompilerFactory.BuildEdgeCompiler();
            var buffer   = new AttributeRecord[8];
            var emitter  = new NedAttributeRecordEmitter(buffer);

            compiler.Compile(
                System.Text.Encoding.UTF8.GetBytes("{\"Name\":\"Charlie\"}"),
                emitter);

            Assert.Equal(1, emitter.Count);
            Assert.Equal(AttributeIds.Name, emitter.Written[0].AttributeId);
            Assert.Equal("Charlie", emitter.Written[0].Value.StringValue);
        }

        [Fact]
        public void BuildEdgeCompiler_GeoPosition_EmitsThreeRecords()
        {
            var compiler = AttributeCompilerFactory.BuildEdgeCompiler();
            var buffer   = new AttributeRecord[8];
            var emitter  = new NedAttributeRecordEmitter(buffer);

            compiler.Compile(
                System.Text.Encoding.UTF8.GetBytes(
                    "{\"GeoPosition\":{\"Latitude\":32.0,\"Longitude\":34.0,\"Altitude\":100.0}}"),
                emitter);

            Assert.Equal(3, emitter.Count);
            Assert.Contains(emitter.Written.ToArray(), r => r.AttributeId == AttributeIds.GeoLat);
            Assert.Contains(emitter.Written.ToArray(), r => r.AttributeId == AttributeIds.GeoLon);
            Assert.Contains(emitter.Written.ToArray(), r => r.AttributeId == AttributeIds.GeoAlt);
        }

        // ── Private stubs ─────────────────────────────────────────────────────

        /// <summary>
        /// IEntityPatchContext whose authority methods always return false.
        /// Backed by a minimal managed-component dict so managed lookups don't throw;
        /// tracks the number of GetManagedComponent calls.
        /// </summary>
        private sealed class DenyAuthPatchContext : IEntityPatchContext
        {
            private readonly Dictionary<Type, object> _managed = new();
            public int GetManagedComponentCallCount { get; private set; }

            public ref T GetUnmanagedComponent<T>() where T : struct
            {
                // Return a default in a static field — good enough for the deny tests.
                return ref _UnmanagedHolder<T>.Value;
            }

            public T GetManagedComponent<T>() where T : class
            {
                GetManagedComponentCallCount++;
                if (!_managed.TryGetValue(typeof(T), out var raw))
                {
                    raw = Activator.CreateInstance<T>()!;
                    _managed[typeof(T)] = raw;
                }
                return (T)raw;
            }

            public void FlushDirtyMarks() { }
            public bool CanWrite<T>()       where T : struct => false;
            public bool CanWriteManaged<T>() where T : class  => false;

            // Per-type unmanaged storage (static, not per-instance, but fine for tests).
            private static class _UnmanagedHolder<T> where T : struct
            {
                public static T Value;
            }
        }

        /// <summary>
        /// Geographic transform stub whose conversion semantics match
        /// <see cref="FactoryTestGeoTransform"/> but also counts <c>ToCartesian</c> calls.
        /// </summary>
        private sealed class CountingGeoTransform : IGeographicTransform
        {
            public int ToCartesianCallCount { get; private set; }

            public void SetOrigin(double lat, double lon, double alt) { }

            public Vector3 ToCartesian(double lat, double lon, double alt)
            {
                ToCartesianCallCount++;
                return new Vector3((float)lon, (float)lat, (float)alt);
            }

            public (double lat, double lon, double alt) ToGeodetic(Vector3 p)
                => (p.Y, p.X, p.Z);
        }
    }
}
