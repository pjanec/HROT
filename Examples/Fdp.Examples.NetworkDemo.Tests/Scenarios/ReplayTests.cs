using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Fdp.Examples.NetworkDemo;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Core;

namespace Fdp.Examples.NetworkDemo.Tests.Scenarios
{
    public class ReplayTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _recordingFile = "TestRecording.fdp";
        private readonly string _metaFile = "TestRecording.fdp.meta";
        private readonly string _metaJsonFile = "TestRecording.fdp.meta.json";

        public ReplayTests(ITestOutputHelper output)
        {
            _output = output;
            // Ensure clean start
            CleanupFiles();
        }

        public void Dispose()
        {
            CleanupFiles();
        }

        private void CleanupFiles()
        {
            try 
            {
                if (File.Exists(_recordingFile)) File.Delete(_recordingFile);
                if (File.Exists(_metaFile)) File.Delete(_metaFile);
                if (File.Exists(_metaJsonFile)) File.Delete(_metaJsonFile);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Warning: Cleanup failed: {ex.Message}");
            }
        }

        private bool CheckForEntities(NetworkDemoApp app)
        {
            var entities = app.World.Query().With<Fdp.Toolkit.Replication.Components.NetworkIdentity>().Build();
            foreach (var e in entities) return true;
            return false;
        }

        [Fact]
        public async Task Recording_And_Replay_Cycle_Works()
        {
            const int framesToRecord = 20;
            const int framesToReplay = 10;
            int nodeId = 100;

            // ========================================================================
            // 1. RECORDING PHASE
            // ========================================================================
            _output.WriteLine("=== PHASE 1: RECORDING ===");
            using (var app = new NetworkDemoApp())
            {
                // live mode, with recording path
                await app.InitializeAsync(nodeId, replayMode: false, recPath: _recordingFile, autoSpawn: true, testMode: true);

                // Run for N frames
                // We'll manually tick the kernel if possible, or use a short loop
                // NetworkDemoApp doesn't expose Tick directly, but we can access Kernel
                
                for (int i = 0; i < framesToRecord; i++)
                {
                    app.Kernel.Update(1.0f / 60.0f);
                    await Task.Delay(10); // Small delay to allow async tasks (like NLog/Recorder) to breathe
                }
                
                _output.WriteLine("Recording phase complete. Disposing...");
            } // Dispose triggers Recorder.Dispose -> Flush to disk

            // Verify files exist
            Assert.True(File.Exists(_recordingFile), "Recording file should exist");
            Assert.True(File.Exists(_metaFile) || File.Exists(_metaJsonFile), "Metadata file should exist");

            var fileInfo = new FileInfo(_recordingFile);
            Assert.True(fileInfo.Length > 0, $"Recording file should not be empty (Size: {fileInfo.Length})");
            _output.WriteLine($"Recording Size: {fileInfo.Length} bytes");


            // ========================================================================
            // 2. REPLAY PHASE
            // ========================================================================
            _output.WriteLine("=== PHASE 2: REPLAY ===");
            using (var app = new NetworkDemoApp())
            {
                // replay mode, reading from same file
                await app.InitializeAsync(nodeId, replayMode: true, recPath: _recordingFile, autoSpawn: false);
                
                // Assert Replay specific components exist
                 // Note: ReplayBridgeSystem injects ReplayTime singleton
                
                bool replayTimeUpdated = false;
                
                for (int i = 0; i < framesToReplay; i++)
                {
                    app.Kernel.Update(1.0f / 60.0f);
                    
                    // Verify ReplayTime
                    if (app.World.HasSingleton<ReplayTime>())
                    {
                        var rt = app.World.GetSingleton<ReplayTime>();
                        if (rt.Frame > 0)
                        {
                            replayTimeUpdated = true;
                            // _output.WriteLine($"Replay Frame: {rt.Frame} Time: {rt.Time:F2}");
                        }
                    }
                    
                    // Verify Entities exist (Should rely on what was recorded)
                    // In autoSpawn=true (recorded phase), a tank is spawned.
                    // So we should see entities in the Shadow World being bridged to Live World.
                    
                    // Query for NetworkIdentity which ReplayBridgeSystem injects
                    if (i > 5) // Give it a few frames to load
                    {
                        Assert.True(CheckForEntities(app), "Expected NetworkIdentities to be present during replay");
                    }
                    
                    await Task.Delay(10);
                }
                
                Assert.True(replayTimeUpdated, "ReplayTime singleton should have been updated during playback");
            }
        }
    }
}
