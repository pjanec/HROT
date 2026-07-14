using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints.Attributes;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.AI.Behaviors.Brains;
using Hrot.AI.Behaviors.Generated;
using Hrot.AI.Behaviors.Trees;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors
{
    /// <summary>
    /// Self-registration entry point for the <b>curated</b> (hand-authored, C#-defined) CGF
    /// Brain-tier behaviors — the ones whose topology is produced by <see cref="FbtTreeCatalog"/>
    /// or a hand-built FastHSM rather than by a JSON-authored generated registrar.
    ///
    /// <para>
    /// This class replaces the retired <c>AiBehaviorFactory</c>. It is discovered and invoked by
    /// <see cref="Fdp.Toolkit.Blueprints.BlueprintRegistrarScanner"/> exactly like the generated
    /// per-asset registrars: the scanner injects a fresh <see cref="BehaviorRegistry"/> (staging)
    /// and an <see cref="ActionRegistry{TBlackboard,TContext}"/> populated from this assembly's
    /// <c>[FbtRegistrar]</c> node logic. There is no reflection entry point and no closure over
    /// <c>IGeographicTransform</c>/<c>NetworkEntityMap</c> — behaviors reach that context at
    /// activation time through world singletons via their named resolvers (Phase 2b).
    /// </para>
    ///
    /// <para><b>Behavior ownership.</b>
    /// <list type="bullet">
    ///   <item>Topologies registered here (no generated registrar exists): MoveToLocation,
    ///         FollowRoute, JoinFormation, WanderMilitary, FireAtTarget, and the Idle HSM.</item>
    ///   <item>HullDownAttackRun and PlatoonHillAttack topologies are owned by their generated
    ///         <c>[BlueprintRegistrar]</c>s; this class supplies only their named resolvers via
    ///         <see cref="BehaviorRegistry.RegisterResolver"/> (bound by name, order-independent).</item>
    /// </list>
    /// </para>
    /// </summary>
    [BlueprintRegistrar]
    public static class CgfCuratedBehaviorRegistrar
    {
        /// <summary>
        /// Registers all curated behavior topologies and named resolvers into
        /// <paramref name="beh"/> using the scanner-provided <paramref name="actionRegistry"/>.
        /// </summary>
        public static unsafe void Register(
            BehaviorRegistry beh,
            ActionRegistry<BrainBlackboard, BTreeContext> actionRegistry)
        {
            // The injected registry is already populated from this assembly's [FbtRegistrar]
            // (via BTreeActionRegistryFactory.BuildFromAssembly), including the paired
            // [BTreeDeactivator]s. Bake the resource-owning bit off that seam so branch-abort
            // deactivators fire (mirrors the generated registrars' Compile(name, isResourceOwning)).
            System.Func<string, bool> isResourceOwning = name => actionRegistry.TryGetDeactivator(name, out _);

            // ── FbtTreeCatalog BTree topologies ─────────────────────────────────────────
            beh.Register(BehaviorNames.MoveToLocation, new BehaviorDefinition
            {
                Name             = BehaviorNames.MoveToLocation,
                BrainTier        = BehaviorConstants.BrainTierBTree,
                ParamsDtoType    = typeof(CgfNodes.MoveToLocationParams),
                BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                    FbtTreeCatalog.GetMoveToLocation(isResourceOwning), actionRegistry),
            });

            beh.Register(BehaviorNames.FollowRoute, new BehaviorDefinition
            {
                Name             = BehaviorNames.FollowRoute,
                BrainTier        = BehaviorConstants.BrainTierBTree,
                ParamsDtoType    = typeof(CgfNodes.FollowRouteParams),
                BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                    FbtTreeCatalog.GetFollowRoute(isResourceOwning), actionRegistry),
            });

            beh.Register(BehaviorNames.JoinFormation, new BehaviorDefinition
            {
                Name             = BehaviorNames.JoinFormation,
                BrainTier        = BehaviorConstants.BrainTierBTree,
                ParamsDtoType    = typeof(CgfNodes.JoinFormationParams),
                BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                    FbtTreeCatalog.GetJoinFormation(isResourceOwning), actionRegistry),
            });

            beh.Register(BehaviorNames.WanderMilitary, new BehaviorDefinition
            {
                Name             = BehaviorNames.WanderMilitary,
                BrainTier        = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                    FbtTreeCatalog.GetWanderMilitary(isResourceOwning), actionRegistry),
            });

            beh.Register(BehaviorNames.FireAtTarget, new BehaviorDefinition
            {
                Name             = BehaviorNames.FireAtTarget,
                BrainTier        = BehaviorConstants.BrainTierBTree,
                ParamsDtoType    = typeof(CgfNodes.FireAtTargetParams),
                BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(
                    FbtTreeCatalog.GetFireAtTarget(isResourceOwning), actionRegistry),
            });

            // ── Idle: a single-state FastHSM with no transitions ────────────────────────
            var idleHsmBuilder = new HsmBuilder("Idle_HSM");
            idleHsmBuilder.State("Idle").Initial();
            var idleGraph = idleHsmBuilder.Build();
            HsmNormalizer.Normalize(idleGraph);
            var idleFlat = HsmFlattener.Flatten(idleGraph);
            HsmDefinitionBlob idleHsmBlob = HsmEmitter.Emit(idleFlat);
            MachineMetadata idleHsmMetadata = HsmEmitter.BuildMachineMetadata(idleGraph);

            beh.Register(BehaviorNames.Idle, new BehaviorDefinition
            {
                Name          = BehaviorNames.Idle,
                BrainTier     = BehaviorConstants.BrainTierHsm,
                HsmDefinition = idleHsmBlob,
                HsmMetadata   = idleHsmMetadata,
            });

            // ── Named resolvers (Phase 2b/2c) ───────────────────────────────────────────
            // Bound to the topology defs by name (order-independent). For MoveToLocation/
            // FollowRoute/FireAtTarget the topology is registered above; for HullDownAttackRun
            // and PlatoonHillAttack the topology is owned by their generated registrars and the
            // overlay carries the params DTO type the generated def expresses only via
            // ManagedBlackboardVariables.
            beh.RegisterResolver(BehaviorNames.MoveToLocation, CgfNodes.ResolveMoveToParams);
            beh.RegisterResolver(BehaviorNames.FollowRoute,
                (json, ptr, world, self) => CgfNodes.ParseFollowRouteParams(json, ptr));
            beh.RegisterResolver(BehaviorNames.FireAtTarget, CgfNodes.ResolveFireAtTargetParams);
            beh.RegisterResolver(BehaviorNames.HullDownAttackRun,
                (json, ptr, world, self) => HillAttackTankNodes.ParseHullDownAttackParams(json, ptr),
                typeof(HullDownAttackParams));
            beh.RegisterResolver(BehaviorNames.PlatoonHillAttack,
                HillAttackCommanderNodes.ResolvePlatoonHillAttackParams,
                typeof(PlatoonHillAttackParams));
        }
    }
}
