using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Definitions.Behavior;
using Hrot.Presentation.Behavior;

namespace Hrot.CGF.Configuration
{
    /// <summary>
    /// Bridge that loads CGF behavior definitions dynamically from the isolated
    /// <c>Hrot.AI.Behaviors</c> assembly at startup.
    ///
    /// <para>
    /// <c>Hrot.CGF</c> carries <b>no compile-time dependency</b> on
    /// <c>Hrot.AI.Behaviors</c>.  All BTree node logic, action delegates, and
    /// interpreter construction are owned exclusively by the AI assembly so that
    /// the editor's <c>FbtAssemblyHotReloader</c> can reload them independently.
    /// </para>
    /// </summary>
    public static class CgfBehaviorSetup
    {
        /// <summary>
        /// Dynamically loads <c>Hrot.AI.Behaviors.dll</c> from the deployment directory
        /// into a dedicated <see cref="AssemblyLoadContext"/> and invokes
        /// <c>AiBehaviorFactory.BuildRegistrationAction</c> via reflection to populate
        /// <paramref name="registry"/> with all CGF Brain-tier behavior definitions.
        ///
        /// <para>
        /// This path is used by <see cref="Hrot.CGF.CgfSubsystem"/> at startup.
        /// The editor uses <c>FbtAssemblyHotReloader.TriggerInitialLoad()</c> instead
        /// so that the same code path is exercised on every hot-reload.
        /// </para>
        /// </summary>
        /// <param name="geoTransform">
        /// Geographic coordinate transform used by MoveToLocation.  May be <c>null</c>
        /// in contexts that use only Cartesian coordinates.
        /// </param>
        public static void LoadFromAiAssembly(
            BehaviorRegistry registry,
            IGeographicTransform? geoTransform,
            NetworkEntityMap entityMap)
        {
            if (registry  == null) throw new ArgumentNullException(nameof(registry));
            if (entityMap == null) throw new ArgumentNullException(nameof(entityMap));

            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Hrot.AI.Behaviors.dll");
            if (!File.Exists(dllPath))
                throw new FileNotFoundException(
                    $"Hrot.AI.Behaviors.dll not found at '{dllPath}'.  " +
                    "Build the Hrot.AI.Behaviors project before starting the cluster node.", dllPath);

            // Load into a non-collectible ALC so the Default ALC cannot lock the file,
            // and so the interpreter delegates remain valid for the process lifetime.
            var alc = new AssemblyLoadContext("AiBehaviors.Startup", isCollectible: false);
            Assembly aiAssembly;
            using (var fs = File.OpenRead(dllPath))
                aiAssembly = alc.LoadFromStream(fs);

            var factoryType = aiAssembly.GetType("Hrot.AI.Behaviors.AiBehaviorFactory");
            var buildMethod = factoryType?.GetMethod(
                "BuildRegistrationAction",
                BindingFlags.Public | BindingFlags.Static);

            if (buildMethod == null)
                throw new InvalidOperationException(
                    "AiBehaviorFactory.BuildRegistrationAction not found in Hrot.AI.Behaviors.dll. " +
                    "Rebuild the assembly and retry.");

            var applyAction = (Action<BehaviorRegistry>?)buildMethod.Invoke(
                null, new object?[] { geoTransform, entityMap });

            applyAction?.Invoke(registry);
        }

        /// <summary>
        /// Creates a <see cref="ScenarioBehaviorRemapper"/> pre-registered with all
        /// CGF behavior param DTO types that carry <c>[RemapNetworkId]</c> properties.
        /// Used by load handlers to rewrite network IDs after two-pass ID allocation.
        /// </summary>
        public static ScenarioBehaviorRemapper CreateBehaviorRemapper()
        {
            var remapper = new ScenarioBehaviorRemapper();
            BehaviorSchemaDiscovery.AutoRegister(new BehaviorUiRegistry(), remapper);
            return remapper;
        }
    }
}
