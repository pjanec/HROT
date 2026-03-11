using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTM;
using Bagira.IG.Components;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using ModuleHost.Core.Abstractions;

namespace Bagira.Map.Common.Replication.Utils
{
    /// <summary>
    /// Compiles a list of <see cref="EntityAttributePayload"/> items into a
    /// deduplicated, overwrite-safe list of ECS component objects.
    ///
    /// <para>
    /// <b>Problem addressed:</b> Naively applying one component per attribute into
    /// a command buffer causes the last write to silently overwrite earlier mutations
    /// when two or more attributes (e.g. <c>eaName</c> and a future <c>eaAffiliation</c>)
    /// target the same ECS component (e.g. <see cref="IgEntityData"/>).
    /// </para>
    ///
    /// <para>
    /// <b>Solution:</b> This compiler groups attributes by their target component type,
    /// reads (or creates) a single baseline instance for each group, applies all
    /// relevant attribute mutations to that instance, and emits it exactly once.
    /// </para>
    ///
    /// <para>
    /// The compiler is used in two scenarios:
    /// <list type="bullet">
    ///   <item>
    ///     <b>Entity creation</b> – pass the component list produced by
    ///     <see cref="DescriptorMapper.MapToComponents"/> as <paramref name="baseComponents"/>
    ///     so attribute overrides are merged on top of descriptor-derived defaults.
    ///   </item>
    ///   <item>
    ///     <b>Entity attribute update</b> – pass existing ECS-component snapshots as
    ///     <paramref name="baseComponents"/> so only the targeted field is mutated
    ///     while all other fields retain their live values.
    ///   </item>
    /// </list>
    /// </para>
    ///
    /// <para>No heap allocations occur if <paramref name="attributes"/> is null or empty.</para>
    /// </summary>
    public static class EntityAttributeCompiler
    {
        /// <summary>
        /// Merges the <paramref name="attributes"/> overrides into a copy of
        /// <paramref name="baseComponents"/> and returns the merged list.
        ///
        /// <para>
        /// The returned list replaces modified component entries with the patched
        /// versions; components not touched by any attribute are carried through
        /// unchanged. New component entries are appended when <paramref name="baseComponents"/>
        /// does not yet contain a matching type.
        /// </para>
        /// </summary>
        /// <param name="attributes">
        /// Fine-grained attribute patches to apply.  May be <c>null</c> or empty,
        /// in which case a copy of <paramref name="baseComponents"/> is returned.
        /// </param>
        /// <param name="baseComponents">
        /// Existing component overrides to merge into (typically produced by
        /// <see cref="DescriptorMapper.MapToComponents"/>).  The list is NOT mutated;
        /// the method produces and returns a new list.
        /// </param>
        /// <param name="geoTransform">
        /// Optional geographic transform required when handling
        /// <see cref="EntityAttribute.eaGeoPosition"/> attributes.
        /// If <c>null</c> and a geo-position attribute is present, the attribute is
        /// silently skipped.
        /// </param>
        /// <returns>
        /// A new list containing all components from <paramref name="baseComponents"/>
        /// with attribute-targeted components replaced/added.
        /// </returns>
        public static List<object> CompileOverrides(
            IReadOnlyList<EntityAttributePayload>? attributes,
            IReadOnlyList<object>? baseComponents,
            IGeographicTransform? geoTransform = null)
        {
            // Start with a shallow copy of the base list.
            var result = baseComponents != null
                ? new List<object>(baseComponents)
                : new List<object>();

            if (attributes == null || attributes.Count == 0)
                return result;

            // ── IgEntityData attributes ───────────────────────────────────────
            // Collect all attributes that map to IgEntityData so we can apply
            // them together to a single instance (avoiding the overwrite flaw).
            bool needsIgData = false;
            foreach (var a in attributes)
                if (a._d == EntityAttribute.eaName) { needsIgData = true; break; }

            if (needsIgData)
            {
                // Find existing IgEntityData in the base list or create a default.
                IgEntityData? existing = null;
                foreach (var c in result)
                    if (c is IgEntityData igData) { existing = igData; break; }

                var patched = existing != null
                    ? new IgEntityData { Name = existing.Name, ForceId = existing.ForceId, CommanderId = existing.CommanderId }
                    : new IgEntityData();

                // Apply all IgEntityData-relevant attributes.
                foreach (var attr in attributes)
                {
                    if (attr._d == EntityAttribute.eaName)
                        patched.Name = attr.Name ?? string.Empty;
                }

                // Replace or append the component in the result list (exactly once).
                int idx = -1;
                for (int i = 0; i < result.Count; i++)
                    if (result[i] is IgEntityData) { idx = i; break; }
                if (idx >= 0)
                    result[idx] = patched;
                else
                    result.Add(patched);
            }

            // ── SimTransform attributes ───────────────────────────────────────
            bool hasGeoAttr = false;
            EntityAttributePayload geoAttr = default;
            foreach (var a in attributes)
                if (a._d == EntityAttribute.eaGeoPosition) { hasGeoAttr = true; geoAttr = a; break; }

            if (hasGeoAttr && geoTransform != null)
            {
                SimTransform? existingSt = null;
                foreach (var c in result)
                    if (c is SimTransform st) { existingSt = st; break; }

                var patched = new SimTransform
                {
                    Position = existingSt.HasValue
                        ? existingSt.Value.Position
                        : Vector3.Zero,
                    Rotation = existingSt.HasValue
                        ? existingSt.Value.Rotation
                        : Quaternion.Identity,
                };

                var geo = geoAttr.GeoPosition;
                patched.Position = geoTransform.ToCartesian(
                    geo.Latitude, geo.Longitude, geo.Altitude);

                int idx = -1;
                for (int i = 0; i < result.Count; i++)
                    if (result[i] is SimTransform) { idx = i; break; }
                if (idx >= 0)
                    result[idx] = patched;
                else
                    result.Add(patched);
            }

            return result;
        }

