using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Interfaces
{
    /// <summary>
    /// Projects N TKB descriptor DTOs into M ECS components on a live entity.
    /// Mirrors IDescriptorTranslator for TKB content; same N:M projection mechanics.
    ///
    /// <para>⭐⭐⭐ <b>Why the registration guard on <see cref="Inject"/> is not just defensive coding —
    /// it is how a host narrows what it materialises.</b> Every write is double-gated: ① the TEMPLATE
    /// decides whether the type carries the descriptor at all (<c>GetDescriptor&lt;TDto&gt;() == null
    /// ⇒ return</c>), and ② the WORLD decides whether this host wants the component
    /// (<c>IsComponentTypeRegistered&lt;TComponent&gt;()</c>). ⇒ 🔒 <b>a translator whose components a
    /// host never registered is already a no-op on that host.</b></para>
    ///
    /// <para>⛔⛔ <b>Therefore the translator LIST is NOT the narrowing lever.</b> Give every node its
    /// full projection set and let gate ② do the narrowing; express "this host does not want X" by
    /// <b>not registering X</b>, which is one loud decision in the host's component registry, rather
    /// than by omitting a translator, which fails silently for every entity the host ever spawns.
    /// ⚠ An EMPTY list is never a curation choice — it disables both gates at once.
    /// 📌 Measured 2026-08-30 (<c>CE-138</c>): a host reached production with no translators, spawning
    /// entities that carried identity and DIS type but none of their type's kinematics, combat,
    /// perception, behaviour or presentation.</para>
    ///
    /// <para>📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.1 (the guard), §6.3/§6.5 (one list per node,
    /// shared by <c>NetworkSpawningSystem</c>, <c>BlueprintApplicationSystem</c> and
    /// <c>GhostPromotionSystem</c>).</para>
    /// </summary>
    public interface ITkbEntityTranslator
    {
        /// <summary>
        /// Returns the CLR types of TKB descriptor DTOs this translator consumes.
        /// The pipeline uses this to track which descriptors have been projected.
        /// </summary>
        IEnumerable<Type> GetConsumedDescriptors();

        /// <summary>
        /// Projects data from <paramref name="template"/> into ECS components on
        /// <paramref name="entity"/>. Implementations MUST call
        /// <c>repo.IsComponentTypeRegistered&lt;T&gt;()</c> before every
        /// <c>repo.AddComponent&lt;T&gt;()</c> call.
        /// </summary>
        void Inject(EntityRepository repo, Entity entity, TkbTemplate template);

        /// <summary>
        /// ⭐⭐⭐ <b>The component types <see cref="Inject"/> can ADD — the declaration half of what this
        /// translator does.</b> 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a, §6.6b.
        ///
        /// <para>⭐⭐ <b>Why it exists, and it is NOT an optimisation.</b> It is the TEMPLATE FILTER in the
        /// derivation of a template's mandatory (ghost-promotion) components:
        /// <c>[PerInstanceValue] ∩ produced ∩ ingressible ∩ registered</c>. ⛔ Without it the derivation
        /// could require a component this template will never carry, and a HARD requirement that never
        /// arrives is a ghost that <b>never promotes, every frame, forever</b>. ⇒ this member is what makes
        /// a hard requirement SAFE to derive.</para>
        ///
        /// <para>⛔⛔ <b>DECLARE WHAT THIS TRANSLATOR ADDS, NOT WHAT IT READS OR REMOVES.</b> Components the
        /// implementation only reads, or explicitly strips, are NOT produced — declaring them would widen the
        /// mandatory set toward the deadlock above. ⭐ A translator that adds nothing returns an empty
        /// sequence, and that is a correct answer *(the observer and strip shapes both do)*.</para>
        ///
        /// <para>⚠ <b>DECLARE IT UNCONDITIONALLY, even when <see cref="Inject"/> adds it only under a
        /// branch.</b> The consumer intersects with what the host REGISTERS and with what its network stack
        /// can deliver, so a component named here but not added on some path costs nothing. ⛔ The dangerous
        /// direction is the other one: a component OMITTED here is silently dropped from the gate, and the
        /// symptom is a ghost promoted before its real position arrived — an origin flash, not an error.</para>
        ///
        /// <para>⛔ <b>No default implementation, deliberately.</b> A defaulted <c>Array.Empty</c> would let a
        /// new translator silently narrow every derived gate on its host — the SILENT-DEFAULT pattern this
        /// repo has measured three times. ⇒ the compiler asks every implementer the question.</para>
        /// </summary>
        IEnumerable<Type> GetProducedComponents();
    }
}
