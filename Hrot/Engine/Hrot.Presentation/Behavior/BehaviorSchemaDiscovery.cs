using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Toolkit.Behavior;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.Presentation.Behavior
{
    /// <summary>
    /// Reads every behaviour's declared params DTO (<see cref="BehaviorAttribute"/>, via the one scan
    /// <see cref="ParamsDtoRegistry"/> — <c>CE-2134</c>) and registers each one with
    /// <see cref="ScenarioBehaviorRemapper"/> and — since <c>CE-235</c> — the <see cref="BehaviorRegistry"/> itself.
    /// ⛔ <c>CE-1023</c>: it also fed a UI registry for the mission panel's compiled form (<c>BehaviorUiCompiler</c>); both are
    /// deleted — every screen draws <see cref="BehaviorParamsForm"/> from the params TYPE.
    ///
    /// <para>Intended to replace the manual <c>Register&lt;T&gt;</c> call sites in
    /// <c>BehaviorUiSetup</c> and <c>CgfBehaviorSetup</c> (Phase 5b, BATCH-06).</para>
    ///
    /// <para>
    /// ⭐⭐⭐ <b><c>CE-235</c> — this is the ONE producer of the authored JSON contract.</b> A
    /// declared params DTO (<c>[Behavior(Params = …)]</c> since <c>CE-2134</c>; <c>[BehaviorContract]</c> on the DTO before) already drove the editor's parameter form
    /// (now <see cref="BehaviorParamsForm"/>) and scenario network-id remapping; it is the same shape a
    /// scenario writes and the resolver deserializes. The only consumer it was missing was the
    /// runtime <see cref="BehaviorDefinition"/>, which is what <c>GET /behaviors</c> reads — so the
    /// debug API published the <i>blackboard layout</i> instead (<c>CE-224</c>).
    /// 📄 <c>Behavior_Parameter_Resolver_Detailed_Design.md</c> §3.2: the authored DTO is
    /// <i>"editor fields + JSON schema"</i>.
    /// </para>
    /// </summary>
    public static class BehaviorSchemaDiscovery
    {
        /// <summary>
        /// Registers every declared behaviour params DTO with <paramref name="remapper"/>, and — when
        /// <paramref name="behaviorRegistry"/> is supplied — binds each DTO type to its behavior's
        /// <see cref="BehaviorDefinition.JsonParamsDtoType"/>.
        /// </summary>
        /// <param name="behaviorRegistry">
        /// ⭐ The runtime behavior registry, so the authored contract reaches
        /// <c>GET /behaviors</c>. Optional only because the editor-side callers that predate
        /// <c>CE-235</c> have no registry in hand at that point; ⛔ <b>a caller that HAS one must
        /// pass it</b> — the silent-default pattern is a named defect class in this repo.
        /// Binding is order-independent: see <see cref="BehaviorRegistry.RegisterJsonParamsDtoType"/>.
        /// </param>
        public static void AutoRegister(
            ScenarioBehaviorRemapper remapper,
            BehaviorRegistry? behaviorRegistry = null)
        {
            var remapMethod  = typeof(ScenarioBehaviorRemapper).GetMethod("Register")!;

            foreach (var (behaviorName, type) in AuthoredContracts())
            {
                remapMethod.MakeGenericMethod(type).Invoke(remapper,   new object[] { behaviorName });
                behaviorRegistry?.RegisterJsonParamsDtoType(behaviorName, type);
            }
        }

        /// <summary>
        /// <c>CE-235</c> — binds only the authored JSON contracts into
        /// <paramref name="behaviorRegistry"/>, for callers that build the runtime registry and have
        /// no scenario remapper to populate (headless hosts, tests, the CGF loader).
        /// </summary>
        /// <param name="declarations">
        /// The declarations to bind; <c>null</c> means <see cref="ParamsDtoRegistry.Default"/>. ⛔ A caller that built the
        /// definitions from one copy of a hot-reloadable assembly passes <see cref="ParamsDtoRegistry.Pinning"/> of it.
        /// </param>
        public static void BindJsonParamsDtoTypes(BehaviorRegistry behaviorRegistry, ParamsDtoRegistry? declarations = null)
        {
            if (behaviorRegistry is null) throw new System.ArgumentNullException(nameof(behaviorRegistry));

            foreach (var (behaviorName, type) in AuthoredContracts(declarations))
                behaviorRegistry.RegisterJsonParamsDtoType(behaviorName, type);
        }

        /// <summary>
        /// ⭐ <c>CE-2134</c> — the declared contracts, from the ONE scan (<see cref="ParamsDtoRegistry"/>): each behaviour's
        /// <c>[Behavior(Params = …)]</c>, wherever it sits (a curated topology or resolver, an intent mapper, an assembly).
        /// ⛔ HISTORY: it scanned <c>Hrot.Core</c> for <c>[BehaviorContract]</c> on the DTOs, so a DTO could serve one behaviour.
        /// </summary>
        private static IEnumerable<(string BehaviorName, System.Type DtoType)> AuthoredContracts(ParamsDtoRegistry? declarations = null)
            => (declarations ?? ParamsDtoRegistry.Default).Behaviors.Select(d => (d.Name, d.ParamsType));
    }
}
