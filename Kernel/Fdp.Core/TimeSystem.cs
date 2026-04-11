using System;

namespace Fdp.Kernel
{
    /// <summary>
    /// Manages the game loop clock and pushes time data into the EntityRepository.
    /// Supports Real-time (Variable Step) and Deterministic (Fixed Step) modes.
    /// </summary>
    public class TimeSystem
    {
        private readonly EntityRepository _repo;
        private readonly TimeProvider _timeProvider;
        
        private long _lastTimestamp;
        private double _accumulatedTotalTime;
        private double _accumulatedUnscaledTotalTime;
        private long _frameCount;
        private long _startWallTicks;

        // Configuration
        public float TimeScale { get; set; } = 1.0f;
        public float MaxDeltaTime { get; set; } = 0.1f; // Cap dt to prevent physics explosions during lag spikes

        // Budgeting (for time-sliced systems)
        private long _frameStartTimestamp;
        private double _currentFrameBudgetMs;

        public TimeSystem(EntityRepository repo, TimeProvider? timeProvider = null)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _timeProvider = timeProvider ?? TimeProvider.System;
            _startWallTicks = _timeProvider.GetTimestamp();
            Reset();
        }

        public void Reset()
        {
            _lastTimestamp = _timeProvider.GetTimestamp();
            _startWallTicks = _lastTimestamp; // Reset start time on Reset? Or keep original? Usually reset for simulation restart.
            _accumulatedTotalTime = 0;
            _accumulatedUnscaledTotalTime = 0;
            _frameCount = 0;
            _repo.SetSingletonUnmanaged(new GlobalTime 
            { 
                TimeScale = 1.0f,
                StartWallTicks = _startWallTicks
            });
        }

        /// <summary>
        /// Advances time based on the wall clock (Variable Step).
        /// Call this at the start of your Game Loop.
        /// </summary>
        /// <param name="budgetMs">Optional CPU budget for this frame (for time-slicing).</param>
        public void Update(double budgetMs = double.PositiveInfinity)
        {
            long now = _timeProvider.GetTimestamp();
            
            // Calculate raw delta
            double rawDt = _timeProvider.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
            _lastTimestamp = now;

            // Apply budget tracking
            _frameStartTimestamp = now;
            _currentFrameBudgetMs = budgetMs;

            // Apply Caps and Scales
            float unscaledDt = (float)rawDt;
            
            // Prevent spiral of death by capping dt
            if (unscaledDt > MaxDeltaTime) 
                unscaledDt = MaxDeltaTime;

            float dt = unscaledDt * TimeScale;

            // Update State
            _accumulatedTotalTime += dt;
            _accumulatedUnscaledTotalTime += unscaledDt;
            _frameCount++;

            // PUSH TO REPOSITORY
            PushToRepository(dt, unscaledDt);
        }

        /// <summary>
        /// Advances time by a fixed amount (Deterministic Step).
        /// Used for Flight Recorder Playback, Unit Tests, or FixedUpdate loops.
        /// </summary>
        public void Step(float fixedDeltaTime)
        {
            // Update State
            float unscaledDt = fixedDeltaTime; // Assume 1:1 for fixed steps usually
            float dt = fixedDeltaTime * TimeScale;

            _accumulatedTotalTime += dt;
            _accumulatedUnscaledTotalTime += unscaledDt;
            _frameCount++;

            // Mock budget for fixed steps (infinite)
            _currentFrameBudgetMs = double.PositiveInfinity;
            _frameStartTimestamp = _timeProvider.GetTimestamp();

            // PUSH TO REPOSITORY
            PushToRepository(dt, unscaledDt);
        }

        /// <summary>
        /// Forcefully sets the time state (e.g. when seeking in a replay).
        /// </summary>
        public void SnapTo(double totalTime, long frameCount)
        {
            _accumulatedTotalTime = totalTime;
            _frameCount = frameCount;
            // Note: We don't change DeltaTime here, the next Update/Step will handle it
            
            // Sync internal clock to avoid a huge delta on next Update()
            _lastTimestamp = _timeProvider.GetTimestamp();
        }

        private void PushToRepository(float dt, float unscaledDt)
        {
            // We use SetSingletonUnmanaged to ensure it goes into the Tier 1 storage
            // which the Flight Recorder is configured to capture.
            _repo.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = dt,
                UnscaledDeltaTime = unscaledDt,
                TotalTime = _accumulatedTotalTime,
                UnscaledTotalTime = _accumulatedUnscaledTotalTime,
                FrameNumber = _frameCount,
                TimeScale = TimeScale,
                StartWallTicks = _startWallTicks
            });
        }

        // ====================================================================
        // Budgeting Helpers (For Time-Sliced Systems)
        // ====================================================================

        /// <summary>
        /// Checks if there is CPU time remaining in the current frame budget.
        /// </summary>
        public bool HasTimeRemaining(double estimatedCostMs)
        {
            if (double.IsPositiveInfinity(_currentFrameBudgetMs)) return true;

            long now = _timeProvider.GetTimestamp();
            double elapsedMs = _timeProvider.GetElapsedTime(_frameStartTimestamp, now).TotalMilliseconds;
            
            return (elapsedMs + estimatedCostMs) <= _currentFrameBudgetMs;
        }

        /// <summary>
        /// Gets ms elapsed since Update() started.
        /// </summary>
        public double GetFrameElapsedMs()
        {
            long now = _timeProvider.GetTimestamp();
            return _timeProvider.GetElapsedTime(_frameStartTimestamp, now).TotalMilliseconds;
        }
    }
}
