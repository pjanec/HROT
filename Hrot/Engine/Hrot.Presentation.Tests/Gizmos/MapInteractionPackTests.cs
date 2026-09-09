using System;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Replication.Components;
using Hrot.ScenarioEditor.Map;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-23</c> <c>S2b</c> — the rails for <see cref="MapInteractionPack"/>.</b>
    /// 📄 Design: <c>docs/UX/UX_Feature_Map_Parity.md</c> §3.2 · §3.2b (UML) · ⭐ §3.2d (the amendments).
    ///
    /// <para>🔴 <b>Why a pack exists at all, in one sentence:</b> <c>S2a</c> measured that the difference
    /// between a working map and a dark one was <b>one constructor argument</b> that one of five hand-written
    /// compositions got wrong (<c>CE-123</c>). These rails pin the properties that make that class of bug
    /// unreachable rather than merely unlikely.</para>
    /// </summary>
    public sealed class MapInteractionPackTests : IDisposable
    {
        private readonly EntityRepository _world;

        public MapInteractionPackTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<NetworkIdentity>();
        }

        public void Dispose() => _world.Dispose();

        private MapInteractionContext Ctx() => new() { World = _world };

        // ── ① the construct-vs-schedule fence ──────────────────────────────────────────────────

        /// <summary>
        /// 🔴🔴 <b>The structural rule, asserted structurally.</b> The pack may not schedule, and the way
        /// that is guaranteed is that <see cref="MapInteractionContext"/> cannot hand it a kernel.
        ///
        /// <para>🔒 User ruling: <i>"pack owns construction, host decides scheduling"</i>, and
        /// <c>DESIGN_Subsystem_Composition_Unification.md</c> §3.2 forbids a shared bundle from touching
        /// the run-set at all — the run-set follows the host's ROLE. A review note would decay; an absent
        /// property cannot.</para>
        /// </summary>
        [Fact]
        public void TheContext_CannotHandThePackAKernel()
        {
            var offenders = typeof(MapInteractionContext)
                .GetProperties()
                .Where(p => p.PropertyType.Name.Contains("Kernel", StringComparison.Ordinal)
                         || p.PropertyType.Name.Contains("Module", StringComparison.Ordinal))
                .Select(p => $"{p.Name} : {p.PropertyType.Name}")
                .ToArray();

            Assert.True(offenders.Length == 0,
                "MapInteractionContext must not expose a kernel or a module host:\n  "
              + string.Join("\n  ", offenders)
              + "\n🔒 The pack CONSTRUCTS; the HOST SCHEDULES. Withholding the kernel is what makes the "
              + "§3.2 violation unreachable instead of merely forbidden — the same technique "
              + "UiBundleContext uses.");
        }

        // ── ② the CE-123 invariant, now owned by one place instead of five ─────────────────────

        /// <summary>
        /// 🔴🔴 <b>The map is never gated by selection — and now there is exactly one place that could
        /// get it wrong.</b>
        ///
        /// <para>A selection predicate belongs on <c>DataDrivenGizmoSystem</c> (drag handles live on the
        /// selection) and never on <c>StatelessGizmoSystem</c>, where it is one blanket gate over every
        /// projector the host owns. This rail proves the pack routes it to exactly one of the two.</para>
        /// </summary>
        [Fact]
        public void ThePack_RoutesTheSelectionPredicateToTheHandles_NeverToTheMap()
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform());
            _world.AddComponent(entity, new NetworkIdentity(1L));

            // ⚠⚠ Asserted on a PER-ENTITY probe, not on the buffer being non-empty. An earlier version of
            // this rail checked `Buffer.GetFrame().Length > 0` and stayed GREEN through the inverse edit,
            // because GLOBAL rules are dispatched before the per-entity loop and bypass the predicate
            // entirely (StatelessGizmoSystem:71-75). That is the same fact that made SimHost's dark map
            // still report 605 primitives — the grid. A rail that a global rule can satisfy cannot see
            // CE-123 at all.
            var probe = new CountingProbeGizmo();

            // A predicate nothing satisfies. If it reached the stateless system, no entity would draw.
            var mi = MapInteractionPack.Build(new MapInteractionContext
            {
                World = _world,
                IsSelectedPredicate = static (_, _) => false,
                StartEnabled = true,
                ContributeExtras = regs => regs.Stateless.Register(probe, new[] { typeof(SimTransform) }),
            });

            mi.StatelessSystem.Execute(_world, 0.016f);

            Assert.True(probe.DrawCount > 0,
                "A per-entity projector did not run while a selection predicate was in play. That predicate "
              + "must reach DataDrivenGizmoSystem ONLY — on StatelessGizmoSystem it is one blanket gate over "
              + "every projector, which is exactly CE-123 (SimHost's map: 3 non-Line primitives for 8 "
              + "entities, all of them global rules).");
        }

        // ── ③ the ordering hazard that fails silently (§3.2d ③) ────────────────────────────────

        /// <summary>
        /// 🔴🔴 <b>A host's own gizmos must land BEFORE the systems are constructed.</b>
        ///
        /// <para><c>StatelessGizmoSystem</c> sizes its visibility cache from <c>registry.Rules.Count</c>
        /// in its constructor, and <c>Execute</c>'s guard is
        /// <c>if (r &lt; cache.Length &amp;&amp; !cache[r]) continue;</c> — so a rule registered afterwards
        /// lands beyond the cache and <b>silently ignores its visibility policy</b>. Four hosts contribute
        /// projectors reflection cannot construct, so this window is not optional.</para>
        /// </summary>
        [Fact]
        public void ContributeExtras_RunsBeforeTheSystemsAreBuilt_SoExtrasAreCoveredByTheCache()
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new SimTransform());

            // ⭐ Half one: an extra registered through the window REACHES the constructed system.
            var drawn = new CountingProbeGizmo();
            var reached = MapInteractionPack.Build(new MapInteractionContext
            {
                World = _world,
                ContributeExtras = regs => regs.Stateless.Register(drawn, new[] { typeof(SimTransform) }),
            });
            reached.StatelessSystem.Execute(_world, 0.016f);
            Assert.True(drawn.DrawCount > 0,
                "A projector contributed through ContributeExtras must be in the registry the constructed "
              + "StatelessGizmoSystem reads.");

            // 🔴 Half two — the one that matters. Registered with a NEVER-visible policy, it must NOT draw.
            // If the extra had landed AFTER the system was constructed it would sit beyond the visibility
            // cache, and Execute's guard `if (r < cache.Length && !cache[r]) continue;` would let it
            // through — drawing despite its own policy. That is the silent failure this window prevents.
            var suppressed = new CountingProbeGizmo();
            var gated = MapInteractionPack.Build(new MapInteractionContext
            {
                World = _world,
                ContributeExtras = regs => regs.Stateless.Register(
                    suppressed, new[] { typeof(SimTransform) }, NeverVisiblePolicy.Instance),
            });
            gated.StatelessSystem.Execute(_world, 0.016f);

            Assert.Equal(0, suppressed.DrawCount);
        }

        /// <summary>⭐ And the registries handed to a contributor are the ones the systems actually use.</summary>
        [Fact]
        public void ContributeExtras_ReceivesTheSameRegistriesTheSystemsUse()
        {
            MapInteractionRegistries? captured = null;

            var mi = MapInteractionPack.Build(new MapInteractionContext
            {
                World = _world,
                ContributeExtras = regs => captured = regs,
            });

            Assert.NotNull(captured);
            Assert.Same(mi.GizmoRegistry,     captured!.Gizmos);
            Assert.Same(mi.StatelessRegistry, captured!.Stateless);
            Assert.Same(mi.Settings,          captured!.Settings);
            Assert.Same(mi.Buffer,            captured!.Buffer);
            Assert.Same(mi.InteractionBus,    captured!.InteractionBus);
        }

        // ── ④ the initial gate state — the amendment that kept IG and the editor alive (§3.2d ①) ──

        /// <summary>
        /// 🔴 <b><c>StartEnabled</c> defaults to headless-first, and is REACHABLE.</b>
        ///
        /// <para>⚠ "Start disabled for everyone" was measured unsafe: the only production driver of
        /// <c>AddListener()</c> is <c>PerspectiveCoordinatorSystem</c>, so a standalone IG or editor has no
        /// viewer-attach path and would sit behind a permanently shut gate. The per-host truth survives as
        /// this one named input — <c>R-137</c>.</para>
        /// </summary>
        [Fact]
        public void TheGroupStartsDisabledByDefault_AndAHostCanAskForEnabled()
        {
            Assert.False(MapInteractionPack.Build(Ctx()).GizmoGroup.Enabled,
                "GZH-003 headless-first: the default must be disabled.");

            Assert.True(
                MapInteractionPack.Build(new MapInteractionContext { World = _world, StartEnabled = true })
                    .GizmoGroup.Enabled,
                "A host with a window at startup must be able to say so — IG and the editor have no other "
              + "way to open their gate (§3.2d ①).");
        }

        /// <summary>⭐ The gate the pack returns actually drives the group the pack returns.</summary>
        [Fact]
        public void TheGate_DrivesTheGroupItWasBuiltWith()
        {
            var mi = MapInteractionPack.Build(Ctx());
            Assert.False(mi.GizmoGroup.Enabled);

            mi.Gate.AddListener();
            Assert.True(mi.GizmoGroup.Enabled);

            mi.Gate.RemoveListener();
            Assert.False(mi.GizmoGroup.Enabled);
        }

        // ── ⑤ what the pack builds, and what it deliberately does not ──────────────────────────

        /// <summary>⭐ One buffer, shared by all three systems — that is what makes one frame.</summary>
        [Fact]
        public void ThePack_BuildsTheWholeSetAndTheGroupHoldsTheThreeSystems()
        {
            var mi = MapInteractionPack.Build(Ctx());

            Assert.NotNull(mi.Buffer);
            Assert.NotNull(mi.InteractionBus);
            Assert.NotNull(mi.GizmoRegistry);
            Assert.NotNull(mi.StatelessRegistry);
            Assert.NotNull(mi.Settings);
            Assert.NotNull(mi.GlobalManager);
            Assert.NotNull(mi.DataDrivenSystem);
            Assert.NotNull(mi.StatelessSystem);
            Assert.NotNull(mi.GizmoGroup);
            Assert.NotNull(mi.Gate);
        }

        /// <summary>
        /// ⭐⭐ <b>No host may call a source-generated per-namespace registrar any more.</b>
        ///
        /// <para>🔴 IG used to call BOTH <c>Hrot.IG.Gizmos.GizmoRegistrar.Register</c> (which forwards to
        /// reflection) AND the generated <c>Hrot.Presentation.Gizmos.GizmoRegistrar.RegisterAll</c>.
        /// <c>CanvasContextMenuGizmo</c> carries <c>[GizmoProjector]</c>, so reflection already found it
        /// and the generated call registered it a SECOND time — measured live as
        /// <c>ContextMenuBinding 10</c> on the IG perspective against <c>9</c> on Scenario.</para>
        ///
        /// <para>⭐ Asserted over source because the defect is in a host's COMPOSITION, and because the
        /// generated registrars still legitimately exist for tests.</para>
        /// </summary>
        [Fact]
        public void NoHost_CallsAGeneratedPerNamespaceGizmoRegistrar()
        {
            var root = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "IOS-IG-SimHost.sln")))
                root = root.Parent;
            Assert.True(root != null, "Could not locate workspace root (IOS-IG-SimHost.sln not found).");

            string[] hosts =
            {
                "Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs",
                "Hrot/Subsystems/Hrot.IG/IgApplication.cs",
                "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
                "Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs",
                "Hrot/Subsystems/Hrot.ReplayBrowser/ReplayBrowserSubsystem.cs",
            };

            var offenders = new System.Collections.Generic.List<string>();
            foreach (string rel in hosts)
            {
                string path = System.IO.Path.Combine(root!.FullName, rel);
                Assert.True(System.IO.File.Exists(path), $"Host file not found: {rel}");

                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    string t = line.TrimStart();
                    if (t.StartsWith("//", StringComparison.Ordinal)) continue;   // the explanatory comments
                    if (t.Contains("Gizmos.GizmoRegistrar.Register", StringComparison.Ordinal))
                        offenders.Add($"{rel}: {t.Trim()}");
                }
            }

            Assert.True(offenders.Count == 0,
                "A host calls a per-namespace gizmo registrar directly:\n  "
              + string.Join("\n  ", offenders)
              + "\n🔴 Reflection already discovers every [GizmoProjector]; an extra generated call "
              + "registers those projectors a SECOND time and they draw twice. Contribute host-specific "
              + "gizmos through MapInteractionContext.ContributeExtras instead.");
        }

        /// <summary>⭐ A host may hand in its own settings store, or share one across hosts (§3.2c).</summary>
        [Fact]
        public void ThePack_UsesAnInjectedSettingsStoreWhenGiven()
        {
            var shared = new GizmoSettingsRegistry();

            var a = MapInteractionPack.Build(new MapInteractionContext { World = _world, Settings = shared });
            var b = MapInteractionPack.Build(new MapInteractionContext { World = _world, Settings = shared });

            Assert.Same(shared, a.Settings);
            Assert.Same(shared, b.Settings);
        }

        /// <summary>⭐ IG's 4096 was a constructor argument; it stays a named per-host input (<c>R-137</c>).</summary>
        [Fact]
        public void ThePack_HonoursAHostsBufferCapacity()
        {
            var mi = MapInteractionPack.Build(new MapInteractionContext { World = _world, BufferCapacity = 4096 });
            Assert.NotNull(mi.Buffer);
        }

        /// <summary>Counts dispatches, so a rail measures whether a rule RAN, not what it drew.</summary>
        private sealed class CountingProbeGizmo : IStatelessGizmo
        {
            public int DrawCount;
            public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder) => DrawCount++;
        }
    }
}
