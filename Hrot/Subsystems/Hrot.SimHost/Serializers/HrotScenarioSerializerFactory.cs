using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Scenario;

namespace Hrot.SimHost.Serializers
{
    public static class HrotScenarioSerializerFactory
    {
        /// <param name="unknownComponentPolicy">
        /// What to do with a component the registry cannot resolve. Defaults to
        /// <see cref="UnknownComponentPolicy.Throw"/>, which is what the editor, CLI tooling and
        /// CI want. ⭐ A live-cluster node passes <see cref="UnknownComponentPolicy.WarnAndSkip"/>
        /// so one retired component cannot make a whole scenario unloadable — measured 2026-09-28,
        /// when P4's unmigrated BrainBlackboard did exactly that to every shipped scenario.
        /// </param>
        public static ScenarioSerializer Build(
            BehaviorRegistry behaviorRegistry,
            BlueprintRegistry? blueprintRegistry = null,
            UnknownComponentPolicy unknownComponentPolicy = UnknownComponentPolicy.Throw)
        {
            var builder = new ScenarioSerializerBuilder(HrotSubsystemTypes.Scenario)
                .WithUnknownComponentPolicy(unknownComponentPolicy)
                .RegisterTranslator(new MissionPlanTranslator(behaviorRegistry))
                .RegisterTranslator(new TargetMemoryTranslator())
                .RegisterTranslator(new PassengerBufferTranslator())
                .RegisterTranslator(new VisHierarchyNodeTranslator())
                .RegisterTranslator(new IsEmbarkedTagTranslator())
                .RegisterTranslator(new PersonalRouteRefTranslator())
                .RegisterTranslator(new UnitSubordinateTranslator())
                .RegisterTranslator(new EditablePolylineTranslator())
                .RegisterTranslator(new RoutePlanTranslator())
                .RegisterTranslator(new BrainDiagnosticsTranslator(behaviorRegistry))
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
