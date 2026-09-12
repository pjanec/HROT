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
    }
}
