using Xunit;
using Moq;
using ModuleHost.Core.Abstractions;
using Fdp.Modules.Geographic.Systems;
using Fdp.Modules.Geographic;

namespace Fdp.Modules.Geographic.Tests
{
    public class GeographicModuleTests
    {
        [Fact]
        public void RegisterSystems_RegistersExpectedSystems()
        {
            // Arrange
            var mockGeo = new Mock<IGeographicTransform>();
            var module = new GeographicModule(mockGeo.Object);
            var mockRegistry = new Mock<ISystemRegistry>();
            
            // Act
            module.RegisterSystems(mockRegistry.Object);
            
            // Assert
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<GeodeticSmoothingSystem>()), Times.Once);
            mockRegistry.Verify(r => r.RegisterSystem(It.IsAny<Fdp.Modules.Geographic.Systems.CoordinateTransformSystem>()), Times.Once);
        }
    }
}
