using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CycloneDDS.Schema;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.IG.Abstractions;
using Hrot.IG.Gizmos;
using Hrot.Map.Definitions;
using Xunit;

namespace Hrot.IG.Tests.Gizmos
{
    // ========================================================================
    // Helper: capturing DDS writer for IGCapabilitiesAnnounce
    // ========================================================================

    internal sealed class CapturingDdsWriter<T> : IDdsWriter<T>
    {
        public readonly List<T> Written = new();
        public void Write(T sample) => Written.Add(sample);
    }

    // ========================================================================
    // SC-GZ015: GlobalDebugSettings ECS singleton
    // ========================================================================

    public class GlobalDebugSettingsTests
    {
        [Fact]
        public void SC_GZ015_1_HasSingleton_AfterSetSingleton_IsTrue()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<GlobalDebugSettings>();

            repo.SetSingleton(new GlobalDebugSettings
            {
                ForceAllGizmosVisible = true,
                DebugLayerMask = 0xFFFF,
            });

            Assert.True(repo.HasSingleton<GlobalDebugSettings>());
        }

        [Fact]
        public void SC_GZ015_2_MarshalSizeOf_Is_4_Bytes()
        {
            // [MarshalAs(UnmanagedType.I1)] bool = 1 byte
            // 1 byte alignment padding
            // ushort = 2 bytes
            // float MaxGizmoFrameMs = 4 bytes (GZ036: added in BATCH-13)
            // Total = 8 bytes
            Assert.Equal(8, Marshal.SizeOf<GlobalDebugSettings>());
        }

        [Fact]
        public void SC_GZ015_3_DataPolicy_Transient_IsSet()
        {
            var attr = (DataPolicyAttribute?)Attribute.GetCustomAttribute(
                typeof(GlobalDebugSettings), typeof(DataPolicyAttribute));
            Assert.NotNull(attr);
            Assert.True((attr!.Policy & DataPolicy.Transient) != 0);
        }

