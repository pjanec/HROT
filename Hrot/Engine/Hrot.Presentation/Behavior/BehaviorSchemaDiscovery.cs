using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Toolkit.Behavior;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.Presentation.Behavior
{
    /// <summary>
    /// Scans the <c>Hrot.Core</c> assembly for types decorated with
    /// <see cref="BehaviorContractAttribute"/> and registers each one with
    /// <see cref="BehaviorUiRegistry"/>, <see cref="ScenarioBehaviorRemapper"/> and — since
    /// <c>CE-235</c> — the <see cref="BehaviorRegistry"/> itself.
    ///
    /// <para>Intended to replace the manual <c>Register&lt;T&gt;</c> call sites in
    /// <c>BehaviorUiSetup</c> and <c>CgfBehaviorSetup</c> (Phase 5b, BATCH-06).</para>
    ///
    /// <para>
    /// ⭐⭐⭐ <b><c>CE-235</c> — this is the ONE producer of the authored JSON contract.</b> A
    /// <c>[BehaviorContract]</c>-tagged DTO already drove the editor's parameter form
    /// (<see cref="BehaviorUiCompiler"/>) and scenario network-id remapping; it is the same shape a
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
        /// Registers all behavior parameter DTOs found in <c>Hrot.Core</c> with
        /// <paramref name="uiRegistry"/> and <paramref name="remapper"/>, and — when
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
            BehaviorUiRegistry uiRegistry,
            ScenarioBehaviorRemapper remapper,
            BehaviorRegistry? behaviorRegistry = null)
        {
            var uiRegMethod  = typeof(BehaviorUiRegistry).GetMethod("Register")!;
            var remapMethod  = typeof(ScenarioBehaviorRemapper).GetMethod("Register")!;

            foreach (var (behaviorName, type) in AuthoredContracts())
            {
                uiRegMethod.MakeGenericMethod(type).Invoke(uiRegistry, new object[] { behaviorName });
                remapMethod.MakeGenericMethod(type).Invoke(remapper,   new object[] { behaviorName });
                behaviorRegistry?.RegisterJsonParamsDtoType(behaviorName, type);
            }
        }

        /// <summary>
        /// <c>CE-235</c> — binds only the authored JSON contracts into
        /// <paramref name="behaviorRegistry"/>, for callers that build the runtime registry and have
        /// no ImGui form or scenario remapper to populate (headless hosts, tests, the CGF loader).
        /// </summary>
        public static void BindJsonParamsDtoTypes(BehaviorRegistry behaviorRegistry)
        {
            if (behaviorRegistry is null) throw new System.ArgumentNullException(nameof(behaviorRegistry));

            foreach (var (behaviorName, type) in AuthoredContracts())
                behaviorRegistry.RegisterJsonParamsDtoType(behaviorName, type);
        }

        /// <summary>
        /// The one enumeration of <c>[BehaviorContract]</c>-tagged DTOs in <c>Hrot.Core</c>, shared by
        /// both entry points so they can never disagree about what the authored set is.
        /// </summary>
        private static IEnumerable<(string BehaviorName, System.Type DtoType)> AuthoredContracts()
            => typeof(BehaviorContractAttribute).Assembly.GetTypes()
                .Select(t => (Type: t, Attr: t.GetCustomAttribute<BehaviorContractAttribute>()))
                .Where(x => x.Attr is not null)
                .Select(x => (x.Attr!.BehaviorName, x.Type));
    }
}
