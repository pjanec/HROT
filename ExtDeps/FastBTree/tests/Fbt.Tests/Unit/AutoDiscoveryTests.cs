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
        [Fact]
        public void ScanAndRegister_FindsGeneratedRegistrar_InTestAssembly()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();

            FbtAutoDiscovery.ScanAndRegister<TestBlackboard, MockContext>(registry);

            Assert.True(registry.TryGetAction("AlwaysSuccessAction", out _));
        }

        [Fact]
        public void ScanAndRegister_FindsBothActionAndCondition()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();

            FbtAutoDiscovery.ScanAndRegister<TestBlackboard, MockContext>(registry);

            Assert.True(registry.TryGetAction("AlwaysSuccessAction", out _));
            Assert.True(registry.TryGetAction("AlwaysSuccessCondition", out _));
        }

        [Fact]
        public void ScanAndRegister_RegisteredAction_IsCallable()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            FbtAutoDiscovery.ScanAndRegister<TestBlackboard, MockContext>(registry);

            Assert.True(registry.TryGetAction("AlwaysSuccessAction", out var action));

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
