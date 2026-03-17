using System;
using System.Numerics;
using System.Linq;
using Xunit;
using Fdp.Examples.CarKinem.Headless;
using Fdp.Kernel;
using Fdp.Examples.CarKinem.Components;
using CarKinem.Trajectory;
using Fdp.Examples.CarKinem.Core;
using FDP.Toolkit.Time.Controllers;

namespace Fdp.Examples.CarKinem.Tests
{
    public class BugVerificationTests : IDisposable
    {
        private HeadlessCarKinemApp _app;

        public BugVerificationTests()
        {
            _app = new HeadlessCarKinemApp();
            _app.Initialize();
        }

        public void Dispose()
        {
            ((IDisposable)_app).Dispose();
        }

        [Fact]
        public void Test_Issue1_Stepping_Moves_Simulation()
        {
            var stepping = _app.SteppingTime;
            stepping.SeedState(_app.TimeController.GetCurrentState());
            _app.TimeController.SwitchTo(stepping);
            
            // Replicate App behavior: App calls Step() then runs systems, but DOES NOT CALL Kernel.Update().
            float dt = 1.0f / 60.0f;
            stepping.Step(dt); 
            
            // In a real app loop, we would call _app.RunSystems() here if exposed.
            // But we want to verifiable correct time propagation.
            // If we run Kernel.Update(), it calls TimeController.Update().
            // If TimeController.Update() returns 0 delta (bug), then GlobalTime is frozen.
            
            _app.Kernel.Update();
            
            var time = _app.Repository.GetSingleton<GlobalTime>();
            
            // Current bug: DeltaTime is 0 even after getting update because SteppingTimeController.Update() returns 0.
            Assert.True(time.DeltaTime > 0.0001f, $"Stepping should produce non-zero DeltaTime. Got {time.DeltaTime}");
        }

        [Fact]
        public void Test_Issue2_Trajectory_Interpolation()
        {
            var points = new Vector2[] { new Vector2(0,0), new Vector2(10,10), new Vector2(20,0) };
            var pool = _app.TrajectoryPool();
            
            int linId = pool.RegisterTrajectory(points, null, false, TrajectoryInterpolation.Linear);
            int splId = pool.RegisterTrajectory(points, null, false, TrajectoryInterpolation.CatmullRom);
            
            float s = 7.07f;
            var (posLin, _, _) = pool.SampleTrajectory(linId, s);
            var (posSpl, _, _) = pool.SampleTrajectory(splId, s);
            
            float diff = Vector2.Distance(posLin, posSpl);
            Assert.True(diff > 0.1f, $"Spline trajectory should differ from Linear trajectory. Diff: {diff}");
        }
        [Fact]
        public void Test_Replay_UI_Interactions()
        {
            // Setup Replay (need a recorded file or mock)
            // Creating a dummy recording via App first?
            
            // 1. Record some frames
            var recPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"replay_test_{Guid.NewGuid()}.fdprec");
            
            // Manually drive recorder
            using(var recorder = new Fdp.Kernel.FlightRecorder.AsyncRecorder(recPath))
            {
                _app.Repository.GetSingletonUnmanaged<GlobalTime>().DeltaTime = 0.016f;
                // Capture Header (done in ctor)
                recorder.CaptureKeyframe(_app.Repository, DateTime.UtcNow.Ticks, blocking: true); // Frame 0
                for(int i=1; i<10; i++)
                {
                    _app.Repository.GetSingletonUnmanaged<GlobalTime>().FrameNumber = (uint)i;
                    recorder.CaptureFrame(_app.Repository, (uint)(i-1), DateTime.UtcNow.Ticks, blocking: true);
                }
            }
            
            // 2. Load Playback
            var playback = new Fdp.Kernel.FlightRecorder.PlaybackController(recPath);
            
            try
            {
                // 3. Test Seek
                playback.SeekToFrame(_app.Repository, 5);
                Assert.Equal(5, playback.CurrentFrame);
                
                // 4. Test Step
                bool moved = playback.StepForward(_app.Repository);
                Assert.True(moved);
                Assert.Equal(6, playback.CurrentFrame);
                
                // 5. Test Rewind/Backward (if supported) check
                // StepBackward is supported in PlaybackController
                playback.StepBackward(_app.Repository);
                Assert.Equal(5, playback.CurrentFrame);
                
                // 6. Test Seek End
                playback.SeekToFrame(_app.Repository, 9);
                Assert.True(playback.IsAtEnd);
                
                // 7. Step at end
                moved = playback.StepForward(_app.Repository);
                Assert.False(moved);
            }
            finally
            {
                playback.Dispose();
                System.IO.File.Delete(recPath);
            }
        }
    }
    
    public static class HeadlessExtensions
    {
        public static global::CarKinem.Trajectory.TrajectoryPoolManager TrajectoryPool(this HeadlessCarKinemApp app)
        {
             var field = typeof(HeadlessCarKinemApp).GetField("_trajectoryPool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
             return (global::CarKinem.Trajectory.TrajectoryPoolManager)field.GetValue(app);
        }
    }
}
