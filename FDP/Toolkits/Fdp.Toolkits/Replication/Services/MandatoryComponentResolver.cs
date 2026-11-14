using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Replication.Services
{
    /// <summary>
    /// ⭐⭐⭐ <b>Derives a template's MANDATORY components — the ghost-promotion gate — from four facts this
    /// host already holds.</b> 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a (the design), §6.6b (as-built).
    ///
    /// <code>
    /// mandatory = componentsWith[PerInstanceValue]     // Fdp.Core, network-agnostic, host-independent
    ///           ∩ ⋃ produced(t) for host translators t whose consumed descriptors this template carries
    ///           ∩ componentsThisHostCanINGRESS          // DescriptorOwnershipMap.CoveredComponentIds
    ///           ∩ componentsThisHostREGISTERS           // EntityRepository.TryGetTable
    ///                                                   // ⇒ HARD, and no timeout
    /// </code>
    ///
    /// <para>⛔⛔ <b>WHY THIS IS NOT A FIELD ON <see cref="TkbTemplate"/>, unlike its birth-critical
    /// sibling.</b> 🔒 The user's ruling was <i>"the tkb in-memory record can be just a readonly cache"</i>,
    /// and for <c>BirthCriticalComponents</c> that worked because the answer is the SAME ON EVERY NODE.
    /// 🔴 <b>Three of the four inputs above are HOST-LOCAL</b> — this node's translators, this node's network
    /// stack, this node's component registry. ⇒ a template is a SHARED record *(the same file loads on CGF,
    /// SimHost, IG and the editor)*, so a host-local answer stored on it would be wrong for every other
    /// reader of the same object. 📌 That host-dependence is precisely why §6.6 ruled a TKB FILE may never
    /// carry this list either: <c>GhostPromotionSystem</c> has no registration guard, so a requirement for a
    /// component the local host never registers aborts promotion every frame, forever, in silence.</para>
    ///
    /// <para>⭐⭐ <b>Over-declaring is FATAL here, which is the opposite of birth-critical.</b> Birth-critical
    /// is intersected with the entity's live component mask, so naming a component it never receives costs
    /// nothing. A mandatory entry is a WAIT: name one that never arrives and the ghost never promotes.
    /// ⇒ every one of the four intersections is load-bearing, and none may be dropped as "defensive".</para>
    ///
    /// <para>🔒 <b>HARD, with no soft timeout — a user ruling, not a default.</b> <i>"i do not want to wait 10
    /// frames by design - this looks like an emergency (hopefully avoidable) case which i do not want to
    /// promote to usual case."</i> ⇒ the derivation must be EXACT rather than approximate-and-time-out;
    /// <c>MandatoryComponent.SoftTimeoutFrames</c> survives only for explicitly authored entries.</para>
    ///
    /// <para>⭐ <b>Why hard cannot hang on the happy path.</b> The creator ran the SAME translator set over the
    /// SAME template, so it has the component; its egress publishes what it owns and the receiver's ingress
    /// writes it. ⇒ a component this template produces on one host is produced on every host composing the
    /// same translators. ⚠ The residual case — a CREATOR that does not register the component and so never
    /// publishes it — is a configuration error, and <c>GhostPromotionSystem</c> reports it rather than
    /// absorbing it silently.</para>
    /// </summary>
    public sealed class MandatoryComponentResolver
    {
        private readonly Dictionary<long, int[]> _cache = new();

        /// <summary>
        /// ⭐⭐ Number of covered component ids the ingress map held when <see cref="_cache"/> was filled.
        /// A change means the host's network stack contributed more pairings, so every cached answer is
        /// re-derived. ⛔ Not a micro-optimisation — see <see cref="Resolve"/>'s ordering note.
        /// </summary>
        private int _ingressGeneration = -1;

        /// <summary>
        /// ⭐⭐⭐ <b>The derived HARD requirements for <paramref name="template"/> on THIS host.</b> Ascending
        /// component ids; empty is a legitimate answer *(a template with no descriptors produces nothing, and
        /// a host with no network stack receives nothing)*.
        ///
        /// <para>⚠⚠ <b>ORDERING HAZARD, handled here rather than by scheduling.</b> The world's
        /// <c>DescriptorOwnershipMap</c> is filled on the network module's FIRST TICK
        /// *(<c>NedReplicationModule.ContributeDescriptorPairings</c>)*, and this system may run before it.
        /// 🔴 An empty ingress set derives ∅, which would promote a ghost with no gate at all — the failure
        /// this whole mechanism exists to prevent, reached by a race. ⇒ <b>the cache is keyed on the ingress
        /// map's SIZE as well as the template</b>, so the answer self-corrects on the tick the map arrives.
        /// ⭐ On a genuinely networkless host the set stays empty forever and nothing is lost: no ingress
        /// means no ghosts, so this method is never reached for one.</para>
        /// </summary>
        public IReadOnlyList<int> Resolve(
            TkbTemplate template,
            EntityRepository repo,
            IReadOnlyList<ITkbEntityTranslator> translators,
            DescriptorOwnershipMap ingressMap)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (ingressMap == null) throw new ArgumentNullException(nameof(ingressMap));

            var ingress = BuildIngressSet(ingressMap, out int ingressCount);

            // ⭐⭐⭐ THE WHOLE ORDERING FIX IS THESE FOUR LINES: the cache is keyed on the ingress map's SIZE
            //   as well as on the template, so any growth of the map re-derives every answer. ⇒ a ∅ derived
            //   before the network module contributed its pairings cannot survive the tick it arrives.
            //
            // ⚠ MEASURED 2026-09-13, and it corrected this class: a first cut ALSO guarded the write with
            //   `if (ingressCount > 0)`. Its red-proof stayed GREEN — because the generation check already
            //   subsumes it, and a guard nothing can redden is a guard that is not doing the work. Removed
            //   rather than kept as belt-and-braces, so the mechanism that IS load-bearing is the one a
            //   future reader finds. 📌 The rails that pin it: AnEmptyIngressMap_DerivesNothing_AndIsNotCached
            //   and AGrowingIngressMap_InvalidatesTheCache.
            if (ingressCount != _ingressGeneration)
            {
                _cache.Clear();
                _ingressGeneration = ingressCount;
            }
            else if (_cache.TryGetValue(template.TkbType, out var cached))
            {
                return cached;
            }

            var derived = Derive(template, repo, translators, ingress);
            _cache[template.TkbType] = derived;
            return derived;
        }

        private static HashSet<int> BuildIngressSet(DescriptorOwnershipMap map, out int count)
        {
            // ⭐ CoveredComponentIds is the union the derivation wants and it ALREADY EXISTED — fed by
            //   RegisterFromTranslator, i.e. by every descriptor translator that declares TargetComponentIds.
            //
            // ⚠⚠ DIRECTION IS DELIBERATELY NOT FILTERED, and that is a measured decision, not laziness.
            //   📐 2026-09-13: in Hrot.Network.NED, ZERO translators under Replication/Map/Ingress/ declare
            //   TargetComponentIds — the interface defaults it to empty and only the EGRESS side overrides.
            //   ⇒ an "ingress-only" filter would yield ∅ and collapse the whole derivation. The pairing
            //   descriptor↔component is direction-agnostic by nature: it says the wire CAN carry this
            //   component, which is exactly the question here.
            //
            // ⭐ And note what it correctly EXCLUDES: SimVelocity reaches DescriptorOwnershipMap only through
            //   the explicit RegisterMapping(dtWorldPos, …) authority block, which fills no reverse pairing.
            //   ⇒ SimVelocity carries [PerInstanceValue] yet is excluded — for the right reason, "nothing
            //   ingresses it", rather than by mislabelling the component. 📄 §6.6a "THE FOURTH INTERSECTION".
            var set = new HashSet<int>();
            foreach (int id in map.CoveredComponentIds)
                set.Add(id);
            count = set.Count;
            return set;
        }

        private static int[] Derive(
            TkbTemplate template,
            EntityRepository repo,
            IReadOnlyList<ITkbEntityTranslator> translators,
            HashSet<int> ingress)
        {
            if (ingress.Count == 0) return Array.Empty<int>();

            var perInstance = ComponentAttributeSets.PerInstanceValue;
            if (perInstance.Count == 0) return Array.Empty<int>();

            // ── ② the TEMPLATE FILTER: which translators will actually run for this template ──────────
            var descriptorTypes = new HashSet<Type>();
            foreach (var (type, _, _) in template.GetAllDescriptors())
                descriptorTypes.Add(type);

            var produced = new HashSet<Type>();
            if (translators != null)
            {
                foreach (var t in translators)
                {
                    if (t == null) continue;
                    if (!RunsForTemplate(t, descriptorTypes)) continue;

                    foreach (var componentType in t.GetProducedComponents())
                        if (componentType != null) produced.Add(componentType);
                }
            }
            if (produced.Count == 0) return Array.Empty<int>();

            // ── ①∩②∩③∩④ ───────────────────────────────────────────────────────────────────────────
            var result = new List<int>();
            foreach (int id in perInstance)
            {
                var componentType = ComponentTypeRegistry.GetType(id);
                if (componentType == null) continue;              // never touched on this host
                if (!produced.Contains(componentType)) continue;  // ② this template will not carry it
                if (!ingress.Contains(id)) continue;              // ③ this wire cannot deliver it
                if (!repo.TryGetTable(componentType, out _)) continue; // ④ this host does not register it

                result.Add(id);
            }

            result.Sort();
            return result.ToArray();
        }

        /// <summary>
        /// ⭐ A translator runs for a template when the template carries one of its consumed descriptors —
        /// ⚠ <b>or when it consumes NONE at all</b>, which is the OBSERVER shape
        /// (<c>AiDiagnosticsTkbTranslator</c>): its <c>Inject</c> is gated on world state rather than on a
        /// descriptor, so it runs for every template and its products count for every template.
        /// ⛔ Treating an empty consumed set as "matches nothing" would silently drop such a translator's
        /// contribution, which is the same class of miss the interface's no-default rule guards against.
        /// </summary>
        private static bool RunsForTemplate(ITkbEntityTranslator translator, HashSet<Type> descriptorTypes)
        {
            bool declaredAny = false;
            foreach (var consumed in translator.GetConsumedDescriptors())
            {
                declaredAny = true;
                if (consumed != null && descriptorTypes.Contains(consumed)) return true;
            }
            return !declaredAny;
        }
    }
}
