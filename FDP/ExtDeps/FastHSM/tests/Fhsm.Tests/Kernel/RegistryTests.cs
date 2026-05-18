using System;
using Xunit;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Kernel
{
    public class RegistryTests
    {
        private HsmDefinitionBlob CreateBlob(uint hash)
        {
            var header = new HsmDefinitionHeader { StructureHash = hash };
            // Constructor requires non-null arrays usually? 
            // The kernel code checks for them, but struct itself might not validate in ctor.
            // Let's pass empty arrays.
            return new HsmDefinitionBlob(
                header, 
                Array.Empty<StateDef>(), 
                Array.Empty<TransitionDef>(), 
                Array.Empty<RegionDef>(), 
                Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(),
                Array.Empty<ushort>()
            );
        }

        [Fact]
        public void Register_And_Get_Works()
        {
            var registry = new HsmDefinitionRegistry();
            var blob = CreateBlob(0x1234);
            
            registry.Register(blob);
            
            var retrieved = registry.Get(0x1234);
            Assert.Equal(0x1234u, retrieved.Header.StructureHash);
        }

        [Fact]
        public void Get_Missing_Throws()
        {
            var registry = new HsmDefinitionRegistry();
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => registry.Get(0x9999));
        }

        [Fact]
        public void Register_Overwrites_Existing()
        {
            var registry = new HsmDefinitionRegistry();
            var blob1 = CreateBlob(0xAAAA);
            var blob2 = CreateBlob(0xAAAA); 
            
            // Modify state count to distinguish
            // Since it's a struct and we can't modify after construction easily (readonly members?),
            // We need to create it with different values.
            
            // Struct members are fields? 
            // In C# structs returned by value, modifying a property modifies the copy.
            // HsmBlob fields are public or private?
            // HsmHeader is public field? Let's check.
            // Assuming modifying the copy allows us to register the modified copy.
            
            var header1 = new HsmDefinitionHeader { StructureHash = 0xAAAA, StateCount = 1 };
            blob1 = new HsmDefinitionBlob(header1, Array.Empty<StateDef>(), Array.Empty<TransitionDef>(), Array.Empty<RegionDef>(), Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());

            var header2 = new HsmDefinitionHeader { StructureHash = 0xAAAA, StateCount = 2 };
            blob2 = new HsmDefinitionBlob(header2, Array.Empty<StateDef>(), Array.Empty<TransitionDef>(), Array.Empty<RegionDef>(), Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());
            
            registry.Register(blob1);
            Assert.Equal(1, registry.Get(0xAAAA).Header.StateCount);
            
            registry.Register(blob2);
            Assert.Equal(2, registry.Get(0xAAAA).Header.StateCount);
        }
    }
}
