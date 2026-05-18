using System;
using System.Runtime.CompilerServices;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Public API for HSM kernel execution.
    /// Generic wrapper that inlines to void* core.
    /// </summary>
    public static class HsmKernel
    {
        /// <summary>
        /// Process batch of instances through one tick.
        /// ARCHITECT DIRECTIVE 1: Thin shim pattern with AggressiveInlining.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void UpdateBatch<TInstance, TContext>(
            HsmDefinitionBlob definition,
            Span<TInstance> instances,
            in TContext context,
            float deltaTime)
            where TInstance : unmanaged
            where TContext : unmanaged
        {
            var dummyPage = new CommandPage();
            UpdateBatch(definition, instances, context, deltaTime, ref dummyPage);
        }

        /// <summary>
        /// Process batch of instances through one tick (with Command Buffer).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void UpdateBatch<TInstance, TContext>(
            HsmDefinitionBlob definition,
            Span<TInstance> instances,
            in TContext context,
            float deltaTime,
            ref CommandPage commandPage)
            where TInstance : unmanaged
            where TContext : unmanaged
        {
            if (instances.Length == 0) return;
            
            // Pin and get pointers
            fixed (TInstance* instPtr = instances)
            fixed (TContext* ctxPtr = &context)
            fixed (CommandPage* cmdPtr = &commandPage)
            {
                // Call non-generic core
                HsmKernelCore.UpdateBatchCore(
                    definition,
                    instPtr,
                    instances.Length,
                    sizeof(TInstance),
                    ctxPtr,
                    deltaTime,
                    cmdPtr);
            }
        }
        
        /// <summary>
        /// Overload for single instance.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Update<TInstance, TContext>(
            HsmDefinitionBlob definition,
            ref TInstance instance,
            in TContext context,
            float deltaTime)
            where TInstance : unmanaged
            where TContext : unmanaged
        {
            var dummyPage = new CommandPage();
            Update(definition, ref instance, context, deltaTime, ref dummyPage);
        }

        /// <summary>
        /// Overload for single instance (with Command Buffer).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Update<TInstance, TContext>(
            HsmDefinitionBlob definition,
            ref TInstance instance,
            in TContext context,
            float deltaTime,
            ref CommandPage commandPage)
            where TInstance : unmanaged
            where TContext : unmanaged
        {
            fixed (TInstance* instPtr = &instance)
            fixed (TContext* ctxPtr = &context)
            fixed (CommandPage* cmdPtr = &commandPage)
            {
                HsmKernelCore.UpdateBatchCore(
                    definition,
                    instPtr,
                    1,
                    sizeof(TInstance),
                    ctxPtr,
                    deltaTime,
                    cmdPtr);
            }
        }
        
        /// <summary>
        /// Trigger state machine to start processing from Idle.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Trigger<TInstance>(ref TInstance instance)
            where TInstance : unmanaged
        {
            fixed (TInstance* ptr = &instance)
            {
                InstanceHeader* header = (InstanceHeader*)ptr;
                
                // Only trigger if idle
                if (header->Phase == InstancePhase.Idle)
                {
                    header->Phase = InstancePhase.Entry;
                }
            }
        }
    }
}
