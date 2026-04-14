using System;
using System.Numerics;
using Xunit;
using Moq;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Systems;
using Fdp.Modules.Geographic.Components;

using PositionGeodetic = Fdp.Modules.Geographic.Components.PositionGeodetic;

namespace Fdp.Modules.Geographic.Tests.Systems
{
    /// <summary>
    /// Tests for <see cref="GeodeticSmoothingSystem"/>.
    /// After MOD1-P1T3, the system uses <c>.WithoutOwned&lt;Position&gt;()</c> to select
    /// ghost (remote-owned) entities rather than checking <c>NetworkOwnership</c> manually.
    /// </summary>
    public class GeodeticSmoothingSystemTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly Mock<IGeographicTransform> _mockGeo;
        private readonly GeodeticSmoothingSystem _system;
        
        public GeodeticSmoothingSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<Position>();
            _repo.RegisterComponent<PositionGeodetic>();
            
            _mockGeo = new Mock<IGeographicTransform>();
            _system = new GeodeticSmoothingSystem(_mockGeo.Object);
        }
        
        public void Dispose()
        {
            _repo.Dispose();
        }

        // ── Existing behaviour tests (updated for authority-mask API) ─────────────────────────

        /// <summary>
        /// A ghost (non-locally-owned) entity is processed — its position is interpolated.
        /// Previously relied on NetworkOwnership; now uses entity authority mask.
        /// </summary>
        [Fact]
        public void Execute_RemoteEntity_InterpolatesPosition()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new Position { Value = Vector3.Zero });
            _repo.AddComponent(entity, new PositionGeodetic { Latitude = 10, Longitude = 10, Altitude = 100 });

            // Ghost: not locally owned → system processes this entity.
            _repo.SetAuthority<Position>(entity, false);
            
            _mockGeo.Setup(g => g.ToCartesian(10, 10, 100))
                .Returns(new Vector3(10, 0, 0));
                
            _system.Execute(_repo, 0.05f);  // t = clamp(0.05*10, 0, 1) = 0.5 → Lerp(0, 10, 0.5) = 5
            
            var pos = _repo.GetComponentRO<Position>(entity);
            Assert.Equal(5.0f, pos.Value.X, precision: 2);
        }

        /// <summary>
        /// A locally-owned entity is skipped by <see cref="GeodeticSmoothingSystem"/>.
        /// Previously relied on NetworkOwnership; now uses entity authority mask.
        /// </summary>
        [Fact]
        public void Execute_LocalEntity_Ignored()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new Position { Value = Vector3.Zero });
            _repo.AddComponent(entity, new PositionGeodetic { Latitude = 10, Longitude = 10, Altitude = 100 });
            
            // Locally owned → system skips this entity.
            _repo.SetAuthority<Position>(entity, true);
            
            _mockGeo.Setup(g => g.ToCartesian(
                    It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>()))
                .Returns(new Vector3(10, 0, 0));
                
            _system.Execute(_repo, 0.05f);
            
            var pos = _repo.GetComponentRO<Position>(entity);
            Assert.Equal(0.0f, pos.Value.X);    // unchanged
        }

        // ── New authority-guard tests (MOD1-P1T3) ────────────────────────────────────────────

        /// <summary>
        /// MOD1-P1T3 T4: <see cref="GeodeticSmoothingSystem"/> must process only ghost
        /// (non-locally-owned) entities when owned and ghost entities coexist.
        /// </summary>
        [Fact]
        public void GeodeticSmoothingSystem_ProcessesOnlyGhostEntities()
        {
            // Owned entity — should NOT be processed.
            var ownedEntity = _repo.CreateEntity();
            _repo.AddComponent(ownedEntity, new Position { Value = Vector3.Zero });
            _repo.AddComponent(ownedEntity, new PositionGeodetic { Latitude = 20, Longitude = 20, Altitude = 0 });
            _repo.SetAuthority<Position>(ownedEntity, true);

            // Ghost entity — should be processed.
            var ghostEntity = _repo.CreateEntity();
            _repo.AddComponent(ghostEntity, new Position { Value = Vector3.Zero });
            _repo.AddComponent(ghostEntity, new PositionGeodetic { Latitude = 30, Longitude = 30, Altitude = 0 });
            _repo.SetAuthority<Position>(ghostEntity, false);

            var targetPos = new Vector3(50f, 0f, 0f);
            _mockGeo.Setup(g => g.ToCartesian(30, 30, 0)).Returns(targetPos);
            _mockGeo.Setup(g => g.ToCartesian(20, 20, 0)).Returns(new Vector3(99, 0, 0));

            _system.Execute(_repo, 1.0f);   // t = 1.0 → full snap to target

            var ghostPos = _repo.GetComponentRO<Position>(ghostEntity);
            var ownedPos = _repo.GetComponentRO<Position>(ownedEntity);

            Assert.Equal(50f, ghostPos.Value.X, precision: 2);  // ghost was processed
            Assert.Equal(0f,  ownedPos.Value.X, precision: 2);  // owned was NOT processed
        }
    }
}

