using System;
using System.Collections.Generic;

namespace Fdp.ModuleHost.Scheduling
{
    /// <summary>
    /// Performance profiling data for a system.
    /// </summary>
    public class SystemProfileData
    {
        public string SystemName { get; }
        
        public long ExecutionCount { get; private set; }
        public double TotalMs { get; private set; }
        public double AverageMs => ExecutionCount > 0 ? TotalMs / ExecutionCount : 0;
        public double MinMs { get; private set; } = double.MaxValue;
        public double MaxMs { get; private set; }
        public double LastMs { get; private set; }
        
        public int ErrorCount { get; private set; }
        public Exception? LastError { get; private set; }
        
        /// <summary>⭐ CE-4364 (the scale rig) — bytes the system allocated on its thread, over every execution.</summary>
        public long TotalBytes { get; private set; }
        public double AverageBytes => ExecutionCount > 0 ? (double)TotalBytes / ExecutionCount : 0;

        private readonly Queue<double> _recentExecutions = new();
        private readonly Queue<long> _recentBytes = new();
        private long _recentBytesSum;
        private const int MaxRecentSamples = 60; // Last 60 executions
        
        public SystemProfileData(string systemName)
        {
            SystemName = systemName;
        }
        
        public void RecordExecution(double milliseconds) => RecordExecution(milliseconds, 0);

        /// <summary>One execution: its time and ⭐ CE-4364 the bytes it allocated (the garbage budget: 0 B per steady frame).</summary>
        public void RecordExecution(double milliseconds, long bytes)
        {
            TotalBytes += bytes;
            _recentBytes.Enqueue(bytes);
            _recentBytesSum += bytes;
            if (_recentBytes.Count > MaxRecentSamples)
                _recentBytesSum -= _recentBytes.Dequeue();
            ExecutionCount++;
            TotalMs += milliseconds;
            LastMs = milliseconds;
            
            if (milliseconds < MinMs)
                MinMs = milliseconds;
            if (milliseconds > MaxMs)
                MaxMs = milliseconds;
            
            _recentExecutions.Enqueue(milliseconds);
            if (_recentExecutions.Count > MaxRecentSamples)
                _recentExecutions.Dequeue();
        }
        
        public void RecordError(Exception ex)
        {
            ErrorCount++;
            LastError = ex;
        }
        
        /// <summary>
        /// Get average of recent executions (last 60).
        /// </summary>
        public double GetRecentAverageMs()
        {
            if (_recentExecutions.Count == 0)
                return 0;
            
            double sum = 0;
            foreach (var ms in _recentExecutions)
                sum += ms;
            
            return sum / _recentExecutions.Count;
        }
        
        /// <summary>⭐ CE-4364 — the average bytes allocated per execution over the recent executions (the last 60).</summary>
        public double GetRecentAverageBytes() => _recentBytes.Count == 0 ? 0 : (double)_recentBytesSum / _recentBytes.Count;

        /// <summary>
        /// Reset all statistics.
        /// </summary>
        public void Reset()
        {
            ExecutionCount = 0;
            TotalMs = 0;
            MinMs = double.MaxValue;
            MaxMs = 0;
            LastMs = 0;
            ErrorCount = 0;
            LastError = null;
            _recentExecutions.Clear();
            TotalBytes = 0;
            _recentBytes.Clear();
            _recentBytesSum = 0;
        }
    }
}
