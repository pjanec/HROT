using System;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fhsm.Compiler;
using Xunit;

namespace Fhsm.Tests.Kernel
{
    public unsafe class HotReloadTests
    {
        private readonly HotReloadManager _manager = new HotReloadManager();

        [Fact]
        public void TryReload_FirstLoad_ReturnsNewMachine()
        {
            var blob = CreateDummyBlob(0x1111, 0xAAAA);
            var instance = new HsmInstance64();
            Span<HsmInstance64> instances = stackalloc HsmInstance64[1];
            instances[0] = instance;

            var result = _manager.TryReload(0x1111, blob, instances);

            Assert.Equal(ReloadResult.NewMachine, result);
        }

        [Fact]
        public void TryReload_SameHashes_ReturnsNoChange()
        {
            var blob = CreateDummyBlob(0x1111, 0xAAAA);
            var instance = new HsmInstance64();
            Span<HsmInstance64> instances = stackalloc HsmInstance64[1];
            instances[0] = instance;

            // Load first
            _manager.TryReload(0x1111, blob, instances);

            // Reload same
            var result = _manager.TryReload(0x1111, blob, instances);

            Assert.Equal(ReloadResult.NoChange, result);
        }

        [Fact]
        public void TryReload_ParameterChanged_ReturnsSoftReload_And_PreservesState()
        {
            var blob1 = CreateDummyBlob(0x1111, 0xAAAA);
            var blob2 = CreateDummyBlob(0x1111, 0xBBBB); // Different param hash

            var instance = new HsmInstance64();
            instance.Header.Phase = InstancePhase.Activity; // Simulate running state
            
            Span<HsmInstance64> instances = stackalloc HsmInstance64[1];
            instances[0] = instance;

            // 1. Load initial
            _manager.TryReload(0x1111, blob1, instances);

            // 2. Load update
            var result = _manager.TryReload(0x1111, blob2, instances);

            Assert.Equal(ReloadResult.SoftReload, result);
            
            // Verify state preserved
            Assert.Equal(InstancePhase.Activity, instances[0].Header.Phase);
        }

        [Fact]
        public void TryReload_StructureChanged_ReturnsHardReset_And_ClearsState()
        {
            var blob1 = CreateDummyBlob(0x1111, 0xAAAA);
            var blob2 = CreateDummyBlob(0x2222, 0xAAAA); // Different structure hash

            var instance = new HsmInstance64();
            instance.Header.Phase = InstancePhase.Activity; // Simulate running state
            instance.Header.MachineId = 0x1111; // Must match old hash for reset to apply
            instance.Header.Generation = 5;
            
            // Set some dummy state
            instance.ActiveLeafIds[0] = 123;
            instance.TimerDeadlines[0] = 9999;
            instance.HistorySlots[0] = 1;
            instance.EventCount = 1;

            Span<HsmInstance64> instances = stackalloc HsmInstance64[1];
            instances[0] = instance;

            // 1. Load initial
            _manager.TryReload(0x1111, blob1, instances);

            // 2. Load update (Structure changed)
            // Note: We use same machineId (registry key) but the blob content changed
            var result = _manager.TryReload(0x1111, blob2, instances);

            Assert.Equal(ReloadResult.HardReset, result);
            
            // Verify state cleared
            ref var inst = ref instances[0];
            Assert.Equal(InstancePhase.Idle, inst.Header.Phase);
            Assert.Equal(6, inst.Header.Generation); // Incremented
            Assert.Equal(0x2222, (int)inst.Header.MachineId); // Updated to new hash
            
            Assert.Equal(0xFFFF, (int)inst.ActiveLeafIds[0]);
            Assert.Equal(0, (int)inst.TimerDeadlines[0]);
            Assert.Equal(0xFFFF, (int)inst.HistorySlots[0]);
            Assert.Equal(0, inst.EventCount);
        }

        private HsmDefinitionBlob CreateDummyBlob(uint structureHash, uint paramHash)
        {
            var blob = new HsmDefinitionBlob();
            blob.Header.StructureHash = structureHash;
            blob.Header.ParameterHash = paramHash;
            return blob;
        }
    }
}
