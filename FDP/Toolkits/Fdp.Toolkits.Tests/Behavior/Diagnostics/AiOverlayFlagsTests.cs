using System;
using System.Runtime.CompilerServices;
using Fdp.Toolkit.Behavior.Diagnostics;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests.Diagnostics
{
    // SC-P4-01 tests
    public class AiOverlayFlagsTests
    {
        [Fact]
        public void AiOverlayFlags_IsUshort_WithFlagsAttribute()
        {
            Assert.True(typeof(AiOverlayFlags).IsEnum);
            Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(AiOverlayFlags)));
            Assert.True(Attribute.IsDefined(typeof(AiOverlayFlags), typeof(FlagsAttribute)));
        }

        [Fact]
        public void DebugState_HasAiField_And_SizeIsEight()
        {
            Assert.Equal(8, Unsafe.SizeOf<DebugState>());
        }

        [Fact]
        public void DebugState_DefaultAiFieldIsNone()
        {
            var ds = default(DebugState);
            Assert.Equal(AiOverlayFlags.None, ds.Ai);
        }

        [Fact]
        public void DebugState_BehaviorFieldUnchanged_WhenAiSet()
        {
            // Setting Ai must not disturb Behavior bits and vice versa.
            var ds = new DebugState
            {
                Behavior = BehaviorDebugFlags.EnableTraceBuffer,
                Ai       = AiOverlayFlags.UtilityDecision,
            };
            Assert.Equal(BehaviorDebugFlags.EnableTraceBuffer, ds.Behavior);
            Assert.Equal(AiOverlayFlags.UtilityDecision, ds.Ai);
        }
    }
}
