using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CycloneDDS.Schema;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.Player.Abstractions;
using Hrot.Player.Gizmos;
using Hrot.Common.Components;
using Hrot.Map.Definitions;
using Xunit;

namespace Hrot.Player.Tests.Gizmos
{
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
        public void SC_GZ015_2_MarshalSizeOf_Is_12_Bytes()
        {
            // [MarshalAs(UnmanagedType.I1)] bool ForceAllGizmosVisible  = 1 byte (offset 0)
            // 1 byte alignment padding                                  = 1 byte
            // ushort DebugLayerMask                                     = 2 bytes (offset 2)
            // float MaxGizmoFrameMs (GZ036: added in BATCH-13)          = 4 bytes (offset 4)
            // [MarshalAs(UnmanagedType.I1)] bool AutoEnableAiTracing    = 1 byte (offset 8) — behav-diag-1
            // 3 bytes trailing alignment padding                        = 3 bytes
            // Total = 12 bytes
            Assert.Equal(12, Marshal.SizeOf<GlobalDebugSettings>());
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
}
