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
        // CE-2049: the shipped generator (Fdp.Toolkits.Analyzers BTreeActionGenerator) keys an action by its FULLY
        //   QUALIFIED method name — the convention every HROT binding resolves by (E6(A)). The deleted Fbt.SourceGen
        //   keyed it by the short name, which is why these rails were red once it was gone.
        private static readonly string ActionKey =
            typeof(AnnotatedTestActions).FullName + "." + nameof(AnnotatedTestActions.AlwaysSuccessAction);

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

            Assert.True(registry.TryGetAction(ActionKey, out _));
        }
    }
}
