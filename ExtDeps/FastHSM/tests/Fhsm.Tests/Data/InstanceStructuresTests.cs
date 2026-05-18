using System;
using System.Runtime.InteropServices;
using Fhsm.Kernel.Data;
using Xunit;

namespace Fhsm.Tests.Data
{
    public class InstanceStructuresTests
    {
        // === Size Tests (CRITICAL) ===

        [Fact]
        public void InstanceHeader_Is_Exactly_16_Bytes()
        {
            Assert.Equal(16, Marshal.SizeOf<InstanceHeader>());
        }

        [Fact]
        public void HsmInstance64_Is_Exactly_64_Bytes()
        {
            unsafe
            {
                Assert.Equal(64, sizeof(HsmInstance64));
            }
        }

        [Fact]
        public void HsmInstance128_Is_Exactly_128_Bytes()
        {
            unsafe
            {
                Assert.Equal(128, sizeof(HsmInstance128));
            }
        }

        [Fact]
        public void HsmInstance256_Is_Exactly_256_Bytes()
        {
            unsafe
            {
                Assert.Equal(256, sizeof(HsmInstance256));
            }
        }

        // === Field Offset Tests ===

        [Fact]
        public void InstanceHeader_FieldOffsets_Are_Correct()
        {
            unsafe
            {
                var header = new InstanceHeader();
                var basePtr = (byte*)&header;
                
                Assert.Equal(0, (byte*)&header.MachineId - basePtr);
                Assert.Equal(4, (byte*)&header.RngState - basePtr);
                Assert.Equal(8, (byte*)&header.Generation - basePtr);
                Assert.Equal(10, (byte*)&header.Flags - basePtr);
                Assert.Equal(12, (byte*)&header.MicroStep - basePtr);
            }
        }

        [Fact]
        public void HsmInstance64_FieldOffsets_Are_Correct()
        {
            unsafe
            {
                var instance = new HsmInstance64();
                var basePtr = (byte*)&instance;

                Assert.Equal(0, (byte*)&instance.Header - basePtr);
                Assert.Equal(16, (byte*)instance.ActiveLeafIds - basePtr);
                Assert.Equal(24, (byte*)instance.TimerDeadlines - basePtr);
                Assert.Equal(32, (byte*)instance.HistorySlots - basePtr);
                Assert.Equal(36, (byte*)&instance.EventCount - basePtr);
                Assert.Equal(40, (byte*)instance.EventBuffer - basePtr);
            }
        }

        [Fact]
        public void HsmInstance128_FieldOffsets_Are_Correct()
        {
            unsafe
            {
                var instance = new HsmInstance128();
                var basePtr = (byte*)&instance;

                Assert.Equal(0, (byte*)&instance.Header - basePtr);
                Assert.Equal(16, (byte*)instance.ActiveLeafIds - basePtr);
                Assert.Equal(24, (byte*)instance.TimerDeadlines - basePtr);
                Assert.Equal(40, (byte*)instance.HistorySlots - basePtr);
                Assert.Equal(56, (byte*)&instance.InterruptSlotUsed - basePtr);
                Assert.Equal(60, (byte*)instance.EventBuffer - basePtr);
            }
        }

        [Fact]
        public void HsmInstance256_FieldOffsets_Are_Correct()
        {
            unsafe
            {
                var instance = new HsmInstance256();
                var basePtr = (byte*)&instance;

                Assert.Equal(0, (byte*)&instance.Header - basePtr);
                Assert.Equal(16, (byte*)instance.ActiveLeafIds - basePtr);
                Assert.Equal(32, (byte*)instance.TimerDeadlines - basePtr);
                Assert.Equal(64, (byte*)instance.HistorySlots - basePtr);
                Assert.Equal(96, (byte*)&instance.InterruptSlotUsed - basePtr);
                Assert.Equal(100, (byte*)instance.EventBuffer - basePtr);
            }
        }

        // === Initialization Tests ===

