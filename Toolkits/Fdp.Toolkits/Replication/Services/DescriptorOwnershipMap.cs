using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Services
{
    /// <summary>
    /// Maps SST descriptor types to FDP component types and component IDs for ownership tracking.
    /// Bridges SST's descriptor-level ownership with FDP's component-level ownership.
    ///
    /// <para>
    /// This is the <em>Single Source of Truth</em> for the network-descriptor -> ECS-component
    /// mapping.  It is populated during module bootstrap by iterating all registered
    /// <c>IDescriptorTranslator</c> instances and calling
    /// <see cref="RegisterFromTranslator"/> with each translator's
    /// <c>DescriptorOrdinal</c> and <c>TargetComponentIds</c>.  Once populated,
    /// <c>OwnershipIngressSystem</c> and <c>DeferredTakeoverSystem</c> call
    /// <see cref="GetComponentIdsForDescriptor"/> to obtain the precise integer component IDs
    /// on which <c>EntityRepository.SetAuthority(entity, componentId, bool)</c> must be called
    /// -- eliminating the legacy try/catch hack that miscast ordinals as component IDs.
    /// </para>
    /// </summary>
    public class DescriptorOwnershipMap
    {
        private readonly Dictionary<long, Type[]> _descriptorToComponentTypes = new();
        private readonly Dictionary<long, int[]>  _descriptorToComponentIds   = new();
        private readonly Dictionary<Type, long>   _componentTypeToDescriptor  = new();

        // -- Registration ---------------------------------------------------------

        /// <summary>
        /// Registers which FDP component <em>types</em> correspond to a descriptor.
        /// Suitable when component IDs are not known at registration time.
        /// </summary>
        public void RegisterMapping(long descriptorTypeId, params Type[] componentTypes)
        {
            _descriptorToComponentTypes[descriptorTypeId] = componentTypes;
            foreach (var type in componentTypes)
                _componentTypeToDescriptor[type] = descriptorTypeId;
        }

        /// <summary>
        /// Registers which FDP component IDs (integers) correspond to a descriptor.
        /// This is the preferred overload -- it is used by <c>OwnershipIngressSystem</c> and
        /// <c>DeferredTakeoverSystem</c> to avoid the legacy ordinal--component-ID mismatch.
        /// </summary>
        public void RegisterMapping(long descriptorTypeId, params int[] componentIds)
        {
            _descriptorToComponentIds[descriptorTypeId] = componentIds;
        }

        /// <summary>
        /// Convenience registration from a translator's declared
        /// <c>DescriptorOrdinal</c> and <c>TargetComponentIds</c>.
        /// </summary>
        public void RegisterFromTranslator(long descriptorOrdinal, IReadOnlyList<int> targetComponentIds)
        {
            if (targetComponentIds == null || targetComponentIds.Count == 0) return;
            var ids = new int[targetComponentIds.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = targetComponentIds[i];
            _descriptorToComponentIds[descriptorOrdinal] = ids;
        }

        // -- Lookup ---------------------------------------------------------------

        /// <summary>
        /// Returns the ECS component IDs registered for the given descriptor ordinal.
        /// Returns an empty span when not registered.
        /// </summary>
        public ReadOnlySpan<int> GetComponentIdsForDescriptor(long descriptorTypeId)
        {
            return _descriptorToComponentIds.TryGetValue(descriptorTypeId, out var ids)
                ? ids
                : ReadOnlySpan<int>.Empty;
        }

        /// <summary>
        /// Returns the FDP component <em>types</em> for a descriptor (legacy path).
        /// Prefer <see cref="GetComponentIdsForDescriptor"/> for authority-mask operations.
        /// </summary>
        public Type[] GetComponentsForDescriptor(long descriptorTypeId)
        {
            return _descriptorToComponentTypes.TryGetValue(descriptorTypeId, out var types)
                ? types
                : Array.Empty<Type>();
        }

        /// <summary>
        /// Returns the descriptor type ID for a component type, or 0 if not registered.
        /// </summary>
        public long GetDescriptorForComponent(Type componentType)
        {
            return _componentTypeToDescriptor.TryGetValue(componentType, out var id) ? id : 0;
        }
    }
}
