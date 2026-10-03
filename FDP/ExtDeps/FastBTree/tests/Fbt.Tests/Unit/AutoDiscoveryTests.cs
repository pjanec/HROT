using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Fbt.Tests.Unit
{
    public class AutoDiscoveryTests
    {
        // CE-2049: the shipped generator (Fdp.Toolkits.Analyzers BTreeActionGenerator) keys an action by its FULLY
        //   QUALIFIED method name — the convention every HROT binding resolves by (E6(A)). The deleted Fbt.SourceGen
        //   keyed it by the short name, which is why these rails were red once it was gone.
        private static readonly string ActionKey =
            typeof(AnnotatedTestActions).FullName + "." + nameof(AnnotatedTestActions.AlwaysSuccessAction);
        private static readonly string ConditionKey =
            typeof(AnnotatedTestActions).FullName + "." + nameof(AnnotatedTestActions.AlwaysSuccessCondition);

        [Fact]
        public void ScanAndRegister_FindsGeneratedRegistrar_InTestAssembly()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();

            FbtAutoDiscovery.ScanAndRegister<TestBlackboard, MockContext>(registry);

            Assert.True(registry.TryGetAction(ActionKey, out _));
        }

        [Fact]
        public void ScanAndRegister_FindsBothActionAndCondition()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();

            FbtAutoDiscovery.ScanAndRegister<TestBlackboard, MockContext>(registry);

            Assert.True(registry.TryGetAction(ActionKey, out _));
            Assert.True(registry.TryGetAction(ConditionKey, out _));
        }

        [Fact]
        public void ScanAndRegister_RegisteredAction_IsCallable()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            FbtAutoDiscovery.ScanAndRegister<TestBlackboard, MockContext>(registry);

            Assert.True(registry.TryGetAction(ActionKey, out var action));

            unsafe
            {
                var bb = new TestBlackboard();
                var state = new BehaviorTreeState();
                var ctx = new MockContext();
                var result = action!(ref bb, ref state, ref ctx, 0);
                Assert.Equal(NodeStatus.Success, result);
            }
        }

        [Fact]
        public void ScanAndRegister_SkipsNonReflectableAssemblies_Safely()
        {
            // Sanity test: scanning all loaded assemblies must never propagate an exception
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            var ex = Record.Exception(() =>
                FbtAutoDiscovery.ScanAndRegister<TestBlackboard, MockContext>(registry));
            Assert.Null(ex);
        }

        [Fact]
        public void FbtRegistrarAttribute_IsAppliedToGeneratedClass()
        {
            // The source generator should emit FbtActionRegistrar with [FbtRegistrar] in the test assembly
            var assembly = Assembly.GetExecutingAssembly();
            var registrarType = assembly.GetTypes()
                .FirstOrDefault(t => t.Name == "FbtActionRegistrar");

            Assert.NotNull(registrarType);
            Assert.True(registrarType!.IsDefined(typeof(FbtRegistrarAttribute), false));
        }
    }
}
