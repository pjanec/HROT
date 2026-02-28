using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Bagira.BDC.SSTD;
using Bagira.SimHost;
using Bagira.SimHost.Translators;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;
using ModuleHost.Network.Cyclone.Modules;
using ModuleHost.Network.Cyclone.Systems;
using Xunit;

namespace Bagira.SimHost.Tests
{
    public class SimHostComponentRegistrationTests
    {
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

        [Fact]
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
                if (module is CycloneNetworkModule cyclone)
                {
                    return (IEnumerable)GetPrivateField(cyclone, "_customTranslators");
                }
            }

            throw new InvalidOperationException("CycloneNetworkModule not found in kernel modules.");
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new InvalidOperationException($"Field '{fieldName}' not found on {target.GetType().Name}.");
            return field.GetValue(target)!;
        }
    }
}
