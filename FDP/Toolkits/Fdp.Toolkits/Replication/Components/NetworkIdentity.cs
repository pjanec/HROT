using System;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// Unique identifier for a networked entity across the distributed system.
    /// Used to map local entities to their global representation.
    /// </summary>
    [ComponentId(GlobalComponentIds.NetworkIdentity)]
    // CE-277(e): NOT [DataPolicy(NoScenario)]. Measured (StagingEntityExtractor.cs:239/280/305):
    // the LOAD path READS the network id back out of the scenario DOM to pre-allocate/remap ids,
    // so this component MUST be written to the scenario file. It is stripped from a loaded entity's
    // InitialComponents by StagingEntityExtractor.BuildStaticMask AFTER its id is consumed — a
    // different concern from save-exclusion. Marking it NoScenario broke 8 extractor rails.
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
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