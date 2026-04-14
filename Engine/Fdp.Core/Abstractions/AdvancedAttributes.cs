using System;

namespace ModuleHost.Core.Abstractions
{
    public enum ExecutionMode
    {
        Synchronous,
        SlowBackground,
        FastBackground,
        Parallel
    }

    public enum SnapshotMode
    {
        None,
        OnDemand,
        Continual
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class ExecutionPolicyAttribute : Attribute
    {
        public ExecutionMode Mode { get; }
        public int Priority { get; }

        public ExecutionPolicyAttribute(ExecutionMode mode, int priority = 0)
        {
            Mode = mode;
            Priority = priority;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class SnapshotPolicyAttribute : Attribute
    {
        public SnapshotMode Mode { get; }

        public SnapshotPolicyAttribute(SnapshotMode mode)
        {
            Mode = mode;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class WatchEventsAttribute : Attribute
    {
        public Type EventType { get; }

        public WatchEventsAttribute(Type eventType)
        {
            EventType = eventType;
        }
    }
}
