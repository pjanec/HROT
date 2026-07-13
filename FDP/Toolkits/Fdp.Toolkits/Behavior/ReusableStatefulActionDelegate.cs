using Fbt;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// S3-G (stage 2): four-parameter stateful action delegate for code-first behaviour authoring.
    ///
    /// <para>Mirrors <see cref="Fbt.Compiler.ReusableActionDelegate{TValue,TContext}"/> but adds a
    /// second projected reference — the mutable <typeparamref name="TWorkingState"/> — which lives in a
    /// <c>BlueprintBlackboard*</c> partition slot rather than inline in the blackboard parameter region.
    /// This is the runtime analogue of the JSON emitter's <c>ThreeParamReusableStateful</c> binding
    /// (see <c>BTreeBridgeEmitCore.EmitStatefulActionThunks</c>): the compiler emits a thunk with this
    /// shape; <see cref="StatefulBTreeActionBinder"/> curries the identical shape at run time so the code
    /// <c>[BTreeDefinition]</c> builder can bind the same node methods.</para>
    ///
    /// <para>Defined in the FDP toolkit (not FastBTree) so FastBTree stays free of FDP-bound partition
    /// types; the binder curries this into FastBTree's generic
    /// <c>BTreeBuilder.Action(NodeLogicDelegate,…)</c> seam.</para>
    /// </summary>
    /// <typeparam name="TParams">Static parameter DTO projected from the blackboard. Must be unmanaged.</typeparam>
    /// <typeparam name="TWorkingState">Mutable working state projected from a partition slot. Must be unmanaged.</typeparam>
    /// <typeparam name="TContext">Context struct type.</typeparam>
    public delegate NodeStatus ReusableStatefulActionDelegate<TParams, TWorkingState, TContext>(
        ref TParams p,
        ref TWorkingState ws,
        ref BehaviorTreeState state,
        ref TContext ctx)
        where TParams : unmanaged
        where TWorkingState : unmanaged
        where TContext : struct, IAIContext;
}
