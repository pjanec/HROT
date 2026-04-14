using System;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit; // xUnit
using Fdp.Examples.CarKinem.Headless;
using Fdp.Core;
using CarKinem.Core;

namespace Fdp.Examples.CarKinem.Tests
{
    public class HeadlessRecordingTests : IDisposable
    {
        private string _tempDir;

        public HeadlessRecordingTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "FDP_Rec_Tests_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { }
            }
        }

        [Fact]
        public void Test_Recording_And_Playback_Flow()
        {
            string recPath = Path.Combine(_tempDir, "test.fdprec");
            
            // 1. RECORD PHASE
            using (var app = new HeadlessCarKinemApp())
            {
                app.Initialize();
                app.SpawnFastOne(); // Ensure entities exist
                
                // Advance a bit without recording
                for (int i=0; i<10; i++) app.Update();
                
                // Start Recording
                app.StartRecording(recPath);
                Assert.NotNull(app.Recorder);
                
                // Check entity exists
                var q = app.Repository.Query().With<VehicleState>().Build();
                Assert.True(q.Count() > 0);
                
                // Simulate 100 frames
                for (int i=0; i<100; i++) app.Update();
                
                // Stop
                app.StopRecording();
                Assert.Null(app.Recorder);
                
                // Ensure file created
                Assert.True(File.Exists(recPath));
                var fileInfo = new FileInfo(recPath);
                Assert.True(fileInfo.Length > 0);
            }
            
            // 2. PLAYBACK PHASE
            using (var app = new HeadlessCarKinemApp())
            {
                app.Initialize();
                
                // Start Playback
                app.StartPlayback(recPath);
                Assert.NotNull(app.Playback);
                
                int framesReplayed = 0;
                
                // Run until playback finishes
                // app.Update() calls Playback.StepForward.
                
                while (!app.Playback.IsAtEnd)
                {
                   app.Update();
                   framesReplayed++;
                   
                   if (framesReplayed > 200) break; // Safety break
                }
                
                Assert.True(framesReplayed > 90);
                
                // Verify entity state (basic check)
                var q = app.Repository.Query().With<VehicleState>().Build();
                Assert.True(q.Count() > 0);
            }
        }

        [Fact]
        public void Test_StartPlayback_Stops_ActiveRecording()
        {
            string recPath = Path.Combine(_tempDir, "collision.fdprec");
            
            using (var app = new HeadlessCarKinemApp())
            {
                app.Initialize();
                app.SpawnFastOne();
                
                app.StartRecording(recPath);
                for(int i=0; i<10; i++) app.Update();
                Assert.NotNull(app.Recorder);
                
                // Attempt to start playback WITHOUT manual stop
                // This will fail if logic is incorrect
                app.StartPlayback(recPath);
                
                Assert.Null(app.Recorder);
                Assert.NotNull(app.Playback);
            }
        }
    }
}
