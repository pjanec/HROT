using System;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2377</c> — marks a static <c>IEnumerable&lt;string&gt; Validate(string json)</c> as the OPTIONAL extra check of one behaviour's
    /// params, run by <see cref="BehaviorParamsValidation.Validate"/> after the strict DTO read. ⛔ SIDE-EFFECT-FREE: no world, no resolver call —
    /// it must be safe at scenario-load prepare, where the entities do not exist yet. It returns the problems (empty = fine).
    /// <para>Discovered beside the params DTO declarations (<c>BehaviorSchemaDiscovery.BindValidators</c>, called by the one loader
    /// <c>CgfBehaviorSetup.LoadFromAiAssembly</c>); the NAME is the behaviour's registry name, as on <see cref="BehaviorResolverAttribute"/>.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BehaviorValidatorAttribute : Attribute
    {
        public string BehaviorName { get; }
        public BehaviorValidatorAttribute(string behaviorName) { BehaviorName = behaviorName; }
    }
}
