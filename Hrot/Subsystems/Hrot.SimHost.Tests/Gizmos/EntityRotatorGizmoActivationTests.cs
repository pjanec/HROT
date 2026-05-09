using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Lifecycle.Events;
using Hrot.SimHost.Gizmos;
using Xunit;

namespace Hrot.SimHost.Tests.Gizmos
{
    // SC-ER001 through SC-ER006: EntityRotatorGizmo activation and input-bridge tests.
    // Verifies BATCH-24 ECS-driven rotation workflow.

    // =========================================================================
    // SC_ER001 / SC_ER002: marker component and definition contract
    // =========================================================================

    public sealed class EntityRotatorGizmoMarkerTests
    {
        // SC_ER001: ActiveRotationToolRequest is a pure marker struct with no instance fields.
        // Empty C# structs have a managed size of 1 byte (CLR minimum); the important
        // guarantee is that the struct carries no payload fields.
        [Fact]
        public void SC_ER001_ActiveRotationToolRequest_IsMarkerWithNoFields()
        {
            Assert.Equal(1, Unsafe.SizeOf<ActiveRotationToolRequest>());
            Assert.Empty(typeof(ActiveRotationToolRequest).GetFields());
        }

        // SC_ER002: EntityRotatorGizmoDefinition declares exactly SimTransform
        // and ActiveRotationToolRequest as required components.
        [Fact]
        public void SC_ER002_EntityRotatorGizmoDefinition_RequiredComponents_ContainsBothTypes()
        {
            // Component types must be registered before GizmoRegistry.Register() to
            // populate the ComponentTypeRegistry with valid IDs.
            using var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<ActiveRotationToolRequest>();

            var def = new EntityRotatorGizmoDefinition();

            Assert.Equal(2, def.RequiredComponents.Length);
            Assert.Contains(typeof(SimTransform),               def.RequiredComponents);
            Assert.Contains(typeof(ActiveRotationToolRequest),  def.RequiredComponents);
        }
    }

    // =========================================================================
    // SC_ER003 / SC_ER004: DataDrivenGizmoSystem late-activation and teardown
    // =========================================================================

    public sealed class EntityRotatorGizmoSystemTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly DebugPrimitiveBuffer _buffer;
        private readonly DataDrivenGizmoSystem _sys;
        private readonly Entity _entity;

        public EntityRotatorGizmoSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<SimTransform>();
            _repo.RegisterComponent<ActiveRotationToolRequest>();
            _repo.RegisterEvent<ConstructionOrder>();
            _repo.RegisterEvent<DestructionOrder>();
            _repo.RegisterEvent<GizmoComponentActivatedEvent>();

            var registry = new GizmoRegistry();
            registry.Register(new EntityRotatorGizmoDefinition());

            _buffer = new DebugPrimitiveBuffer();
            // Null predicate: all active gizmos are always drawn.
            _sys = new DataDrivenGizmoSystem(registry, _buffer, isSelectedPredicate: null);

            // Create an entity with both required components already present.
            _entity = _repo.CreateEntity();
            _repo.AddComponent<SimTransform>(_entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity,
            });
            _repo.AddComponent<ActiveRotationToolRequest>(_entity, default);
        }

        public void Dispose() => _repo.Dispose();

        // SC_ER003: Publishing GizmoComponentActivatedEvent causes the system to
        // construct an EntityRotatorGizmo and call UpdateAndDraw on the same frame.
        [Fact]
        public void SC_ER003_GizmoComponentActivatedEvent_ActivatesGizmoAndDraws()
        {
            _repo.Bus.Publish(new GizmoComponentActivatedEvent { Entity = _entity });
            _repo.Bus.SwapBuffers();
            _sys.Execute(_repo, 0f);

            // UpdateAndDraw emits a DrawArrow primitive into the buffer.
            // A non-empty frame confirms the gizmo was created and drawn.
            Assert.True(_buffer.GetFrame().Length > 0,
                "Expected at least one draw primitive after gizmo activation.");
        }

        // SC_ER004: Removing ActiveRotationToolRequest causes the system to tear down
        // the gizmo on the next Execute so no further primitives are emitted.
        [Fact]
        public void SC_ER004_RemovingMarkerComponent_TearsDownGizmo()
        {
            // Frame 1: activate gizmo.
            _repo.Bus.Publish(new GizmoComponentActivatedEvent { Entity = _entity });
            _repo.Bus.SwapBuffers();
            _sys.Execute(_repo, 0f);
            Assert.True(_buffer.GetFrame().Length > 0,
                "Pre-condition: gizmo must be active after frame 1.");

            // Frame 2: remove the marker component, then execute.
            // The 1b teardown scan will find the mask is no longer satisfied and dispose
            // the gizmo.  No draw primitives should appear in the buffer.
            _repo.RemoveComponent<ActiveRotationToolRequest>(_entity);
            _buffer.Clear();
            _repo.Bus.SwapBuffers();
            _sys.Execute(_repo, 0f);

            Assert.Equal(0, _buffer.GetFrame().Length);
        }
    }
}
