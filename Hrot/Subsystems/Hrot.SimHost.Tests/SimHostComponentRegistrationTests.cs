using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using Hrot.NED.Descriptors;
using Hrot.SimHost;
using Hrot.SimHost.Configuration;
using Hrot.Map.Common.Replication;
using Hrot.Common.Abstractions;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;
using Fdp.ModuleHost.Network.Cyclone.Modules;
using Fdp.ModuleHost.Network.Cyclone.Services;
using Fdp.ModuleHost.Network.Cyclone.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    [Collection("SimHostDds")]
    public class SimHostComponentRegistrationTests : IDisposable
    {
        // Provides DdsIdAllocatorServer on every domain used by the tests in this class.
        private readonly (DdsParticipant Participant, DdsIdAllocatorServer Server, Thread Thread, CancellationTokenSource Cts)[] _allocators;

        private static readonly int[] AllocatorDomains = { 0, 96, 97, 98, 99 };

        public SimHostComponentRegistrationTests()
        {
            _allocators = AllocatorDomains.Select(d =>
            {
                var p      = new DdsParticipant((uint)d);
                var server = new DdsIdAllocatorServer(p);
                var cts    = new CancellationTokenSource();
                var thread = new Thread(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        server.ProcessRequests();
                        Thread.Sleep(1);
                    }
                }) { IsBackground = true, Name = $"Test-IdAllocServer-{d}" };
                thread.Start();
                return (p, server, thread, cts);
            }).ToArray();
        }

        public void Dispose()
        {
            foreach (var (p, server, thread, cts) in _allocators)
            {
                cts.Cancel();
                thread.Join(TimeSpan.FromSeconds(2));
                cts.Dispose();
                server.Dispose();
                p.Dispose();
            }
        }

        [Fact]
        public void RegisterSimComponents_DoesNotRegisterEntityMaster()
        {
            using var repo = new EntityRepository();

            var method = typeof(SimHostApp).GetMethod(
                "RegisterSimComponents",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            method!.Invoke(null, new object[] { repo });

            Assert.Throws<InvalidOperationException>(() => repo.GetComponentTable<EntityMaster>());
        }

        [Fact]
        public void SimHostComponents_AllCombatAndPerceptionComponentsRegistered()
        {
            using var repo = new EntityRepository();

            var method = typeof(SimHostApp).GetMethod(
                "RegisterSimComponents",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            method!.Invoke(null, new object[] { repo });

            Assert.Null(Record.Exception(() => repo.GetComponentTable<PerceptionReceptor>()));
            Assert.Null(Record.Exception(() => repo.GetComponentTable<WeaponState>()));
            Assert.Null(Record.Exception(() => repo.GetComponentTable<Health>()));
            Assert.Null(Record.Exception(() => repo.GetComponentTable<Faction>()));
            Assert.Null(Record.Exception(() => repo.GetComponentTable<PhysicsCollider>()));
        }

        [Fact]
        public void OnLoad_RegistersCycloneNetworkCleanupSystem()
        {
            var app = new SimHostApp();
            app.InitializeHeadless(domainIdOverride: 98);

            var profile = app.Kernel.SystemScheduler.GetProfileData<CycloneNetworkCleanupSystem>();

            Assert.NotNull(profile);
        }

        [Fact(Skip = "Requires NED factory injection; NED-specific translators are validated in Hrot.SimHost.Integration.Tests or Hrot.Network.NED.Tests")]
        public void OnLoad_RegistersFireInteractionEventTranslator()
        {
            var app = new SimHostApp();
            app.InitializeHeadless(domainIdOverride: 99);

            var translators = GetCustomTranslators(app).Cast<object>().ToList();

            Assert.Contains(translators, t => t is FireInteractionEventTranslator);
        }

        private static IEnumerable GetCustomTranslators(SimHostApp app)
        {
            var kernel = app.Kernel;
            var modules = (IEnumerable)GetPrivateField(kernel, "_modules");

            foreach (var entry in modules)
            {
                var moduleProperty = entry.GetType().GetProperty("Module");
                var module = moduleProperty?.GetValue(entry);
                if (module is INedReplicationModule)
                {
                    return (IEnumerable)GetPrivateField(module, "_sharedTranslators");
                }
            }

            throw new InvalidOperationException("NedReplicationModule not found in kernel modules.");
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new InvalidOperationException($"Field '{fieldName}' not found on {target.GetType().Name}.");
            return field.GetValue(target)!;
        }

        // ── BUG1-F001: Domain zero guard ─────────────────────────────────────

        [Fact]
        public void InitializeEmbedded_DomainZero_UsesDomainZero()
        {
            // Before fix, domainOverride=0 was silently treated as null (fallback to config.json).
            // After fix, 0 is passed through as a valid domain.
            var app = new SimHostApp();
            app.InitializeEmbedded(headless: true, domainIdOverride: 0, nodeIdOverride: 0);
            // Initialization must complete without exception; the app should be alive.
            Assert.NotNull(app.WorldOrNull);
        }

        // ── BUG1-F002: NodeId override ───────────────────────────────────────

        [Fact]
        public void InitializeHeadless_NodeIdZero_FallsBackToLegacyConstant()
        {
            var app = new SimHostApp();
            app.InitializeHeadless(domainIdOverride: 97, nodeIdOverride: 0);
            Assert.Equal(SimHostNetworkConstants.LocalNodeId, app.TestHook_ResolvedLocalNodeId);
        }

        [Fact]
        public void InitializeHeadless_NodeIdTen_ResolvedToTen()
        {
            var app = new SimHostApp();
            app.InitializeHeadless(domainIdOverride: 96, nodeIdOverride: 10);
            Assert.Equal(10, app.TestHook_ResolvedLocalNodeId);
        }
    }
}
