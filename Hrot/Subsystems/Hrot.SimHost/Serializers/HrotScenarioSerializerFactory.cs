using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Scenario;

namespace Hrot.SimHost.Serializers
{
    public static class HrotScenarioSerializerFactory
    {
        public static ScenarioSerializer Build(BehaviorRegistry behaviorRegistry)
        {
            return new ScenarioSerializerBuilder(HrotSubsystemTypes.Scenario)
                .RegisterTranslator(new MissionPlanTranslator(behaviorRegistry))
                .RegisterTranslator(new TargetMemoryTranslator())
                .RegisterTranslator(new PassengerBufferTranslator())
                .RegisterTranslator(new VisHierarchyNodeTranslator())
                .RegisterTranslator(new IsEmbarkedTagTranslator())
                .RegisterTranslator(new PersonalRouteRefTranslator())
                .RegisterTranslator(new UnitSubordinateTranslator())
                .Build();
        }
    }
}
