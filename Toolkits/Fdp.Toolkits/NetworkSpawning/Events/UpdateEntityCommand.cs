using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.NetworkSpawning.Events
{
    /// <summary>
    /// Universal command to update one or more components on an existing network entity.
    /// Bridged from DDS UpdateEntityDescriptorRequest by the responsible translator.
    /// </summary>
    public class UpdateEntityCommand
    {
        /// <summary>
        /// The network entity ID to update. Must be registered in NetworkEntityMap.
        /// </summary>
        public long NetworkId;

        /// <summary>
        /// List of component instances to apply. Each item replaces the existing component.
        /// Uses the same List&lt;object&gt; / EntityComponentReflector pattern as SpawnEntityCommand.
        /// </summary>
        public List<object>? ComponentsToUpdate;

        /// <summary>
        /// Optional correlation ID.
        /// </summary>
        public Guid RequestId;
    }
}
