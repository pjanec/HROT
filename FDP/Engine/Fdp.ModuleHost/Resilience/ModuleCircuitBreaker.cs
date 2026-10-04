// File: ModuleHost/Resilience/ModuleCircuitBreaker.cs

using System;

namespace Fdp.ModuleHost.Resilience
{
    /// <summary>
    /// Circuit breaker states following the standard pattern.
    /// </summary>
    public enum CircuitState
    {
        /// <summary>
        /// Normal operation - module can run.
        /// </summary>
        Closed,
        
        /// <summary>
        /// Module has failed too many times - skipping execution.
        /// </summary>
        Open,
        
        /// <summary>
        /// Testing recovery - allow one execution to see if module recovered.
        /// </summary>
        HalfOpen
    }
    
    /// <summary>
    /// Tracks module health and prevents repeated execution of failing modules.
    /// Implements the Circuit Breaker pattern for resilience.
    /// </summary>
    public class ModuleCircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly int _resetTimeoutMs;
        
        private int _failureCount;
        private DateTime _lastFailureTime;
        private CircuitState _state = CircuitState.Closed;
        
        private readonly object _lock = new object();

        /// <summary>
        /// ⭐ CE-3032 — raised (outside the lock) on every state change: <c>(from, to, reason)</c>. 🔴 Why: an open
        /// circuit SKIPS the module for <c>resetTimeoutMs</c> and nothing said so — perception went dark for 10 s at a
        /// time with only per-tick timeout lines to show for it. The kernel logs each transition once.
        /// </summary>
        public Action<CircuitState, CircuitState, string>? StateChanged { get; set; }
        
        /// <summary>
        /// Creates a circuit breaker with specified thresholds.
        /// </summary>
        /// <param name="failureThreshold">Number of consecutive failures before opening circuit (default: 3)</param>
        /// <param name="resetTimeoutMs">Milliseconds before attempting recovery (default: 5000)</param>
        public ModuleCircuitBreaker(int failureThreshold = 3, int resetTimeoutMs = 5000)
        {
            if (failureThreshold <= 0)
                throw new ArgumentException("Failure threshold must be positive", nameof(failureThreshold));
            if (resetTimeoutMs <= 0)
                throw new ArgumentException("Reset timeout must be positive", nameof(resetTimeoutMs));
            
            _failureThreshold = failureThreshold;
            _resetTimeoutMs = resetTimeoutMs;
        }
        
        /// <summary>
        /// Current circuit state (for diagnostics).
        /// </summary>
        public CircuitState State
        {
            get { lock (_lock) return _state; }
        }
        
        /// <summary>
        /// Number of consecutive failures recorded.
        /// </summary>
        public int FailureCount
        {
            get { lock (_lock) return _failureCount; }
        }
        
        /// <summary>
        /// Determines if the module can run this frame.
        /// </summary>
        /// <returns>True if module should execute, false if circuit is open</returns>
        public bool CanRun()
        {
            (CircuitState From, CircuitState To, string Why)? changed = null;
            bool result;
            lock (_lock)
            {
                if (_state == CircuitState.Closed)
                {
                    result = true;
                }
                else if (_state == CircuitState.Open)
                {
                    // Check if enough time has passed to attempt recovery
                    var timeSinceFailure = DateTime.UtcNow - _lastFailureTime;
                    if (timeSinceFailure.TotalMilliseconds > _resetTimeoutMs)
                    {
                        // Transition to HalfOpen - allow one test execution
                        _state = CircuitState.HalfOpen;
                        changed = (CircuitState.Open, CircuitState.HalfOpen, "reset timeout elapsed");
                        result = true;
                    }
                    else
                    {
                        result = false; // Still in cooldown
                    }
                }
                else
                {
                    // HalfOpen state - allow execution to test recovery
                    result = true;
                }
            }
            if (changed is { } c) StateChanged?.Invoke(c.From, c.To, c.Why);
            return result;
        }
        
        /// <summary>
        /// Records successful module execution.
        /// Resets failure count and closes circuit if in HalfOpen state.
        /// </summary>
        public void RecordSuccess()
        {
            bool recovered = false;
            lock (_lock)
            {
                if (_state == CircuitState.HalfOpen)
                {
                    // Recovery successful - close circuit
                    _state = CircuitState.Closed;
                    _failureCount = 0;
                    recovered = true;
                }
                else if (_state == CircuitState.Closed)
                {
                    // Successful execution in normal state - reset failure count
                    _failureCount = 0;
                }
                // Note: Success in Open state shouldn't happen, but handle gracefully
            }
            if (recovered) StateChanged?.Invoke(CircuitState.HalfOpen, CircuitState.Closed, "recovered");
        }
        
        /// <summary>
        /// Records module failure (exception or timeout).
        /// Increments failure count and opens circuit if threshold exceeded.
        /// </summary>
        /// <param name="reason">Reason for failure (for logging)</param>
        public void RecordFailure(string reason)
        {
            CircuitState? openedFrom = null;
            lock (_lock)
            {
                _lastFailureTime = DateTime.UtcNow;
                _failureCount++;
                
                if (_state == CircuitState.HalfOpen)
                {
                    // Recovery attempt failed - reopen circuit immediately
                    _state = CircuitState.Open;
                    openedFrom = CircuitState.HalfOpen;
                }
                else if (_state == CircuitState.Closed && _failureCount >= _failureThreshold)
                {
                    // Threshold exceeded - open circuit
                    _state = CircuitState.Open;
                    openedFrom = CircuitState.Closed;
                }
            }
            if (openedFrom is { } from) StateChanged?.Invoke(from, CircuitState.Open, reason);
        }
        
        /// <summary>
        /// Resets the circuit breaker to closed state (manual recovery).
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _state = CircuitState.Closed;
                _failureCount = 0;
            }
        }
    }
}
