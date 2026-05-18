using Fbt.Runtime;
using Fbt.Tests.TestFixtures;
using Xunit;

namespace Fbt.Tests.Unit
{
    /// <summary>Tests for BHU-012: BTree generator SharedAi offset computation and registration.</summary>
    public class SharedAiGeneratorTests
    {
        // ---- SC2: SharedAi-annotated methods registered under compound key -----

        [Fact]
        public void SharedAiCondition_SequentialOffset_RegisteredUnderCompoundKey()
        {
            // SequentialDto.FieldB is the second field after int FieldA (4 bytes) -> offset 4
            var registry = new ActionRegistry<SharedAiTestBlackboard, SharedAiTestContext>();
            Fbt.Tests.Generated.FbtActionRegistrar.RegisterAll(registry);

            // Compound key: "SequentialCondition@4"
            bool found = registry.TryGetCondition("SequentialCondition@4", out var cond);
            Assert.True(found, "Compound key 'SequentialCondition@4' must be registered");
            Assert.NotNull(cond);
        }

        [Fact]
        public void SharedAiAction_ExplicitOffset_RegisteredUnderCompoundKey()
        {
            // ExplicitDto.FieldAt12 has [FieldOffset(12)] -> offset 12
            var registry = new ActionRegistry<SharedAiTestBlackboard, SharedAiTestContext>();
            Fbt.Tests.Generated.FbtActionRegistrar.RegisterAll(registry);

            // Compound key: "ExplicitAction@12"
            bool found = registry.TryGetAction("ExplicitAction@12", out var action);
            Assert.True(found, "Compound key 'ExplicitAction@12' must be registered");
            Assert.NotNull(action);
        }

        // ---- SC3: Group anchor action is registered normally ------------------

        [Fact]
        public void GroupAnchorAction_RegisteredUnderMethodName()
        {
            var registry = new ActionRegistry<SharedAiTestBlackboard, SharedAiTestContext>();
            Fbt.Tests.Generated.FbtActionRegistrar.RegisterAll(registry);

            bool found = registry.TryGetAction("GroupAnchorAction", out _);
            Assert.True(found, "GroupAnchorAction must be registered under its plain method name");
        }

        // ---- SC4: Compound key content verification via call ------------------

        [Fact]
        public unsafe void SharedAiCondition_CompoundKey_IsCallable_ReturnsExpectedStatus()
        {
            var registry = new ActionRegistry<SharedAiTestBlackboard, SharedAiTestContext>();
            Fbt.Tests.Generated.FbtActionRegistrar.RegisterAll(registry);

            Assert.True(registry.TryGetCondition("SequentialCondition@4", out var cond));

            // Set FieldB (offset 4) to a positive value -> condition should return Success
            var bb    = new SharedAiTestBlackboard();
            var state = new BehaviorTreeState();
            var ctx   = new SharedAiTestContext();

            // Write 1.0f into bb at byte offset 4
            unsafe
            {
                *(float*)((byte*)&bb + 4) = 1.0f;
            }

            var result = cond!(ref bb, ref state, ref ctx, 0);
            Assert.Equal(NodeStatus.Success, result);
        }
    }
}
