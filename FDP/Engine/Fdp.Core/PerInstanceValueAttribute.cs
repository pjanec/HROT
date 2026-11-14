using System;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐⭐ <b>"This component's AUTHORITATIVE INITIAL VALUE belongs to the INSTANCE, not the template —
    /// so a TKB default must never be mistaken for the real thing."</b>
    ///
    /// <para>📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a. Consumer: the promotion gate,
    /// <c>GhostPromotionSystem</c>, via <see cref="TkbTemplate.MandatoryComponents"/>.</para>
    ///
    /// <h3>The rule</h3>
    /// <para>A TKB translator stamps a value derived from the template's descriptors — the same value for
    /// every instance of that type. For most components that IS the correct initial state
    /// (<c>Health</c> from <c>MaxHealth</c>, <c>VehicleParams</c> from the physics DTO, an empty
    /// <c>TargetMemory</c>). ⛔ For a few it is a placeholder that must LOSE to the instance's own value:
    /// a spawn coordinate, an authored faction. This attribute marks those.</para>
    ///
    /// <h3>⭐ The test, and the two it must not be confused with</h3>
    /// <list type="bullet">
    ///   <item>✅ <b>"Would the template's default be WRONG, not merely initial?"</b> ⇒ this attribute.</item>
    ///   <item>⛔ <i>"Can this component start empty?"</i> ⇒ that is <see cref="BirthCriticalAttribute"/> —
    ///   a question about OWNERSHIP on the CREATE leg, not about promotion timing.</item>
    ///   <item>⛔ <i>"Can it be set at spawn?"</i> ⇒ almost everything can. <c>StagingEntityExtractor</c>
    ///   restores whatever a scenario saved, so a damaged tank's <c>Health</c> is per-instance too — 📐 but
    ///   that arrives through <c>SpawnEntityCommand.InitialComponents</c> at spawn <b>step 8</b>,
    ///   synchronously, before any system runs. <b>Nothing ever has to WAIT for it</b>, so it is not this.</item>
    /// </list>
    ///
    /// <h3>⭐⭐ CREATE leg versus PROMOTE leg — the distinction this attribute lives on</h3>
    /// <list type="table">
    ///   <item><term>create</term><description><c>InitialComponents</c> is applied ON TOP of the TKB
    ///   defaults (<c>NetworkSpawningSystem</c> step 4 then step 8) — synchronously ⇒ no gate is ever
    ///   needed.</description></item>
    ///   <item><term>promote</term><description>a GHOST on another node receives the value
    ///   ASYNCHRONOUSLY, over the wire, AFTER the shell exists ⇒ the only thing a promotion gate can be
    ///   about, and the only reason this attribute exists.</description></item>
    /// </list>
    ///
    /// <h3>⭐ It is NETWORK-AGNOSTIC, deliberately</h3>
    /// <para>It states <i>"per-instance; the template cannot know it"</i> — ⛔ <b>NOT</b> <i>"arrives over the
    /// wire"</i>. The wire is merely how it arrives when there is one, and a networkless node has no ghosts
    /// so the gate never runs there at all. ⚠ Putting a network fact on a component type in
    /// <c>Fdp.Core</c> is exactly what <c>DESIGN_Role_Affinity_Ownership.md</c> §2.3 forbids.</para>
    /// <para>📐 The case that forces the separation: <see cref="SimVelocity"/> genuinely IS per-instance
    /// (<c>SpawnEntityCommand.InitialVelocity</c>) and so carries this attribute — but the wire writes
    /// <c>NetworkVelocity</c>, not <see cref="SimVelocity"/>, so the derivation's INGRESS intersection drops
    /// it. ⇒ excluded for the right reason, not by mislabelling the component.</para>
    ///
    /// <h3>⛔⛔ ADDING THIS TO A COMPONENT IS NOT FREE — the requirement it produces is HARD</h3>
    /// <para>Unlike <see cref="BirthCriticalAttribute"/>, over-declaring here is not harmless: a hard
    /// requirement that never arrives makes <c>GhostPromotionSystem</c> <c>return</c> every frame, forever
    /// — a ghost that never promotes, silently.</para>
    /// <para>🔒 <b>THE ADDITION RULE: a component may carry this only if its descriptor's egress guarantees a
    /// BASELINE sample for a NEWLY SPAWNED entity.</b> ⛔ NOT "is published unconditionally" — change-driven
    /// egress is the norm here and is deliberate, to keep redundant traffic off the wire. Today the baseline
    /// is guaranteed two ways:</para>
    /// <list type="bullet">
    ///   <item><c>SmartEgressUtil</c>'s <b>"Guaranteed First Publish"</b> — it tracks whether a descriptor
    ///   has ever been sent for a newly spawned entity. Covers <c>EntityInfo</c>, <c>EntityMaster</c>,
    ///   <c>EntityMission</c>, <c>WeaponState</c>.</item>
    ///   <item><c>GeoSpatialEgressTranslator</c>'s <b>zero-seeded shadow</b> — it deliberately avoids
    ///   <c>SmartEgressUtil</c> (too costly at 60 Hz) and diffs against a <c>NetworkTransform</c> seeded to
    ///   zeros, which its own comment calls <i>"a BEHAVIOURAL REQUIREMENT, not a detail … Zeros force a
    ///   first publish"</i>. Covers <see cref="SimTransform"/>.</item>
    ///   <item><c>REFRESH_INTERVAL = 600</c> (10 s at 60 Hz) is the backstop for dropped UDP, ⛔ not the
    ///   design.</item>
    /// </list>
    /// <para>⚠ <b>The shape that would break it:</b> a purely diff-driven egress whose shadow is seeded from
    /// the LIVE value — the first comparison says <i>"unchanged"</i>, no baseline is sent, and the gate waits
    /// for the heartbeat or forever. 📌 That is precisely the trap <c>GeoSpatialEgressTranslator</c>
    /// documents having avoided.</para>
    ///
    /// <h3>⚠ The derivation this feeds — it is NOT simply "everything with this attribute"</h3>
    /// <code>
    /// mandatory = componentsWith[PerInstanceValue]
    ///           ∩ produced(host translators whose consumed descriptors this template carries)
    ///           ∩ componentsThisHostCanINGRESS      // DescriptorOwnershipMap
    ///           ∩ componentsThisHostRegisters       // HARD, no timeout
    /// </code>
    /// <para>⭐ The three intersections are what make a HARD requirement safe: they answer <i>"will this
    /// entity, on THIS host, actually receive it?"</i>. 📐 Today the result is exactly
    /// <see cref="SimTransform"/> and <c>EntityInfo</c>.</para>
    /// </summary>
    /// <example>
    /// <code>
    /// [ComponentId(GlobalComponentIds.EntityInfo)]
    /// [PerInstanceValue]        // faction is authored per spawn; the template default must not win
    /// public struct EntityInfo { ... }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PerInstanceValueAttribute : Attribute
    {
    }
}
