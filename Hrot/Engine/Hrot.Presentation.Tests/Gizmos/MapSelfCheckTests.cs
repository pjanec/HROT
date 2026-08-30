using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Replication.Components;
using Hrot.ScenarioEditor.Map;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-23</c> <c>S3</c> — the rails for declare-and-report.</b>
    /// 📄 Design: <c>docs/UX/UX_Feature_Map_Parity.md</c> ⭐ **§3.2e**.
    ///
    /// <para>🔴 <b>The headline rail is <see cref="TheSelfCheck_ReportsTheExactCE123Shape"/>.</b> It builds
    /// the map exactly as SimHost had it — every system present, scheduled and enabled, with a selection
    /// predicate reaching the stateless system — and asserts the map now SAYS SO. That configuration went
    /// unreported for weeks.</para>
    /// </summary>
    public sealed class MapSelfCheckTests : IDisposable
    {
        private readonly EntityRepository _world;

        public MapSelfCheckTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<NetworkIdentity>();
        }

        public void Dispose() => _world.Dispose();

        private Entity Spawn(long id)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = new Vector3(10f, 20f, 0f) });
            _world.AddComponent(e, new NetworkIdentity(id));
            return e;
        }

        private static void RunFrames(MapInteraction mi, EntityRepository world, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                mi.Buffer.Clear();
                mi.StatelessSystem.Execute(world, 0.016f);
                mi.SelfCheck.Execute(world, 0.016f);
            }
        }

        // ── ① the half that matters: running and silent ────────────────────────────────────────

        /// <summary>
        /// 🔴🔴🔴 <b>The <c>CE-123</c> configuration, reproduced — and now it reports.</b>
        ///
        /// <para>Everything present, scheduled and enabled; one selection predicate reaching
        /// <c>StatelessGizmoSystem</c>, where it gates every projector at once. That is precisely what
        /// SimHost looked like: 8 entities, zero entity shapes, and nothing said.</para>
        /// </summary>
        [Fact]
        public void TheSelfCheck_ReportsTheExactCE123Shape()
        {
            for (int i = 0; i < 8; i++) Spawn(100 + i);

            var reports = new List<string>();

            // ⚠ Built by hand rather than through the pack ON PURPOSE: the pack no longer ALLOWS this
            // configuration (MapInteractionContext has no stateless predicate). This rail reconstructs the
            // defect the pack now prevents, so the diagnostic is proven against the real shape.
            var buffer   = new DebugPrimitiveBuffer();
            var registry = new StatelessGizmoRegistry();
            var settings = new Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingsRegistry();
            GizmoReflectionRegistrar.RegisterAll(new GizmoRegistry(), registry, settings);

            var gated = new StatelessGizmoSystem(registry, buffer, isSelectedPredicate: (_, _) => false);
            var selfCheck = new MapSelfCheckSystem(buffer, () => true, reports.Add);

            for (int i = 0; i < MapSelfCheckSystem.GraceFrames + 2; i++)
            {
                buffer.Clear();
                gated.Execute(_world, 0.016f);
                selfCheck.Execute(_world, 0.016f);
            }

            Assert.NotEmpty(reports);
            string message = reports[0];
            Assert.Contains("RUNNING AND DRAWING NOTHING", message, StringComparison.Ordinal);
            Assert.Contains("8 entities", message, StringComparison.Ordinal);
            Assert.Contains("CE-123", message, StringComparison.Ordinal);
        }

        /// <summary>⭐ A healthy map says nothing at all — the diagnostic must not cry wolf.</summary>
        [Fact]
        public void TheSelfCheck_IsSilentWhenTheMapIsDrawing()
        {
            for (int i = 0; i < 4; i++) Spawn(200 + i);

            var reports = new List<string>();
            var mi = MapInteractionPack.Build(new MapInteractionContext
            {
                World = _world,
                StartEnabled = true,
                ReportMapDiagnostic = reports.Add,
            });

            RunFrames(mi, _world, MapSelfCheckSystem.GraceFrames + 5);

            Assert.Empty(reports);
        }

        /// <summary>
        /// ⭐⭐ <b>A DISABLED group is legitimately silent</b> — <c>GZH-003</c> headless-first. A headless
        /// node with no viewer must not be reported as broken, or the diagnostic is noise on every cluster.
        /// </summary>
        [Fact]
        public void TheSelfCheck_SaysNothingWhileTheGateIsShut()
        {
            for (int i = 0; i < 8; i++) Spawn(300 + i);

            var reports = new List<string>();
            var mi = MapInteractionPack.Build(new MapInteractionContext
            {
                World = _world,
                StartEnabled = false,          // headless: nothing attached
                ReportMapDiagnostic = reports.Add,
            });

            // The group is off, so in production the systems would not run at all. Drive the self-check
            // directly to prove it stays quiet even when it IS ticked.
            for (int i = 0; i < MapSelfCheckSystem.GraceFrames + 5; i++)
                mi.SelfCheck.Execute(_world, 0.016f);

            Assert.Empty(reports);
        }

        /// <summary>⭐ An empty world is not a broken map.</summary>
        [Fact]
        public void TheSelfCheck_SaysNothingWhenThereAreNoEntities()
        {
            var reports = new List<string>();
            var mi = MapInteractionPack.Build(new MapInteractionContext
            {
                World = _world,
                StartEnabled = true,
                ReportMapDiagnostic = reports.Add,
            });

            RunFrames(mi, _world, MapSelfCheckSystem.GraceFrames + 5);

            Assert.Empty(reports);
        }

        /// <summary>
        /// ⭐⭐ <b>It latches, and it reports RECOVERY.</b> A message every frame is a message nobody reads;
        /// a message that never clears leaves the operator unsure whether the fix worked.
        /// </summary>
        [Fact]
        public void TheSelfCheck_ReportsOnceAndThenReportsRecovery()
        {
            var entity = Spawn(400L);

            var reports = new List<string>();
            var buffer = new DebugPrimitiveBuffer();
            var selfCheck = new MapSelfCheckSystem(buffer, () => true, reports.Add);

            // Broken: eligible entity, no shapes. Runs well past the grace window.
            for (int i = 0; i < MapSelfCheckSystem.GraceFrames * 3; i++)
            {
                buffer.Clear();
                selfCheck.Execute(_world, 0.016f);
            }
            Assert.Single(reports);
            Assert.Contains("RUNNING AND DRAWING NOTHING", reports[0], StringComparison.Ordinal);

            // Recovered: something draws an entity shape again.
            for (int i = 0; i < 3; i++)
            {
                buffer.Clear();
                Hrot.ScenarioEditor.Gizmos.EntityPresentationGizmoShared.DrawSemanticShape(
                    buffer, entity, 400L, 0UL, 5f, 2f, conditionMask: 0u);
                selfCheck.Execute(_world, 0.016f);
            }

            Assert.Equal(2, reports.Count);
            Assert.Contains("drawing entity shapes again", reports[1], StringComparison.Ordinal);
        }

        // ── ② the declare half — narrower, and honestly scoped ─────────────────────────────────

        /// <summary>⭐ A host that scheduled the group services everything the map declares.</summary>
        [Fact]
        public void TheDeclareHalf_ReportsNothingWhenTheHostSchedulesTheGroup()
        {
            var mi = MapInteractionPack.Build(new MapInteractionContext { World = _world });

            var missing = mi.Unserviceable(mi.GizmoGroup.GetSystems());

            Assert.Empty(missing);
        }

        /// <summary>
        /// ⭐⭐ A host that schedules NOTHING gets one named message per required system, each with a
        /// reason — never a silent no-op.
        /// </summary>
        [Fact]
        public void TheDeclareHalf_NamesEverySystemTheHostDidNotSchedule()
        {
            var mi = MapInteractionPack.Build(new MapInteractionContext { World = _world });

            var missing = mi.Unserviceable(Array.Empty<object>());

            Assert.Equal(mi.RequiredSystems.Count, missing.Count);
            Assert.Contains(missing, m => m.Contains("StatelessGizmoSystem", StringComparison.Ordinal)
                                       && m.Contains("no entity shapes", StringComparison.Ordinal));
            Assert.Contains(missing, m => m.Contains("DataDrivenGizmoSystem", StringComparison.Ordinal));
            Assert.Contains(missing, m => m.Contains("GlobalGizmoManager", StringComparison.Ordinal));
        }

        /// <summary>
        /// 🔴🔴 <b>The honesty rail (§3.2e).</b> The declare half reports NOTHING on the <c>CE-123</c>
        /// configuration — every system present and scheduled, map blank.
        ///
        /// <para>⚠ This rail asserts a LIMITATION on purpose. §3.2a claimed declare-and-report <i>"would
        /// have caught"</i> that bug; it would not, and a future reader must not rebuild their confidence
        /// on that sentence. What catches it is <see cref="TheSelfCheck_ReportsTheExactCE123Shape"/>.</para>
        /// </summary>
        [Fact]
        public void TheDeclareHalf_CannotSeeCE123_WhichIsWhyTheSelfCheckExists()
        {
            for (int i = 0; i < 8; i++) Spawn(500 + i);

            var mi = MapInteractionPack.Build(new MapInteractionContext
            {
                World = _world,
                StartEnabled = true,
            });

            // Exactly SimHost's situation: the group scheduled, all three systems in it.
            var missing = mi.Unserviceable(mi.GizmoGroup.GetSystems());

            Assert.Empty(missing);
        }

        /// <summary>⭐ The declared set is the set the pack actually puts in the group.</summary>
        [Fact]
        public void TheDeclaredSet_MatchesWhatThePackBuilds()
        {
            var mi = MapInteractionPack.Build(new MapInteractionContext { World = _world });

            var inGroup = mi.GizmoGroup.GetSystems().Select(s => s.GetType()).ToHashSet();

            foreach (Type required in mi.RequiredSystems)
                Assert.Contains(required, inGroup);
        }

        /// <summary>⭐ And the self-check ships inside the group, so a host cannot forget to wire it.</summary>
        [Fact]
        public void TheSelfCheck_IsTheLastMemberOfTheGroup()
        {
            var mi = MapInteractionPack.Build(new MapInteractionContext { World = _world });

            var systems = mi.GizmoGroup.GetSystems();

            Assert.Same(mi.SelfCheck, systems[^1]);
        }
    }
}
