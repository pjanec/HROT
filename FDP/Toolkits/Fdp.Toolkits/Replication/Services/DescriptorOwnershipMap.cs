using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// ⭐⭐⭐ <b><c>Q59-E</c> — the REVERSE index, component id → every descriptor that covers it.</b>
        ///
        /// <para>📄 <c>docs/blueprints/Architect_Question_59_…md</c> §7.3. 🔒 User ruling <c>2026-08-26</c>:
        /// *"attributes are entity-related, network agnostic. In contrary, descriptors are Ned network
        /// concept."</para>
        ///
        /// <para>⭐⭐ <b>Why it was added HERE rather than as a new type.</b> This class is already, in its own
        /// words, *"the Single Source of Truth for the descriptor → component mapping"*, and
        /// <see cref="RegisterFromTranslator"/> is already the entry point. ⛔ A rival
        /// <c>ComponentDescriptorMap</c> was written first and DELETED before shipping — 📌 the seam law: the
        /// seam existed and was under-adopted, not missing.</para>
        ///
        /// <para>⚠⚠ <b>Two gaps this fills, both measured <c>2026-08-26</c>:</b>
        /// ① <see cref="RegisterFromTranslator"/> filled ONLY the forward direction, so
        /// <see cref="GetDescriptorForComponent"/> saw nothing from translators at all — only from the manual
        /// <c>Type[]</c> overload. ② <c>_componentTypeToDescriptor</c> is <b>single-valued</b>, and a component
        /// genuinely has SEVERAL covering descriptors: <c>SimTransform</c> is declared by both
        /// <c>BdcWorldPosTranslator</c> and <c>GeoSpatialEgressTranslator</c>. ⇒ last-writer-wins would mark
        /// the wrong descriptor.</para>
        ///
        /// <para>⭐ Keyed by component <b>id</b>, not <see cref="Type"/> — that is what translators declare, and
        /// it avoids the *"legacy ordinal-to-component-ID mismatch"* this class's own docs warn about.</para>
        /// </summary>
        private readonly Dictionary<int, SortedSet<long>> _componentIdToDescriptors = new();

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

            // ⭐⭐⭐ Q59-E — and the REVERSE, which this method never used to record. Additive and
            //    multi-valued: several translators legitimately cover one component.
            foreach (int id in ids)
            {
                if (!_componentIdToDescriptors.TryGetValue(id, out var set))
                    _componentIdToDescriptors[id] = set = new SortedSet<long>();
                set.Add(descriptorOrdinal);
            }
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
        /// ⚠ <b>Single-valued and fed only by the <see cref="Type"/> overload</b> — for the translator-fed,
        /// multi-valued answer use <see cref="GetDescriptorsForComponentId"/>.
        /// </summary>
        public long GetDescriptorForComponent(Type componentType)
        {
            return _componentTypeToDescriptor.TryGetValue(componentType, out var id) ? id : 0;
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>Q59-E</c> — every descriptor covering <paramref name="componentId"/>.</b>
        ///
        /// <para>⭐⭐ This is what lets an attribute applier stay network-agnostic: it records the COMPONENT it
        /// wrote, and this answers which descriptors must be republished. ⇒ no FDP type needs a descriptor
        /// vocabulary, which is why <c>Fdp.Toolkit.Replication.DescriptorOrdinal</c> and its conversion could
        /// be deleted outright.</para>
        ///
        /// <para>⚠ <b>EMPTY is a legitimate answer, not a failure.</b> A networkless host registers no
        /// translators, so nothing is republishable and marking nothing is correct. ⛔ Do not add a
        /// non-empty guard — the editor's all-in-one in-process mode is a supported configuration.</para>
        /// </summary>
        public ReadOnlySpan<long> GetDescriptorsForComponentId(int componentId)
            => _componentIdToDescriptors.TryGetValue(componentId, out var set)
                ? set.ToArray()
                : ReadOnlySpan<long>.Empty;

        /// <summary>⭐ The component ids any translator declared — for coverage rails and diagnostics.</summary>
        public IEnumerable<int> CoveredComponentIds => _componentIdToDescriptors.Keys;
    }
}
