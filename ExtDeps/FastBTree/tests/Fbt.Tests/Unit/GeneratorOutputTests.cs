using Fbt;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Fbt.Tests.Unit
{
    public class GeneratorOutputTests
    {
        [Fact]
        public void GeneratedRegistrar_ContainsBTreeAction_Method()
        {
            var registrarType = Type.GetType("Fbt.Tests.Generated.FbtActionRegistrar, Fbt.Tests");
            Assert.NotNull(registrarType);

            // Use GetMethods because multiple RegisterAll overloads exist (one per group).
            var methods = registrarType!.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "RegisterAll")
                .ToArray();
            Assert.NotEmpty(methods);
        }

        [Fact]
        public void GeneratedRegistrar_IsTaggedWithFbtRegistrarAttribute()
        {
            var registrarType = Type.GetType("Fbt.Tests.Generated.FbtActionRegistrar, Fbt.Tests");
            Assert.NotNull(registrarType);

            bool hasAttr = registrarType!.IsDefined(typeof(FbtRegistrarAttribute), false);
            Assert.True(hasAttr);
        }

        [Fact]
        public void GeneratedRegistrar_RegisterAll_PopulatesRegistry()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();

            var registrarType = Type.GetType("Fbt.Tests.Generated.FbtActionRegistrar, Fbt.Tests");
            Assert.NotNull(registrarType);

            var method = registrarType!.GetMethod(
                "RegisterAll",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[] { typeof(ActionRegistry<TestBlackboard, MockContext>) },
                null);
            Assert.NotNull(method);

            method!.Invoke(null, new object[] { registry });

            Assert.True(registry.TryGetAction("AlwaysSuccessAction", out _));
        }
    }
}
