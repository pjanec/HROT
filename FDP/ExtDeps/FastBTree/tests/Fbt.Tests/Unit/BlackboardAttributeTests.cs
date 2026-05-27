using System;
using System.Linq;
using System.Reflection;
using Fbt;
using Fbt.Kernel;
using Xunit;

namespace Fbt.Tests.Unit
{
    // ---- K-03 test fixtures (private nested structs -- visible via Assembly.GetTypes()) ----

    public class BlackboardAttributeTests
    {
        [BlackboardDtoStruct]
        private struct DecoratedDtoStruct { }

        private struct UndecoratedDtoStruct { }

        // ---- K-04 test fixtures ----

        private static void ActionWithReadOnly([BlackboardReadOnly] ref int param) { }
        private static void ActionWithReadWrite([BlackboardReadWrite] ref int param) { }
        private static void ActionUnannotated(ref int param) { }

        // ============================================================
        // TASK-BB-K-01: BTreeDefinitionAttribute.BlackboardManaged
        // ============================================================

        [Fact]
        public void BTreeDefinitionAttribute_BlackboardManaged_DefaultsFalse()
        {
            var attr = new BTreeDefinitionAttribute("TestTree");
            Assert.False(attr.BlackboardManaged);
        }

        [Fact]
        public void BTreeDefinitionAttribute_BlackboardManaged_RoundTripsTrue()
        {
            var attr = new BTreeDefinitionAttribute("TestTree") { BlackboardManaged = true };
            Assert.True(attr.BlackboardManaged);
        }

        // ============================================================
        // TASK-BB-K-02: BTreeDefinitionAttribute.HeavyDtoType
        // ============================================================

        [Fact]
        public void BTreeDefinitionAttribute_HeavyDtoType_DefaultsNull()
        {
            var attr = new BTreeDefinitionAttribute("TestTree");
            Assert.Null(attr.HeavyDtoType);
        }

        [Fact]
        public void BTreeDefinitionAttribute_HeavyDtoType_CanBeSet()
        {
            var attr = new BTreeDefinitionAttribute("TestTree") { HeavyDtoType = typeof(int) };
            Assert.Equal(typeof(int), attr.HeavyDtoType);
        }

        [Fact]
        public void BTreeDefinitionAttribute_HeavyDtoType_NullMeansNoHeavyComponent()
        {
            // Null HeavyDtoType means the runtime provisions no heavy component (regression guard).
            var attr = new BTreeDefinitionAttribute("TestTree");
            Assert.Null(attr.HeavyDtoType);
        }

        // ============================================================
        // TASK-BB-K-03: BlackboardDtoStructAttribute
        // ============================================================

        [Fact]
        public void BlackboardDtoStructAttribute_DecoratedStruct_IsDiscoverable()
        {
            var decorated = typeof(BlackboardAttributeTests).Assembly
                .GetTypes()
                .Where(t => t.IsDefined(typeof(BlackboardDtoStructAttribute), false))
                .ToList();

            Assert.Contains(typeof(DecoratedDtoStruct), decorated);
        }

        [Fact]
        public void BlackboardDtoStructAttribute_UndecoratedStruct_IsNotDiscovered()
        {
            var decorated = typeof(BlackboardAttributeTests).Assembly
                .GetTypes()
                .Where(t => t.IsDefined(typeof(BlackboardDtoStructAttribute), false))
                .ToList();

            Assert.DoesNotContain(typeof(UndecoratedDtoStruct), decorated);
        }

        [Fact]
        public void BlackboardDtoStructAttribute_CanBeReadBackFromDecoratedStruct()
        {
            var attr = typeof(DecoratedDtoStruct).GetCustomAttribute<BlackboardDtoStructAttribute>();
            Assert.NotNull(attr);
        }

        // ============================================================
        // TASK-BB-K-04: BlackboardReadOnlyAttribute / BlackboardReadWriteAttribute
        // ============================================================

        [Fact]
        public void BlackboardReadOnlyAttribute_IsReadableViaParameterInfo()
        {
            var method = typeof(BlackboardAttributeTests)
                .GetMethod(nameof(ActionWithReadOnly), BindingFlags.NonPublic | BindingFlags.Static)!;
            var param = method.GetParameters()[0];
            Assert.NotNull(param.GetCustomAttribute<BlackboardReadOnlyAttribute>());
        }

        [Fact]
        public void BlackboardReadWriteAttribute_IsReadableViaParameterInfo()
        {
            var method = typeof(BlackboardAttributeTests)
                .GetMethod(nameof(ActionWithReadWrite), BindingFlags.NonPublic | BindingFlags.Static)!;
            var param = method.GetParameters()[0];
            Assert.NotNull(param.GetCustomAttribute<BlackboardReadWriteAttribute>());
        }

        [Fact]
        public void UnannotatedParameter_HasNeitherAttribute()
        {
            var method = typeof(BlackboardAttributeTests)
                .GetMethod(nameof(ActionUnannotated), BindingFlags.NonPublic | BindingFlags.Static)!;
            var param = method.GetParameters()[0];
            Assert.Null(param.GetCustomAttribute<BlackboardReadOnlyAttribute>());
            Assert.Null(param.GetCustomAttribute<BlackboardReadWriteAttribute>());
        }
    }
}
