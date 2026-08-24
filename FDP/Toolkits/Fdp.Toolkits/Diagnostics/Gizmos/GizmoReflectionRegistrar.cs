using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// <b>ST-031 — uniform gizmo membership by REFLECTION</b> (Q53 Option A;
    /// <c>DESIGN_Uniform_Gizmo_Membership.md</c> §8.2).
    ///
    /// <para>Discovers every <c>[GizmoProjector]</c> in the loaded assemblies and registers it, replacing
    /// the five hand-rolled per-host family lists. ⭐ <b>The point is that it has NO compile-time reference
    /// to any host assembly</b>, so the cycle that killed the compile-time pack never arises: a pack in
    /// <c>Hrot.Common</c> would have to reference <c>Hrot.IG</c>/<c>Hrot.SimHost</c>/<c>Hrot.CGF</c>, and all
    /// three already reference <c>Hrot.Common</c> (<c>ST-028</c>).</para>
    ///
    /// <para><b>Why reflect-ALL is safe here and NOT for components.</b> A gizmo is data-free: the registry
    /// needs only the component's static <b>id</b>, the draw decision is made per entity by mask bit, and a
    /// projector whose components no entity carries simply draws nothing. ⛔ A component TABLE is a
    /// different thing — memory, SoD, recorder schema, DDS layout — which is why components stay role-gated
    /// and are deliberately NOT reflected (<c>DESIGN_Reflection_World_Priming.md</c>).</para>
    ///
    /// <para>🔒 The ruling this implements: <i>"support all and decide on current presence of
    /// component"</i>. Membership is uniform; presence decides drawing.</para>
    ///
    /// <para>⚠ <b>The one real risk, and the rail that covers it:</b> reflection sees only assemblies
    /// ALREADY LOADED. A mode that never loads a projector's assembly silently declares fewer families than
    /// the source contains — which no compile error would catch. That is what the completeness rail
    /// (<c>ST-033</c>) exists to detect, per mode.</para>
    /// </summary>
    public static class GizmoReflectionRegistrar
    {
        /// <summary>
        /// Registers every discovered projector into <paramref name="statelessRegistry"/>.
        ///
        /// <para><paramref name="gizmoRegistry"/> is accepted for signature parity with the generated
        /// <c>GizmoRegistrar.RegisterAll</c> this replaces — which also does not use it — so a host's call
        /// site changes in name only.</para>
        /// </summary>
        /// <returns>
        /// The projector types registered, so a caller (or a rail) can assert WHAT was found rather than
        /// trusting that something was.
        /// </returns>
        public static IReadOnlyList<Type> RegisterAll(
            GizmoRegistry gizmoRegistry,
            StatelessGizmoRegistry statelessRegistry,
            GizmoSettingsRegistry settings)
        {
            if (statelessRegistry == null) throw new ArgumentNullException(nameof(statelessRegistry));

            var registered = new List<Type>();

            foreach (Type type in DiscoverProjectorTypes())
            {
                var attr = type.GetCustomAttribute<GizmoProjectorAttribute>();
                if (attr == null) continue;

                object instance;
                try
                {
                    instance = Instantiate(type, settings);
                }
                catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException)
                {
                    // ⛔ Deliberately LOUD. The generated registrar could not have compiled with an
                    // un-constructible projector, so reflection must not be the thing that turns an
                    // authoring mistake into a silently missing gizmo.
                    throw new InvalidOperationException(
                        $"GizmoReflectionRegistrar: '{type.FullName}' is decorated with [GizmoProjector] but "
                        + "could not be constructed. A projector needs either a parameterless constructor "
                        + "or one taking GizmoSettingsRegistry.", ex);
                }

                if (instance is IGlobalStatelessGizmo global)
                {
                    // Mirrors the generator: a global projector takes no component mask.
                    statelessRegistry.RegisterGlobal(global);
                    registered.Add(type);
                    continue;
                }

                if (instance is not IStatelessGizmo stateless)
                {
                    // The generator reports this as a diagnostic and skips; match that rather than throw,
                    // so reflection is not stricter than the compile-time path it replaces.
                    continue;
                }

                EnsureComponentIds(attr.RequiredComponents);
                statelessRegistry.Register(stateless, attr.RequiredComponents);
                registered.Add(type);
            }

            return registered;
        }

        /// <summary>
        /// Resolves the component ids a projector's mask needs — <b>ID-ONLY</b>.
        ///
        /// <para>⭐⭐ <b>This is the <c>ST-027</c> correction.</b> That batch called
        /// <c>repo.RegisterComponent&lt;T&gt;()</c>, which creates a real component TABLE. Two consequences
        /// were measured, and both were live:</para>
        ///
        /// <para>🔴 <b>1. It armed a behavioural change.</b> <c>EntityRepository.IsComponentTypeRegistered</c>
        /// is <c>_componentTables.ContainsKey(...)</c> — table-based — and translators are guarded on exactly
        /// that: <c>BehaviorTkbTranslator:52,100</c>, <c>PerceptionTkbTranslator:29,39</c> and
        /// <c>VehicleKinematicsTkbTranslator:56</c> all read <c>if (registered &amp;&amp; !HasComponent) →
        /// add it</c>. So creating tables on IG/SimHost/CGF would have made spawned entities gain
        /// brain/perception components they never carried. ⭐ An id creates no table, so those guards stay
        /// false.</para>
        ///
        /// <para>🔴 <b>2. Id-only is necessary but NOT sufficient</b> for the recorder.
        /// <c>GetOrRegisterManaged</c> defaults <c>_isRecordable</c>/<c>_isSaveable</c> to <c>true</c>
        /// (<c>ComponentType.cs:157-158</c>), and <c>AsyncRecorder.BuildSchemaManifest</c> iterates
        /// <c>GetRecordableTypeIds()</c> — by ID, not by table — so a gizmo-only id would still land in the
        /// <c>.fdp</c> schema. Hence the flags are cleared below.</para>
        ///
        /// <para>⚠⚠ <b>But only for ids this call CREATES</b>, and that condition is load-bearing.
        /// <c>SetRecordable</c> is process-global, and under <c>--mode all</c> a co-tenant may genuinely
        /// simulate one of these components. Clearing the flag unconditionally would drop that host's real
        /// data from the recording. ⭐ Checking <c>GetId(type) == -1</c> first makes it order-safe both ways:
        /// if a simulating host registered it earlier we leave its policy alone, and if it registers later,
        /// <c>RegisterComponent</c> re-applies <c>SetRecordable</c>/<c>SetSaveable</c> from the DataPolicy on
        /// every call (<c>EntityRepository.cs</c>), so the real policy wins.</para>
        /// </summary>
        private static void EnsureComponentIds(Type[] requiredComponents)
        {
            if (requiredComponents == null) return;

            foreach (Type component in requiredComponents)
            {
                bool weAreCreatingIt = ComponentTypeRegistry.GetId(component) == -1;

                int id = ComponentTypeRegistry.GetOrRegisterManaged(component);
                if (id < 0) continue;

                if (weAreCreatingIt)
                {
                    // A gizmo-only component: no host in this process simulates it, so it must not reach
                    // the recorder schema or a save file.
                    ComponentTypeRegistry.SetRecordable(id, false);
                    ComponentTypeRegistry.SetSaveable(id, false);
                }
            }
        }

        private static object Instantiate(Type type, GizmoSettingsRegistry settings)
        {
            // Mirrors the generator's rule exactly: it passes `settings` when the class has a constructor
            // taking GizmoSettingsRegistry, and nothing otherwise.
            var withSettings = type.GetConstructor(new[] { typeof(GizmoSettingsRegistry) });
            if (withSettings != null) return withSettings.Invoke(new object?[] { settings });

            return Activator.CreateInstance(type)
                ?? throw new MissingMethodException(type.FullName, ".ctor");
        }

        /// <summary>
        /// Every <c>[GizmoProjector]</c> type in the loaded, non-system assemblies.
        ///
        /// <para>The assembly filter and the <c>ReflectionTypeLoadException</c> tolerance follow
        /// <c>RepositoryPriming</c>, which has done this in production for the replay browser's world —
        /// same shape, so there is one idiom for "reflect what is loaded", not two.</para>
        /// </summary>
        public static IReadOnlyList<Type> DiscoverProjectorTypes()
        {
            var found = new List<Type>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;

                string? fullName = assembly.FullName;
                if (!string.IsNullOrEmpty(fullName) &&
                    (fullName.StartsWith("System", StringComparison.Ordinal) ||
                     fullName.StartsWith("Microsoft", StringComparison.Ordinal)))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type is null || type.IsAbstract || type.IsInterface) continue;
                    if (type.GetCustomAttribute<GizmoProjectorAttribute>() == null) continue;
                    found.Add(type);
                }
            }

            // Stable order, so two hosts register in the same sequence and a failure is reproducible.
            return found.OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
        }
    }
}
