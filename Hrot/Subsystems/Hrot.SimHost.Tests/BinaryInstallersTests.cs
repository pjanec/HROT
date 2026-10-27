using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Fdp.Toolkit.Replication.Attributes;
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

        // ⛔⛔ REMOVED by Q59-E: EntityData_DescriptorDirtyBit_SetAfterNameWrite.
        //
        // It asserted that EntityDataAttributeInstaller sets the dtEntityInfo bit in
        // BinaryPatchContext.DirtyDescriptorMask — i.e. that an INSTALLER names a DESCRIPTOR.
        // 🔒 The user's ruling is that it must not: "attributes are entity-related, network agnostic. In
        // contrary, descriptors are Ned network concept." ⇒ the installer now records only the COMPONENT it
        // wrote, and the world's DescriptorOwnershipMap — fed by the network layer's translators — supplies
        // the descriptors. So the behaviour this asserted is DELIBERATELY GONE.
        //
        // ⭐ The CLAIM it protected is not lost, it moved and got stronger:
        //   TheBinaryApplyTellsSmartEgressTests asserts the dtEntityInfo descriptor really does end up dirty
        //   after a binary Name apply — through SmartEgress, which is what actually drives republication,
        //   rather than through a local mask that AX-015 measured as read by nothing in production.
        // ⚠ Not a "rush removal": the mechanism was measured, the replacement rail is named, and
        //   BinaryPatchContext.MarkDescriptorDirty itself is retained and still exercised by
        //   BinaryInterpreterTests.

        // ── SimTransformAttributeInstaller — P4T2 ────────────────────────────

        [Fact]
        public void SimTransform_FullUpdate_LocalPositionWrittenAsCopy()
        {
            // ⭐ CE-4073 — Position.X/Y/Z are LOCAL ENU metres: a plain copy per axis, no geographic transform in sight.
            var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new SimTransformAttributeInstaller())
                .Build();

            var listCtx = new ListPatchContext(null);
            var ctx     = interpreter.CreateContext(listCtx);
            interpreter.Apply(ctx, new[]
            {
                Float64Record(AttributeIds.PositionX, 34.0),
                Float64Record(AttributeIds.PositionY, 32.0),
                Float64Record(AttributeIds.PositionZ, 100.0),
            });

            ref SimTransform st = ref listCtx.GetUnmanagedComponent<SimTransform>();
            Assert.Equal(new Vector3(34f, 32f, 100f), st.Position);
        }

        [Fact]
        public void SimTransform_PartialUpdate_KeepsTheOtherAxes()
        {
            // ⭐ CE-4073 — the three axes are independent copies, so a patch of ONE keeps the other two BY CONSTRUCTION
            //   (the geodetic installer needed a ToGeodetic pre-fill for this).
            var seedTransform = new SimTransform { Position = new Vector3(30f, 50f, 7f) };
            var listCtx = new ListPatchContext(new List<object> { seedTransform });

            var interpreter = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new SimTransformAttributeInstaller())
                .Build();

            var ctx = interpreter.CreateContext(listCtx);
            interpreter.Apply(ctx, new[] { Float64Record(AttributeIds.PositionY, 32.0) });

            ref SimTransform st = ref listCtx.GetUnmanagedComponent<SimTransform>();
            Assert.Equal(new Vector3(30f, 32f, 7f), st.Position);
        }

        [Fact]
        public void SimTransform_AuthorityGuard_NoWriteWhenDenied()
        {
            var interpreter  = new BinaryInterpreterBuilder<EntityAttributeChange>(r => r.AttributeId)
                .AddInstaller(new SimTransformAttributeInstaller())
                .Build();

            var denyCtx   = new DenyAuthPatchContext();
            var binaryCtx = interpreter.CreateContext(denyCtx);
            interpreter.Apply(binaryCtx, new[] { Float64Record(AttributeIds.PositionX, 10.0) });

            // The typed registration's authority gate: a node that does not own SimTransform never even fetches it.
            Assert.Equal(0, denyCtx.GetManagedComponentCallCount);
            Assert.False(denyCtx.UnmanagedFetched);
        }

        // ⛔⛔ REMOVED by Q59-E, for the same reason as the EntityData one above — see that comment.
        //    The SimTransform/dtWorldPos pairing is now asserted by TheJsonAndBinaryPathsAgreeTests and
        //    TheDescriptorMapIsWiredTests, through the map rather than a local mask.

        // ── BuildBinaryInterpreter — P4T3 ─────────────────────────────────────

        [Fact]
        public void BuildBinaryInterpreter_AppliesNameAndLocalPosition()
        {
            var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter();

            var listCtx = new ListPatchContext(null);
            var ctx     = interpreter.CreateContext(listCtx);
            interpreter.Apply(ctx, new[]
            {
                StringRecord(AttributeIds.Name,    "Delta"),
                Float64Record(AttributeIds.PositionX, 34.0),
                Float64Record(AttributeIds.PositionY, 32.0),
                Float64Record(AttributeIds.PositionZ, 100.0),
            });

            var data2 = listCtx.GetUnmanagedComponent<Fdp.Core.EntityInfo>();
            Assert.Equal("Delta", data2.Name.ToString());

            ref SimTransform st = ref listCtx.GetUnmanagedComponent<SimTransform>();
            Assert.Equal(new Vector3(34f, 32f, 100f), st.Position);
        }

        [Fact]
        public void BuildBinaryInterpreter_RetiredGeodeticIds_AreNotHandled()
        {
            // ⭐ CE-4073 — ids 10–12 (the retired geodetic latitude / longitude / altitude) have no handler any more: a stale sender is
            //   ignored, never read as a metre.
            var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter();

            var listCtx = new ListPatchContext(new List<object> { new SimTransform { Position = new Vector3(1f, 2f, 3f) } });
            var ctx     = interpreter.CreateContext(listCtx);
            var ex = Record.Exception(() => interpreter.Apply(ctx, new[]
            {
                Float64Record(AttributeIds.RetiredGeoLat, 32.0),
                Float64Record(AttributeIds.RetiredGeoLon, 34.0),
                Float64Record(AttributeIds.RetiredGeoAlt, 100.0),
            }));

            Assert.Null(ex);
            ref SimTransform st = ref listCtx.GetUnmanagedComponent<SimTransform>();
            Assert.Equal(new Vector3(1f, 2f, 3f), st.Position);
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
        public void BuildEdgeCompiler_LocalPosition_EmitsThreeRecords()
        {
            var compiler = AttributeCompilerFactory.BuildEdgeCompiler();
            var buffer   = new AttributeRecord[8];
            var emitter  = new NedAttributeRecordEmitter(buffer);

            compiler.Compile(
                System.Text.Encoding.UTF8.GetBytes(
                    "{\"Position\":{\"X\":34.0,\"Y\":32.0,\"Z\":100.0}}"),
                emitter);

            Assert.Equal(3, emitter.Count);
            Assert.Contains(emitter.Written.ToArray(), r => r.AttributeId == AttributeIds.PositionX);
            Assert.Contains(emitter.Written.ToArray(), r => r.AttributeId == AttributeIds.PositionY);
            Assert.Contains(emitter.Written.ToArray(), r => r.AttributeId == AttributeIds.PositionZ);
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
            public bool UnmanagedFetched { get; private set; }

            public ref T GetUnmanagedComponent<T>() where T : struct
            {
                UnmanagedFetched = true;
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
    }
}
