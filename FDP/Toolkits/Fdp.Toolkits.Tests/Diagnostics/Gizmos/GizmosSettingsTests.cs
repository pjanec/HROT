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
            Assert.Equal(SettingType.CsFloat32, v.Type);
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
            Assert.Equal(SettingType.CsInt32, v.Type);
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

    // ==========================================================================
    // SC-GZ049: Settings Scopes — Global / Project / Session
    // ==========================================================================

    public class GizmoSettingsScopeTests
    {
        private static GizmoSettingsRegistry MakeReg(string key, GizmoSettingValue def)
        {
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting(key, def);
            return reg;
        }

        // SC-GZ049-1: Write with Session scope; GetScope returns Session.
        [Fact]
        public void SC_GZ049_1_Write_Session_Scope_IsReturned()
        {
            var reg  = MakeReg("k", GizmoSettingValue.From(false));
            var hash = GizmoSettingsRegistry.ComputeHash("k");
            reg.Write(hash, GizmoSettingValue.From(true), scope: SettingScope.Session);
            Assert.Equal(SettingScope.Session, reg.GetScope(hash));
        }

        // SC-GZ049-2: Project-scoped write not included in Global SaveToDisk.
        [Fact]
        public void SC_GZ049_2_ProjectScope_NotSavedToGlobalFile()
        {
            string file = Path.GetTempFileName();
            try
            {
                var reg  = MakeReg("Proj.Key", GizmoSettingValue.From(false));
                var hash = GizmoSettingsRegistry.ComputeHash("Proj.Key");
                reg.Write(hash, GizmoSettingValue.From(true), scope: SettingScope.Project);
                reg.SaveToDisk(file, SettingScope.Global);
                Assert.DoesNotContain("Proj.Key", File.ReadAllText(file));
            }
            finally { File.Delete(file); }
        }

        // SC-GZ049-3: Global-scoped write is included in Global SaveToDisk.
        [Fact]
        public void SC_GZ049_3_GlobalScope_SavedToGlobalFile()
        {
            string file = Path.GetTempFileName();
            try
            {
                var reg  = MakeReg("Glob.Key", GizmoSettingValue.From(false));
                var hash = GizmoSettingsRegistry.ComputeHash("Glob.Key");
                reg.Write(hash, GizmoSettingValue.From(true), scope: SettingScope.Global);
                reg.SaveToDisk(file, SettingScope.Global);
                Assert.Contains("Glob.Key", File.ReadAllText(file));
            }
            finally { File.Delete(file); }
        }

        // SC-GZ049-4: DiscardScope(Session) resets session settings to default.
        [Fact]
        public void SC_GZ049_4_DiscardScope_Session_ResetsToDefault()
        {
            var reg  = MakeReg("sess", GizmoSettingValue.From(0));
            var hash = GizmoSettingsRegistry.ComputeHash("sess");
            reg.Write(hash, GizmoSettingValue.From(99), scope: SettingScope.Session);
            Assert.Equal(99, reg.Read(hash).IntValue);

            reg.DiscardScope(SettingScope.Session);

            Assert.Equal(0, reg.Read(hash).IntValue);
            Assert.Equal(SettingScope.Global, reg.GetScope(hash));
        }

        // SC-GZ049-5: DiscardScope(Project) does NOT affect Global or Session settings.
        [Fact]
        public void SC_GZ049_5_DiscardProjectScope_DoesNotAffectOtherScopes()
        {
            var reg = new GizmoSettingsRegistry();
            reg.RegisterSetting("g", GizmoSettingValue.From(0));
            reg.RegisterSetting("p", GizmoSettingValue.From(0));
            reg.RegisterSetting("s", GizmoSettingValue.From(0));
            var hg = GizmoSettingsRegistry.ComputeHash("g");
            var hp = GizmoSettingsRegistry.ComputeHash("p");
            var hs = GizmoSettingsRegistry.ComputeHash("s");

            reg.Write(hg, GizmoSettingValue.From(1), scope: SettingScope.Global);
            reg.Write(hp, GizmoSettingValue.From(2), scope: SettingScope.Project);
            reg.Write(hs, GizmoSettingValue.From(3), scope: SettingScope.Session);

            reg.DiscardScope(SettingScope.Project);

            Assert.Equal(1, reg.Read(hg).IntValue);  // Global unchanged
            Assert.Equal(0, reg.Read(hp).IntValue);  // Project reset
            Assert.Equal(3, reg.Read(hs).IntValue);  // Session unchanged
        }

        // SC-GZ049-6: Write without scope argument defaults to Global.
        [Fact]
        public void SC_GZ049_6_Write_NoScope_DefaultsToGlobal()
        {
            var reg  = MakeReg("def", GizmoSettingValue.From(false));
            var hash = GizmoSettingsRegistry.ComputeHash("def");
            reg.Write(hash, GizmoSettingValue.From(true));
            Assert.Equal(SettingScope.Global, reg.GetScope(hash));
        }

        // SC-GZ049-8: LoadFromDisk with Project scope assigns Project to loaded settings.
        [Fact]
        public void SC_GZ049_8_LoadFromDisk_AssignsGivenScope()
        {
            string file = Path.GetTempFileName();
            try
            {
                // Prepare and save a file with a Global setting.
                var src  = MakeReg("loaded", GizmoSettingValue.From(false));
                var hash = GizmoSettingsRegistry.ComputeHash("loaded");
                src.Write(hash, GizmoSettingValue.From(true), scope: SettingScope.Global);
                src.SaveToDisk(file, SettingScope.Global);

                // Load it as Project scope into a fresh registry.
                var dst = MakeReg("loaded", GizmoSettingValue.From(false));
                dst.LoadFromDisk(file, SettingScope.Project);

                Assert.True(dst.Read(hash).BoolValue);
                Assert.Equal(SettingScope.Project, dst.GetScope(hash));
            }
            finally { File.Delete(file); }
        }
    }
}
