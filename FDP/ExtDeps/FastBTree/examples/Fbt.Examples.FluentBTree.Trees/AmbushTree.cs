using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;

namespace Fbt.Examples.FluentBTree
{
    public static class AmbushTree
    {
        // Creates a pre-wired builder with all delegates registered.
        // Call Compile() + GetRegistry() on the returned builder to get a usable interpreter.
        public static BTreeBuilder<CombatBlackboard, CombatContext> CreateBuilder()
        {
            return new BTreeBuilder<CombatBlackboard, CombatContext>()
                .Selector(s => s
                    .Sequence(seq => seq
                        .Condition(dto => dto.ThreatVisible, CombatActions.HasThreat)
                        .Condition(dto => dto.AmmoCount, CombatActions.CheckAmmo)
                        .Action(dto => dto.AmmoCount, CombatActions.AimAndFire)
                    )
                    .Action(CombatActions.HoldPosition)
                );
        }

        // Source-generator entry point. Must return BehaviorTreeBlob with zero parameters.
        // Called by the generated FbtTreeCatalog.GetAmbush_BT().
        [BTreeDefinition("Ambush_BT")]
        public static BehaviorTreeBlob BuildAmbushTree()
        {
            return CreateBuilder().Compile("Ambush_BT");
        }

        // Creates a fully-wired Interpreter ready for ticking.
        // Use this in the sample app and in tests.
        public static Interpreter<CombatBlackboard, CombatContext> CreateInterpreter()
        {
            var builder = CreateBuilder();
            var blob = builder.Compile("Ambush_BT");
            return new Interpreter<CombatBlackboard, CombatContext>(blob, builder.GetRegistry());
        }
    }
}
