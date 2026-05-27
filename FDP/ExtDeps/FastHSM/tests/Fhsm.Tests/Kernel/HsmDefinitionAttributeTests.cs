using System;
using Xunit;
using Fhsm.Kernel.Attributes;

namespace Fhsm.Tests.Kernel
{
    /// <summary>Tests for TASK-BB-K-01 and TASK-BB-K-02 on HsmDefinitionAttribute.</summary>
    public class HsmDefinitionAttributeTests
    {
        // ============================================================
        // TASK-BB-K-01: HsmDefinitionAttribute.BlackboardManaged
        // ============================================================

        [Fact]
        public void HsmDefinitionAttribute_BlackboardManaged_DefaultsFalse()
        {
            var attr = new HsmDefinitionAttribute("TestMachine");
            Assert.False(attr.BlackboardManaged);
        }

        [Fact]
        public void HsmDefinitionAttribute_BlackboardManaged_RoundTripsTrue()
        {
            var attr = new HsmDefinitionAttribute("TestMachine") { BlackboardManaged = true };
            Assert.True(attr.BlackboardManaged);
        }

        // ============================================================
        // TASK-BB-K-02: HsmDefinitionAttribute.HeavyDtoType
        // ============================================================

        [Fact]
        public void HsmDefinitionAttribute_HeavyDtoType_DefaultsNull()
        {
            var attr = new HsmDefinitionAttribute("TestMachine");
            Assert.Null(attr.HeavyDtoType);
        }

        [Fact]
        public void HsmDefinitionAttribute_HeavyDtoType_CanBeSet()
        {
            var attr = new HsmDefinitionAttribute("TestMachine") { HeavyDtoType = typeof(int) };
            Assert.Equal(typeof(int), attr.HeavyDtoType);
        }

        [Fact]
        public void HsmDefinitionAttribute_HeavyDtoType_NullMeansNoHeavyComponent()
        {
            // Null HeavyDtoType means the runtime provisions no heavy component (regression guard).
            var attr = new HsmDefinitionAttribute("TestMachine");
            Assert.Null(attr.HeavyDtoType);
        }

        // ============================================================
        // Regression: existing MachineName and AssetId still work
        // ============================================================

        [Fact]
        public void HsmDefinitionAttribute_MachineNameIsPreserved()
        {
            var attr = new HsmDefinitionAttribute("MyMachine");
            Assert.Equal("MyMachine", attr.MachineName);
        }

        [Fact]
        public void HsmDefinitionAttribute_AssetIdDefaultsNull()
        {
            var attr = new HsmDefinitionAttribute("MyMachine");
            Assert.Null(attr.AssetId);
        }
    }
}
