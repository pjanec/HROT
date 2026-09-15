using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐⭐ <b>Resolves the component-id sets declared by <see cref="BirthCriticalAttribute"/> and
    /// <see cref="PerInstanceValueAttribute"/> — scanned ONCE, cached, process-wide.</b>
    ///
    /// <para>📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a.</para>
    ///
    /// <para>⭐⭐ <b>Why a scan rather than a registry walk.</b> <c>ComponentTypeRegistry</c> only knows the
    /// types something has already touched, and these sets are needed at TKB-registration time — which can
    /// precede the first <c>ComponentType&lt;T&gt;.ID</c> access for the very components being asked about.
    /// ⇒ the assembly scan is the only complete answer. 📌 It is also the established house pattern:
    /// <c>RecordingExportService.cs:815-824</c> already scans loaded assemblies for
    /// <see cref="ComponentIdAttribute"/> for exactly this reason.</para>
    ///
    /// <para>⭐ <b>Ids are stable, so the cache cannot drift.</b> <see cref="ComponentIdAttribute"/> is
    /// mandatory — <c>ComponentType.cs</c> THROWS for a component type without one — so an id is a compile-time
    /// constant, identical in every process and on every node.</para>
    ///
    /// <para>⚠ <b>Cost.</b> One reflection pass over the loaded assemblies, on first access, behind a lock.
    /// Every later read is an array reference. ⛔ Do not call <see cref="Invalidate"/> from production code;
    /// it exists for tests that load a component assembly late.</para>
    /// </summary>
    public static class ComponentAttributeSets
    {
        private static readonly object _gate = new object();
        private static int[]? _birthCritical;
        private static int[]? _perInstanceValue;
        private static int[]? _requiresPeerInit;

        /// <summary>
        /// ⭐ Component ids declared <see cref="BirthCriticalAttribute"/> — <b>the creator owns these at
        /// birth whatever its role</b>. Ascending, never null, safe to hold.
        ///
        /// <para>⚠ This is the WHOLE set, unfiltered by template: over-declaring is free because the create
        /// leg intersects it with the entity's live component mask. See the attribute for why that removes
        /// the need for any per-template list.</para>
        /// </summary>
        public static IReadOnlyList<int> BirthCritical
        {
            get { EnsureScanned(); return _birthCritical!; }
        }

        /// <summary>
        /// ⭐ Component ids declared <see cref="PerInstanceValueAttribute"/> — <b>the candidate set for the
        /// promotion gate</b>. Ascending, never null, safe to hold.
        ///
        /// <para>⛔⛔ <b>This is NOT the mandatory set.</b> It is intersected with what the template produces,
        /// what this host can ingress, and what this host registers — see the attribute. Using it raw would
        /// create hard requirements that never arrive.</para>
        /// </summary>
        public static IReadOnlyList<int> PerInstanceValue
        {
            get { EnsureScanned(); return _perInstanceValue!; }
        }

        /// <summary>
        /// ⭐ Component ids declared <see cref="RequiresPeerInitAttribute"/> — <b>a peer node must initialise
        /// these</b> before the entity is fully live (the reliable-init barrier's role-filter, CE-283 §3b).
        /// Ascending, never null, safe to hold.
        ///
        /// <para>⚠ The WHOLE set, unfiltered by template — the creator's role-filter intersects it with the
        /// entity's live component mask, exactly as the birth-critical create leg does.</para>
        /// </summary>
        public static IReadOnlyList<int> RequiresPeerInit
        {
            get { EnsureScanned(); return _requiresPeerInit!; }
        }

        /// <summary>
        /// ⚠ <b>Tests only.</b> Drops the cache so the next read rescans. ⛔ Production code must never call
        /// this — the sets are compile-time facts and a mid-run change means a template's derived view
        /// silently changes meaning.
        /// </summary>
        public static void Invalidate()
        {
            lock (_gate) { _birthCritical = null; _perInstanceValue = null; _requiresPeerInit = null; }
        }

        private static void EnsureScanned()
        {
            if (_birthCritical != null && _perInstanceValue != null && _requiresPeerInit != null) return;

            lock (_gate)
            {
                if (_birthCritical != null && _perInstanceValue != null && _requiresPeerInit != null) return;

                var birth = new SortedSet<int>();
                var perInstance = new SortedSet<int>();
                var requiresPeerInit = new SortedSet<int>();

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        // A partially loadable assembly still contributes the types it could load.
                        types = ex.Types.Where(t => t != null).ToArray()!;
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var type in types)
                    {
                        if (type == null) continue;

                        bool isBirth = type.GetCustomAttribute<BirthCriticalAttribute>(inherit: false) != null;
                        bool isPerInstance = type.GetCustomAttribute<PerInstanceValueAttribute>(inherit: false) != null;
                        bool isPeerInit = type.GetCustomAttribute<RequiresPeerInitAttribute>(inherit: false) != null;
                        if (!isBirth && !isPerInstance && !isPeerInit) continue;

                        // ⛔⛔ LOUD, not skipped. Either attribute on a type with no [ComponentId] is a
                        //   programmer error whose symptom would otherwise be an entity that silently does
                        //   not move (birth-critical) or a ghost that silently never promotes
                        //   (per-instance). ⚠ This is the one place the existing reflection scan's bare
                        //   `catch { continue; }` must NOT be copied.
                        var idAttr = type.GetCustomAttribute<ComponentIdAttribute>(inherit: false);
                        if (idAttr == null)
                        {
                            string which = isBirth ? nameof(BirthCriticalAttribute)
                                         : isPerInstance ? nameof(PerInstanceValueAttribute)
                                         : nameof(RequiresPeerInitAttribute);
                            throw new InvalidOperationException(
                                $"Component type '{type.FullName}' declares [{which}] but has no " +
                                "[ComponentId]. These attributes resolve to component IDS, so the type " +
                                "must declare one.");
                        }

                        if (isBirth) birth.Add(idAttr.Id);
                        if (isPerInstance) perInstance.Add(idAttr.Id);
                        if (isPeerInit) requiresPeerInit.Add(idAttr.Id);
                    }
                }

                _birthCritical = birth.ToArray();
                _perInstanceValue = perInstance.ToArray();
                _requiresPeerInit = requiresPeerInit.ToArray();
            }
        }
    }
}
