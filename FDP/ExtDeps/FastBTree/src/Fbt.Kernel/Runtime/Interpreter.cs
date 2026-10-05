using System;
using System.Collections.Generic;

namespace Fbt.Runtime
{
    /// <summary>
    /// Interprets and executes a behavior tree.
    /// </summary>
    public class Interpreter<TBlackboard, TContext> : ITreeRunner<TBlackboard, TContext>
        where TBlackboard : struct
        where TContext : struct, IAIContext, ITreeTracer
    {
        private readonly BehaviorTreeBlob _blob;
        private readonly NodeLogicDelegate<TBlackboard, TContext>[] _actionDelegates;
        private readonly ActionRegistry<TBlackboard, TContext> _registry;
        // Used for diagnostics/debugging -- not currently used in tick but available for hot reload introspection.
        private readonly int _blobStructureHash;

        private readonly string[] _unboundMethodNames;

        /// <summary>Exposes the compiled blob for diagnostic/visualizer tools.</summary>
        public BehaviorTreeBlob Blob => _blob;

        /// <summary>
        /// The blob method names the registry could NOT resolve at construction, so
        /// <c>BindActions</c> substituted the <see cref="NodeStatus.Failure"/> fallback.
        /// Empty when every node is bound.
        ///
        /// <para>
        /// This exists because the fallback is otherwise INVISIBLE: it is a silent behavioural
        /// change announced only by a <c>Console.WriteLine</c>. A registrar that is skipped —
        /// e.g. because a reflection <c>typeof(ActionRegistry&lt;,&gt;)</c> filter disagrees with
        /// the generator about <c>TBlackboard</c>, or because a registrar body threw and was
        /// swallowed — produces a tree that ticks, returns <c>Failure</c> forever, and compiles
        /// cleanly. Exposing the miss list lets a rail assert ZERO fallbacks instead of scraping
        /// console output, which is the symptom rather than the definition.
        /// </para>
        /// </summary>
        public IReadOnlyList<string> UnboundMethodNames => _unboundMethodNames;

        /// <summary>
        /// CE-365 -- what runs a <c>NodeType.Subtree</c> node. Null (the default) keeps the historical
        /// behaviour: a Subtree node returns Failure.
        /// <para>
        /// Settable rather than a constructor argument because the host resolves its per-site keys
        /// from the BLOB, which does not exist until this interpreter has been built around it.
        /// </para>
        /// </summary>
        public ISubtreeHost<TBlackboard, TContext>? SubtreeHost { get; set; }

        public Interpreter(BehaviorTreeBlob blob, ActionRegistry<TBlackboard, TContext> registry)
        {
            _blob = blob ?? throw new ArgumentNullException(nameof(blob));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            _actionDelegates = BindActions(blob, registry, out _unboundMethodNames);
            _registry = registry;
            _blobStructureHash = blob.StructureHash;

            // V1 blob legacy fallback: blobs produced by CompileFromJson (Version == 1)
            // do not have the IsResourceOwning bit baked in at compile time.
            // Patch in-memory to set the bit for any Action/Condition node whose method name
            // has a registered deactivator. V2 blobs (from BTreeBuilder.Compile) skip this.
            if (_blob.Version < 2)
            {
                for (int i = 0; i < _blob.Nodes.Length; i++)
                {
                    ref var node = ref _blob.Nodes[i];
                    if (node.Type is not (NodeType.Action or NodeType.Condition)) continue;
                    int pi = node.PayloadIndex;
                    if ((uint)pi >= (uint)_blob.MethodNames.Length) continue;
                    if (_registry.TryGetDeactivator(_blob.MethodNames[pi], out _))
                        node.SetResourceOwning();
                }
            }
        }

        public NodeStatus Tick(
            ref TBlackboard blackboard,
            ref BehaviorTreeState state,
            ref TContext context)
        {
            // === STEP 1: Snapshot oldPath BEFORE any structural bounds-check. ===
            // 8 NodeIndexStack slots + RunningNodeIndex = 9 entries total.
            Span<ushort> oldPath = stackalloc ushort[9];
            unsafe
            {
                for (int i = 0; i < 8; i++)
                    oldPath[i] = state.NodeIndexStack[i];
            }
            oldPath[8] = state.RunningNodeIndex;

            // === STEP 2: HOT RELOAD CHECK with pathWasReset flag. ===
            // Safety net: if the running node index is out of bounds for the current blob,
            // fire deactivators for the old path first, then reset state to prevent
            // out-of-bounds access after a structural hot reload.
            bool pathWasReset = false;
            if (state.RunningNodeIndex > 0 && (int)state.RunningNodeIndex >= _blob.Nodes.Length)
            {
                Span<ushort> emptyPath = stackalloc ushort[9]; // zero-initialized
                SweepExitedNodes(oldPath, emptyPath, ref blackboard, ref state, ref context);
                state.RunningNodeIndex = 0;
                state.StackPointer = 0;
                unchecked { state.TreeVersion++; }
                pathWasReset = true;
                // Do NOT return -- continue to ExecuteNode on the same frame.
            }

            // === PAUSED CHECK ===
            if ((state.InstanceFlags & BehaviorInstanceFlags.Paused) != 0)
                return NodeStatus.Running;

            // === EXECUTE TREE ===
            if (_blob.Nodes.Length == 0) return NodeStatus.Success; // Empty tree safety

            var result = ExecuteNode(0, ref blackboard, ref state, ref context);
            
            // === CLEANUP ===
            if (result != NodeStatus.Running)
            {
                state.RunningNodeIndex = 0;
            }

            // === STEP 4: Post-tick delta sweep. ===
            // Skipped when pathWasReset to avoid double-firing deactivators.
            if (!pathWasReset)
            {
                Span<ushort> newPath = stackalloc ushort[9];
                unsafe
                {
                    for (int i = 0; i < 8; i++)
                        newPath[i] = state.NodeIndexStack[i];
                }
                newPath[8] = state.RunningNodeIndex;
                SweepExitedNodes(oldPath, newPath, ref blackboard, ref state, ref context);
            }
            
            return result;
        }

        // Sweeps oldPath for entries not present in newPath and invokes deactivators for each.
        private void SweepExitedNodes(
            Span<ushort> oldPath,
            Span<ushort> newPath,
            ref TBlackboard blackboard,
            ref BehaviorTreeState state,
            ref TContext context)
        {
            for (int i = 0; i < 9; i++)
            {
                ushort old = oldPath[i];
                if (old == 0) continue;
                if (newPath.Contains(old)) continue;
                SweepExitedNode(old, ref blackboard, ref state, ref context);
            }
        }

        // Invokes the deactivator for nodeIndex if it is resource-owning, and handles
        // Parallel subtree sweeping. Replaces InvokeDeactivatorIfRegistered.
        private void SweepExitedNode(
            ushort nodeIndex,
            ref TBlackboard blackboard,
            ref BehaviorTreeState state,
            ref TContext context)
        {
            if ((uint)nodeIndex >= (uint)_blob.Nodes.Length) return;
            ref var node = ref _blob.Nodes[nodeIndex];

            // CE-365 / F14 -- a Subtree node's deactivation CANNOT go through the lookup below:
            // its PayloadIndex indexes SubtreeAssetIds, not MethodNames, so that read would hit the
            // wrong array. The host owns the reset. Without this the abandoned child keeps its
            // cursor and the next entry resumes mid-tree.
            if (node.Type == NodeType.Subtree)
            {
                SubtreeHost?.Reset(ref context, _blob, nodeIndex);
                return;
            }

            if (node.IsResourceOwning)
            {
                int pi = node.PayloadIndex;
                if ((uint)pi < (uint)_blob.MethodNames.Length)
                {
                    if (_registry.TryGetDeactivator(_blob.MethodNames[pi], out var deactivator))
                        deactivator.Invoke(ref blackboard, ref state, ref context, pi);
                }
            }
            if (node.Type == NodeType.Parallel)
                SweepParallelChildren(nodeIndex, ref blackboard, ref state, ref context);
        }

        // For a Parallel node that exited the active path: sweeps children whose
        // completion bit in LocalRegisters is NOT set (still running).
        // Iterates every node index in [childIndex, childIndex + childNode.SubtreeOffset)
        // and calls InvokeDeactivatorIfRegistered on each, so deeply-nested action leaves
        // get their deactivators called even when Parallel overwrote RunningNodeIndex.
        private void SweepParallelChildren(
            ushort parallelNodeIndex,
            ref TBlackboard blackboard,
            ref BehaviorTreeState state,
            ref TContext context)
        {
            ref var parallelNode = ref _blob.Nodes[parallelNodeIndex];
            int childCount = parallelNode.ChildCount;
            if (childCount > 16) childCount = 16;

            // LocalRegisters[3] stores the child-state bitfield used by ExecuteParallel.
            int childStatesBits;
            unsafe { childStatesBits = state.LocalRegisters[3]; }

            int childIndex = parallelNodeIndex + 1;
            for (int i = 0; i < childCount; i++)
            {
                if (childIndex >= _blob.Nodes.Length) break;
                int finishedBit = 1 << (i + 16);
                ref var childNode = ref _blob.Nodes[childIndex];

                if ((childStatesBits & finishedBit) == 0)
                {
                    // Child was still running; sweep its entire definition block.
                    int end = childIndex + childNode.SubtreeOffset;
                    for (int j = childIndex; j < end && j < _blob.Nodes.Length; j++)
                    {
                        SweepExitedNode((ushort)j, ref blackboard, ref state, ref context);
                    }
                }

                childIndex += childNode.SubtreeOffset;
            }
        }

        private NodeStatus ExecuteNode(
            int nodeIndex,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            ref var node = ref _blob.Nodes[nodeIndex];
            
            switch (node.Type)
            {
                case NodeType.Sequence:
                    return ExecuteSequence(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.Selector:
                    return ExecuteSelector(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.Action:
                case NodeType.Condition:
                    return ExecuteAction(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.Inverter:
                    return ExecuteInverter(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.Wait:
                    return ExecuteWait(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.Repeater:
                    return ExecuteRepeater(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.Parallel:
                    return ExecuteParallel(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.Cooldown:
                    return ExecuteCooldown(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.ForceSuccess:
                    return ExecuteForceSuccess(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.ForceFailure:
                    return ExecuteForceFailure(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.UntilSuccess:
                    return ExecuteUntilSuccess(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.UntilFailure:
                    return ExecuteUntilFailure(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.ObserverSelector:
                    return ExecuteObserverSelector(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.Subtree:
                    return ExecuteSubtree(nodeIndex, ref bb, ref state, ref ctx);
                default:
                    return NodeStatus.Failure; // Unknown/Unimplemented node type
            }
        }

        private NodeStatus ExecuteParallel(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            int policy = _blob.IntParams[node.PayloadIndex];
            int childCount = node.ChildCount;
            // Max 16 children supported for Parallel due to 32-bit register usage
            if (childCount > 16) childCount = 16; 
            
            unsafe
            {
                // Use LocalRegisters[3] as bitfield for child results to avoid conflict with Repeater (Reg[0])
                // Bit 0-15: Success flags
                // Bit 16-31: Finished flags
                ref int childStatesBits = ref state.LocalRegisters[3];
                
                if (state.RunningNodeIndex == 0)
                {
                    childStatesBits = 0; // Reset on fresh start
                }
                
                int successCount = 0;
                int failureCount = 0;
                int runningCount = 0;
                
                // Execute all children
                int childIndex = nodeIndex + 1;
                for (int i = 0; i < childCount; i++)
                {
                    int finishedBit = 1 << (i + 16);
                    
                    // Skip if already finished
                    if ((childStatesBits & finishedBit) != 0)
                    {
                        // Check if it was a success
                        int successBit = 1 << i;
                        if ((childStatesBits & successBit) != 0)
                            successCount++;
                        else
                            failureCount++;
                            
                        // Move to next child's index
                        childIndex += _blob.Nodes[childIndex].SubtreeOffset;
                        continue;
                    }
                    
                    // Execute child
                    var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);
                    
                    if (result == NodeStatus.Success)
                    {
                        childStatesBits |= (1 << i); // Mark success
                        childStatesBits |= finishedBit; // Mark finished
                        successCount++;
                    }
                    else if (result == NodeStatus.Failure)
                    {
                        childStatesBits |= finishedBit; // Mark finished (no success bit)
                        failureCount++;
                    }
                    else // Running
                    {
                        runningCount++;
                    }
                    
                    // Move to next child
                    childIndex += _blob.Nodes[childIndex].SubtreeOffset;
                }
                
                // Check policy
                // Policy 0: RequireAll
                // Policy 1: RequireOne
                
                if (policy == 0) // RequireAll
                {
                    // Fail if any child fails
                    if (failureCount > 0)
                    {
                        childStatesBits = 0;
                        state.RunningNodeIndex = 0;
                        return NodeStatus.Failure;
                    }
                    // Success only if ALL children succeeded
                    if (successCount == childCount)
                    {
                        childStatesBits = 0;
                        state.RunningNodeIndex = 0;
                        return NodeStatus.Success;
                    }
                }
                else // RequireOne (Selector-like parallel)
                {
                    // Success if any child succeeds
                    if (successCount > 0)
                    {
                        childStatesBits = 0;
                        state.RunningNodeIndex = 0;
                        return NodeStatus.Success;
                    }
                    // Failure only if ALL children failed
                    if (failureCount == childCount)
                    {
                        childStatesBits = 0;
                        state.RunningNodeIndex = 0;
                        return NodeStatus.Failure;
                    }
                }
                
                // Still have running children
                state.RunningNodeIndex = (ushort)nodeIndex;
                return NodeStatus.Running;
            }
        }

        private NodeStatus ExecuteCooldown(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            float cooldownDuration = _blob.FloatParams[node.PayloadIndex];
            
            // Check last execution time (using first async token slot, same as Wait)
            var token = new AsyncToken(state.AsyncData);
            
            // If Version > 0, we have executed before.
            // (Using Version field to flag validity, storing 0 usually means invalid/empty)
            if (token.Version > 0)
            {
                float lastExecTime = token.FloatA;
                float timeSinceLastExec = ctx.Time - lastExecTime;
                
                if (timeSinceLastExec < cooldownDuration)
                {
                    // Still on cooldown
                    return NodeStatus.Failure;
                }
            }
            
            // Execute child
            int childIndex = nodeIndex + 1;
            var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);
            
            // Update last execution time on success
            if (result == NodeStatus.Success)
            {
                // Store Current Time, and Version=1 to indicate it is set
                var newToken = AsyncToken.FromFloat(ctx.Time, 1);
                state.AsyncData = newToken.PackedValue;
            }
            
            return result;
        }

        private NodeStatus ExecuteForceSuccess(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            int childIndex = nodeIndex + 1;
            var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);
            
            if (result == NodeStatus.Running)
                return NodeStatus.Running;
                
            return NodeStatus.Success; // Force success
        }

        private NodeStatus ExecuteForceFailure(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            int childIndex = nodeIndex + 1;
            var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);
            
            if (result == NodeStatus.Running)
                return NodeStatus.Running;
                
            return NodeStatus.Failure; // Force failure
        }

        private NodeStatus ExecuteUntilSuccess(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            // Re-execute child each tick until it returns Success; propagate Running or Failure as Running.
            int childIndex = nodeIndex + 1;
            var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);

            if (result == NodeStatus.Success)
                return NodeStatus.Success;

            // Failure means "try again next tick" -- treat as Running.
            return NodeStatus.Running;
        }

        private NodeStatus ExecuteUntilFailure(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            // Re-execute child each tick until it returns Failure; propagate Running or Success as Running.
            int childIndex = nodeIndex + 1;
            var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);

            if (result == NodeStatus.Failure)
                return NodeStatus.Success;

            // Success means "try again next tick" -- treat as Running.
            return NodeStatus.Running;
        }

        private NodeStatus ExecuteWait(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            // Get duration from FloatParams
            float duration = _blob.FloatParams[node.PayloadIndex];
            
            // Check if we're resuming a wait
            // Use ushort cast to match RunningNodeIndex type
            if (state.RunningNodeIndex == nodeIndex)
            {
                // Unpack async token
                var token = new AsyncToken(state.AsyncData);
                float startTime = token.FloatA;

                // Check if duration has elapsed
                float elapsed = ctx.Time - startTime;
                if (elapsed >= duration)
                {
                    state.RunningNodeIndex = 0;
                    ctx.TraceWaitCompleted(nodeIndex, duration);
                    return NodeStatus.Success;
                }

                return NodeStatus.Running;
            }
            else
            {
                // First execution - pack start time
                var token = AsyncToken.FromFloat(ctx.Time, 0);
                state.AsyncData = token.PackedValue;
                state.RunningNodeIndex = (ushort)nodeIndex;
                ctx.TraceWaitStarted(nodeIndex, duration);
                return NodeStatus.Running;
            }
        }

        private NodeStatus ExecuteRepeater(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            int repeatCount = _blob.IntParams[node.PayloadIndex];
            
            unsafe
            {
                ref int currentIteration = ref state.LocalRegisters[0];
                
                // If not running, start fresh
                if (state.RunningNodeIndex == 0)
                {
                    currentIteration = 0;
                }
                
                while (repeatCount < 0 || currentIteration < repeatCount)
                {
                    // Repeater has exactly one child
                    int childIndex = nodeIndex + 1;
                    var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);
                    
                    if (result == NodeStatus.Running)
                    {
                        return NodeStatus.Running;
                    }
                    
                    if (result == NodeStatus.Failure)
                    {
                        currentIteration = 0; // Reset on failure
                        return NodeStatus.Failure;
                    }
                    
                    // Child succeeded, increment counter
                    currentIteration++;

                    // CE-450: a FOREVER repeater YIELDS after each completed iteration — one iteration per tick.
                    //   Looping in the same tick never returned when the child succeeds immediately (a hang, not a
                    //   slow frame). RunningNodeIndex = this node makes the next tick resume here (ancestors skip
                    //   finished siblings by index), and the child starts fresh because it no longer matches
                    //   RunningNodeIndex. At the root (index 0) that is simply "not running" ⇒ the next tick re-enters
                    //   the root, which is the same thing for a forever loop (its count is never read).
                    //   ⚠ A BOUNDED repeater keeps its in-tick semantics (Repeater_ExecutesCorrectly: Repeat(3) runs
                    //   three times in one tick) — it always terminates.
                    if (repeatCount < 0)
                    {
                        state.RunningNodeIndex = (ushort)nodeIndex;
                        return NodeStatus.Running;
                    }

                    // If more iterations remain, continue
                    if (repeatCount < 0 || currentIteration < repeatCount)
                    {
                        // Reset child for next iteration
                        // Since child returned Success, RunningNodeIndex is already 0.
                        // We loop again, ExecuteNode will start child fresh.
                        continue;
                    }
                }
                
                // All iterations complete
                currentIteration = 0;
                state.RunningNodeIndex = 0;
                return NodeStatus.Success;
            }
        }

        private NodeStatus ExecuteSequence(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            int childCount = node.ChildCount;
            int currentChildIndex = nodeIndex + 1;

            for (int i = 0; i < childCount; i++)
            {
                ref var childNode = ref _blob.Nodes[currentChildIndex];

                // Resume logic: if running node passes this child's subtree, it means this child already SUCCEEDED
                if (state.RunningNodeIndex > 0 && 
                    state.RunningNodeIndex >= (currentChildIndex + childNode.SubtreeOffset))
                {
                    // Skip this child (it succeeded in previous tick)
                    currentChildIndex += childNode.SubtreeOffset;
                    continue;
                }

                var status = ExecuteNode(currentChildIndex, ref bb, ref state, ref ctx);

                if (status == NodeStatus.Running)
                {
                    return NodeStatus.Running;
                }
                
                if (status == NodeStatus.Failure)
                {
                    return NodeStatus.Failure;
                }

                // If success, proceed to next child
                currentChildIndex += childNode.SubtreeOffset;
            }

            return NodeStatus.Success;
        }

        private NodeStatus ExecuteSelector(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            int childCount = node.ChildCount;
            int currentChildIndex = nodeIndex + 1;

            for (int i = 0; i < childCount; i++)
            {
                ref var childNode = ref _blob.Nodes[currentChildIndex];

                // Resume logic: if running node passes this child's subtree, it means this child already FAILED
                if (state.RunningNodeIndex > 0 && 
                    state.RunningNodeIndex >= (currentChildIndex + childNode.SubtreeOffset))
                {
                    // Skip this child (it failed in previous tick)
                    currentChildIndex += childNode.SubtreeOffset;
                    continue;
                }

                var status = ExecuteNode(currentChildIndex, ref bb, ref state, ref ctx);

                if (status == NodeStatus.Running)
                {
                    return NodeStatus.Running;
                }
                
                if (status == NodeStatus.Success)
                {
                    return NodeStatus.Success;
                }

                // If failure, proceed to next child
                currentChildIndex += childNode.SubtreeOffset;
            }

            return NodeStatus.Failure;
        }

        /// <summary>
        /// CE-3041 -- the priority-abort selector (<c>Fbt.Kernel.md</c>: "Selector with abort-on-priority-change").
        /// 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.3a.
        ///
        /// <para>
        /// While a LOWER branch runs, each tick re-checks the GUARD of every HIGHER branch (the branch's leading
        /// Condition -- see <see cref="TryEvaluateGuard"/>). A guard that passes runs its branch fresh; if that
        /// branch takes over (Running or Success) the lower branch is abandoned and the post-tick sweep fires its
        /// deactivator, exactly as any other exit. If the higher branch FAILS anyway, the lower branch resumes where
        /// it was -- a branch that cannot take over aborts nothing.
        /// </para>
        /// <para>
        /// Only guards are evaluated speculatively: a higher branch with no leading Condition is not observed (it
        /// behaves as in a plain selector), so no Action ever runs just to find out whether it would succeed.
        /// With nothing running inside this node it IS a plain selector.
        /// </para>
        /// </summary>
        private NodeStatus ExecuteObserverSelector(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            ushort running = state.RunningNodeIndex;
            bool runningInside = running > nodeIndex && running < nodeIndex + node.SubtreeOffset;
            if (!runningInside)
                return ExecuteSelector(nodeIndex, ref node, ref bb, ref state, ref ctx);

            int childCount = node.ChildCount;
            int currentChildIndex = nodeIndex + 1;

            for (int i = 0; i < childCount; i++)
            {
                ref var childNode = ref _blob.Nodes[currentChildIndex];
                int childEnd = currentChildIndex + childNode.SubtreeOffset;

                if (running >= childEnd)
                {
                    // A HIGHER branch than the running one: observed through its guard.
                    if (TryEvaluateGuard(currentChildIndex, ref bb, ref state, ref ctx, out bool passes) && passes)
                    {
                        state.RunningNodeIndex = 0;                          // run the higher branch fresh
                        var taken = ExecuteNode(currentChildIndex, ref bb, ref state, ref ctx);
                        if (taken != NodeStatus.Failure)
                            return taken;                                    // the lower branch is abandoned
                        state.RunningNodeIndex = running;                    // it could not take over: resume
                    }
                    currentChildIndex = childEnd;
                    continue;
                }

                var status = ExecuteNode(currentChildIndex, ref bb, ref state, ref ctx);
                if (status != NodeStatus.Failure)
                    return status;
                currentChildIndex = childEnd;
            }

            return NodeStatus.Failure;
        }

        /// <summary>
        /// CE-3041 -- a branch's GUARD: the Condition it starts with. A Condition is its own guard; a Sequence's guard
        /// is its first child's; an Inverter's is its child's, inverted (Running stays Running). Anything else has no
        /// guard and the method returns false. A guard passes only on Success. The running cursor is left exactly as
        /// it was found.
        /// </summary>
        private bool TryEvaluateGuard(
            int nodeIndex,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx,
            out bool passes)
        {
            bool observed = TryEvaluateGuardStatus(nodeIndex, ref bb, ref state, ref ctx, out var status);
            passes = observed && status == NodeStatus.Success;
            return observed;
        }

        private bool TryEvaluateGuardStatus(
            int nodeIndex,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx,
            out NodeStatus status)
        {
            ref var node = ref _blob.Nodes[nodeIndex];
            switch (node.Type)
            {
                case NodeType.Condition:
                {
                    ushort saved = state.RunningNodeIndex;
                    status = ExecuteAction(nodeIndex, ref node, ref bb, ref state, ref ctx);
                    state.RunningNodeIndex = saved;
                    return true;
                }
                case NodeType.Sequence when node.ChildCount > 0:
                    return TryEvaluateGuardStatus(nodeIndex + 1, ref bb, ref state, ref ctx, out status);
                case NodeType.Inverter:
                    if (!TryEvaluateGuardStatus(nodeIndex + 1, ref bb, ref state, ref ctx, out var inner))
                    {
                        status = NodeStatus.Failure;
                        return false;
                    }
                    status = inner switch
                    {
                        NodeStatus.Success => NodeStatus.Failure,
                        NodeStatus.Failure => NodeStatus.Success,
                        _ => inner,
                    };
                    return true;
                default:
                    status = NodeStatus.Failure;
                    return false;
            }
        }

        private NodeStatus ExecuteAction(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            // Safety check for payload index
            if (node.PayloadIndex < 0 || node.PayloadIndex >= _actionDelegates.Length)
                return NodeStatus.Failure;

            var actionDelegate = _actionDelegates[node.PayloadIndex];
            var status = actionDelegate(ref bb, ref state, ref ctx, node.PayloadIndex);

            // Engine-emitted trace: every action/condition evaluation. Devirtualized
            // by the JIT because TContext is a struct constrained to ITreeTracer.
            ctx.TraceNodeEvaluated(nodeIndex, status);

            if (status == NodeStatus.Running)
            {
                state.RunningNodeIndex = (ushort)nodeIndex;
            }
            else if (state.RunningNodeIndex == nodeIndex)
            {
                state.RunningNodeIndex = 0;
            }

            return status;
        }

        /// <summary>
        /// CE-365 -- a hosting node. Dispatches to <see cref="SubtreeHost"/>, or keeps the historical
        /// Failure stub when none is set.
        ///
        /// <para>
        /// THE RUNNING BOOKKEEPING BELOW IS NOT OPTIONAL and mirrors ExecuteAction's. The post-tick
        /// sweep diffs NodeIndexStack + RunningNodeIndex to find nodes that LEFT the active path, so
        /// a hosting node that never records itself as running is never swept -- and F14 (the host
        /// abandons a still-Running child) silently does nothing. Measured: E6_R3 read a non-zero
        /// child cursor after an abandonment until this was added.
        /// </para>
        /// </summary>
        private NodeStatus ExecuteSubtree(
            int nodeIndex,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            if (SubtreeHost is not { } host)
                return NodeStatus.Failure;

            var status = host.Tick(ref bb, ref ctx, _blob, nodeIndex);

            ctx.TraceNodeEvaluated(nodeIndex, status);

            if (status == NodeStatus.Running)
            {
                state.RunningNodeIndex = (ushort)nodeIndex;
            }
            else if (state.RunningNodeIndex == nodeIndex)
            {
                state.RunningNodeIndex = 0;
            }

            return status;
        }

        private NodeStatus ExecuteInverter(
            int nodeIndex,
            ref NodeDefinition node,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            var childIndex = nodeIndex + 1;
            var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);
            
            return result switch
            {
                NodeStatus.Success => NodeStatus.Failure,
                NodeStatus.Failure => NodeStatus.Success,
                _ => result // Running stays Running
            };
        }

        private NodeLogicDelegate<TBlackboard, TContext>[] BindActions(
            BehaviorTreeBlob blob,
            ActionRegistry<TBlackboard, TContext> registry,
            out string[] unbound)
        {
            if (blob.MethodNames == null)
            {
                unbound = Array.Empty<string>();
                return Array.Empty<NodeLogicDelegate<TBlackboard, TContext>>();
            }

            var delegates = new NodeLogicDelegate<TBlackboard, TContext>[blob.MethodNames.Length];
            var fallback = new NodeLogicDelegate<TBlackboard, TContext>((ref TBlackboard bb, ref BehaviorTreeState st, ref TContext ctx, int p) => NodeStatus.Failure);
            List<string>? misses = null;

            for (int i = 0; i < blob.MethodNames.Length; i++)
            {
                string name = blob.MethodNames[i];
                if (registry.TryGetAction(name, out var action))
                {
                    delegates[i] = action;
                }
                else
                {
                    Console.WriteLine($"[FastBTree] Warning: Action '{name}' not found in registry. Using fallback Failure.");
                    (misses ??= new List<string>()).Add(name);
                    delegates[i] = fallback;
                }
            }

            unbound = misses?.ToArray() ?? Array.Empty<string>();
            return delegates;
        }
    }
}
