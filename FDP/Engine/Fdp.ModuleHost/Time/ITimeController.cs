using System;
using Fdp.Core;

namespace Fdp.ModuleHost.Time
{
    /// <summary>
    /// Abstraction over time control logic.
    /// Implementations: MasterTimeController, SlaveTimeController, SteppedTimeController.
    /// </summary>
    public interface ITimeController : IDisposable
    {
        /// <summary>
        /// Update clock state and calculate time for this frame.
        /// Called once per frame by ModuleHostKernel.
        /// </summary>
        /// <returns>GlobalTime struct with current frame time data</returns>
        GlobalTime Update();
        
        /// <summary>
        /// Change simulation speed.
        /// </summary>
        void SetTimeScale(float scale);
        
        /// <summary>
        /// Get current time scale.
        /// </summary>
        float GetTimeScale();
        
        /// <summary>
        /// Get current mode (Continuous or Deterministic).
        /// </summary>
        TimeMode GetMode();
        
        /// <summary>
        /// Get current time state for transfer/save.
        /// </summary>
        GlobalTime GetCurrentState();
        
        /// <summary>
        /// Initialize controller with specific time state.
        /// </summary>
        void SeedState(GlobalTime state);

        /// <summary>
        /// ⭐ CE-3293 (R-3036 rule 7) — bumped by every time DISCONTINUITY this clock makes (<c>SnapAndPause</c>: a load, a
        /// preview Stop, a seek). The kernel restarts the event history and every step window when it changes. A clock that never
        /// snaps keeps 0.
        /// </summary>
        long TimelineEpoch => 0;
    }
    
    /// <summary>
    /// Time synchronization mode.
    /// </summary>
    public enum TimeMode
    {
        /// <summary>
        /// Continuous (Real-Time/Scaled) mode.
        /// Uses PLL for smooth synchronization.
        /// </summary>
        Continuous,
        
        /// <summary>
        /// Deterministic (Lockstep/Stepped) mode.
        /// Frame-by-frame synchronization via ACKs.
        /// </summary>
        Deterministic
    }
}
