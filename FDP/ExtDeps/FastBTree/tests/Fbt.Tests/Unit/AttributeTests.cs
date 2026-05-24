using System.Reflection;
using Xunit;
using Fbt;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    /// <summary>Tests for the Phase 2 marker attributes (FBT-010).</summary>
    public class AttributeTests
    {
        // ---- Test targets decorated with the attributes ----

        [BTreeAction]
        private static void AnnotatedAction() { }

        [BTreeDefinition("TestTree")]
        private static void AnnotatedDefinition() { }

        [FbtRegistrar]
        private sealed class AnnotatedRegistrar { }

        // ---- FBT-010: SC1 ----

        /// <summary>A static method can be annotated with [BTreeAction].</summary>
        [Fact]
        public void BTreeActionAttribute_CanBeAppliedToMethod()
        {
            var method = typeof(AttributeTests)
                .GetMethod(nameof(AnnotatedAction), BindingFlags.Static | BindingFlags.NonPublic)!;

            Assert.NotNull(method.GetCustomAttribute<BTreeActionAttribute>());
        }

        // ---- FBT-010: SC2 ----

        /// <summary>[BTreeDefinition] exposes the tree name via its TreeName property.</summary>
        [Fact]
        public void BTreeDefinitionAttribute_ExposesTreeName()
        {
            var method = typeof(AttributeTests)
                .GetMethod(nameof(AnnotatedDefinition), BindingFlags.Static | BindingFlags.NonPublic)!;

            var attr = method.GetCustomAttribute<BTreeDefinitionAttribute>();
            Assert.NotNull(attr);
            Assert.Equal("TestTree", attr!.TreeName);
        }

        // ---- FBT-010: SC (class target) ----

        /// <summary>[FbtRegistrar] can be applied to a class and is retrievable via reflection.</summary>
        [Fact]
        public void FbtRegistrarAttribute_CanBeAppliedToClass()
        {
            var attr = typeof(AnnotatedRegistrar).GetCustomAttribute<FbtRegistrarAttribute>();
            Assert.NotNull(attr);
        }

        // ---- TASK-EQL-001 contract tests (T1-T4) ----

        /// <summary>T1: NodeDeactivatorDelegate lives in the Fbt namespace.</summary>
        [Fact]
        public void T1_NodeDeactivatorDelegate_IsInFbtNamespace()
        {
            Assert.Equal("Fbt", typeof(NodeDeactivatorDelegate<,>).Namespace);
        }

        /// <summary>T2: BTreeDeactivatorAttribute lives in the Fbt namespace.</summary>
        [Fact]
        public void T2_BTreeDeactivatorAttribute_IsInFbtNamespace()
        {
            Assert.Equal("Fbt", typeof(BTreeDeactivatorAttribute).Namespace);
        }

        /// <summary>T3: BTreeDeactivatorAttribute constructor arg is accessible via TargetAction.</summary>
        [Fact]
        public void T3_BTreeDeactivatorAttribute_ExposesTargetAction()
        {
            var attr = new BTreeDeactivatorAttribute("Foo.Bar");
            Assert.Equal("Foo.Bar", attr.TargetAction);
        }

        /// <summary>T4: A matching lambda can be assigned to NodeDeactivatorDelegate without a cast.</summary>
        [Fact]
        public void T4_NodeDeactivatorDelegate_AcceptsLambdaWithoutCast()
        {
            NodeDeactivatorDelegate<TestBlackboard, MockContext> d =
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => { };
            Assert.NotNull(d);
        }
    }
}
