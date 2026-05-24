using System;

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

        /// <summary>Exposes the compiled blob for diagnostic/visualizer tools.</summary>
        public BehaviorTreeBlob Blob => _blob;

        public Interpreter(BehaviorTreeBlob blob, ActionRegistry<TBlackboard, TContext> registry)
        {
            _blob = blob ?? throw new ArgumentNullException(nameof(blob));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            
            _actionDelegates = BindActions(blob, registry);
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
                    // ObserverSelector uses standard selector semantics in the interpreter.
                    return ExecuteSelector(nodeIndex, ref node, ref bb, ref state, ref ctx);
                case NodeType.Subtree:
                    // Subtree execution requires external orchestration; return Failure as a safe stub.
                    return NodeStatus.Failure;
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
            ActionRegistry<TBlackboard, TContext> registry)
        {
            if (blob.MethodNames == null) return Array.Empty<NodeLogicDelegate<TBlackboard, TContext>>();

            var delegates = new NodeLogicDelegate<TBlackboard, TContext>[blob.MethodNames.Length];
            var fallback = new NodeLogicDelegate<TBlackboard, TContext>((ref TBlackboard bb, ref BehaviorTreeState st, ref TContext ctx, int p) => NodeStatus.Failure);

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
                    delegates[i] = fallback;
                }
            }

            return delegates;
        }
    }
}
