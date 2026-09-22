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
                .RegisterTranslator(new RoutePlanTranslator())
                .RegisterTranslator(new BrainBlackboardTranslator(behaviorRegistry))
                // ⛔ P4-① (2026-09-22): Blackboard1024Translator is GONE with its component. It was an
                //    extract-only clipboard dump gated on HeavyDtoType, which is null everywhere ⇒ it
                //    could only ever emit an empty object. 📄 §30.13.
                .RegisterTranslator(new BTreeTraceWorkingMemoryTranslator(behaviorRegistry))
                .RegisterTranslator(new HsmTraceWorkingMemoryTranslator(behaviorRegistry))
                .RegisterTranslator(new BlueprintStateTranslator(blueprintRegistry))
                .RegisterTranslator(new DisEntityTypeTranslator());

            return builder.Build();
        }
    }
}
