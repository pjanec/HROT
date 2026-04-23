using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Scenario;

namespace Hrot.SimHost.Serializers
{
    public static class HrotScenarioSerializerFactory
    {
        public static ScenarioSerializer Build(DoctrineRegistry doctrineRegistry)
        {
            return new ScenarioSerializerBuilder(HrotSubsystemTypes.Scenario)
                .RegisterTranslator(new MissionPlanTranslator(doctrineRegistry))
                .RegisterTranslator(new TargetMemoryTranslator())
                .RegisterTranslator(new PassengerBufferTranslator())
                .RegisterTranslator(new VisHierarchyNodeTranslator())
                .RegisterTranslator(new IsEmbarkedTagTranslator())
                .RegisterTranslator(new PersonalRouteRefTranslator())
                .Build();
        }
    }
}
