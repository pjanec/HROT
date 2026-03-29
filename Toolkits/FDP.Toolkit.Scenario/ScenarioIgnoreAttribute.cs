using System;

namespace FDP.Toolkit.Scenario
{
    /// <summary>
    /// Marks a field on an ECS component struct as excluded from scenario
    /// serialisation/deserialisation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>FdpAutoSerializer</c> inspects this attribute once at build time when compiling
    /// field-level extraction/injection delegates.  Fields marked
    /// <c>[ScenarioIgnore]</c> are silently omitted from the compiled delegate — they
    /// produce no entry in the JSON DOM and are never read back during load.
    /// </para>
    /// <para>
    /// Use this attribute for fields that carry runtime-only cached state (e.g.
    /// precomputed values, handles, or debug counters) that must not leak into the
    /// persistent scenario format.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class ScenarioIgnoreAttribute : Attribute { }
}
