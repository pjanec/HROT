using System.Reflection;
using Fdp.Toolkit.Behavior;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.Presentation.Behavior
{
    /// <summary>
    /// Scans the <c>Hrot.Core</c> assembly for types decorated with
    /// <see cref="BehaviorContractAttribute"/> and registers each one with both
    /// <see cref="BehaviorUiRegistry"/> and <see cref="ScenarioBehaviorRemapper"/>.
    ///
    /// <para>Intended to replace the manual <c>Register&lt;T&gt;</c> call sites in
    /// <c>BehaviorUiSetup</c> and <c>CgfBehaviorSetup</c> (Phase 5b, BATCH-06).</para>
    /// </summary>
    public static class BehaviorSchemaDiscovery
    {
        /// <summary>
        /// Registers all behavior parameter DTOs found in <c>Hrot.Core</c> with
        /// <paramref name="uiRegistry"/> and <paramref name="remapper"/>.
        /// </summary>
        public static void AutoRegister(BehaviorUiRegistry uiRegistry, ScenarioBehaviorRemapper remapper)
        {
            var uiRegMethod  = typeof(BehaviorUiRegistry).GetMethod("Register")!;
            var remapMethod  = typeof(ScenarioBehaviorRemapper).GetMethod("Register")!;

            var dtoTypes = typeof(BehaviorContractAttribute).Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<BehaviorContractAttribute>() != null);

            foreach (var type in dtoTypes)
            {
                var attr = type.GetCustomAttribute<BehaviorContractAttribute>()!;
                uiRegMethod.MakeGenericMethod(type).Invoke(uiRegistry, new object[] { attr.BehaviorName });
                remapMethod.MakeGenericMethod(type).Invoke(remapper,   new object[] { attr.BehaviorName });
            }
        }
    }
}
