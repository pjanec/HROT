using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Replication;

/// <summary>
/// ⭐⭐⭐ <b><c>Q59-E</c> — which descriptors cover a given ECS component. The INVERSE of what the network
/// layer already declares.</b>
///
/// <para>📄 <c>docs/blueprints/Architect_Question_59_Attribute_Vocabulary_Single_Source.md</c> §7 · §9.</para>
///
/// <para>🔒 <b>User ruling, <c>2026-08-26</c>:</b> *"attributes are entity-related, network agnostic. In
/// contrary, descriptors are Ned network concept and descriptor compiler/translator belongs to network
/// namespace."*</para>
///
/// <para>⭐⭐⭐ <b>Why this type exists, and why it is NOT a new vocabulary.</b> An attribute applier's only
/// legitimate act is *"I wrote component X."* ⛔ It must not name a descriptor — that is a NED grouping.
/// ⭐ But something has to translate *"component X changed"* into *"republish descriptor N"*, and
/// <see cref="IDescriptorTranslator"/> **already declares both halves** —
/// <see cref="IDescriptorTranslator.DescriptorOrdinal"/> and
/// <see cref="IDescriptorTranslator.TargetComponentIds"/>. ⇒ ⭐⭐ this class only INVERTS that, so no new
/// declaration is introduced anywhere.</para>
///
/// <para>⭐ <b>It is network-agnostic despite its subject.</b> 📐 An ordinal arrives as an opaque
/// <c>long</c> off an interface that lives in <c>Fdp.Core.Abstractions</c>; a component arrives as an
/// <c>int</c> id. ⛔ No DDS type, no <c>EDescriptorType</c>, nothing to convert — which is exactly why
/// <c>Fdp.Toolkit.Replication.DescriptorOrdinal</c> and its conversion could be deleted. <b>FDP declares the
/// SEAM; NED declares the VALUES.</b></para>
///
/// <para>⚠⚠ <b>A component maps to a SET of ordinals, not one — measured, not assumed.</b>
/// 📐 <c>2026-08-26</c>: <c>GlobalComponentIds.SimTransform</c> is declared by <b>both</b>
/// <c>BdcWorldPosTranslator</c> and <c>GeoSpatialEgressTranslator</c>; <c>NetworkIdentity</c>/<c>TkbIdentity</c>
/// by <b>both</b> <c>BdcEntityMasterTranslator</c> and <c>EntityMasterEgressTranslator</c>. ⇒ ⭐ marking
/// EVERY covering descriptor dirty is the correct behaviour.</para>
///
/// <para>⭐⭐ <b>An EMPTY map is a legitimate state, not a failure.</b> On a networkless host — the editor's
/// all-in-one in-process mode — there are no translators, so nothing is republishable and marking nothing is
/// right. ⛔ Do not add a *"map must be non-empty"* guard: it would break the networkless build, which is a
/// supported configuration by design.</para>
/// </summary>
public sealed class ComponentDescriptorMap
{
    /// <summary>⭐ The shared empty map — a networkless host's correct state.</summary>
    public static readonly ComponentDescriptorMap Empty = new(new Dictionary<int, long[]>(0));

    private readonly IReadOnlyDictionary<int, long[]> _byComponentId;

    private ComponentDescriptorMap(IReadOnlyDictionary<int, long[]> byComponentId)
        => _byComponentId = byComponentId;

    /// <summary>
    /// ⭐⭐⭐ Inverts <paramref name="translators"/> into component id → covering descriptor ordinals.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Translators that declare no <c>TargetComponentIds</c> contribute nothing, silently — and that is
    /// deliberate.</b> 📐 Measured <c>2026-08-26</c>: only 9 of 41 egress translators declare it, and most of
    /// the rest do not need to *(their dirty marks come from domain systems calling
    /// <c>SmartEgressUtil.MarkDirty</c> with an explicit ordinal, not from the attribute path)*. ⇒ ⛔ a
    /// *"every translator must declare"* rule would be noise. ⭐ The invariant that DOES matter is narrower and
    /// is railed instead: <b>every component an attribute definition writes must be covered by some
    /// translator.</b>
    /// </remarks>
    public static ComponentDescriptorMap Build(IEnumerable<IDescriptorTranslator> translators)
    {
        if (translators is null) return Empty;

        var acc = new Dictionary<int, SortedSet<long>>();

        foreach (var t in translators)
        {
            if (t is null) continue;

            var ids = t.TargetComponentIds;
            if (ids is null || ids.Count == 0) continue;

            foreach (int componentId in ids)
            {
                if (!acc.TryGetValue(componentId, out var set))
                    acc[componentId] = set = new SortedSet<long>();
                set.Add(t.DescriptorOrdinal);
            }
        }

        return new ComponentDescriptorMap(
            acc.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
    }

    /// <summary>
    /// ⭐ The descriptors covering <paramref name="componentId"/>, or empty when none do.
    /// ⛔ Empty is not an error — see the class remarks.
    /// </summary>
    public ReadOnlySpan<long> OrdinalsFor(int componentId)
        => _byComponentId.TryGetValue(componentId, out var ordinals)
            ? ordinals
            : ReadOnlySpan<long>.Empty;

    /// <summary>⭐ The covered component ids — for the coverage rail, and for diagnostics.</summary>
    public IEnumerable<int> CoveredComponentIds => _byComponentId.Keys;

    /// <summary>⭐ True when no translator declared anything, i.e. a networkless host.</summary>
    public bool IsEmpty => _byComponentId.Count == 0;
}
