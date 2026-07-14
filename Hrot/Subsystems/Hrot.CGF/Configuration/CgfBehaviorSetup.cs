using System;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Hrot.Presentation.Behavior;

namespace Hrot.CGF.Configuration
{
    /// <summary>
    /// Bridge that populates the CGF behavior definitions from the <c>Hrot.AI.Behaviors</c>
    /// assembly at startup by running the attribute-driven <see cref="BlueprintRegistrarScanner"/>.
    ///
    /// <para>
    /// Registration is entirely self-registration: every behavior — curated
    /// (<c>CgfCuratedBehaviorRegistrar</c>) and JSON-authored (generated per-asset registrars) —
    /// is discovered through its <c>[BlueprintRegistrar]</c> attribute and registers under its own
    /// name. There is no reflection entry point and no closure over geographic/entity context;
    /// behaviors reach that context at activation time through world singletons via their named
    /// resolvers. The editor exercises the same registrars through its
    /// <c>FbtAssemblyHotReloader</c>/<see cref="Fdp.Toolkit.Behavior.AiHotReloadCoordinator"/> path.
    /// </para>
    /// </summary>
    public static class CgfBehaviorSetup
    {
        /// <summary>
        /// Discovers and invokes every <c>[BlueprintRegistrar]</c> in the AI behaviors assembly,
        /// populating <paramref name="behaviorRegistry"/> with all CGF Brain-tier behavior
        /// definitions (and, when supplied, committing blueprint definitions into
        /// <paramref name="blueprintRegistry"/>).
        ///
        /// <para>
        /// This is the single startup registration entry point used by
        /// <see cref="Hrot.CGF.CgfSubsystem"/> and <c>ReplayBrowserSubsystem</c>. The scanner injects
        /// an <c>ActionRegistry</c> populated from the assembly's <c>[FbtRegistrar]</c> node logic so
        /// every tree's bound actions/conditions resolve to real logic at runtime.
        /// </para>
        /// </summary>
        /// <param name="behaviorRegistry">Receives all behavior definitions. Must not be null.</param>
        /// <param name="blueprintRegistry">
        /// Optional. When supplied, receives the committed blueprint definitions. Pass <c>null</c>
        /// in contexts that only need the behavior registry (e.g. the replay browser).
        /// </param>
        public static void LoadFromAiAssembly(
            BehaviorRegistry behaviorRegistry,
            BlueprintRegistry? blueprintRegistry = null)
        {
            if (behaviorRegistry == null) throw new ArgumentNullException(nameof(behaviorRegistry));

            // The AI behaviors assembly is compile-time referenced; scan that single instance so
            // behavior and blueprint definitions share one type identity across the process.
            var aiAssembly = typeof(Hrot.AI.Behaviors.CgfCuratedBehaviorRegistrar).Assembly;

            var bpStaging = new BlueprintRegistryStaging();
            BlueprintRegistrarScanner.Scan(aiAssembly, bpStaging, behaviorRegistry);
            (blueprintRegistry ?? new BlueprintRegistry()).CommitStaging(bpStaging);
        }

        /// <summary>
        /// Creates a <see cref="ScenarioBehaviorRemapper"/> pre-registered with all
        /// CGF behavior param DTO types that carry <c>[RemapNetworkId]</c> properties.
        /// Used by load handlers to rewrite network IDs after two-pass ID allocation.
        /// </summary>
        public static ScenarioBehaviorRemapper CreateBehaviorRemapper()
        {
            var remapper = new ScenarioBehaviorRemapper();
            BehaviorSchemaDiscovery.AutoRegister(new BehaviorUiRegistry(), remapper);
            return remapper;
        }
    }
}
