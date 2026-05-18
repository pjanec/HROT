using System;
using System.Runtime.InteropServices;
using Fhsm.Kernel.Data;
using Xunit;

namespace Fhsm.Tests.Data
{
    public class EventCommandTests
    {
        // === HsmEvent Tests ===

        [Fact]
        public void HsmEvent_Is_Exactly_24_Bytes()
        {
            Assert.Equal(24, Marshal.SizeOf<HsmEvent>());
        }

        [Fact]
        public void HsmEvent_FieldOffsets_Are_Correct()
        {
            unsafe
            {
                var evt = new HsmEvent();
                var basePtr = (byte*)&evt;
                
                Assert.Equal(0, (byte*)&evt.EventId - basePtr);
                Assert.Equal(2, (byte*)&evt.Priority - basePtr);
                Assert.Equal(3, (byte*)&evt.Flags - basePtr);
                Assert.Equal(4, (byte*)&evt.Timestamp - basePtr);
                Assert.Equal(8, (byte*)evt.Payload - basePtr);
            }
        }

        [Fact]
        public void HsmEvent_Can_Write_And_Read_Payload()
        {
            unsafe
            {
                var evt = new HsmEvent();
                byte* payloadPtr = evt.Payload;

                // Fill with known pattern
                for(int i = 0; i < 16; i++)
                {
                    payloadPtr[i] = (byte)(i + 1);
                }

                // Verify
                for(int i = 0; i < 16; i++)
                {
                    Assert.Equal((byte)(i + 1), payloadPtr[i]);
                }
            }
        }

        [Fact]
        public void HsmEvent_Can_Store_Int_Payload()
        {
             unsafe
            {
                var evt = new HsmEvent();
                int testValue = unchecked((int)0xDEADBEEF); // using unchecked constant
                
                // Write int to first 4 bytes
                *(int*)evt.Payload = testValue;
                
                // Read back
                int readValue = *(int*)evt.Payload;
                Assert.Equal(testValue, readValue);
            }
        }

        [Fact]
        public void HsmEvent_Can_Store_Float_Payload()
        {
             unsafe
            {
                var evt = new HsmEvent();
                float testValue = 123.456f;
                
                // Write float
                *(float*)evt.Payload = testValue;
                
                // Read back
                float readValue = *(float*)evt.Payload;
                Assert.Equal(testValue, readValue);
            }
        }
        
        struct TestStructPayload
        {
            public int A;
            public int B;
            public int C;
            public int D; // 16 bytes total
        }

        [Fact]
        public void HsmEvent_Can_Store_Struct_Payload()
        {
             unsafe
            {
                var evt = new HsmEvent();
                var payload = new TestStructPayload { A = 1, B = 2, C = 3, D = 4 };
                
                // Write struct
                *(TestStructPayload*)evt.Payload = payload;
                
                // Read back
                var readValue = *(TestStructPayload*)evt.Payload;
                Assert.Equal(1, readValue.A);
                Assert.Equal(2, readValue.B);
                Assert.Equal(3, readValue.C);
                Assert.Equal(4, readValue.D);
            }
        }

        [Fact]
        public void HsmEvent_Priority_Field_Works()
        {
            var evt = new HsmEvent();
            evt.Priority = EventPriority.Interrupt;
            Assert.Equal(EventPriority.Interrupt, evt.Priority);
        }
        
        [Fact]
        public void HsmEvent_Flags_Can_Combine()
        {
            var evt = new HsmEvent();
            evt.Flags = EventFlags.IsDeferred | EventFlags.IsIndirect;
            Assert.True(evt.Flags.HasFlag(EventFlags.IsDeferred));
            Assert.True(evt.Flags.HasFlag(EventFlags.IsIndirect));
            Assert.False(evt.Flags.HasFlag(EventFlags.IsConsumed));
        }

        // === EventFlags Tests ===

        [Fact]
        public void EventFlags_Size_Is_1_Byte()
        {
            Assert.Equal(1, sizeof(EventFlags));
        }

        [Fact]
        public void EventFlags_Values_Are_Correct()
        {
            Assert.Equal(1, (byte)EventFlags.IsDeferred);
            Assert.Equal(2, (byte)EventFlags.IsIndirect);
            Assert.Equal(4, (byte)EventFlags.IsConsumed);
        }

        // === CommandPage Tests ===

