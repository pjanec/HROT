using System;
using Fdp.Core;
using Fdp.ModuleHost.Time;
namespace Fdp.Toolkit.Time.Controllers
{
    /// <summary>
    /// Time controller for manual stepping only.
    /// Does not measure wall clock - advances only when Step() is called.
    /// Use for: Paused simulations, frame-by-frame debugging, tools.
    ///
    /// <para><b>First-frame DeltaTime contract:</b>
    /// <see cref="Update"/> returns <c>DeltaTime = 0</c> until the first <see cref="Step"/>
    /// call is made.  This is by design: the seed state carries a zero delta to
    /// indicate that no simulation time has elapsed yet.
    /// Any caller that drives a <see cref="ModuleHostKernel"/> or reads
    /// <c>GlobalTime.DeltaTime</c> from a singleton <b>must call <see cref="Step"/> before
    /// calling the kernel's <c>Update()</c></b> to avoid systems seeing <c>DeltaTime = 0</c>
    /// and producing zero-velocity or degenerate results.
    /// The correct call order each tick is:
    /// <code>
    /// controller.Step(fixedDelta);   // advance time
    /// kernel.Update();               // kernel reads DeltaTime off the controller
    /// </code>
    /// </para>
    /// </summary>
    public class SteppingTimeController : ISteppableTimeController
    {
        private double _totalTime;
        private long _frameNumber;
        private float _timeScale;
        private double _unscaledTotalTime;
        
        private float _lastDeltaTime;
        private float _lastUnscaledDeltaTime;
        
        /// <summary>
        /// Create a stepping controller with initial state.
        /// </summary>
        public SteppingTimeController(GlobalTime seedState)
        {
            SeedState(seedState);
        }
        
        /// <summary>
        /// Update() returns the time state corresponding to the last step.
        /// Use Step() to advance time.
        /// </summary>
        public GlobalTime Update()
        {
            // Return state including the delta from the last step
            // This allows the Kernel to see the time progression when Update() is called.
            return new GlobalTime
            {
                FrameNumber = _frameNumber,
                DeltaTime = _lastDeltaTime,
                TotalTime = _totalTime,
                TimeScale = _timeScale,
                UnscaledDeltaTime = _lastUnscaledDeltaTime,
                UnscaledTotalTime = _unscaledTotalTime,
                TotalWallTicks = (long)(_unscaledTotalTime * TimeSpan.TicksPerSecond)
            };
        }
        
        /// <summary>
        /// Manually advance time by fixed deltaTime.
        /// </summary>
        public GlobalTime Step(float fixedDeltaTime)
        {
            float scaledDelta = fixedDeltaTime * _timeScale;
            
            _totalTime += scaledDelta;
            _frameNumber++;
            _unscaledTotalTime += fixedDeltaTime;
            
            _lastDeltaTime = scaledDelta;
            _lastUnscaledDeltaTime = fixedDeltaTime;
            
            return Update();
        }
        
        public void SetTimeScale(float scale)
        {
            if (scale < 0.0f)
                throw new ArgumentException("TimeScale cannot be negative", nameof(scale));
            
            _timeScale = scale;
        }
        
        public float GetTimeScale()
        {
            return _timeScale;
        }

        public TimeMode GetMode()
        {
            return TimeMode.Deterministic;
        }
        
        public GlobalTime GetCurrentState()
        {
            return new GlobalTime
            {
                FrameNumber = _frameNumber,
                DeltaTime = 0.0f,
                TotalTime = _totalTime,
                TimeScale = _timeScale,
                UnscaledDeltaTime = 0.0f,
                UnscaledTotalTime = _unscaledTotalTime,
                TotalWallTicks = (long)(_unscaledTotalTime * TimeSpan.TicksPerSecond)
            };
        }

        public void SeedState(GlobalTime state)
        {
            _totalTime = state.TotalTime;
            _frameNumber = state.FrameNumber;
            _timeScale = state.TimeScale;
            _unscaledTotalTime = state.UnscaledTotalTime;
            // Reset delta to zero: the seed establishes a temporal baseline but does not
            // constitute a completed time step.  The next call to Update() will therefore
            // return DeltaTime = 0 until Step() is called.  See class-level XML for the
            // required Step-before-Update call order.
            _lastDeltaTime = 0.0f;
            _lastUnscaledDeltaTime = 0.0f;
        }
        
        public void Dispose()
        {
            // No resources to clean up
        }
    }
}
