using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Fdp.Examples.CarKinem.Visualization;
using Fdp.Examples.CarKinem.Core;
using CarKinem.Core; 
using CarKinem.Formation;
using Fdp.Examples.CarKinem.Components;
using Raylib_cs;

using ExamplePresets = Fdp.Examples.CarKinem.Core.ExampleVehiclePresets; 

namespace Fdp.Examples.CarKinem.Tests
{
    public class VehicleVisualizerTests
    {
        [Fact]
        public void VehicleVisualizer_GetPosition_ReturnsVehicleStatePosition()
        {
            // Arrange
            var visualizer = new VehicleVisualizer();
            var view = new FakeSimulationView();
            var entity = new Entity(1, 1);
            
            var expectedPos = new Vector2(100, 200);
            view.AddComponent(entity, new SimTransform { Position = new Vector3(expectedPos.X, expectedPos.Y, 0) });
            view.AddComponent(entity, new VehicleState());
            
            // Act
            var pos = visualizer.GetPosition(view, entity);
            
            // Assert
            Assert.NotNull(pos);
            Assert.Equal(expectedPos, pos.Value);
        }

        [Fact]
        public void VehicleVisualizer_GetColorForClass_ReturnsCorrectPresets()
        {
            // Testing VehiclePresets used by Visualizer
            
            // Arrange
            var view = new FakeSimulationView();
            var entity = new Entity(1, 1);
            var paramsCar = new VehicleParams { Class = VehicleClass.PersonalCar };
            var paramsTruck = new VehicleParams { Class = VehicleClass.Truck };
            
            // Act & Assert - PersonalCar (Red-ish)
            var colorCar = ExamplePresets.GetColorForEntity(view, entity, paramsCar);
            Assert.Equal(200, colorCar.R);
            Assert.Equal(100, colorCar.G);
            Assert.Equal(100, colorCar.B);
            
            // Act & Assert - Truck (Blue-ish)
            var colorTruck = ExamplePresets.GetColorForEntity(view, entity, paramsTruck);
            Assert.Equal(100, colorTruck.R);
            Assert.Equal(150, colorTruck.G);
            Assert.Equal(200, colorTruck.B);
        }
        
        [Fact]
        public void VehicleVisualizer_GetColor_FormationMember_ReturnsCyan()
        {
            // Arrange
            var view = new FakeSimulationView();
            var entity = new Entity(1, 1);
            var vParams = new VehicleParams { Class = VehicleClass.PersonalCar };
            
            // Add FormationMember component
            view.AddComponent(entity, new FormationMember());
            
            // Act
            var color = ExamplePresets.GetColorForEntity(view, entity, vParams);
            
            // Assert
            Assert.Equal(ExamplePresets.ColorFormationMember, color);
        }
    }

    // Minimal Fake Simulation View for Unit Testing
    public class FakeSimulationView : ISimulationView
    {
        private Dictionary<Entity, Dictionary<Type, object>> _entityComponents = new Dictionary<Entity, Dictionary<Type, object>>();

        public void AddComponent<T>(Entity e, T component) where T : struct
        {
            if (!_entityComponents.ContainsKey(e)) _entityComponents[e] = new Dictionary<Type, object>();
            _entityComponents[e][typeof(T)] = component;
        }

        public uint Tick => 0;
        public float Time => 0;

        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
        {
            if (_entityComponents.TryGetValue(e, out var comps) && comps.TryGetValue(typeof(T), out var obj))
            {
                 _tempStorage<T>.Value = (T)obj;
                 return ref _tempStorage<T>.Value;
            }
            throw new Exception($"Component {typeof(T)} not found on entity {e}");
        }

        public T GetManagedComponentRO<T>(Entity e) where T : class
        {
             throw new NotImplementedException();
        }

        public bool IsAlive(Entity e) => true;

        public bool HasComponent<T>(Entity e) where T : unmanaged
        {
             return _entityComponents.ContainsKey(e) && _entityComponents[e].ContainsKey(typeof(T));
        }

        public bool HasManagedComponent<T>(Entity e) where T : class => false;

        public ReadOnlySpan<T> ConsumeEvents<T>() where T : unmanaged => ReadOnlySpan<T>.Empty;
        
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
        
        public QueryBuilder Query() => throw new NotImplementedException();
        
        public System.Collections.Generic.IReadOnlyList<T> ConsumeManagedEvents<T>() => throw new NotImplementedException();
        
        // Static holder for ref return trick (unsafe for concurrency but ok for single thread unit test)
        private static class _tempStorage<T> where T : struct { public static T Value; }
    }
}
