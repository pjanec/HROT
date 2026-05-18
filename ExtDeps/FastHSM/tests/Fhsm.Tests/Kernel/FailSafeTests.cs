using System;
using Xunit;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Fhsm.Tests.Kernel
{
    public unsafe class FailSafeTests
    {
        private HsmDefinitionBlob CreateInfiniteLoopBlob()
        {
            // Single state with self-loop transition
            var states = new StateDef[1];
            states[0] = new StateDef 
            { 
                ParentIndex = 0xFFFF, 
                FirstTransitionIndex = 0, 
                TransitionCount = 1,
                Depth = 0
            };

            var transitions = new TransitionDef[1];
            transitions[0] = new TransitionDef 
            { 
                SourceStateIndex = 0, 
                TargetStateIndex = 0,  // Self-loop! State 0 -> State 0
                EventId = 0, // Epsilon transition (always loop)
                GuardId = 0,  // No guard = always fires
                ActionId = 0,
                Flags = 0
            };

            var header = new HsmDefinitionHeader();
            header.StructureHash = 0x12345678;
            header.StateCount = 1;
            header.TransitionCount = 1;
            header.RegionCount = 1;

            return new HsmDefinitionBlob(
                header,
                states,
                transitions,
                Array.Empty<RegionDef>(),
                Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(),
                Array.Empty<ushort>()
            );
        }

        [Fact]
        public void InfiniteLoop_Detected_And_Stops()
        {
            var blob = CreateInfiniteLoopBlob();
            var instances = new HsmInstance64[1];
            instances[0].Header.MachineId = 0x12345678;
            instances[0].Header.Phase = InstancePhase.RTC;
            instances[0].Header.Flags = InstanceFlags.DebugTrace;
            instances[0].Header.RngState = 123;
            instances[0].ActiveLeafIds[0] = 0; // Start at State 0
            
            int context = 0;
            
            // Queue event 0 (triggers self-loop)
            HsmEventQueue.TryEnqueue(
                (void*)Unsafe.AsPointer(ref instances[0]), 
                sizeof(HsmInstance64), 
                new HsmEvent { EventId = 0, Priority = EventPriority.Normal }
            );
            
            // 65KB buffer should be enough for 100 iterations * 16 bytes = 1600 bytes
            var traceBuffer = new HsmTraceBuffer(65536);
            traceBuffer.FilterLevel = TraceLevel.All;
            HsmKernelCore.SetTraceBuffer(traceBuffer);
            
            try 
            {
                // Run multiple frames to allow phase transitions (Idle->Entry->RTC->Idle)
                for(int i=0; i<10; i++)
                {
                    HsmKernel.UpdateBatch(blob, instances.AsSpan(), context, 0.016f);
                    if (instances[0].Header.Phase == InstancePhase.Idle) break;
                }
            }
            finally
            {
                HsmKernelCore.SetTraceBuffer(null);
            }
            
            // 1. Check Safe State (0xFFFF)
            Assert.True(instances[0].ActiveLeafIds[0] == 0xFFFF, $"Exp FFFF, Got {instances[0].ActiveLeafIds[0]}. Phase: {instances[0].Header.Phase}");
            
            // 2. Check Phase Reset to Idle
            Assert.Equal(InstancePhase.Idle, instances[0].Header.Phase);
            
            // 3. Check Trace Error
            var traceData = traceBuffer.GetTraceData();
            Assert.True(traceData.Length > 0, "Trace buffer is empty");
            
            bool foundError = false;
            int offset = 0;
            while (offset < traceData.Length)
            {
                var header = MemoryMarshal.Read<TraceRecordHeader>(traceData.Slice(offset));
                if (header.OpCode == TraceOpCode.Error)
                {
                    foundError = true;
                    break;
                }

                int size = 12; // Default size
                switch (header.OpCode)
                {
                    case TraceOpCode.Transition:
                    case TraceOpCode.GuardEvaluated:
                    case TraceOpCode.TimerSet:
                        size = 16;
                        break;
                    default:
                        size = 12;
                        break;
                }
                
                if (offset + size > traceData.Length) break;
                offset += size;
            }
            
            Assert.True(foundError, "Error record not found in trace");
        }
    }
}
