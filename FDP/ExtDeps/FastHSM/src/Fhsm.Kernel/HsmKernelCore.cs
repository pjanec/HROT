using System;
using System.Runtime.CompilerServices;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Non-generic kernel core. Compiled once, no generic expansion.
    /// Uses void* for type erasure.
    /// </summary>
    internal static unsafe class HsmKernelCore
    {
        private static HsmTraceBuffer? _traceBuffer = null;

        public static void SetTraceBuffer(HsmTraceBuffer? buffer)
        {
            _traceBuffer = buffer;
        }

        // Event ID for Timer Fired (System Reserved)
        private const ushort TimerEventId = 0xFFFE;

        // Configuration offsets for CurrentEventId scratch space
        private const int CurrentEventId_Offset_64 = 20;
        private const int CurrentEventId_Offset_128 = 58;
        private const int CurrentEventId_Offset_256 = 98;

        // Internal struct for LCA path
        private struct TransitionPath
        {
            public ushort LCA;
            public ushort ExitCount;  // Number of states to exit
            public ushort EntryCount; // Number of states to enter
            public fixed ushort ExitPath[16];  // Max depth 16
            public fixed ushort EntryPath[16];
        }

        /// <summary>
        /// Process instances through one tick.
        /// </summary>
        internal static void UpdateBatchCore(
            HsmDefinitionBlob definition,
            void* instancePtr,
            int instanceCount,
            int instanceSize,
            void* contextPtr,
            float deltaTime,
            void* commandPagePtr)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (instancePtr == null) throw new ArgumentNullException(nameof(instancePtr));
            if (instanceCount <= 0) return;
            
            var cmdWriter = new HsmCommandWriter((CommandPage*)commandPagePtr, 4080);

            // Process each instance
            for (int i = 0; i < instanceCount; i++)
            {
                byte* instPtr = (byte*)instancePtr + (i * instanceSize);
                InstanceHeader* header = (InstanceHeader*)instPtr;
                
                // Skip instances with invalid phase or wrong definition
                if (!ValidateInstance(header, definition))
                {
                    continue;
                }
                
                // Process based on current phase
                ProcessInstancePhase(
                    definition,
                    instPtr,
                    instanceSize,
                    contextPtr,
                    deltaTime,
                    header,
                    ref cmdWriter);
            }
        }
        
        private static bool ValidateInstance(InstanceHeader* header, HsmDefinitionBlob definition)
        {
            if (header->MachineId != definition.Header.StructureHash) return false;
            if ((header->Flags & InstanceFlags.Terminated) != 0) return false;
            if (header->Phase > InstancePhase.Activity) return false;
            return true;
        }
        
        /// <summary>
        /// Reset instance internals (set active states to 0xFFFF).
        /// </summary>
        public static void ResetInstance(byte* instancePtr, int instanceSize)
        {
            ushort* activeLeafIds = GetActiveLeafIds(instancePtr, instanceSize, out int count);
            if (activeLeafIds != null)
            {
                for (int i = 0; i < count; i++) activeLeafIds[i] = 0xFFFF;
            }
        }
        
        private static void ProcessInstancePhase(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            void* contextPtr,
            float deltaTime,
            InstanceHeader* header,
            ref HsmCommandWriter cmdWriter)
        {
            switch (header->Phase)
            {
                case InstancePhase.Idle:
                    // Timer Phase
                    ProcessTimerPhase(definition, instancePtr, instanceSize, deltaTime);
                    
                    // Check if any events in queue (triggered by timers or external)
                    if (HsmEventQueue.GetCount(instancePtr, instanceSize) > 0)
                    {
                        header->Phase = InstancePhase.Entry;
                    }
                    break;
                    
                case InstancePhase.Entry:
                    {
                        // Check if uninitialized
                        ushort* activeLeafIds = GetActiveLeafIds(instancePtr, instanceSize, out int regionCount);
                        if (activeLeafIds != null && activeLeafIds[0] == 0xFFFF)
                        {
                            InitializeMachine(definition, instancePtr, instanceSize, contextPtr, activeLeafIds, ref cmdWriter);
                            
                            // Advance to Activity immediately (skip event phase this tick)
                            header->Phase = InstancePhase.Activity;
                            break;
                        }
                    
                        // Advance to Event processing
                        ProcessEventPhase(definition, instancePtr, instanceSize, contextPtr, ref cmdWriter);
                    }
                    break;
                    
                case InstancePhase.RTC:
                    ushort eventId = GetCurrentEventId(instancePtr, instanceSize);
                    ProcessRTCPhase(definition, instancePtr, instanceSize, contextPtr, eventId, ref cmdWriter);
                    break;
                    
                case InstancePhase.Activity:
                    ProcessActivityPhase(definition, instancePtr, instanceSize, contextPtr, deltaTime, ref cmdWriter);
                    break;
            }
        }

        private static void InitializeMachine(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            void* contextPtr,
            ushort* activeLeafIds,
            ref HsmCommandWriter cmdWriter)
        {
            if (definition.Header.StateCount == 0) return;

            InstanceHeader* header = (InstanceHeader*)instancePtr;

            // Initialize RNG
            if (header->RngState == 0)
            {
                header->RngState = 0x5EEDCAFE; 
            }

            // 1. Initialize Root (Slot 0)
            InitializeSlot(definition, instancePtr, instanceSize, contextPtr, activeLeafIds, ref cmdWriter, 0, 0, 0xFFFF);
            
            // 2. Initialize Orthogonal Regions
            var regions = definition.Regions;
            int totalSlots = definition.Header.RegionCount;
            
            // Iterative initialization: if parent of a region becomes active, initialize that region
            bool changed = true;
            // Limit loop to avoid infinite in case of cycles (though unlikely)
            int loopGuard = 0;
            while (changed && loopGuard < 16)
            {
                changed = false;
                loopGuard++;
                
                for (int r = 1; r < regions.Length; r++) // Skip 0 (Root/Default)
                {
                    // Assuming RegionDef array order matches Slot Indices 1..N ?
                    // Actually, FlattenRegions logic creates RegionDefs in order.
                    // If we assume RegionDef[i] -> Slot[i].
                    // activeLeafIds has size of Header.RegionCount.
                    // If RegionCount > regions.Length (because Root not in array?),
                    // In FlattenRegions I added root at index 0.
                    // So RegionDef[i] corresponds directly to Slot[i].
                    
                    if (r >= totalSlots) break;
                    if (activeLeafIds[r] != 0xFFFF) continue; // Already initialized

                    ref readonly var region = ref regions[r];
                    ushort parent = region.ParentStateIndex;

                    // Check if parent is active in any initialized slot
                    bool parentActive = false;
                    for (int s = 0; s < totalSlots; s++)
                    {
                        if (activeLeafIds[s] != 0xFFFF)
                        {
                            if (IsAncestor(definition, activeLeafIds[s], parent))
                            {
                                parentActive = true;
                                break;
                            }
                        }
                    }

                    if (parentActive)
                    {
                         InitializeSlot(definition, instancePtr, instanceSize, contextPtr, activeLeafIds, ref cmdWriter, r, region.InitialStateIndex, region.ParentStateIndex);
                         changed = true;
                    }
                }
            }
        }

        private static void InitializeSlot(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            void* contextPtr,
            ushort* activeLeafIds,
            ref HsmCommandWriter cmdWriter,
            int slotIndex,
            ushort startState,
            ushort fromParent)
        {
             InstanceHeader* header = (InstanceHeader*)instancePtr;
            
            // 1. Find target state (Drill down)
            ushort targetState = startState;
            
            while (true)
            {
                ref readonly var state = ref definition.GetState(targetState);
                if ((state.Flags & StateFlags.IsComposite) != 0 && state.FirstChildIndex != 0xFFFF)
                {
                    // If parallel, we stop here? No, parallel usually doesn't have "FirstChild" semantics for initial state.
                    // Validator ensures we don't rely on initial for parallel.
                    // BUT IsComposite is true.
                    // If parallel, we should probably stop drill down for THIS slot,
                    // as children will be handled by other regions.
                    // BUT we need to enter the parallel state itself.
                    // IsParallel is set on the state.
                    
                    if ((state.Flags & StateFlags.IsParallel) != 0)
                    {
                        // Stop at parallel state, it is the leaf for this slot
                        break;
                    }
                    
                    targetState = state.FirstChildIndex;
                }
                else
                {
                    break;
                }
            }
            
            // 2. Compute Entry Path
            // fromParent is the state that is already active (or None 0xFFFF).
            // We path from fromParent -> Target.
            // But if fromParent is the Parallel State (for orthogonal region),
            // We path from Parallel -> Child -> ...Target.
            
            ushort ancestorsStart = fromParent;
            
            TransitionPath path = ComputeLCA(definition, ancestorsStart, targetState);
            // Verify path? ComputeLCA assumes exiting ancestorsStart.
            // But we are ENTERING ancestorsStart? No, ancestorsStart is already active (container).
            // We are entering sub-states.
            // ComputeLCA(A, B) returns exit A -> LCA -> enter B.
            // If fromParent is ancestor of Target. LCA = fromParent.
            // Exit count = 0. Entry path = fromParent -> ... -> Target.
            // BUT EntryPath includes fromParent if it's the target? No.
            // TransitionPath includes target in EntryPath.
            // It EXCLUDES LCA in EntryPath?
            // ComputeLCA implementation:
            // path.EntryCount = ...
            // path.EntryPath[i] = targetChain[commonDepth + i].
            // targetChain includes LCA at commonDepth-1.
            // So EntryPath starts AFTER LCA.
            
            // So if LCA == fromParent, EntryPath starts at child of fromParent.
            // This is exactly what we want! We don't want to re-enter fromParent.
            
            if (_traceBuffer != null && (header->Flags & InstanceFlags.DebugTrace) != 0)
            {
                _traceBuffer.WriteStateChange(header->MachineId, targetState, true);
            }

            // 3. Execute Entry Actions
            for (int i = 0; i < path.EntryCount; i++)
            {
                ushort stateId = path.EntryPath[i];
                ref readonly var state = ref definition.GetState(stateId);

                if (state.OnEntryActionId != 0 && state.OnEntryActionId != 0xFFFF)
                {
                    ExecuteAction(state.OnEntryActionId, instancePtr, contextPtr, ref cmdWriter);
                }
            }
            
            // 4. Set Active State
            activeLeafIds[slotIndex] = targetState;

            // 5. Check for terminal state.
            ref readonly var leafStateDef = ref definition.GetState(targetState);
            if ((leafStateDef.Flags & StateFlags.IsFinal) != 0)
                header->Flags |= InstanceFlags.Terminated;
        }

        // --- Task 1: Timer Phase ---

        private static void ProcessTimerPhase(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            float deltaTime)
        {
            uint* timers = GetTimerArray(instancePtr, instanceSize, out int timerCount);
            if (timers == null || timerCount == 0) return;

            ushort* activeLeafIds = GetActiveLeafIds(instancePtr, instanceSize, out int regionCount);
            
            // Decrement all timers
            uint deltaTicks = (uint)(deltaTime * 1000f); // ms
            
            for (int i = 0; i < timerCount; i++)
            {
                if (timers[i] > 0)
                {
                    if (timers[i] > deltaTicks)
                    {
                        timers[i] -= deltaTicks;
                    }
                    else
                    {
                        timers[i] = 0;
                        // Timer fired
                        FireTimerEvent(definition, instancePtr, instanceSize, i, activeLeafIds, regionCount);
                    }
                }
            }
        }

        private static void FireTimerEvent(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            int timerIndex,
            ushort* activeLeafIds,
            int regionCount)
        {
            var timerEvent = new HsmEvent
            {
                EventId = TimerEventId,
                Priority = EventPriority.Normal,
                Timestamp = 0
            };
            
            HsmEventQueue.TryEnqueue(instancePtr, instanceSize, timerEvent);
        }

        // --- Task 2: Event Phase ---

        private static void ProcessEventPhase(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            void* contextPtr,
            ref HsmCommandWriter cmdWriter)
        {
            const int MaxEventsPerTick = 10;
            int eventsProcessed = 0;
            InstanceHeader* header = (InstanceHeader*)instancePtr;
            
            while (eventsProcessed < MaxEventsPerTick)
            {
                if (!HsmEventQueue.TryDequeue(instancePtr, instanceSize, out HsmEvent evt))
                {
                    break;
                }
                
                eventsProcessed++;
                
                // Trace event handled
                if (_traceBuffer != null && (header->Flags & InstanceFlags.DebugTrace) != 0)
                {
                    byte result = 0; // 0=consumed
                    if ((evt.Flags & EventFlags.IsDeferred) != 0)
                        result = 1; // 1=deferred
                    
                    _traceBuffer.WriteEventHandled(header->MachineId, evt.EventId, result);
                }

                if ((evt.Flags & EventFlags.IsDeferred) != 0)
                {
                    // Re-enqueue deferred event with Low priority
                    evt.Priority = EventPriority.Low;
                    HsmEventQueue.TryEnqueue(instancePtr, instanceSize, evt);
                    continue;
                }
                
                StoreCurrentEventId(instancePtr, instanceSize, evt.EventId);
                header->Phase = InstancePhase.RTC;
                return; // Go to RTC phase
            }
            
            // No more events, go to Idle
            header->Phase = InstancePhase.Idle;
        }

        // --- Task 4: Activity Phase ---

        private static void ProcessActivityPhase(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            void* contextPtr,
            float deltaTime,
            ref HsmCommandWriter cmdWriter)
        {
            InstanceHeader* header = (InstanceHeader*)instancePtr;
            ushort* activeLeafIds = GetActiveLeafIds(instancePtr, instanceSize, out int regionCount);
            
            // Execute activities for all active states
            for (int r = 0; r < regionCount; r++)
            {
                ushort leafId = activeLeafIds[r];
                if (leafId == 0xFFFF) continue;
                
                // Walk up from leaf to root, executing activities
                ushort current = leafId;
                while (current != 0xFFFF)
                {
                    ref readonly var state = ref definition.GetState(current);
                    
                    // Execute activity if present
                    if (state.ActivityActionId != 0 && state.ActivityActionId != 0xFFFF)
                    {
                        ExecuteAction(state.ActivityActionId, instancePtr, contextPtr, ref cmdWriter);
                    }
                    
                    current = state.ParentIndex;
                }
            }
            
            // Return to Idle
            header->Phase = InstancePhase.Idle;
        }

        // --- Task 3: RTC Phase ---

        private static void ProcessRTCPhase(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            void* contextPtr,
            ushort currentEventId,
            ref HsmCommandWriter cmdWriter)
        {
            const int MaxRTCIterations = 100;
            InstanceHeader* header = (InstanceHeader*)instancePtr;
            ushort* activeLeafIds = GetActiveLeafIds(instancePtr, instanceSize, out int regionCount);
            
            int iteration = 0;
            while (true)
            {
                if (iteration >= MaxRTCIterations)
                {
                    // Fail-Safe: Infinite loop detected
                     if (_traceBuffer != null)
                    {
                        _traceBuffer.WriteError(header->MachineId, 1); // Error 1: RTC Loop
                    }
                    
                    // Reset to 0xFFFF (Safe State)
                    for (int i = 0; i < regionCount; i++) activeLeafIds[i] = 0xFFFF;
                    
                    // Force phase to Idle to stop processing
                    header->Phase = InstancePhase.Idle;
                    return;
                }

                iteration++;

                TransitionDef? selectedTransition = SelectTransition(
                    definition,
                    instancePtr,
                    instanceSize,
                    activeLeafIds,
                    regionCount,
                    currentEventId,
                    contextPtr);
                
                if (selectedTransition == null)
                {
                    // Consumed
                    break;
                }
                
                ExecuteTransition(definition, instancePtr, instanceSize, selectedTransition.Value, activeLeafIds, regionCount, contextPtr, ref cmdWriter);
                
                // Event consumed - subsequent iterations check for Epsilon (0) transitions
                currentEventId = 0;
            }
            
            // Advance to Activity
            header->Phase = InstancePhase.Activity;
        }

        private static TransitionDef? SelectTransition(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            ushort* activeLeafIds,
            int regionCount,
            ushort eventId,
            void* contextPtr)
        {
            // 1. Global transitions
            var globalSpan = definition.GlobalTransitions;
            for (int i = 0; i < globalSpan.Length; i++)
            {
                ref readonly var gt = ref globalSpan[i];
                if (gt.EventId == eventId)
                {
                    if (gt.GuardId == 0 || EvaluateGuard(gt.GuardId, instancePtr, contextPtr, eventId))
                    {
                        return new TransitionDef
                        {
                            SourceStateIndex = activeLeafIds[0],
                            TargetStateIndex = gt.TargetStateIndex,
                            EventId = gt.EventId,
                            GuardId = gt.GuardId,
                            ActionId = gt.ActionId,
                            Flags = gt.Flags,
                            Cost = 0
                        };
                    }
                }
            }

            // 2. Active state transitions
            TransitionDef? bestTransition = null;
            byte highestPriority = 0;

            for (int r = 0; r < regionCount; r++)
            {
                ushort leafId = activeLeafIds[r];
                if (leafId == 0xFFFF) continue;

                ushort current = leafId;
                while (current != 0xFFFF)
                {
                    ref readonly var state = ref definition.GetState(current);

                    if (state.FirstTransitionIndex != 0xFFFF)
                    {
                        for (ushort t = 0; t < state.TransitionCount; t++)
                        {
                            ushort transIndex = (ushort)(state.FirstTransitionIndex + t);
                            ref readonly var trans = ref definition.GetTransition(transIndex);

                            // Match event
                            if (trans.EventId == eventId)
                            {
                                // Priority is top 4 bits (12-15) of Flags
                                byte priority = (byte)((ushort)(trans.Flags) >> 12);

                                if (trans.GuardId == 0 || EvaluateGuard(trans.GuardId, instancePtr, contextPtr, eventId))
                                {
                                    if (bestTransition == null || priority > highestPriority)
                                    {
                                        bestTransition = trans;
                                        highestPriority = priority;
                                    }
                                }
                            }
                        }
                    }

                    current = state.ParentIndex;
                }
            }

            return bestTransition;
        }
        
        private static bool EvaluateGuard(ushort guardId, byte* instancePtr, void* contextPtr, ushort eventId)
        {
            bool result = HsmActionDispatcher.EvaluateGuard(guardId, instancePtr, contextPtr, eventId);

            if (_traceBuffer != null)
            {
                InstanceHeader* header = (InstanceHeader*)instancePtr;
                if ((header->Flags & InstanceFlags.DebugTrace) != 0)
                {
                    _traceBuffer.WriteGuardEvaluated(header->MachineId, guardId, result, 0);
                }
            }

            return result;
        }

        // --- Transition Execution Logic (BATCH-10) ---

        private static void ExecuteTransition(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            TransitionDef transition,
            ushort* activeLeafIds,
            int regionCount,
            void* contextPtr,
            ref HsmCommandWriter cmdWriter)
        {
            // NEW: Region arbitration for orthogonal regions
            // NEW: Region arbitration for orthogonal regions
            if (definition.Header.RegionCount > 1)
            {
                ArbitrateOutputLanes(definition, instancePtr, activeLeafIds, regionCount);
            }

            ushort sourceStateId = transition.SourceStateIndex;
            ushort targetStateId = transition.TargetStateIndex;
            
            // Compute LCA path
            TransitionPath path = ComputeLCA(definition, sourceStateId, targetStateId);
            
            InstanceHeader* header = (InstanceHeader*)instancePtr;
            if (_traceBuffer != null && (header->Flags & InstanceFlags.DebugTrace) != 0)
            {
                _traceBuffer.WriteTransition(
                    header->MachineId,
                    transition.SourceStateIndex,
                    transition.TargetStateIndex,
                    transition.EventId);
            }
            
            // 1. Execute exit actions (leaf -> LCA)
            for (int i = 0; i < path.ExitCount; i++)
            {
                ushort stateId = path.ExitPath[i];
                
                if (_traceBuffer != null && (header->Flags & InstanceFlags.DebugTrace) != 0)
                {
                    _traceBuffer.WriteStateChange(header->MachineId, stateId, false);
                }

                ref readonly var state = ref definition.GetState(stateId);
                
                // Cancel timers owned by this state (clears all for now)
                CancelTimers(instancePtr, instanceSize);

                if (state.OnExitActionId != 0 && state.OnExitActionId != 0xFFFF)
                {
                    ExecuteAction(state.OnExitActionId, instancePtr, contextPtr, ref cmdWriter);
                }
                
                // Save history if this state has history
                if ((state.Flags & StateFlags.IsHistory) != 0 || state.HistorySlotIndex != 0xFFFF)
                {
                    SaveHistory(definition, instancePtr, instanceSize, stateId, activeLeafIds[0]);
                }
            }
            
            // 2. Execute transition action
            if (transition.ActionId != 0 && transition.ActionId != 0xFFFF)
            {
                ExecuteAction(transition.ActionId, instancePtr, contextPtr, ref cmdWriter);
            }
            
            // 3. Execute entry actions (LCA -> leaf)
            ushort finalLeafId = targetStateId;
            bool historyRestored = false;
            
            for (int i = 0; i < path.EntryCount; i++)
            {
                ushort stateId = path.EntryPath[i];
                
                if (_traceBuffer != null && (header->Flags & InstanceFlags.DebugTrace) != 0)
                {
                    _traceBuffer.WriteStateChange(header->MachineId, stateId, true);
                }

                ref readonly var state = ref definition.GetState(stateId);
                
                // Check if history state
                if ((state.Flags & StateFlags.IsHistory) != 0)
                {
                    // Restore history
                    bool isDeep = (state.Flags & StateFlags.IsDeepHistory) != 0;
                    if (RestoreHistory(definition, instancePtr, instanceSize, stateId, isDeep))
                    {
                        historyRestored = true;
                        break; 
                    }
                }
                
                if (state.OnEntryActionId != 0 && state.OnEntryActionId != 0xFFFF)
                {
                    ExecuteAction(state.OnEntryActionId, instancePtr, contextPtr, ref cmdWriter);
                }
                
                // If composite, resolve to initial child
                if ((state.Flags & StateFlags.IsComposite) != 0)
                {
                    ushort initialChild = state.FirstChildIndex;
                    if (initialChild != 0xFFFF)
                    {
                        finalLeafId = initialChild;
                    }
                }
            }
            
            // 4. Update active state
            if (!historyRestored)
            {
                activeLeafIds[0] = finalLeafId;

                // Check for terminal state.
                ref readonly var finalStateDef = ref definition.GetState(finalLeafId);
                if ((finalStateDef.Flags & StateFlags.IsFinal) != 0)
                    header->Flags |= InstanceFlags.Terminated;
            }
            
            // 5. Recall Deferred Events
            HsmEventQueue.RecallDeferredEvents(instancePtr, instanceSize);
        }

        private static void ExecuteAction(
            ushort actionId,
            byte* instancePtr,
            void* contextPtr,
            ref HsmCommandWriter cmdWriter)
        {
            if (_traceBuffer != null)
            {
                InstanceHeader* header = (InstanceHeader*)instancePtr;
                if ((header->Flags & InstanceFlags.DebugTrace) != 0)
                {
                    _traceBuffer.WriteActionExecuted(header->MachineId, actionId);
                }
            }

            fixed(HsmCommandWriter* writerPtr = &cmdWriter)
            {
                HsmActionDispatcher.ExecuteAction(actionId, instancePtr, contextPtr, writerPtr);
            }
        }

        private static void ArbitrateOutputLanes(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            ushort* activeLeafIds,
            int regionCount)
        {
            byte combinedMask = 0;
            int firstRegionWithConflict = -1;
            
            for (int i = 0; i < regionCount; i++)
            {
                if (activeLeafIds[i] == 0xFFFF) continue; // Skip uninitialized regions
                
                ref readonly var state = ref definition.GetState(activeLeafIds[i]);
                byte laneMask = state.OutputLaneMask;
                
                if (laneMask == 0) continue; // State writes to no lanes
                
                // Check for conflicts
                if ((combinedMask & laneMask) != 0)
                {
                    // Conflict detected! First region wins.
                    if (firstRegionWithConflict == -1)
                    {
                        firstRegionWithConflict = i;
                    }
                    
                    // Suppress this region's output (clear its mask bits)
                    // Implementation: Set a flag or skip command execution
                    // For v1.0: Log conflict (if tracing enabled) and continue
                    // Full arbitration with priority would be P4 (future)
                    
                    if (_traceBuffer != null)
                    {
                        InstanceHeader* header = (InstanceHeader*)instancePtr;
                        _traceBuffer.WriteConflict(
                            header->MachineId,
                            activeLeafIds[i], 
                            laneMask, 
                            (byte)(combinedMask & laneMask)
                        );
                    }
                }
                else
                {
                    combinedMask |= laneMask;
                }
            }
        }

        private static TransitionPath ComputeLCA(
            HsmDefinitionBlob definition,
            ushort sourceStateId,
            ushort targetStateId)
        {
            var path = new TransitionPath();
            
            if (sourceStateId == targetStateId)
            {
                path.LCA = sourceStateId;
                path.ExitCount = 0;
                path.EntryCount = 0;
                return path;
            }
            
            ushort* sourceChain = stackalloc ushort[16];
            ushort* targetChain = stackalloc ushort[16];
            
            int sourceDepth = BuildAncestorChain(definition, sourceStateId, sourceChain);
            int targetDepth = BuildAncestorChain(definition, targetStateId, targetChain);
            
            ushort lca = 0xFFFF;
            int commonDepth = 0;
            
            int minDepth = sourceDepth < targetDepth ? sourceDepth : targetDepth;
            for (int i = 0; i < minDepth; i++)
            {
                if (sourceChain[i] == targetChain[i])
                {
                    lca = sourceChain[i];
                    commonDepth = i + 1;
                }
                else
                {
                    break;
                }
            }
            
            path.LCA = lca;
            
            path.ExitCount = (ushort)(sourceDepth - commonDepth);
            for (int i = 0; i < path.ExitCount; i++)
            {
                path.ExitPath[i] = sourceChain[sourceDepth - 1 - i];
            }
            
            path.EntryCount = (ushort)(targetDepth - commonDepth);
            for (int i = 0; i < path.EntryCount; i++)
            {
                path.EntryPath[i] = targetChain[commonDepth + i];
            }
            
            return path;
        }

        private static int BuildAncestorChain(
            HsmDefinitionBlob definition,
            ushort stateId,
            ushort* chain)
        {
            int depth = 0;
            ushort current = stateId;
            ushort* tempChain = stackalloc ushort[16];
            
            while (current != 0xFFFF && depth < 16)
            {
                tempChain[depth++] = current;
                ref readonly var state = ref definition.GetState(current);
                current = state.ParentIndex;
            }
            
            for (int i = 0; i < depth; i++)
            {
                chain[i] = tempChain[depth - 1 - i];
            }
            
            return depth;
        }

        private static void SaveHistory(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            ushort compositeStateId,
            ushort activeLeafId)
        {
            ref readonly var state = ref definition.GetState(compositeStateId);
            if (state.HistorySlotIndex == 0xFFFF) return;
            
            ushort* historySlots = GetHistorySlots(instancePtr, instanceSize, out int slotCount);
            if (historySlots == null) return;
            
            if (state.HistorySlotIndex < slotCount)
            {
                historySlots[state.HistorySlotIndex] = activeLeafId;
            }
        }

        private static ushort GetHistorySlot(byte* instancePtr, int instanceSize, ushort slotIndex)
        {
             ushort* slots = GetHistorySlots(instancePtr, instanceSize, out int count);
             if (slots != null && slotIndex < count) return slots[slotIndex];
             return 0xFFFF;
        }

        private static void SetActiveLeafId(byte* instancePtr, int instanceSize, int regionIndex, ushort stateId)
        {
             ushort* active = GetActiveLeafIds(instancePtr, instanceSize, out int count);
             if (active != null && regionIndex < count) active[regionIndex] = stateId;
        }

        private static bool IsAncestor(HsmDefinitionBlob definition, ushort descendant, ushort ancestor)
        {
            if (descendant == ancestor) return true;
            
            ushort current = descendant;
            while (current != 0xFFFF)
            {
                if (current == ancestor) return true;
                ref readonly var state = ref definition.GetState(current);
                current = state.ParentIndex;
            }
            return false;
        }

        


        private static void RestoreDeepHistory(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            ushort stateIndex)
        {
            ref readonly var state = ref definition.GetState(stateIndex);
            
            for (ushort i = 0; i < definition.Header.StateCount; i++)
            {
                ref readonly var child = ref definition.GetState(i);
                if (child.ParentIndex == stateIndex)
                {
                    if (child.HistorySlotIndex != 0xFFFF)
                    {
                        RestoreHistory(definition, instancePtr, instanceSize, i, true);
                    }
                }
            }
        }

        private static bool RestoreHistory(
            HsmDefinitionBlob definition,
            byte* instancePtr,
            int instanceSize,
            ushort stateIndex,
            bool isDeep)
        {
            ref readonly var state = ref definition.GetState(stateIndex);
            if (state.HistorySlotIndex == 0xFFFF) return false;

            ushort savedStateId = GetHistorySlot(instancePtr, instanceSize, state.HistorySlotIndex);

            if (savedStateId == 0xFFFF) return false;

            // Verify the saved state is actually a descendant of the history state
            if (!IsAncestor(definition, savedStateId, stateIndex))
            {
                return false;
            }

            // Restore
            if (isDeep)
            {
                SetActiveLeafId(instancePtr, instanceSize, 0, savedStateId);
                RestoreDeepHistory(definition, instancePtr, instanceSize, savedStateId);
            }
            else
            {
                // Shallow History: Restore the direct child of the composite state
                // We need to find the ancestor of savedStateId that is a child of stateIndex
                ushort target = savedStateId;
                
                if (definition.GetState(target).ParentIndex != stateIndex)
                {
                    // Walk up
                   while (target != 0xFFFF)
                   {
                       ref readonly var s = ref definition.GetState(target);
                       if (s.ParentIndex == stateIndex) break;
                       target = s.ParentIndex;
                   }
                }
                
                // If found, drill down to its initial state (mimic initialization)
                if (target != 0xFFFF)
                {
                    ushort leaf = DrillDownToInitial(definition, target);
                    SetActiveLeafId(instancePtr, instanceSize, 0, leaf);
                }
                else
                {
                    // Fallback (shouldn't happen if IsAncestor check passed)
                    SetActiveLeafId(instancePtr, instanceSize, 0, savedStateId);
                }
            }
            
            return true;
        }

        private static ushort DrillDownToInitial(HsmDefinitionBlob definition, ushort startState)
        {
            ushort targetState = startState;
            while (true)
            {
                ref readonly var state = ref definition.GetState(targetState);
                if ((state.Flags & StateFlags.IsComposite) != 0 && state.FirstChildIndex != 0xFFFF)
                {
                    // For history restore, we follow Initial children normally.
                    // If we hit a Parallel state, we stop?
                    // Usually History restores into Parallel means entering Parallel.
                    if ((state.Flags & StateFlags.IsParallel) != 0) break;
                    
                    targetState = state.FirstChildIndex;
                }
                else
                {
                    break;
                }
            }
            return targetState;
        }
        private static ushort* GetHistorySlots(byte* instancePtr, int instanceSize, out int count)
        {
            switch (instanceSize)
            {
                case 64: count = 2; return (ushort*)(instancePtr + 32);
                case 128: count = 8; return (ushort*)(instancePtr + 40);
                case 256: count = 16; return (ushort*)(instancePtr + 64);
                default: count = 0; return null;
            }
        }

        // --- Helpers ---

        private static uint* GetTimerArray(byte* instancePtr, int instanceSize, out int count)
        {
            switch (instanceSize)
            {
                case 64: count = 2; return (uint*)(instancePtr + 24);
                case 128: count = 4; return (uint*)(instancePtr + 24);
                case 256: count = 8; return (uint*)(instancePtr + 32);
                default: count = 0; return null;
            }
        }

        private static ushort* GetActiveLeafIds(byte* instancePtr, int instanceSize, out int count)
        {
            switch (instanceSize)
            {
                case 64: count = 2; return (ushort*)(instancePtr + 16);
                case 128: count = 4; return (ushort*)(instancePtr + 16);
                case 256: count = 8; return (ushort*)(instancePtr + 16);
                default: count = 0; return null;
            }
        }
        
        private static void StoreCurrentEventId(byte* instancePtr, int instanceSize, ushort eventId)
        {
            ushort* ptr = null;
            if (instanceSize == 64) ptr = (ushort*)(instancePtr + CurrentEventId_Offset_64);
            else if (instanceSize == 128) ptr = (ushort*)(instancePtr + CurrentEventId_Offset_128);
            else if (instanceSize == 256) ptr = (ushort*)(instancePtr + CurrentEventId_Offset_256);
            
            if (ptr != null) *ptr = eventId;
        }
        
        private static ushort GetCurrentEventId(byte* instancePtr, int instanceSize)
        {
            ushort* ptr = null;
            if (instanceSize == 64) ptr = (ushort*)(instancePtr + CurrentEventId_Offset_64);
            else if (instanceSize == 128) ptr = (ushort*)(instancePtr + CurrentEventId_Offset_128);
            else if (instanceSize == 256) ptr = (ushort*)(instancePtr + CurrentEventId_Offset_256);

            return ptr != null ? *ptr : (ushort)0;
        }

        private static int GetTimerCount(int instanceSize)
        {
            return instanceSize switch
            {
                64 => 2,
                128 => 4,
                256 => 8,
                _ => 0
            };
        }

        /// <summary>
        /// Cancel all timers (called on state exit).
        /// </summary>
        private static unsafe void CancelTimers(byte* instancePtr, int instanceSize)
        {
            // Get timer array based on instance size
            int timerCount = GetTimerCount(instanceSize);
            
            if (instanceSize == 64)
            {
                HsmInstance64* inst = (HsmInstance64*)instancePtr;
                for (int i = 0; i < 2; i++)
                    inst->TimerDeadlines[i] = 0;
            }
            else if (instanceSize == 128)
            {
                HsmInstance128* inst = (HsmInstance128*)instancePtr;
                for (int i = 0; i < 4; i++)
                    inst->TimerDeadlines[i] = 0;
            }
            else if (instanceSize == 256)
            {
                HsmInstance256* inst = (HsmInstance256*)instancePtr;
                for (int i = 0; i < 8; i++)
                    inst->TimerDeadlines[i] = 0;
            }
        }
    }
}
