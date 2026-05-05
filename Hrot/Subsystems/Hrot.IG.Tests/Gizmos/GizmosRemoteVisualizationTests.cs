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
            // Total = 4 bytes
            Assert.Equal(4, Marshal.SizeOf<GlobalDebugSettings>());
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
}
