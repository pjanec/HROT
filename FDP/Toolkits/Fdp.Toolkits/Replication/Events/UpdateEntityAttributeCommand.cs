using System.Collections.Generic;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Toolkit.Replication.Events
{
    /// <summary>
    /// Managed event expressing an intent to patch an entity's attributes.
    /// Bridged to DDS by the egress translator.
    ///
    /// <para>
    /// Published by UI tools (e.g. <c>EntityRotationTool</c>) when the operator
    /// requests a change to an attribute on an entity that may be owned by a remote
    /// authoritative node.  In distributed mode the translator converts this event into
    /// an <c>UpdateEntityAttributeRequest</c> DDS sample; in offline (Editor) mode the
    /// local fast-path in the tool applies the change directly to the ECS component.
    /// </para>
    ///
    /// <para>⭐⭐⭐ <b><c>AX-005b</c> — this event is the FDP-internal cross-node change intent, and it
    /// ALREADY EXISTED.</b> 📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §11.3 asked for a NEW
    /// <c>EntityAttributeChangeIntent</c>; 📐 measured <c>2026-08-25</c>: this type is FDP-internal
    /// *(`Fdp.Toolkits`, no DDS reference)*, it is drained by
    /// <c>UpdateEntityAttributeCommandEgressTranslator</c>, and that translator is **REGISTERED IN
    /// PRODUCTION** *(`SharedTranslatorPack.cs:79`)* and already published by <c>ExConOrbatAdapter</c>.
    /// ⇒ ⭐⭐ **the cross-node request path is live; only its BINARY arm was missing.** ⛔ Adding a second
    /// internal event and a second translator writing the same DDS topic would be two implementations of
    /// one concept (ruling 9). ⇒ <b>EXTENDED, not duplicated.</b></para>
    /// </summary>
    public sealed class UpdateEntityAttributeCommand
    {
        /// <summary>
        /// Network entity ID of the target entity.
        /// Resolved from <c>NetworkIdentity.Value</c> by the publishing tool.
        /// </summary>
        public long NetworkId;

        /// <summary>
        /// Hierarchical JSON attribute patch, e.g. <c>{"Heading":340.7}</c>.
        /// Processed by <c>JsonAttributeCompiler</c> on the authoritative node.
        /// ⭐ Optional since <c>AX-005b</c> — a purely binary command leaves this empty.
        /// </summary>
        public string AttributePatchJson = string.Empty;

        /// <summary>
        /// ⭐⭐⭐ <b><c>AX-005b</c> — the BINARY arm: strongly-typed attribute changes in
        /// <b>FDP-INTERNAL</b> terms.</b>
        ///
        /// <para>⛔⛔ <c>R-134</c>: this is <see cref="EntityAttributeChange"/> — the internal record with
        /// <see cref="AttributeValueKind"/> — and ⛔ **never** the DDS <c>AttributeRecord</c>. The egress
        /// translator is the sole place the two meet. ⭐ Precedent: internal
        /// <c>Fdp.Toolkits.Navigation.NavigationIntent</c> vs its wire twin, converted only in its egress.</para>
        ///
        /// <para>⭐⭐ <b>Why here rather than a parallel event.</b> The two arms address the same request to
        /// the same owner over the same DDS topic and differ only in how the value is encoded. ⇒ one event,
        /// one translator, one registration — ⛔ and no second path to keep in step.</para>
        ///
        /// <para>⚠ <see langword="null"/> or empty means *"JSON only"*, which is what every existing
        /// publisher sends. ⇒ ⭐ the extension is **additive**: `ExConOrbatAdapter` and the shipped
        /// translator behave exactly as before.</para>
        /// </summary>
        public IReadOnlyList<EntityAttributeChange>? AttributeChanges;
    }
}
