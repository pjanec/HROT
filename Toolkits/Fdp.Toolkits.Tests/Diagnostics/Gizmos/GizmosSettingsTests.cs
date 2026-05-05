using System.IO;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // ==========================================================================
    // SC-GZ007-6, SC-GZ007-7: GizmoSettingValue struct
    // ==========================================================================

    public class GizmoSettingValueTests
    {
        [Fact]
        public void SC_GZ007_7_SizeOf_Is_8_Bytes()
        {
            Assert.Equal(8, Marshal.SizeOf<GizmoSettingValue>());
        }

        [Fact]
        public void SC_GZ007_6_Float_RoundTrips()
        {
            var v = GizmoSettingValue.From(3.14f);
            Assert.Equal(SettingType.Float32, v.Type);
            Assert.Equal(3.14f, v.FloatValue);
        }

        [Fact]
        public void Bool_From_RoundTrips()
        {
            var t = GizmoSettingValue.From(true);
            var f = GizmoSettingValue.From(false);
            Assert.Equal(SettingType.Bool, t.Type);
            Assert.True(t.BoolValue);
            Assert.False(f.BoolValue);
        }

        [Fact]
        public void Int32_From_RoundTrips()
        {
            var v = GizmoSettingValue.From(42);
            Assert.Equal(SettingType.Int32, v.Type);
            Assert.Equal(42, v.IntValue);
        }

        [Fact]
        public void Equality_SameValues_AreEqual()
        {
            Assert.Equal(GizmoSettingValue.From(true),   GizmoSettingValue.From(true));
            Assert.Equal(GizmoSettingValue.From(42),     GizmoSettingValue.From(42));
            Assert.Equal(GizmoSettingValue.From(1.5f),   GizmoSettingValue.From(1.5f));
        }

        [Fact]
        public void Equality_DifferentValues_AreNotEqual()
        {
            Assert.NotEqual(GizmoSettingValue.From(true), GizmoSettingValue.From(false));
            // Int 1 vs Float 1.0f — different Type tags
            Assert.NotEqual(GizmoSettingValue.From(1), GizmoSettingValue.From(1.0f));
        }

        [Fact]
        public void Operators_EqualAndNotEqual_Work()
        {
            var a = GizmoSettingValue.From(true);
            var b = GizmoSettingValue.From(true);
            var c = GizmoSettingValue.From(false);
            Assert.True(a == b);
            Assert.True(a != c);
        }
    }

    // ==========================================================================
    // SC-GZ007-1 through SC-GZ007-5: GizmoSettingsRegistry
    // ==========================================================================

    public class GizmoSettingsRegistryTests
    {
        [Fact]
        public void SC_GZ007_1_Register_Read_Returns_Default()
        {
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("NavMesh.ShowGrid", GizmoSettingValue.From(false));
            var hash = GizmoSettingsRegistry.ComputeHash("NavMesh.ShowGrid");
            var v = reg.Read(hash);
            Assert.Equal(SettingType.Bool, v.Type);
            Assert.False(v.BoolValue);
        }

        [Fact]
        public void SC_GZ007_2_Write_Then_Read_Returns_NewValue()
        {
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("ShowGrid", GizmoSettingValue.From(false));
            var hash = GizmoSettingsRegistry.ComputeHash("ShowGrid");
            reg.Write(hash, GizmoSettingValue.From(true));
            Assert.True(reg.Read(hash).BoolValue);
        }

        [Fact]
        public void SC_GZ007_3_ResetToDefault_Restores_Original()
        {
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("Thickness", GizmoSettingValue.From(1.0f));
            var hash = GizmoSettingsRegistry.ComputeHash("Thickness");
            reg.Write(hash, GizmoSettingValue.From(5.0f));
            reg.ResetToDefault(hash);
            Assert.Equal(1.0f, reg.Read(hash).FloatValue);
        }

        [Fact]
        public void SC_GZ007_4_Read_Unregistered_Hash_Returns_Default()
        {
            var reg = new GizmoSettingsRegistry();
            var v = reg.Read(0xDEADBEEFu);
            Assert.Equal(default(GizmoSettingValue), v);
        }

        [Fact]
        public void SC_GZ007_5_Two_Distinct_Keys_Are_Isolated()
        {
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("KeyAlpha", GizmoSettingValue.From(false));
            reg.RegisterSetting("KeyBeta",  GizmoSettingValue.From(10));
            var hashA = GizmoSettingsRegistry.ComputeHash("KeyAlpha");
            var hashB = GizmoSettingsRegistry.ComputeHash("KeyBeta");

            reg.Write(hashA, GizmoSettingValue.From(true));

            // Write to A must not affect B
            Assert.Equal(10, reg.Read(hashB).IntValue);
        }

        [Fact]
        public void IsDirty_Transitions_Correctly()
        {
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("k", GizmoSettingValue.From(false));
            Assert.False(reg.IsDirty);

            var hash = GizmoSettingsRegistry.ComputeHash("k");
            reg.Write(hash, GizmoSettingValue.From(true));
            Assert.True(reg.IsDirty);

            reg.ResetToDefault(hash);
            Assert.False(reg.IsDirty);
        }

        [Fact]
        public void OnSettingChanged_Fires_Once_Per_Write_With_Correct_Hash()
        {
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("x", GizmoSettingValue.From(0));
            var hash = GizmoSettingsRegistry.ComputeHash("x");

            int count = 0;
            uint lastHash = 0;
            reg.OnSettingChanged += h => { count++; lastHash = h; };

            reg.Write(hash, GizmoSettingValue.From(1));

            Assert.Equal(1, count);
            Assert.Equal(hash, lastHash);
        }
    }

    // ==========================================================================
    // SC-GZ008-1 through SC-GZ008-5: GizmoSettingsPersistence
    // ==========================================================================

    public class GizmoSettingsPersistenceTests
    {
        [Fact]
        public void SC_GZ008_1_SaveAndLoad_Restores_Overrides()
        {
            string file = Path.GetTempFileName();
            try
            {
                var reg = new GizmoSettingsRegistry();
                reg.RegisterSetting("NavMesh.ShowGrid", GizmoSettingValue.From(false));
                var hash = GizmoSettingsRegistry.ComputeHash("NavMesh.ShowGrid");
                reg.Write(hash, GizmoSettingValue.From(true));

                GizmoSettingsPersistence.SaveOverrides(reg, file);

                var reg2 = new GizmoSettingsRegistry();
                reg2.RegisterSetting("NavMesh.ShowGrid", GizmoSettingValue.From(false));
                GizmoSettingsPersistence.LoadOverrides(reg2, file);

                Assert.True(reg2.Read(hash).BoolValue);
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void SC_GZ008_2_Default_Values_Not_Written_To_Disk()
        {
            string file = Path.GetTempFileName();
            try
            {
                var reg = new GizmoSettingsRegistry();
                reg.RegisterSetting("NavMesh.ShowGrid", GizmoSettingValue.From(false));
                // Keep at default — do not Write.
                GizmoSettingsPersistence.SaveOverrides(reg, file);

                string json = File.ReadAllText(file);
                Assert.DoesNotContain("NavMesh.ShowGrid", json);
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void SC_GZ008_3_LoadOverrides_MissingFile_Does_Not_Throw()
        {
            var reg = new GizmoSettingsRegistry();
            var ex = Record.Exception(() =>
                GizmoSettingsPersistence.LoadOverrides(reg, @"C:\does\not\exist\gizmos.json"));
            Assert.Null(ex);
        }

        [Fact]
        public void SC_GZ008_5_ResetToDefault_Then_Save_Excludes_Key()
        {
            string file = Path.GetTempFileName();
            try
            {
                var reg = new GizmoSettingsRegistry();
                reg.RegisterSetting("Thickness", GizmoSettingValue.From(1.0f));
                var hash = GizmoSettingsRegistry.ComputeHash("Thickness");
                reg.Write(hash, GizmoSettingValue.From(5.0f));
                reg.ResetToDefault(hash);

                GizmoSettingsPersistence.SaveOverrides(reg, file);

                string json = File.ReadAllText(file);
                Assert.DoesNotContain("Thickness", json);
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void SaveOverrides_Clears_IsDirty()
        {
            string file = Path.GetTempFileName();
            try
            {
                var reg = new GizmoSettingsRegistry();
                reg.RegisterSetting("k", GizmoSettingValue.From(false));
                var hash = GizmoSettingsRegistry.ComputeHash("k");
                reg.Write(hash, GizmoSettingValue.From(true));
                Assert.True(reg.IsDirty);

                GizmoSettingsPersistence.SaveOverrides(reg, file);
                Assert.False(reg.IsDirty);
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void Float32_Roundtrip_Via_Persistence()
        {
            string file = Path.GetTempFileName();
            try
            {
                var reg = new GizmoSettingsRegistry();
                reg.RegisterSetting("Zoom", GizmoSettingValue.From(0.0f));
                var hash = GizmoSettingsRegistry.ComputeHash("Zoom");
                reg.Write(hash, GizmoSettingValue.From(2.5f));
                GizmoSettingsPersistence.SaveOverrides(reg, file);

                var reg2 = new GizmoSettingsRegistry();
                reg2.RegisterSetting("Zoom", GizmoSettingValue.From(0.0f));
                GizmoSettingsPersistence.LoadOverrides(reg2, file);

                Assert.Equal(2.5f, reg2.Read(hash).FloatValue);
            }
            finally { File.Delete(file); }
        }
    }

    // ==========================================================================
    // SC-GZ008-4: GizmoSettingChangedEvent via command buffer
    // ==========================================================================

    public class GizmoSettingChangedEventTests
    {
        [Fact]
        public void SC_GZ008_4_Write_With_Cmd_Publishes_Event()
        {
            using var repo = GizmoTestRepo.Create();
            repo.RegisterEvent<GizmoSettingChangedEvent>();

            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("TestKey", GizmoSettingValue.From(false));
            var hash = GizmoSettingsRegistry.ComputeHash("TestKey");

            var ecb = new EntityCommandBuffer();
            reg.Write(hash, GizmoSettingValue.From(true), ecb);

            ecb.Playback(repo);
            repo.Bus.SwapBuffers();

            var events = ((ISimulationView)repo).ReadEvents<GizmoSettingChangedEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(hash, events[0].KeyHash);
        }
    }
}