        [Fact]
        public void CommandPage_Size_Is_Exactly_4096_Bytes()
        {
             unsafe
            {
                Assert.Equal(4096, sizeof(CommandPage));
            }
        }
        
        [Fact]
        public void CommandPage_Data_Is_4080_Bytes()
        {
            // Verified essentially by the structure test (16 header + 4080 data = 4096)
            // But let's check offset
             unsafe
            {
                var page = new CommandPage();
                var basePtr = (byte*)&page;
                // data starts at 16
                Assert.Equal(16, (byte*)page.Data - basePtr);
                
                // If we check the diff between end of data and start of data in a larger allocation, we could measure it
                // But sizeof - offsetof(Data) is 4080
                Assert.Equal(4080, sizeof(CommandPage) - 16);
            }
        }

        [Fact]
        public void CommandPage_Can_Write_Data()
        {
             unsafe
            {
                var page = new CommandPage();
                page.Data[0] = 0xAA;
                page.Data[4079] = 0xBB;
                Assert.Equal(0xAA, page.Data[0]);
                Assert.Equal(0xBB, page.Data[4079]);
            }
        }

        [Fact]
        public void CommandPage_Header_Fields_Work()
        {
            var page = new CommandPage();
            page.BytesUsed = 123;
            page.PageIndex = 5;
            page.NextPageOffset = 1000;
            
            Assert.Equal(123, page.BytesUsed);
            Assert.Equal(5, page.PageIndex);
            Assert.Equal(1000u, page.NextPageOffset);
        }


        // === HsmCommandWriter Tests ===

        [Fact]
        public void HsmCommandWriter_Can_Create_And_Write()
        {
             unsafe
            {
                var page = new CommandPage();
                var writer = new HsmCommandWriter(&page);
                
                byte[] cmd = new byte[] { 1, 2, 3 };
                bool success = writer.TryWriteCommand(cmd);
                
                Assert.True(success);
                Assert.Equal(3, writer.BytesWritten);
                Assert.Equal(3, page.BytesUsed);
                Assert.Equal(1, page.Data[0]);
                Assert.Equal(2, page.Data[1]);
                Assert.Equal(3, page.Data[2]);
            }
        }

        [Fact]
        public void HsmCommandWriter_Tracks_BytesWritten_Correctly()
        {
             unsafe
            {
                var page = new CommandPage();
                var writer = new HsmCommandWriter(&page);
                
                writer.TryWriteCommand(new byte[] { 1 });
                Assert.Equal(1, writer.BytesWritten);
                
                writer.TryWriteCommand(new byte[] { 2, 3 });
                Assert.Equal(3, writer.BytesWritten);
            }
        }

        [Fact]
        public void HsmCommandWriter_RemainingCapacity_Is_Correct()
        {
             unsafe
            {
                var page = new CommandPage();
                var writer = new HsmCommandWriter(&page); // default cap 4080
                
                Assert.Equal(4080, writer.RemainingCapacity);
                
                writer.TryWriteCommand(new byte[10]);
                Assert.Equal(4070, writer.RemainingCapacity);
            }
        }

        [Fact]
        public void HsmCommandWriter_Fails_When_Full()
        {
             unsafe
            {
                var page = new CommandPage();
                var writer = new HsmCommandWriter(&page, capacity: 10);
                
                Assert.True(writer.TryWriteCommand(new byte[5]));
                Assert.False(writer.TryWriteCommand(new byte[6])); // 5+6 > 10
                
                Assert.Equal(5, writer.BytesWritten); // Should not change
            }
        }

        [Fact]
        public void HsmCommandWriter_Reset_Works()
        {
             unsafe
            {
                var page = new CommandPage();
                var writer = new HsmCommandWriter(&page);
                
                writer.TryWriteCommand(new byte[] { 1, 2, 3 });
                writer.Reset();
                
                Assert.Equal(0, writer.BytesWritten);
                Assert.Equal(0, page.BytesUsed);
                Assert.Equal(4080, writer.RemainingCapacity);
            }
        }
        
        [Fact]
        public void HsmCommandWriter_Updates_Page_BytesUsed()
        {
            unsafe
            {
                var page = new CommandPage();
                var writer = new HsmCommandWriter(&page);
                
                writer.TryWriteCommand(new byte[10]);
                Assert.Equal(10, page.BytesUsed);
            }
        }
    }
}
