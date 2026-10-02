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
    public struct NetworkIdentity
    {
        /// <summary>
        /// Global ID (GUID-like or sequential unique ID).
        /// </summary>
        public long Value;

        public NetworkIdentity(long value)
        {
            Value = value;
        }

        public override string ToString() => $"NetID:{Value}";
    }
}