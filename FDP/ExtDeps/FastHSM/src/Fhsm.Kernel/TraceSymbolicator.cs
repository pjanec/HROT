using System;
using System.Text;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Converts binary trace records to human-readable logs.
    /// </summary>
    public class TraceSymbolicator
    {
        private readonly MachineMetadata _metadata;
        
        public TraceSymbolicator(MachineMetadata metadata)
        {
            _metadata = metadata;
        }
        
        /// <summary>
        /// Symbolicate trace records to text.
        /// </summary>
        public string Symbolicate(ReadOnlySpan<TraceRecord> records)
        {
            var sb = new StringBuilder();
            
            foreach (ref readonly var record in records)
            {
                switch (record.OpCode)
                {
                    case TraceOpCode.StateEnter:
                        var stateEnterName = _metadata.GetStateName(record.StateIndex);
                        sb.AppendLine($"[{record.Timestamp}] ENTER: {stateEnterName}");
                        break;
                        
                    case TraceOpCode.StateExit:
                        var stateExitName = _metadata.GetStateName(record.StateIndex);
                        sb.AppendLine($"[{record.Timestamp}] EXIT: {stateExitName}");
                        break;
                        
                    case TraceOpCode.Transition:
                        var fromState = _metadata.GetStateName(record.StateIndex);
                        var toState = _metadata.GetStateName(record.TargetStateIndex);
                        var eventName = _metadata.GetEventName(record.TriggerEventId);
                        sb.AppendLine($"[{record.Timestamp}] TRANSITION: {fromState} -> {toState} [{eventName}]");
                        break;
                        
                    case TraceOpCode.ActionExecuted:
                        var actionName = _metadata.GetActionName(record.ActionId);
                        sb.AppendLine($"[{record.Timestamp}] ACTION: {actionName}");
                        break;
                        
                    case TraceOpCode.GuardEvaluated:
                        var guardName = _metadata.GetActionName(record.GuardId); // Guards are actions in ID space usually, or separate?
                        // Assuming GuardID maps to ActionName for now as per instructions implies simple mapping
                        var result = record.GuardResult != 0 ? "PASS" : "FAIL";
                        sb.AppendLine($"[{record.Timestamp}] GUARD: {guardName} -> {result}");
                        break;

                    case TraceOpCode.EventHandled:
                         // Instruction didn't specify EventHandled but it is a TraceOpCode.
                         // Let's handle it for completeness or leave it as instructions only showed partial.
                         // Instructions showed "TraceOpCode.StateEnter, StateExit, Transition, ActionExecuted, GuardEvaluated".
                         // Just stick to instructions.
                         break;
                }
            }
            
            return sb.ToString();
        }
    }
}
