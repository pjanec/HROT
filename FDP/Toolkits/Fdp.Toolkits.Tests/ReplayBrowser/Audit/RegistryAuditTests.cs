using System;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Audit
{
    // Component IDs 200, 291 reserved for this file (Fdp.Toolkits.Tests/ReplayBrowser/Audit)
    // NOTE: 201 = GlobalComponentIds.ZoneEnvironmentData (production); use 291 instead.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    [ComponentId(200)]
    internal struct AuditCompA { public int Value; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    [ComponentId(291)] // was 201; 201 = GlobalComponentIds.ZoneEnvironmentData (production conflict)
    internal struct AuditCompB { public float Value; }

    // Event IDs 99001-99002 reserved for this file
    [EventId(99001)]
    internal struct AuditEventA { public int Value; }

    [EventId(99002)]
    internal struct AuditEventB { public float Value; }

    public class RegistryAuditTests : IDisposable
    {
        public RegistryAuditTests()
        {
            ComponentTypeRegistry.Clear();
        }

        public void Dispose() { }

        [Fact]
        public void GetAllRegistered_ComponentTypes_ContainsRegisteredTypes()
        {
            // Trigger registration via ComponentType<T>.ID
            _ = ComponentType<AuditCompA>.ID;
            _ = ComponentType<AuditCompB>.ID;

            var registered = ComponentTypeRegistry.GetAllRegistered();

            Assert.Contains(typeof(AuditCompA), registered);
            Assert.Contains(typeof(AuditCompB), registered);
        }

        [Fact]
        public void GetAllRegistered_EventTypes_ContainsRegisteredTypes()
        {
            // Trigger registration via EventType<T>.Id (static readonly, registered once per process)
            _ = EventType<AuditEventA>.Id;
            _ = EventType<AuditEventB>.Id;

            var registered = EventType.GetAllRegistered();

            Assert.Contains(typeof(AuditEventA), registered);
            Assert.Contains(typeof(AuditEventB), registered);
        }

        [Fact]
        public void HasComponentByTypeId_ReturnsTrue_WhenComponentPresent()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<AuditCompA>();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new AuditCompA { Value = 42 });
            int typeId = ComponentType<AuditCompA>.ID;

            bool result = repo.HasComponentByTypeId(entity, typeId);

            Assert.True(result);
        }

        [Fact]
        public void HasComponentByTypeId_ReturnsFalse_WhenComponentAbsent()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<AuditCompA>();
            repo.RegisterComponent<AuditCompB>();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new AuditCompA { Value = 1 });
            int typeId = ComponentType<AuditCompB>.ID;

            bool result = repo.HasComponentByTypeId(entity, typeId);

            Assert.False(result);
        }
    }
}
