using System;
using Fdp.Core;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// ⭐ <c>CE-3072</c> B3 — one answer of a danger-area sensor: the areas along its route, in route order, with NO threat
    /// (the Brain rates them). 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    /// <para>Published twice per answer on its way, as the ranked path does with its own events:</para>
    /// <list type="bullet">
    ///   <item>by the SOLVER (<c>DangerAlongRouteSolve</c>), keyed by (<see cref="ParentNetworkId"/>, <see cref="LocalChildIndex"/>),
    ///     <see cref="Observer"/> null — read by <c>DangerAreaResultEgressTranslator</c> and, in a fused world (the editor), by
    ///     the Brain's <c>DangerAreaSensorSystem</c>;</item>
    ///   <item>by the Brain's <c>DangerAreaResultIngressTranslator</c>, with <see cref="Observer"/> = the local child sensor.</item>
    /// </list>
    /// </summary>
    public sealed class DangerAreaResultEvent
    {
        /// <summary>The unit's network id (0 = a local-only sensor, keyed by its entity index).</summary>
        public long ParentNetworkId;
        /// <summary>The sensor child's part id (or its entity index when local-only).</summary>
        public int LocalChildIndex;
        /// <summary>The sensor's epoch at solve time — a stale answer is dropped.</summary>
        public uint Epoch;
        /// <summary>The solver's tick (0 is replaced by 1 when written, so the answer reads ready).</summary>
        public uint RefreshTick;
        /// <summary>The Brain's child sensor, when the ingress resolved it; null on the solver's own publish.</summary>
        public Entity Observer = Entity.Null;
        /// <summary>The areas, in route order (first <see cref="Count"/> valid).</summary>
        public DangerAreaDescriptor[] Areas = Array.Empty<DangerAreaDescriptor>();
        /// <summary>How many of <see cref="Areas"/> are valid.</summary>
        public int Count;
    }
}
