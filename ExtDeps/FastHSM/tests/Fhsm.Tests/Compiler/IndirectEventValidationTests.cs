using System;
using Fhsm.Compiler;
using Xunit;

namespace Fhsm.Tests.Compiler
{
    public class IndirectEventValidationTests
    {
        [Fact]
        public void Event_Over16B_NotIndirect_Errors()
        {
            var builder = new HsmBuilder("Test");
            
            builder.Event("BigEvent", 1, payloadSize: 32, isIndirect: false);
            
            var graph = builder.Build();
            var errors = HsmGraphValidator.Validate(graph);
            
            Assert.Contains(errors, e => e.Message.Contains("BigEvent") && e.Message.Contains("IsIndirect") && e.Severity == HsmGraphValidator.ErrorSeverity.Error);
        }

        [Fact]
        public void Event_Over16B_WithIndirect_Passes()
        {
            var builder = new HsmBuilder("Test");
            builder.Event("BigEvent", 1, payloadSize: 32, isIndirect: true);
            
            var graph = builder.Build();
            var errors = HsmGraphValidator.Validate(graph);
            
            Assert.DoesNotContain(errors, e => e.Message.Contains("BigEvent"));
        }

        [Fact]
        public void Event_Indirect_And_Deferred_Warns()
        {
            var builder = new HsmBuilder("Test");
            builder.Event("ConflictEvent", 1, isIndirect: true, isDeferred: true);
            
            var graph = builder.Build();
            var errors = HsmGraphValidator.Validate(graph);
            
            // Should have warning
            Assert.Contains(errors, e => e.Message.Contains("ConflictEvent") && 
                                         e.Message.Contains("IsIndirect") && 
                                         e.Message.Contains("IsDeferred") &&
                                         e.Severity == HsmGraphValidator.ErrorSeverity.Warning);
        }
    }
}