        /// <summary>
        /// Convenience overload: reads the current ECS state for <paramref name="entity"/>
        /// from <paramref name="world"/>, merges in <paramref name="attributes"/>, and
        /// returns a deduplicated list of component objects to write back.
        ///
        /// <para>
        /// Intended use: <c>UpdateEntityAttributeRequestSystem</c> reads live ECS
        /// components, compiles overrides, then calls
        /// <see cref="FDP.Toolkit.NetworkSpawning.EntityComponentReflector.SetComponent"/>
        /// for each item in the returned list.
        /// </para>
        /// </summary>
        public static List<object> CompileFromWorld(
            IReadOnlyList<EntityAttributePayload>? attributes,
            EntityRepository world,
            Entity entity,
            IGeographicTransform? geoTransform = null)
        {
            if (attributes == null || attributes.Count == 0)
                return new List<object>();

            // Build a snapshot of the current ECS state for affected components.
            var snapshot = new List<object>();

            bool wantsIgData = false;
            bool wantsGeoPosition = false;
            foreach (var a in attributes)
            {
                if (a._d == EntityAttribute.eaName)        wantsIgData       = true;
                if (a._d == EntityAttribute.eaGeoPosition) wantsGeoPosition  = true;
                if (wantsIgData && wantsGeoPosition)        break;
            }

            if (wantsIgData)
            {
                var view = (ISimulationView)world;
                var ig = world.HasManagedComponent<IgEntityData>(entity)
                    ? view.GetManagedComponentRO<IgEntityData>(entity)
                    : null;

                snapshot.Add(ig != null
                    ? new IgEntityData { Name = ig.Name, ForceId = ig.ForceId, CommanderId = ig.CommanderId }
                    : new IgEntityData());
            }

            if (wantsGeoPosition && geoTransform != null)
            {
                if (world.HasComponent<SimTransform>(entity))
                    snapshot.Add(world.GetComponentRO<SimTransform>(entity));
                else
                    snapshot.Add(new SimTransform { Rotation = Quaternion.Identity });
            }

            return CompileOverrides(attributes, snapshot, geoTransform);
        }
    }
}
