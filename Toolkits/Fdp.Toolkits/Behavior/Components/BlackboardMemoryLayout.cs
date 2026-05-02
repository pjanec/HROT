using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// Explicit overlay DTO that maps the 128-byte <see cref="BrainBlackboard.Memory"/> buffer
    /// into compiler-verified named regions, eliminating raw byte offsets throughout engine code.
    ///
    /// <para>
    /// Usage — zero-allocation access via <c>Unsafe.As</c>:
    /// <code>
    ///   ref var layout = ref Unsafe.As&lt;BrainBlackboard, BlackboardMemoryLayout&gt;(ref bb);
    ///   layout.Interrupt_MobilityLost = 1;
    /// </code>
    /// </para>
    ///
    /// <para>
    /// Memory regions:
    /// <list type="table">
    ///   <item><term>0-59</term><description>Polymorphic behavior parameters (projected via Unsafe.As from caller DTO)</description></item>
    ///   <item><term>60-125</term><description>Soft-advice / contextual routing data</description></item>
    ///   <item><term>126</term><description>MobilityLost edge-triggered interrupt (set/cleared within a single frame)</description></item>
    ///   <item><term>127</term><description>Reserved hardware-level interrupt slot</description></item>
    /// </list>
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = BehaviorConstants.BrainBlackboardByteSize)]
    public unsafe struct BlackboardMemoryLayout
    {
        /// <summary>
        /// Bytes 0-59: polymorphic behavior parameter payload.
        /// AI developers project their specific DTO (e.g. <c>FireAtTargetParams</c>) onto
        /// this region using <c>Unsafe.As</c>. Must not exceed
        /// <see cref="BehaviorConstants.MaxBehaviorParamByteSize"/> bytes.
        /// </summary>
        [FieldOffset(0)]
        public fixed byte BehaviorParameters[BehaviorConstants.MaxBehaviorParamByteSize];

        /// <summary>
        /// Bytes 60-125: soft-advice / contextual routing data written by engine subsystems
        /// such as <c>RouteContextSystem</c>. Kept well clear of the interrupt registers
        /// so behavior parameter overruns cannot corrupt them.
        /// </summary>
        [FieldOffset(BehaviorConstants.MaxBehaviorParamByteSize)]
        public fixed byte SoftAdvice[BehaviorConstants.SoftAdviceByteSize];

        /// <summary>
        /// Byte 126: MobilityLost edge-triggered interrupt.
        /// Set to 1 by <c>CognitiveInterruptSystem</c> on the tick <c>CanMove</c> transitions
        /// from set to cleared. Cleared back to 0 by <c>CognitiveCleanupSystem</c> at end of frame.
        /// </summary>
        [FieldOffset(BehaviorConstants.Interrupt_MobilityLost_Offset)]
        public byte Interrupt_MobilityLost;

        /// <summary>
        /// Byte 127: reserved for future hardware-level interrupt.
        /// </summary>
        [FieldOffset(BehaviorConstants.Interrupt_Reserved_Offset)]
        public byte Interrupt_Reserved;
    }
}
