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
                    cmdPtr,
                    null);
            }
        }

        /// <summary>
        /// Overload for single instance (with Command Buffer + Trace Context).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Update<TInstance, TContext>(
            HsmDefinitionBlob definition,
            ref TInstance instance,
            in TContext context,
            float deltaTime,
            ref CommandPage commandPage,
            HsmTraceContext* traceCtx)
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
                    cmdPtr,
                    traceCtx);
            }
        }

        /// <summary>
        /// ⭐ <b>O6 / §9.4 — tick ONE instance that lives in a SLOT, not in a component field.</b>
        /// <para>
        /// The generic overloads take the instance size from <c>sizeof(TInstance)</c> — a property of
        /// a type the caller chose. This one takes it from <paramref name="instanceSize"/>, which a
        /// slot-resident caller reads straight off the allocation (<c>slot.PayloadSize</c>).
        /// </para>
        /// <para>
        /// ⛔ <b>The deciding argument is MEMORY SAFETY, not convenience.</b> With occurrence payloads
        /// packed adjacently inside one component, ticking through a generic overload whose type is
        /// larger than the slot reads past the payload and into the NEXT OCCURRENCE'S bytes — no
        /// compiler check, no runtime check. Sizing from the allocation cannot disagree with the
        /// allocation. DESIGN_Occurrence_Scoped_Storage.md §9.4.
        /// </para>
        /// <para>
        /// ⚠ The generic overloads remain the documented surface for ordinary callers; this one is
        /// for callers that genuinely hold a pointer and a size.
        /// </para>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Update(
            HsmDefinitionBlob definition,
            byte* instance,
            int instanceSize,
            void* context,
            float deltaTime,
            CommandPage* commandPage,
            HsmTraceContext* traceCtx = null)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (instanceSize <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(instanceSize),
                    "The instance size must come from the slot's own PayloadSize; a non-positive " +
                    "size means the caller does not know how big its occurrence is.");

            HsmKernelCore.UpdateBatchCore(
                definition, instance, 1, instanceSize, context, deltaTime, commandPage, traceCtx);
        }

        /// <summary>
        /// Overload for batch with Trace Context.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void UpdateBatch<TInstance, TContext>(
            HsmDefinitionBlob definition,
            Span<TInstance> instances,
            in TContext context,
            float deltaTime,
            ref CommandPage commandPage,
            HsmTraceContext* traceCtx)
            where TInstance : unmanaged
            where TContext : unmanaged
        {
            if (instances.Length == 0) return;

            fixed (TInstance* instPtr = instances)
            fixed (TContext* ctxPtr = &context)
            fixed (CommandPage* cmdPtr = &commandPage)
            {
                HsmKernelCore.UpdateBatchCore(
                    definition,
                    instPtr,
                    instances.Length,
                    sizeof(TInstance),
                    ctxPtr,
                    deltaTime,
                    cmdPtr,
                    traceCtx);
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
