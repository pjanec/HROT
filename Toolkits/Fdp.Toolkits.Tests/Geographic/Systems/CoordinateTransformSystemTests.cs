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
    /// Tests for <see cref="CoordinateTransformSystem"/>.
    /// After MOD1-P1T3, the system uses <c>.WithOwned&lt;Position&gt;()</c> to select
    /// locally-owned entities rather than checking <c>NetworkOwnership</c> manually.
    /// </summary>
    public class CoordinateTransformSystemTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly Mock<IGeographicTransform> _mockGeo;
        private readonly CoordinateTransformSystem _system;

        public CoordinateTransformSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<Position>();
            _repo.RegisterComponent<PositionGeodetic>();

            _mockGeo = new Mock<IGeographicTransform>();
            _system = new CoordinateTransformSystem(_mockGeo.Object);
        }

        public void Dispose()
        {
            _repo.Dispose();
        }

        // ── MOD1-P1T3 T3: CoordinateTransformSystem_SkipsGhostEntities ───────────────────────

        /// <summary>
        /// MOD1-P1T3 T3: <see cref="CoordinateTransformSystem"/> must query and process
        /// only locally-owned entities.  Ghost (non-locally-owned) entities must be
        /// completely skipped — verified by checking that the geo-transform mock is only
        /// called for the owned entity's position.
        /// </summary>
        [Fact]
        public void CoordinateTransformSystem_SkipsGhostEntities()
        {
            // ── Owned entity ───────────────────────────────────────────────────────────────────
            var ownedEntity = _repo.CreateEntity();
            var ownedPosition = new Vector3(100f, 200f, 10f);
            _repo.AddComponent(ownedEntity, new Position { Value = ownedPosition });
            _repo.AddComponent(ownedEntity, new PositionGeodetic { Latitude = 0, Longitude = 0, Altitude = 0 });
            _repo.SetAuthority<Position>(ownedEntity, true);   // locally owned

            // ── Ghost entity ───────────────────────────────────────────────────────────────────
            var ghostEntity = _repo.CreateEntity();
            var ghostPosition = new Vector3(500f, 600f, 20f);
            _repo.AddComponent(ghostEntity, new Position { Value = ghostPosition });
            _repo.AddComponent(ghostEntity, new PositionGeodetic { Latitude = 0, Longitude = 0, Altitude = 0 });
            _repo.SetAuthority<Position>(ghostEntity, false);  // NOT locally owned (ghost)

            // ── Stubs ──────────────────────────────────────────────────────────────────────────
            _mockGeo.Setup(g => g.ToGeodetic(ownedPosition)).Returns((32.0, 34.0, 10.0));
            _mockGeo.Setup(g => g.ToGeodetic(ghostPosition)).Returns((99.0, 99.0, 99.0));

            _system.Execute(_repo, 0.016f);

            // ── Assert: geo transform was called for owned but NOT for ghost ──────────────────
            // This directly verifies the authority-based query filter (.WithOwned<Position>()).
            _mockGeo.Verify(g => g.ToGeodetic(ownedPosition), Times.AtLeastOnce(),
                "System should process locally-owned entities.");
            _mockGeo.Verify(g => g.ToGeodetic(ghostPosition), Times.Never(),
                "System must NOT process ghost (non-locally-owned) entities.");
        }

        // ── Basic: owned entity is processed ──────────────────────────────────────────────────

        /// <summary>
        /// A locally-owned entity has its position transformed to geodetic.
        /// </summary>
        [Fact]
        public void CoordinateTransformSystem_ProcessesOwnedEntity()
        {
            var entity = _repo.CreateEntity();
            var pos = new Vector3(200f, 300f, 5f);
            _repo.AddComponent(entity, new Position { Value = pos });
            _repo.AddComponent(entity, new PositionGeodetic { Latitude = 0, Longitude = 0, Altitude = 0 });
            _repo.SetAuthority<Position>(entity, true);

            _mockGeo.Setup(g => g.ToGeodetic(pos)).Returns((31.5, 33.5, 5.0));

            _system.Execute(_repo, 0.016f);

            _mockGeo.Verify(g => g.ToGeodetic(pos), Times.AtLeastOnce());
        }
    }
}