        [Fact]
        public void SC_GZ015_4_ComponentId_Is_185()
        {
            var attr = (ComponentIdAttribute?)Attribute.GetCustomAttribute(
                typeof(GlobalDebugSettings), typeof(ComponentIdAttribute));
            Assert.NotNull(attr);
            Assert.Equal(185, attr!.Id);
        }
    }

    // ========================================================================
    // SC-GZ018: IGCapabilitiesAnnounce and IGCapabilitiesPublisherSystem
    // ========================================================================

    public class IGCapabilitiesTests
    {
        [Fact]
        public void SC_GZ018_1_IGCapabilitiesAnnounce_HasCorrectTopicName()
        {
            var attr = (DdsTopicAttribute?)Attribute.GetCustomAttribute(
                typeof(IGCapabilitiesAnnounce), typeof(DdsTopicAttribute));
            Assert.NotNull(attr);
            Assert.Equal("IGCapabilitiesAnnounce", attr!.TopicName);
        }

        [Fact]
        public void SC_GZ018_2_ExecuteOnce_PublishesExactlyOneRecord()
        {
            using var repo = new EntityRepository();
            var writer = new CapturingDdsWriter<IGCapabilitiesAnnounce>();
            var sys = new IGCapabilitiesPublisherSystem(nodeId: 7, writer: writer);

            sys.Execute(repo, 0f);

            Assert.Single(writer.Written);
            Assert.Equal(PipelineTarget.Map2D, writer.Written[0].SupportedTargets);
            Assert.Equal(0xFFFF, writer.Written[0].SupportedLayerMask);
        }

        [Fact]
        public void SC_GZ018_3_ExecuteTwice_PublishesOnlyOnce()
        {
            using var repo = new EntityRepository();
            var writer = new CapturingDdsWriter<IGCapabilitiesAnnounce>();
            var sys = new IGCapabilitiesPublisherSystem(nodeId: 1, writer: writer);

            sys.Execute(repo, 0f);
            sys.Execute(repo, 0f);

            Assert.Single(writer.Written);
        }
    }

    // ========================================================================
    // SC-GZ044: IGCapabilitiesPublisherSystem DDS hygiene + reflection
    // ========================================================================

    public class IGCapabilitiesPublisherSystemGZ044Tests
    {
        // SC-GZ044-1: IGCapabilitiesAnnounce has a RegisteredGizmosJson field of type string
        [Fact]
        public void SC_GZ044_1_RegisteredGizmosJson_FieldExists()
        {
            var field = typeof(IGCapabilitiesAnnounce).GetField("RegisteredGizmosJson");
            Assert.NotNull(field);
            Assert.Equal(typeof(string), field!.FieldType);
        }

        // SC-GZ044-2: Execute sets RegisteredGizmosJson = "[]"
        [Fact]
        public void SC_GZ044_2_Execute_Sets_RegisteredGizmosJson_Empty()
        {
            using var repo = new EntityRepository();
            var writer = new CapturingDdsWriter<IGCapabilitiesAnnounce>();
            var sys = new IGCapabilitiesPublisherSystem(nodeId: 5, writer: writer);
            sys.Execute(repo, 0f);
            Assert.Single(writer.Written);
            Assert.Equal("[]", writer.Written[0].RegisteredGizmosJson);
        }

        // SC-GZ044-3: RegisteredGizmosJson and LayerNamesJson are independent fields
        [Fact]
        public void SC_GZ044_3_RegisteredGizmosJson_And_LayerNamesJson_AreDistinctFields()
        {
            var announce = new IGCapabilitiesAnnounce
            {
                LayerNamesJson       = "layers",
                RegisteredGizmosJson = "gizmos",
            };
            Assert.Equal("layers", announce.LayerNamesJson);
            Assert.Equal("gizmos", announce.RegisteredGizmosJson);
        }

        // SC-GZ044-4: SupportedShapeMask field is uint
        [Fact]
        public void SC_GZ044_4_SupportedShapeMask_IsUint()
        {
            var field = typeof(IGCapabilitiesAnnounce).GetField("SupportedShapeMask");
            Assert.NotNull(field);
            Assert.Equal(typeof(uint), field!.FieldType);
        }

        // SC-GZ044-5: SupportedShapeMask has bits for all DebugPrimitiveShape values
        [Fact]
        public void SC_GZ044_5_SupportedShapeMask_CoversAllShapes()
        {
            using var repo = new EntityRepository();
            var writer = new CapturingDdsWriter<IGCapabilitiesAnnounce>();
            var sys = new IGCapabilitiesPublisherSystem(nodeId: 9, writer: writer);
            sys.Execute(repo, 0f);

            uint mask = writer.Written[0].SupportedShapeMask;
            foreach (DebugPrimitiveShape shape in Enum.GetValues<DebugPrimitiveShape>())
            {
                uint bit = 1u << (int)shape;
                Assert.True((mask & bit) != 0, $"Shape {shape} (bit {(int)shape}) missing from SupportedShapeMask");
            }
        }

        // SC-GZ044-6: SupportedLayerMask == 0xFFFF
        [Fact]
        public void SC_GZ044_6_SupportedLayerMask_Is_0xFFFF()
        {
            using var repo = new EntityRepository();
            var writer = new CapturingDdsWriter<IGCapabilitiesAnnounce>();
            var sys = new IGCapabilitiesPublisherSystem(nodeId: 3, writer: writer);
            sys.Execute(repo, 0f);
            Assert.Equal(0xFFFF, writer.Written[0].SupportedLayerMask);
        }

        // SC-GZ044-7: Execute twice only writes once (gated by _published)
        [Fact]
        public void SC_GZ044_7_Execute_Twice_WritesOnce()
        {
            using var repo = new EntityRepository();
            var writer = new CapturingDdsWriter<IGCapabilitiesAnnounce>();
            var sys = new IGCapabilitiesPublisherSystem(nodeId: 2, writer: writer);
            sys.Execute(repo, 0f);
            sys.Execute(repo, 0f);
            Assert.Single(writer.Written);
        }
    }
}
