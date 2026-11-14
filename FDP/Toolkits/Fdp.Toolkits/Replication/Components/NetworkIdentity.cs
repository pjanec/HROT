using System;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// Unique identifier for a networked entity across the distributed system.
    /// Used to map local entities to their global representation.
    /// </summary>
    [ComponentId(GlobalComponentIds.NetworkIdentity)]
    // S8 (B, 2026-11-02): NoScenario - a scenario FILE never carries a network id (an entity is named by its GUID; the load allocates a fresh id per entity).
    // Supersedes CE-277(e), which kept it in the file because the load read it back for the old->new remap - that remap is gone.
    [DataPolicy(DataPolicy.NoScenario)]   // recorded + snapshotted as before; never written to / read from a scenario file
    public struct NetworkIdentity
    {
        /// <summary>
        /// Global ID — a sequential, forward-only 32-bit id (CE-4200: engine, wire and gateways all carry <c>int32</c>;
        /// the allocators refuse loudly rather than issue an id above <see cref="int.MaxValue"/>).
        /// </summary>
        public int Value;

        public NetworkIdentity(int value)
        {
            Value = value;
        }

        public override string ToString() => $"NetID:{Value}";
    }
}