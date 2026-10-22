using System;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ <c>CE-3305</c> — a NON-PUBLIC field the flight recorder writes and reads like a public one. The recorder's serializer
    /// (<see cref="FlightRecorder.FdpAutoSerializer"/>) sees public fields and public settable properties only; a component that keeps
    /// its state behind a read-only view (<c>RoutePlan</c>'s waypoints, edited only through <c>Mutate</c>) marks the backing field with
    /// this so a recording — a replay, a checkpoint, a dry run's keyframe — carries it.
    /// <para>⚠ The field must not be <c>readonly</c>: a read assigns it.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class RecordedMemberAttribute : Attribute
    {
    }
}
