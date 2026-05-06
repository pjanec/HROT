using System;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Perception.Components;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.IG.Gizmos;
using Xunit;

namespace Hrot.IG.Tests.Gizmos
{
    // ============================================================================
    // SC-GZ021-VIS: Visibility cone gizmo unit tests.
    // ============================================================================

    public sealed class VisibilityConeGizmoTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public VisibilityConeGizmoTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<SimTransform>();
            _repo.RegisterComponent<PerceptionReceptor>();
        }

        public void Dispose() => _repo.Dispose();

        [Fact]
        public void SC_GZ021_VIS_1_RequiredComponents_ContainsBothTypes()
        {
            var attr = typeof(VisibilityConeGizmo).GetCustomAttribute<GizmoProjectorAttribute>();

            Assert.NotNull(attr);
            Assert.Contains(typeof(SimTransform),       attr!.RequiredComponents);
            Assert.Contains(typeof(PerceptionReceptor), attr.RequiredComponents);
        }

        [Fact]
        public void SC_GZ021_VIS_2_Draw_EmitsAtLeastTwoDrawLineCalls_WhenVisionRangePositive()
        {
            var gizmo = new VisibilityConeGizmo();
            var draw  = new FullCapturingDrawBuilder();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity
            });
            // 60 deg FOV -> half=30 deg -> FieldOfViewCos = cos(PI/6)
            _repo.AddComponent(entity, new PerceptionReceptor
            {
                VisionRange    = 100f,
                FieldOfViewCos = MathF.Cos(MathF.PI / 6f),
                HearingRange   = 50f
            });

            gizmo.Draw(_repo, entity, draw);

            Assert.True(draw.LineCalls.Count >= 2,
                $"Expected at least 2 DrawLine calls, got {draw.LineCalls.Count}.");
        }

        [Fact]
        public void SC_GZ021_VIS_3_Draw_EmitsNoDrawCalls_WhenVisionRangeZero()
        {
            var gizmo = new VisibilityConeGizmo();
            var draw  = new FullCapturingDrawBuilder();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity
            });
            _repo.AddComponent(entity, new PerceptionReceptor
            {
                VisionRange    = 0f,
                FieldOfViewCos = 0.866f,
                HearingRange   = 50f
            });

            gizmo.Draw(_repo, entity, draw);

            Assert.Empty(draw.LineCalls);
            Assert.Empty(draw.ArrowCalls);
            Assert.Empty(draw.SphereCalls);
        }
    }
}
