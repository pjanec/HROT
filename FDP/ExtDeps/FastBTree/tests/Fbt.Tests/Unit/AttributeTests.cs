using System.Reflection;
using Xunit;
using Fbt;

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
    }
}
