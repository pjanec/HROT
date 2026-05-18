using System;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Xunit;

namespace Fhsm.Tests.Tooling
{
    public class TraceSymbolicationTests
    {
        [Fact]
        public void Symbolicate_StateEnter_ReturnsReadableLog()
        {
            var metadata = new MachineMetadata();
            metadata.StateNames[5] = "Combat";
            
            var records = new TraceRecord[1];
            records[0] = new TraceRecord 
            { 
                OpCode = TraceOpCode.StateEnter, 
                StateIndex = 5,
                Timestamp = 1000 
            };
            
            var symbolicator = new TraceSymbolicator(metadata);
            var output = symbolicator.Symbolicate(records);
            
            Assert.Contains("[1000] ENTER: Combat", output);
        }

        [Fact]
        public void Symbolicate_Transition_ShowsSourceTargetEvent()
        {
            var metadata = new MachineMetadata();
            metadata.StateNames[1] = "Idle";
            metadata.StateNames[2] = "Walk";
            metadata.EventNames[10] = "MoveInput";

            var records = new TraceRecord[1];
            records[0] = new TraceRecord 
            { 
                OpCode = TraceOpCode.Transition, 
                StateIndex = 1,
                TargetStateIndex = 2,
                TriggerEventId = 10,
                Timestamp = 2000
            };
            
            var symbolicator = new TraceSymbolicator(metadata);
            var output = symbolicator.Symbolicate(records);
            
            Assert.Contains("[2000] TRANSITION: Idle -> Walk [MoveInput]", output);
        }

        [Fact]
        public void Symbolicate_MultipleRecords_ProducesOrderedLog()
        {
            var metadata = new MachineMetadata();
            metadata.StateNames[1] = "Idle";
            metadata.ActionNames[50] = "ResetTimer";

            var records = new TraceRecord[2];
            records[0] = new TraceRecord 
            { 
                OpCode = TraceOpCode.StateExit, 
                StateIndex = 1,
                Timestamp = 100 
            };
            records[1] = new TraceRecord 
            { 
                OpCode = TraceOpCode.ActionExecuted, 
                ActionId = 50,
                Timestamp = 105 
            };
            
            var symbolicator = new TraceSymbolicator(metadata);
            var output = symbolicator.Symbolicate(records);
            
            var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            Assert.Contains("[100] EXIT: Idle", lines[0]);
            Assert.Contains("[105] ACTION: ResetTimer", lines[1]);
        }
    }
}
