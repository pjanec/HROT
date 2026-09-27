using System;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐⭐ <b>Marks a static method as the CURATED params resolver for one behaviour.</b>
    /// 📄 <c>DESIGN_Behavior_Self_Registration.md</c> §9 item 2.
    ///
    /// <para>⭐ <b>The attribute IS the human declaration.</b> 🔒 <c>R-132</c> rules that <i>"where a
    /// curated and a generated artefact can both fill a slot, curated wins BY DECLARATION, not by
    /// arriving first"</i>. Before <c>CE-374</c> that declaration was <i>"someone typed a
    /// <c>RegisterResolver</c> call into <c>CgfCuratedBehaviorRegistrar</c>"</i>; it is now this
    /// attribute. ⛔ <b>The ruling is unchanged — only its probe moved.</b></para>
    ///
    /// <para>🔴 <b>Why the ruling exists, so nobody relaxes it:</b> two producers for one params
    /// region, bound by registration ORDER, is not a precedence rule — it is a race. It left
    /// <c>PlatoonHillAttack</c> being parsed by a generated resolver that expected a different wire
    /// format, every key hit <c>default: break</c>, the params region stayed zeros, and the platoon
    /// drove to <c>(0,0)</c> with no exception and every rail green.</para>
    ///
    /// <para>⭐⭐ <b>Two method shapes are accepted</b>, and the generator wraps the short one:</para>
    /// <list type="bullet">
    ///   <item><b>6-param</b> — <c>(string json, byte* memory, int capacity, EntityRepository world,
    ///         Entity self, IHostVariableAccess? host)</c>: bound directly as a
    ///         <see cref="BehaviorRegistry.ParseParamsDelegate"/>.</item>
    ///   <item><b>3-param</b> — <c>(string json, byte* memory, int capacity)</c>: the generator emits
    ///         the adapter lambda that the hand-written registrar used to spell out twice.</item>
    /// </list>
    ///
    /// <para>⚠ <b>The NAME is the behaviour's registry name</b> — the same string its
    /// <c>[BTreeDefinition]</c> / <c>[HsmDefinition]</c> carries. ⛔ A resolver may name a behaviour
    /// whose TOPOLOGY is owned by a generated JSON registrar (<c>HullDownAttackRun</c>,
    /// <c>PlatoonHillAttack</c> are exactly that) — the overlay binds by name and is
    /// order-independent.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BehaviorResolverAttribute : Attribute
    {
        /// <summary>The behaviour's registry name — the key the overlay binds under.</summary>
        public string BehaviorName { get; }

        /// <summary>
        /// ⭐ Optional params layout type, passed as <c>RegisterResolver</c>'s third argument.
        /// ⚠ Needed when the topology is owned by a GENERATED registrar that expresses the layout
        /// only via <c>ManagedBlackboardVariables</c> — <c>HullDownAttackRun</c> and
        /// <c>PlatoonHillAttack</c> both do.
        /// </summary>
        public Type? ParamsType { get; set; }

        public BehaviorResolverAttribute(string behaviorName)
        {
            BehaviorName = behaviorName;
        }
    }
}
