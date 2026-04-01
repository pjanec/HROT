using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using ModuleHost.Core.Time;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Controllers
{
    /// <summary>
    /// Slave time controller for Continuous mode.
    /// Uses Phase-Locked Loop (PLL) to smoothly sync with Master clock.
    /// </summary>
    public class SlaveTimeController : ITimeController
    {
        private readonly Stopwatch _wallClock;
        private readonly Func<long>? _tickSource; // For testing
        private readonly TimeConfig _config;
        private readonly string _instanceName;
        
        // Virtual clock (PLL-adjusted)
        private long _virtualWallTicks = 0;
        private long _lastUpdateTicks = 0; // For tick source delta calc
        
        // Time state
        private double _totalTime = 0.0;
        private double _unscaledTotalTime = 0.0;
        private float _timeScale = 1.0f;
        private long _frameNumber = 0;
        
        // PLL state
        private readonly JitterFilter _errorFilter;
        private double _currentError = 0.0;
        
        private readonly FdpEventBus _eventBus;

        public SlaveTimeController(FdpEventBus eventBus, TimeConfig? config = null, string instanceName = "") : this(eventBus, config, null, instanceName)
        {
        }

        internal SlaveTimeController(FdpEventBus eventBus, TimeConfig? config, Func<long>? tickSource, string instanceName = "")
        {
            _wallClock = Stopwatch.StartNew();
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _tickSource = tickSource;
            _config = config ?? TimeConfig.Default;
            _instanceName = instanceName;
            _errorFilter = new JitterFilter(_config.JitterWindowSize);
            
            if (_tickSource != null)
            {
                _virtualWallTicks = _tickSource();
                _lastUpdateTicks = _virtualWallTicks;
            }
            else
            {
                // Use an absolute system timestamp so comparisons with MasterWallTicks
                // (also from Stopwatch.GetTimestamp()) are in the same clock domain.
                _lastUpdateTicks = Stopwatch.GetTimestamp();
            }
            
            // Register as consumer
            _eventBus.Register<TimePulseDescriptor>();
        }
        
        public void OnTimePulseReceived(TimePulseDescriptor pulse)
        {
            // Both currentAbsTicks and pulse.MasterWallTicks use the same clock domain
            // (Stopwatch.GetTimestamp() = absolute system ticks).  Their difference gives
            // the real time elapsed since the master sent this pulse (network latency + jitter).
            long currentAbsTicks = _tickSource != null ? _tickSource() : Stopwatch.GetTimestamp();
            long timeSincePulseTicks = currentAbsTicks - pulse.MasterWallTicks;
            double timeSincePulseSec  = timeSincePulseTicks / (double)Stopwatch.Frequency;

            // Expected sim time: master's snapshot + time elapsed since pulse * current scale.
            double expectedSimTime = pulse.SimTimeSnapshot + timeSincePulseSec * pulse.TimeScale;

            // PLL error expressed in "sim-time ticks" so the jitter filter stays consistent
            // with the existing infrastructure (errorFilter works in Stopwatch-frequency units).
            double simTimeError = expectedSimTime - _totalTime;
            long errorTicks = (long)(simTimeError * Stopwatch.Frequency);
            _errorFilter.AddSample(errorTicks);

            // Update scale
            _timeScale = pulse.TimeScale;

            // Hard Snap: if sim time is far off, snap directly to the correct value and reset
            // the delta baseline.  _virtualWallTicks is NOT snapped here — it accumulates
            // locally and stays in whichever clock domain the node started in, keeping
            // TotalWallTicks monotonically increasing regardless of the pulse domain.
            double errorMs = Math.Abs(simTimeError) * 1000.0;
            if (errorMs > _config.SnapThresholdMs)
            {
                _totalTime = expectedSimTime;
                _lastUpdateTicks = currentAbsTicks;
                _errorFilter.Reset();
                _currentError = 0.0;
            }
        }
        
        public GlobalTime Update()
        {
            // Process pulses
            foreach (var pulse in _eventBus.Consume<TimePulseDescriptor>())
            {
                OnTimePulseReceived(pulse);
            }

            _frameNumber++;
            
            // PLL Calculation
            double filteredError = _errorFilter.GetFilteredValue();
            double correctionFactor = (filteredError / (double)Stopwatch.Frequency) * _config.PLLGain;
            correctionFactor = Math.Clamp(correctionFactor, -_config.MaxSlew, _config.MaxSlew);
            
            // Calculate Delta
            long rawDelta;
            
            if (_tickSource != null)
            {
                long now = _tickSource();
                rawDelta = now - _lastUpdateTicks;
                _lastUpdateTicks = now;
            }
            else
            {
                // Production path: absolute system timestamps, consistent with OnTimePulseReceived.
                long now = Stopwatch.GetTimestamp();
                rawDelta = now - _lastUpdateTicks;
                _lastUpdateTicks = now;
            }
            
            // Apply PLL
            long adjustedDelta = (long)(rawDelta * (1.0 + correctionFactor));
            _virtualWallTicks += adjustedDelta;
            
            // Accumulate
            double virtualDeltaSeconds = adjustedDelta / (double)Stopwatch.Frequency;
            double rawDeltaSeconds = rawDelta / (double)Stopwatch.Frequency;
            
            float dt = (float)(virtualDeltaSeconds * _timeScale);
            
            _totalTime += dt;
            _unscaledTotalTime += rawDeltaSeconds;
            
            _currentError -= correctionFactor * virtualDeltaSeconds;
            
            return new GlobalTime
            {
                FrameNumber = _frameNumber,
                DeltaTime = dt,
                TotalTime = _totalTime,
                TimeScale = _timeScale,
                UnscaledDeltaTime = (float)rawDeltaSeconds,
                UnscaledTotalTime = _unscaledTotalTime,
                StartWallTicks = 0,
                TotalWallTicks = _virtualWallTicks
            };
        }
        
        public void SetTimeScale(float scale)
        {
            // Intentionally ignored: slave time scale is driven by master TimePulse.
            // Called by ModuleHostKernel.SwapTimeController — not a caller error.
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
                TotalWallTicks = _virtualWallTicks
            };
        }

        public void SeedState(GlobalTime state)
        {
            _frameNumber = state.FrameNumber;
            _totalTime = state.TotalTime;
            _unscaledTotalTime = state.UnscaledTotalTime;
            _timeScale = state.TimeScale;
            
            // Bypass the PLL/JitterFilter: set virtual wall ticks directly from the seeded
            // state so that the very next Update() reflects the seeded position without slew.
            _virtualWallTicks = state.TotalWallTicks;
            
            // Reset the delta baseline to NOW so the next Update() measures only the real
            // elapsed time since the seed, not a stale gap from before it.
            if (_tickSource != null)
                _lastUpdateTicks = _tickSource();
            else
                _lastUpdateTicks = Stopwatch.GetTimestamp();
            
            _errorFilter.Reset();
        }
        
        public float GetTimeScale() => _timeScale;
        public TimeMode GetMode() => TimeMode.Continuous;
        
        public void Dispose()
        {
            // Cleanup if needed
        }
    }
    
    /// <summary>
    /// Jitter filter using median of circular buffer.
    /// Rejects network outliers while allowing PLL to track real drift.
    /// </summary>
    internal class JitterFilter
    {
        private readonly long[] _samples;
        private int _index = 0;
        private int _count = 0;
        
        public JitterFilter(int windowSize)
        {
            _samples = new long[windowSize];
        }
        
        public void AddSample(long errorTicks)
        {
            _samples[_index] = errorTicks;
            _index = (_index + 1) % _samples.Length;
            if (_count < _samples.Length)
                _count++;
        }
        
        public double GetFilteredValue()
        {
            if (_count == 0)
                return 0.0;
            
            // Return median of samples (robust against outliers)
            var sorted = _samples.Take(_count).OrderBy(x => x).ToArray();
            return sorted[_count / 2];
        }
        
        public void Reset()
        {
            Array.Clear(_samples, 0, _samples.Length);
            _index = 0;
            _count = 0;
        }
    }
}
