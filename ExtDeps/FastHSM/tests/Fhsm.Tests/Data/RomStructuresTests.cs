using System;
using System.Runtime.InteropServices;
using Fhsm.Kernel.Data;
using Xunit;

namespace Fhsm.Tests.Data
{
    public class RomStructuresTests
    {
        // === Size Tests (CRITICAL) ===

        [Fact]
        public void StateDef_Is_Exactly_32_Bytes()
        {
            Assert.Equal(32, Marshal.SizeOf<StateDef>());
        }

        [Fact]
        public void TransitionDef_Is_Exactly_16_Bytes()
        {
            Assert.Equal(16, Marshal.SizeOf<TransitionDef>());
        }

        [Fact]
        public void RegionDef_Is_Exactly_8_Bytes()
        {
            Assert.Equal(8, Marshal.SizeOf<RegionDef>());
        }

        [Fact]
        public void GlobalTransitionDef_Is_Exactly_16_Bytes()
        {
            Assert.Equal(16, Marshal.SizeOf<GlobalTransitionDef>());
        }

        [Fact]
        public void Enums_Are_Correct_Size()
        {
            Assert.Equal(2, sizeof(StateFlags));
            Assert.Equal(2, sizeof(TransitionFlags));
            Assert.Equal(1, sizeof(EventPriority));
            Assert.Equal(1, sizeof(InstancePhase));
            Assert.Equal(1, sizeof(InstanceFlags));
        }

        // === Field Offset Tests ===

        [Fact]
        public void StateDef_FieldOffsets_Are_Correct()
        {
            unsafe
            {
                var def = new StateDef();
                var basePtr = (byte*)&def;

                Assert.Equal(0, (byte*)&def.ParentIndex - basePtr);
                Assert.Equal(12, (byte*)&def.OnEntryActionId - basePtr);
                Assert.Equal(18, (byte*)&def.Flags - basePtr);
                Assert.Equal(24, (byte*)&def.RegionStartIndex - basePtr);
            }
        }

        [Fact]
        public void TransitionDef_FieldOffsets_Are_Correct()
        {
            unsafe
            {
                var def = new TransitionDef();
                var basePtr = (byte*)&def;

                Assert.Equal(0, (byte*)&def.SourceStateIndex - basePtr);
                Assert.Equal(8, (byte*)&def.GuardId - basePtr);
                Assert.Equal(12, (byte*)&def.Flags - basePtr);
            }
        }

        [Fact]
        public void RegionDef_FieldOffsets_Are_Correct()
        {
            unsafe
            {
                var def = new RegionDef();
                var basePtr = (byte*)&def;

                Assert.Equal(0, (byte*)&def.ParentStateIndex - basePtr);
                Assert.Equal(4, (byte*)&def.Priority - basePtr);
            }
        }

        [Fact]
        public void GlobalTransitionDef_FieldOffsets_Are_Correct()
        {
            unsafe
            {
                var def = new GlobalTransitionDef();
                var basePtr = (byte*)&def;

                Assert.Equal(0, (byte*)&def.TargetStateIndex - basePtr);
                Assert.Equal(4, (byte*)&def.GuardId - basePtr);
                Assert.Equal(8, (byte*)&def.Flags - basePtr);
            }
        }

        // === Initialization Tests ===

        [Fact]
        public void StateDef_Can_Initialize_With_Default_Values()
        {
            var def = new StateDef();
            Assert.Equal(0, def.ParentIndex); // Default is 0, but logical 'none' is 0xFFFF. Struct default is 0.
            Assert.Equal(StateFlags.None, def.Flags);
        }

        [Fact]
        public void StateDef_Can_Set_All_Fields()
        {
            var def = new StateDef
            {
                ParentIndex = 0xFFFF,
                FirstChildIndex = 1,
                Depth = 5,
                Flags = StateFlags.IsComposite | StateFlags.IsFinal
            };

            Assert.Equal(0xFFFF, def.ParentIndex);
            Assert.Equal(1, def.FirstChildIndex);
            Assert.Equal(5, def.Depth);
            Assert.Equal(StateFlags.IsComposite | StateFlags.IsFinal, def.Flags);
        }

        [Fact]
        public void TransitionDef_Can_Initialize()
        {
            var def = new TransitionDef
            {
                SourceStateIndex = 10,
                TargetStateIndex = 20,
                Flags = TransitionFlags.IsExternal | TransitionFlags.HasGuard
            };

            Assert.Equal(10, def.SourceStateIndex);
            Assert.Equal(20, def.TargetStateIndex);
            Assert.True(def.Flags.HasFlag(TransitionFlags.IsExternal));
        }

        // === Flag Manipulation Tests ===

        [Fact]
        public void StateFlags_Can_Combine_Flags()
        {
            var flags = StateFlags.IsComposite | StateFlags.IsHistory;
            Assert.True(flags.HasFlag(StateFlags.IsComposite));
            Assert.True(flags.HasFlag(StateFlags.IsHistory));
            Assert.False(flags.HasFlag(StateFlags.IsFinal));
        }

        [Fact]
        public void TransitionFlags_Extract_Priority()
        {
            // Set priority to 10 (1010 binary -> 0xA)
            // Priority is in bits 12-15.
            ushort priority = 10;
            var flags = TransitionFlags.IsExternal | (TransitionFlags)(priority << 12);

            // Extract
            var extractedPriority = ((ushort)flags & (ushort)TransitionFlags.Priority_Mask) >> 12;

            Assert.Equal(priority, extractedPriority);
            Assert.True(flags.HasFlag(TransitionFlags.IsExternal));
        }

        [Fact]
        public void InstanceFlags_Can_Combine_Flags()
        {
            var flags = InstanceFlags.EventOverflow | InstanceFlags.Error;
            Assert.True((flags & InstanceFlags.EventOverflow) != 0);
            Assert.True((flags & InstanceFlags.Error) != 0);
        }

        // === Edge Case Tests ===

        [Fact]
        public void Indices_Support_Max_Value()
        {
            var def = new StateDef { ParentIndex = 0xFFFF };
            Assert.Equal(65535, def.ParentIndex);
        }

        [Fact]
        public void Depth_Supports_Max_Value()
        {
            // Depth is byte, max 255
            var def = new StateDef { Depth = 255 };
            Assert.Equal(255, def.Depth);
        }

        [Fact]
        public void TransitionFlags_Priority_Max_Value()
        {
            ushort maxPriority = 15;
            var flags = (TransitionFlags)(maxPriority << 12);
            var extracted = ((ushort)flags & (ushort)TransitionFlags.Priority_Mask) >> 12;
            Assert.Equal(maxPriority, extracted);
        }

        [Fact]
        public void RegionDef_Can_Initialize()
        {
            var def = new RegionDef
            {
                ParentStateIndex = 1,
                InitialStateIndex = 2,
                Priority = 5
            };
            Assert.Equal(1, def.ParentStateIndex);
            Assert.Equal(2, def.InitialStateIndex);
            Assert.Equal(5, def.Priority);
        }

        [Fact]
        public void GlobalTransitionDef_Can_Initialize()
        {
            var def = new GlobalTransitionDef
            {
                TargetStateIndex = 5,
                EventId = 10,
                Priority = 1
            };
            Assert.Equal(5, def.TargetStateIndex);
            Assert.Equal(10, def.EventId);
            Assert.Equal(1, def.Priority);
        }

        [Fact]
        public void TransitionFlags_None_Is_Zero()
        {
            Assert.Equal((ushort)0, (ushort)TransitionFlags.None);
        }
    }
}
