using System;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐⭐ <b>"This component CANNOT start empty, so whoever CREATES the entity must OWN it at birth —
    /// whatever role that creator holds."</b>
    ///
    /// <para>📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.1 (the two categories) ·
    /// <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a (why it is an attribute and not a TKB field).</para>
    ///
    /// <h3>The rule it carves out</h3>
    /// <para>Role-affinity ownership says <i>"a node owns a component only if it holds the role that
    /// component belongs to"</i>. ⛔ Applied to spatial state that rule is WRONG: a Brain-role node creating
    /// a tank would produce <see cref="SimTransform"/> UNOWNED, because the Muscle node that will eventually
    /// own it does not exist in the decision yet. ⇒ this attribute is the exception — the creator keeps it
    /// and hands it off later through the explicit <c>DeferredTakeOwnership</c> → <c>OwnershipUpdate</c>
    /// path.</para>
    ///
    /// <h3>⭐ The test, and it is a question about the COMPONENT</h3>
    /// <para><b>"Can this component start empty?"</b> An idle blackboard on tick 0 is correct, so cognitive
    /// state is role-affine and may be claimed a frame later by whoever holds the role.
    /// <c>(0,0,0)</c> is an origin flash, a wrong spatial-hash cell and a bogus first path query, so
    /// position is not.</para>
    ///
    /// <para>⚠ <b>It is NOT "is this value per-instance?"</b> — that is
    /// <see cref="PerInstanceValueAttribute"/>, which answers a different question on a different leg. A
    /// component may carry either, both, or neither. 📐 Today <see cref="SimTransform"/> carries both;
    /// <c>EntityInfo</c> and <see cref="SimVelocity"/> carry only <c>[PerInstanceValue]</c>.</para>
    ///
    /// <h3>🔴 What breaks without it — measured, and it is NOT the "origin flash" older comments claimed</h3>
    /// <para>An earlier account said <i>"every egress translator gates on HasAuthority, so the coordinate is
    /// written and never published"</i>. 🔴 <b>No egress translator reads the per-component
    /// <c>AuthorityMask</c> at all</b> — they consult <c>DescriptorOwnership</c>/<c>NetworkAuthority</c>
    /// (<c>DESIGN_Role_Affinity_Ownership.md</c> §3.6). What actually breaks is quieter:</para>
    /// <list type="bullet">
    ///   <item><c>CarKinematicsSystem.cs:73</c> filters <c>.WithOwned&lt;SimTransform&gt;()</c> ⇒ <b>the
    ///   entity never moves on the node that created it</b>.</item>
    ///   <item><c>GeoSpatialIngressTranslator.cs:90</c> applies an incoming position <b>only when
    ///   <c>HasAuthority&lt;SimTransform&gt;</c> is false</b> ⇒ the creator treats its own entity as remote
    ///   and overwrites its position from the wire.</item>
    /// </list>
    ///
    /// <h3>⛔ And no ROLE may own one either — the mirror failure</h3>
    /// <para>The same <c>GeoSpatialIngressTranslator</c> check, inverted: a node that claimed
    /// <see cref="SimTransform"/> <b>by role</b> while PROMOTING someone else's ghost would declare itself
    /// owner of a position it does not simulate and stop accepting the real owner's updates ⇒ <b>every ghost
    /// on that node freezes</b>. ⇒ birth-critical components are excluded from every role's owned set
    /// (<c>HrotRoleComponentSets</c>); the creator's birthright is the ONLY way to get one at birth.</para>
    ///
    /// <h3>⭐⭐ Over-declaring is FREE; under-declaring is SILENT</h3>
    /// <para>The create leg computes <c>AuthorityMask = componentMask ∧ OwnableMask(...)</c>
    /// (<c>NetworkSpawningSystem.cs:237</c>), so marking a component the entity never carries contributes no
    /// bits — <b>a positionless entity simply never gets one</b>. ⇒ there is no per-template filter and none
    /// is needed. ⛔ The dangerous direction is forgetting it, which surfaces as an entity that will not
    /// move and says nothing about why.</para>
    ///
    /// <h3>⚠ Why an ATTRIBUTE rather than a TKB field</h3>
    /// <para>🔒 User, <c>2026-09-13</c>: <i>"we can add TKB record override any time later. so if it can be
    /// derived or defined via component attribute and it works for all todays or imaginable future use
    /// cases, the tkb in-memory record can be just a readonly cache."</i></para>
    /// <para>📐 The effective set IS per TKB type — but it is per type <b>because of which components that
    /// type carries</b>, and the create leg's intersection already computes exactly that, per entity, at
    /// runtime. A hand-authored per-template list can only restate a component fact, and can restate it
    /// wrongly: 8 authoring sites, 3 producers, and a file-loaded template that got none at all
    /// (<c>CE-259az</c>). ⇒ <see cref="TkbTemplate.BirthCriticalComponents"/> is now a derived read-only
    /// view of this attribute.</para>
    ///
    /// <h3>🔒 The house pattern</h3>
    /// <para>Per-component-type metadata travels as an attribute here — <see cref="ComponentIdAttribute"/>
    /// (mandatory since auto-assignment was removed) and <c>DataPolicyAttribute</c>. This is the same shape
    /// and is read the same way.</para>
    /// </summary>
    /// <example>
    /// <code>
    /// [ComponentId(GlobalComponentIds.SimTransform)]
    /// [BirthCritical]                       // (0,0,0) is never a correct starting value
    /// [PerInstanceValue]                    // ...and the real one is authored or replicated
    /// public struct SimTransform { ... }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class BirthCriticalAttribute : Attribute
    {
    }
}
