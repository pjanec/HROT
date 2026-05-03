using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Core;
using Fdp.Core.Internal;
using System.Runtime.InteropServices;

namespace Fdp.Core.Tests
{
    // Mock Components for lifecycle tests
    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    [ComponentId(175)]
    public struct RigidBody { public float Mass; }

    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    [ComponentId(176)]
    public struct NetIdentity { public int NetworkId; }

    public unsafe class EntityLifecycleTests : IDisposable
    {
        private EntityRepository _repo;
        private NativeEventStream<EntityLifecycleEvent> _lifecycleStream;

        public EntityLifecycleTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<RigidBody>();
            _repo.RegisterComponent<NetIdentity>();

            _lifecycleStream = new NativeEventStream<EntityLifecycleEvent>(1024);
            _repo.RegisterLifecycleStream(_lifecycleStream);
        }

        public void Dispose()
        {
            _repo.Dispose();
            _lifecycleStream.Dispose();
        }

        [Fact]
        public void CreateEntity_EmitsEvent()
        {
            var e = _repo.CreateEntity();
            
            // Verify Event
            _lifecycleStream.Swap(); // Move to read buffer
            var events = _lifecycleStream.Read();
            
            Assert.Single(events.ToArray());
            Assert.Equal(LifecycleEventType.Created, events[0].Type);
            Assert.Equal(e, events[0].Entity);
        }
    }
}