        [Fact]
        public void InstanceHeader_Can_Initialize()
        {
            var header = new InstanceHeader
            {
                MachineId = 12345,
                RngState = 67890,
                Generation = 1,
                Flags = InstanceFlags.Error,
                Phase = InstancePhase.Activity
            };

            Assert.Equal(12345u, header.MachineId);
            Assert.Equal(67890u, header.RngState);
            Assert.Equal(1, header.Generation);
            Assert.Equal(InstanceFlags.Error, header.Flags);
            Assert.Equal(InstancePhase.Activity, header.Phase);
        }

        [Fact]
        public void HsmInstance64_Can_Access_Header()
        {
            unsafe
            {
                var instance = new HsmInstance64();
                instance.Header.MachineId = 54321;
                Assert.Equal(54321u, instance.Header.MachineId);
            }
        }

        // === Fixed Array Access Tests ===

        [Fact]
        public void HsmInstance64_Can_Access_ActiveLeafIds()
        {
            unsafe
            {
                var instance = new HsmInstance64();
                instance.ActiveLeafIds[0] = 10;
                instance.ActiveLeafIds[1] = 20;
                Assert.Equal(10, instance.ActiveLeafIds[0]);
                Assert.Equal(20, instance.ActiveLeafIds[1]);
            }
        }

        [Fact]
        public void HsmInstance64_Can_Access_TimerDeadlines()
        {
            unsafe
            {
                var instance = new HsmInstance64();
                instance.TimerDeadlines[0] = 100;
                instance.TimerDeadlines[1] = 200;
                Assert.Equal(100u, instance.TimerDeadlines[0]);
                Assert.Equal(200u, instance.TimerDeadlines[1]);
            }
        }

        [Fact]
        public void HsmInstance128_Can_Access_HistorySlots()
        {
            unsafe
            {
                var instance = new HsmInstance128();
                for (int i = 0; i < 8; i++)
                {
                    instance.HistorySlots[i] = (ushort)(i * 10);
                }
                
                for (int i = 0; i < 8; i++)
                {
                    Assert.Equal((ushort)(i * 10), instance.HistorySlots[i]);
                }
            }
        }

        // === Event Buffer and Capacity Tests ===

        [Fact]
        public void HsmInstance64_EventBuffer_Can_Hold_24_Bytes()
        {
            unsafe
            {
                var instance = new HsmInstance64();
                // Write 24 bytes
                for (int i = 0; i < 24; i++)
                {
                    instance.EventBuffer[i] = (byte)i;
                }
                // Verify
                for (int i = 0; i < 24; i++)
                {
                    Assert.Equal((byte)i, instance.EventBuffer[i]);
                }
            }
        }

        [Fact]
        public void HsmInstance128_EventBuffer_Can_Hold_68_Bytes()
        {
            unsafe
            {
                var instance = new HsmInstance128();
                // Write 68 bytes
                for (int i = 0; i < 68; i++)
                {
                    instance.EventBuffer[i] = (byte)i;
                }
                // Verify
                for (int i = 0; i < 68; i++)
                {
                    Assert.Equal((byte)i, instance.EventBuffer[i]);
                }
            }
        }

        [Fact]
        public void HsmInstance256_EventBuffer_Can_Hold_156_Bytes()
        {
            unsafe
            {
                var instance = new HsmInstance256();
                // Write 156 bytes
                for (int i = 0; i < 156; i++)
                {
                    instance.EventBuffer[i] = (byte)i;
                }
                // Verify
                for (int i = 0; i < 156; i++)
                {
                    Assert.Equal((byte)i, instance.EventBuffer[i]);
                }
            }
        }

        [Fact]
        public void HsmInstance64_EventCount_Validates_Single_Queue()
        {
            // Tier 1 uses a single shared queue.
            // Ensure EventCount works as expected.
            unsafe
            {
                var instance = new HsmInstance64();
                instance.EventCount = 1;
                Assert.Equal(1, instance.EventCount);
            }
        }

        [Fact]
        public void HsmInstance128_HybridQueue_Metadata_Works()
        {
            // Tier 2 uses Hybrid Queue strategy.
            unsafe
            {
                var instance = new HsmInstance128();
                instance.InterruptSlotUsed = 1;
                instance.EventCount = 2; // Up to 2 normal events
                
                Assert.Equal(1, instance.InterruptSlotUsed);
                Assert.Equal(2, instance.EventCount);
            }
        }
    }
}
