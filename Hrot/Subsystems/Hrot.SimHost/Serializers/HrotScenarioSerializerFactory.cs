using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Scenario;

namespace Hrot.SimHost.Serializers
{
    public static class HrotScenarioSerializerFactory
    {
        public static ScenarioSerializer Build(
            BehaviorRegistry behaviorRegistry,
            BlueprintRegistry? blueprintRegistry = null)
        {
            var builder = new ScenarioSerializerBuilder(HrotSubsystemTypes.Scenario)
                .RegisterTranslator(new MissionPlanTranslator(behaviorRegistry))
                .RegisterTranslator(new TargetMemoryTranslator())
                .RegisterTranslator(new PassengerBufferTranslator())
                .RegisterTranslator(new VisHierarchyNodeTranslator())
                .RegisterTranslator(new IsEmbarkedTagTranslator())
                .RegisterTranslator(new PersonalRouteRefTranslator())
                .RegisterTranslator(new UnitSubordinateTranslator())
                .RegisterTranslator(new EditablePolylineTranslator())
                .RegisterTranslator(new BrainBlackboardTranslator(behaviorRegistry))
                .RegisterTranslator(new Blackboard1024Translator(behaviorRegistry))
                .RegisterTranslator(new BTreeTraceWorkingMemoryTranslator(behaviorRegistry))
                .RegisterTranslator(new HsmTraceWorkingMemoryTranslator(behaviorRegistry))
                .RegisterTranslator(new BlueprintStateTranslator(blueprintRegistry));

            return builder.Build();
        }
    }
}
