using System;
using System.Reflection;
using Fdp.Core;
using Hrot.Common.Serializers;
using Hrot.Map.Definitions;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the 5 Intent DTO managed components defined in
    /// <see cref="GenesisIntentComponents"/> — TASK-S401.
    /// </summary>
    public sealed class GenesisIntentComponentsTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public GenesisIntentComponentsTests()
        {
            _repo = new EntityRepository();
        }

        public void Dispose() => _repo.Dispose();

        // ── InitialPassengersIntent ────────────────────────────────────────────

        [Fact]
        public void InitialPassengersIntent_RegisterManagedComponent_DoesNotThrow()
        {
            var ex = Record.Exception(() => _repo.RegisterManagedComponent<InitialPassengersIntent>());
            Assert.Null(ex);
        }

        [Fact]
        public void InitialPassengersIntent_ComponentTypeRegistry_ReturnsCorrectType()
        {
            _repo.RegisterManagedComponent<InitialPassengersIntent>();
            var type = ComponentTypeRegistry.GetType(HrotComponentIds.InitialPassengersIntent);
            Assert.Equal(typeof(InitialPassengersIntent), type);
        }

        [Fact]
        public void InitialPassengersIntent_HasTransientDataPolicy()
        {
            var attr = typeof(InitialPassengersIntent).GetCustomAttribute<DataPolicyAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(DataPolicy.Transient, attr.Policy);
        }

        // ── InitialVehicleIntent ───────────────────────────────────────────────

        [Fact]
        public void InitialVehicleIntent_RegisterManagedComponent_DoesNotThrow()
        {
            var ex = Record.Exception(() => _repo.RegisterManagedComponent<InitialVehicleIntent>());
            Assert.Null(ex);
        }

        [Fact]
        public void InitialVehicleIntent_ComponentTypeRegistry_ReturnsCorrectType()
        {
            _repo.RegisterManagedComponent<InitialVehicleIntent>();
            var type = ComponentTypeRegistry.GetType(HrotComponentIds.InitialVehicleIntent);
            Assert.Equal(typeof(InitialVehicleIntent), type);
        }

        [Fact]
        public void InitialVehicleIntent_HasTransientDataPolicy()
        {
            var attr = typeof(InitialVehicleIntent).GetCustomAttribute<DataPolicyAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(DataPolicy.Transient, attr.Policy);
        }

        // ── InitialHierarchyIntent ─────────────────────────────────────────────

        [Fact]
        public void InitialHierarchyIntent_RegisterManagedComponent_DoesNotThrow()
        {
            var ex = Record.Exception(() => _repo.RegisterManagedComponent<InitialHierarchyIntent>());
            Assert.Null(ex);
        }

        [Fact]
        public void InitialHierarchyIntent_ComponentTypeRegistry_ReturnsCorrectType()
        {
            _repo.RegisterManagedComponent<InitialHierarchyIntent>();
            var type = ComponentTypeRegistry.GetType(HrotComponentIds.InitialHierarchyIntent);
            Assert.Equal(typeof(InitialHierarchyIntent), type);
        }

        [Fact]
        public void InitialHierarchyIntent_HasTransientDataPolicy()
        {
            var attr = typeof(InitialHierarchyIntent).GetCustomAttribute<DataPolicyAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(DataPolicy.Transient, attr.Policy);
        }

        // ── InitialRouteIntent ─────────────────────────────────────────────────

        [Fact]
        public void InitialRouteIntent_RegisterManagedComponent_DoesNotThrow()
        {
            var ex = Record.Exception(() => _repo.RegisterManagedComponent<InitialRouteIntent>());
            Assert.Null(ex);
        }

        [Fact]
        public void InitialRouteIntent_ComponentTypeRegistry_ReturnsCorrectType()
        {
            _repo.RegisterManagedComponent<InitialRouteIntent>();
            var type = ComponentTypeRegistry.GetType(HrotComponentIds.InitialRouteIntent);
            Assert.Equal(typeof(InitialRouteIntent), type);
        }

        [Fact]
        public void InitialRouteIntent_HasTransientDataPolicy()
        {
            var attr = typeof(InitialRouteIntent).GetCustomAttribute<DataPolicyAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(DataPolicy.Transient, attr.Policy);
        }

        // ── InitialTargetsIntent ───────────────────────────────────────────────

        [Fact]
        public void InitialTargetsIntent_RegisterManagedComponent_DoesNotThrow()
        {
            var ex = Record.Exception(() => _repo.RegisterManagedComponent<InitialTargetsIntent>());
            Assert.Null(ex);
        }

        [Fact]
        public void InitialTargetsIntent_ComponentTypeRegistry_ReturnsCorrectType()
        {
            _repo.RegisterManagedComponent<InitialTargetsIntent>();
            var type = ComponentTypeRegistry.GetType(HrotComponentIds.InitialTargetsIntent);
            Assert.Equal(typeof(InitialTargetsIntent), type);
        }

        [Fact]
        public void InitialTargetsIntent_HasTransientDataPolicy()
        {
            var attr = typeof(InitialTargetsIntent).GetCustomAttribute<DataPolicyAttribute>();
            Assert.NotNull(attr);
            Assert.Equal(DataPolicy.Transient, attr.Policy);
        }
    }
}
