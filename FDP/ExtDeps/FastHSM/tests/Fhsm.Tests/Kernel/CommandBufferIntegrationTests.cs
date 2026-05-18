using System;
using System.Runtime.CompilerServices;
using Fhsm.Kernel;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Fhsm.Compiler;
using Xunit;

namespace Fhsm.Tests.Kernel
{
    [Xunit.Collection("HsmActionDispatcher")]
    public unsafe class CommandBufferIntegrationTests
    {
        // Must match delegate* <void*, void*, HsmCommandWriter*, void>
        [HsmAction(Name = "WriteTestCommand")]
        public static void WriteTestCommand(void* instance, void* context, HsmCommandWriter* writer)
        {
            // Write a simple command payload
            // Command ID: 0x01
            // Payload: 0xAA, 0xBB
            byte[] cmd = new byte[] { 0x01, 0xAA, 0xBB };
            writer->TryWriteCommand(cmd);
        }

        [HsmAction(Name = "WriteAA")]
        public static void WriteAA(void* instance, void* context, HsmCommandWriter* writer)
        {
            writer->TryWriteCommand(new byte[] { 0xAA });
        }

        [HsmAction(Name = "WriteBB")]
        public static void WriteBB(void* instance, void* context, HsmCommandWriter* writer)
        {
            writer->TryWriteCommand(new byte[] { 0xBB });
        }

        [HsmAction(Name = "WriteCC")]
        public static void WriteCC(void* instance, void* context, HsmCommandWriter* writer)
        {
            writer->TryWriteCommand(new byte[] { 0xCC });
        }

        private static ushort ComputeHash(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (ushort)(hash & 0xFFFF);
        }

        [Fact]
        public void Dispatcher_CanExecuteAction_WithCommandWriter()
        {
            // This relies on the Source Generator emitting code for THIS assembly
            // and the HsmActionRegistrar being available in Fhsm.Tests.Generated
            Fhsm.Tests.Generated.HsmActionRegistrar.RegisterAll();
            
            ushort actionId = ComputeHash("WriteTestCommand");
            
            // Create CommandPage
            CommandPage page = new CommandPage();
            HsmCommandWriter writer = new HsmCommandWriter(&page);
            
            // Execute
            HsmActionDispatcher.ExecuteAction(actionId, null, null, &writer);
            
            // Verify
            Assert.Equal(3, writer.BytesWritten);
            Assert.Equal(0x01, page.Data[0]);
            Assert.Equal(0xAA, page.Data[1]);
            Assert.Equal(0xBB, page.Data[2]);
        }
        
        [Fact]
        public void Multiple_Actions_Write_To_Same_Buffer()
        {
            // Setup
            Fhsm.Tests.Generated.HsmActionRegistrar.RegisterAll();

            // Build state machine
            // A -> B (Transition)
            // A Exit: WriteAA
            // Trans: WriteBB
            // B Entry: WriteCC
            
            var builder = new HsmBuilder("MultiWriteMachine");
            builder.RegisterAction("WriteAA");
            builder.RegisterAction("WriteBB");
            builder.RegisterAction("WriteCC");
            builder.Event("Next", 1);
            
            var stateA = builder.State("A")
                .OnExit("WriteAA");
                
            var stateB = builder.State("B")
                .OnEntry("WriteCC");
            
            stateA.On("Next")
                .GoTo("B")
                .Action("WriteBB");
                
            stateA.Initial();
            
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            var flattened = HsmFlattener.Flatten(graph);
            var blob = HsmEmitter.Emit(flattened);
            
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            
            // 1. Enter A (Update)
            HsmKernel.Trigger(ref instance);
            // Run loop to settle
            for(int i=0; i<5; i++) HsmKernel.Update(blob, ref instance, 0, 0.16f);
            
            // 2. Queue event, Update -> Writes AA, BB, CC to COMMAND BUFFER
            var evt = new HsmEvent { EventId = 1, Priority = EventPriority.Normal };
            HsmEventQueue.TryEnqueue(&instance, 64, evt);
            
            var page = new CommandPage();
            // Need multiple updates to process Idle -> Entry -> RTC -> Activity
            for(int i=0; i<5; i++) HsmKernel.Update(blob, ref instance, 0, 0.16f, ref page);
            
            // Check buffer
            // Expect AA, BB, CC
            Assert.Equal(0xAA, page.Data[0]);
            Assert.Equal(0xBB, page.Data[1]);
            Assert.Equal(0xCC, page.Data[2]);
        }

        [Fact]
        public void Command_Buffer_Used_Across_Multiple_Updates()
        {
             // Setup
            Fhsm.Tests.Generated.HsmActionRegistrar.RegisterAll();
            
            var builder = new HsmBuilder("LifecycleMachine");
            builder.RegisterAction("WriteAA");
            builder.RegisterAction("WriteBB");
            builder.Event("Next", 1);
            
            var stateA = builder.State("A")
                .OnEntry("WriteAA");
                
            var stateB = builder.State("B")
                .OnEntry("WriteBB");
                
            stateA.On("Next").GoTo("B");
            
            stateA.Initial();
            
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            var flattened = HsmFlattener.Flatten(graph);
            var blob = HsmEmitter.Emit(flattened);
            
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            
            // 1. Initialize (Enter A -> writes AA)
            HsmKernel.Trigger(ref instance);
            
            var page1 = new CommandPage();
            // Initialize might take one tick if checks pass
            // We run a few ticks to ensure it settles to Activity/Idle
            for(int i=0; i<3; i++) HsmKernel.Update(blob, ref instance, 0, 0.16f, ref page1);
            
            // Verify AA - Check byte 0. If 0, maybe action didn't run? 
            // Or maybe it ran but wrote 0? (Action writes 0xAA)
            Assert.Equal(0xAA, page1.Data[0]);
            
            // 2. Next update (Transition -> Enter B -> writes BB)
            // Use NEW page
            var evt = new HsmEvent { EventId = 1, Priority = EventPriority.Normal };
            HsmEventQueue.TryEnqueue(&instance, 64, evt);

            var page2 = new CommandPage();
            for(int i=0; i<5; i++) HsmKernel.Update(blob, ref instance, 0, 0.16f, ref page2);
            
            // Verify ONLY BB (0th index)
            Assert.Equal(0xBB, page2.Data[0]);
            
            // Verify page1 is untouched by second update
            Assert.Equal(0xAA, page1.Data[0]);
            // page1[1] should be 0 as only 1 byte written
            Assert.Equal(0, page1.Data[1]); 
        }
    }
}
