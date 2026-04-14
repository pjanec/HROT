using System;
using Fdp.Kernel;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// Unique identifier for a networked entity across the distributed system.
    /// Used to map local entities to their global representation.
    /// </summary>
    [ComponentId(GlobalComponentIds.NetworkIdentity)]
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