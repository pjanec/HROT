using System;
using Xunit;
using ModuleHost.Network.Cyclone.Providers;
using Fdp.Kernel; 
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Lifecycle;
using MessagePack;

namespace ModuleHost.Network.Cyclone.Tests.Serialization
{
    [Serializable]
    [MessagePackObject]
    public class TestChatMsg 
    {
        [Key(0)]
        public string Text { get; set; } = "";
    }

    public class ManagedSerializationProviderTests
    {
        [Fact]
        public void CanSerializeAndDeserialize_ManagedObject()
        {
            var provider = new ManagedSerializationProvider<TestChatMsg>();
            
            var msg = new TestChatMsg { Text = "Hello Serialization" };
            
            // 1. Get Size
            int size = provider.GetSize(msg);
            Assert.True(size > 0);
            
            // 2. Encode
            var buffer = new byte[size];
            provider.Encode(msg, buffer);
            
            // Verify buffer is not empty
            bool notEmpty = false;
            foreach(var b in buffer) if(b != 0) notEmpty = true;
            Assert.True(notEmpty);
            
            // 3. Apply
            var mockCmd = new MockCommandBuffer();
            var entity = new Entity(); 
            
            provider.Apply(entity, buffer, mockCmd);
            
            Assert.NotNull(mockCmd.CapturedComponent);
            Assert.Equal("Hello Serialization", ((TestChatMsg)mockCmd.CapturedComponent).Text);
        }
        
        class MockCommandBuffer : IEntityCommandBuffer
        {
            public object CapturedComponent;
            
            public void SetManagedComponent<T>(Entity entity, T? component) where T : class
            {
                CapturedComponent = component;
            }

            public unsafe void SetComponentRaw(Entity entity, int typeId, void* ptr, int size) => throw new NotImplementedException();
            public void SetManagedComponentRaw(Entity entity, int typeId, object obj) => throw new NotImplementedException();
            
            public Entity CreateEntity() => throw new NotImplementedException();
            public void DestroyEntity(Entity entity) {}
            public void AddComponent<T>(Entity entity, in T component) where T : unmanaged {}
            public void SetComponent<T>(Entity entity, in T component) where T : unmanaged {}
            public void RemoveComponent<T>(Entity entity) where T : unmanaged {}
            public void AddManagedComponent<T>(Entity entity, T? component) where T : class => CapturedComponent = component;
            public void RemoveManagedComponent<T>(Entity entity) where T : class {}
            public void PublishEvent<T>(in T evt) where T : unmanaged {}
            public void SetLifecycleState(Entity entity, Fdp.Kernel.EntityLifecycle state) {}
        }
    }
}
