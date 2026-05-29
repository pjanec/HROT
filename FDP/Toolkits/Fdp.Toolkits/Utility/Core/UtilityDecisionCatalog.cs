using System;
using System.Collections.Generic;
using System.Reflection;

namespace Fdp.Toolkit.Utility
{
    // ── Registry ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Holds registered Utility AI decision definitions indexed by integer ID.
    /// Thread-safe for reads after construction; not for concurrent writes.
    /// </summary>
    public sealed class UtilityRegistry
    {
        private readonly Dictionary<int, (UtilityDecisionDef Def, float HysteresisBonus)> _map = new();

        /// <summary>
        /// Registers <paramref name="def"/> under <paramref name="id"/>.
        /// A subsequent call with the same <paramref name="id"/> replaces the previous entry.
        /// </summary>
        public void Register(int id, UtilityDecisionDef def, float hysteresisBonus = 0f)
            => _map[id] = (def, hysteresisBonus);

        /// <summary>
        /// Looks up a decision by <paramref name="id"/>.
        /// Returns <c>false</c> when not found; <paramref name="def"/> and
        /// <paramref name="hysteresisBonus"/> are set to their defaults in that case.
        /// </summary>
        public bool TryGet(int id, out UtilityDecisionDef? def, out float hysteresisBonus)
        {
            if (_map.TryGetValue(id, out var entry))
            {
                def             = entry.Def;
                hysteresisBonus = entry.HysteresisBonus;
                return true;
            }
            def             = null;
            hysteresisBonus = 0f;
            return false;
        }

        /// <summary>
        /// Merges all entries from <paramref name="source"/> into this registry,
        /// overwriting any existing entry with the same ID.
        /// </summary>
        internal void MergeFrom(UtilityRegistry source)
        {
            foreach (var kv in source._map)
                _map[kv.Key] = kv.Value;
        }
    }

    // ── Catalog ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Discovers all Utility AI decision definitions in the loaded assemblies and registers
    /// them into a <see cref="UtilityRegistry"/>.
    /// <para>
    /// Scans for classes that carry <see cref="UtilityDecisionAttribute"/> and implement
    /// <see cref="IUtilityDecisionDefinition"/>; each must expose a static
    /// <c>Build(IUtilityDecisionBuilder)</c> method.
    /// </para>
    /// </summary>
    public static class UtilityDecisionCatalog
    {
        private static UtilityRegistry? _shared;

        /// <summary>
        /// The last registry built by <see cref="RegisterAll"/>.
        /// Initialized to an empty registry so callers never receive <c>null</c>.
        /// </summary>
        public static UtilityRegistry Shared => _shared ??= new UtilityRegistry();

        /// <summary>
        /// Scans all currently-loaded assemblies for <see cref="IUtilityDecisionDefinition"/>
        /// types decorated with <see cref="UtilityDecisionAttribute"/> and a static
        /// <c>Build(IUtilityDecisionBuilder)</c> method.  Registers each found definition
        /// into a new <see cref="UtilityRegistry"/> which is also stored in <see cref="Shared"/>.
        /// </summary>
        public static void RegisterAll(out UtilityRegistry registry)
        {
            registry = new UtilityRegistry();
            var markerType    = typeof(IUtilityDecisionDefinition);
            var attrType      = typeof(UtilityDecisionAttribute);
            var builderType   = typeof(IUtilityDecisionBuilder);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (!markerType.IsAssignableFrom(type) || type.IsInterface || type.IsAbstract)
                        continue;
                    var attr = type.GetCustomAttribute<UtilityDecisionAttribute>();
                    if (attr == null) continue;
                    var buildMethod = type.GetMethod("Build",
                        BindingFlags.Public | BindingFlags.Static,
                        null, new[] { builderType }, null);
                    if (buildMethod == null) continue;

                    var builder = new UtilityDecisionBuilder();
                    buildMethod.Invoke(null, new object[] { builder });
                    var def = builder.Build(attr);
                    int id  = ComputeId(attr.AssetId);
                    registry.Register(id, def, attr.HysteresisBonus);
                }
            }

            _shared = registry;
        }

        /// <summary>
        /// Derives the integer decision ID from <paramref name="assetId"/> via FNV-1a-32.
        /// </summary>
        public static int ComputeId(string assetId) => (int)In.Fnv1a32(assetId);
    }

    // ── Manifest entry ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Lightweight, allocation-free summary of a single <see cref="IUtilityDecisionDefinition"/>
    /// emitted by the source generator into the generated decision catalog.
    /// </summary>
    public readonly struct UtilityDecisionManifestEntry
    {
        /// <summary>FNV-1a-32 integer ID of the decision asset.</summary>
        public int BlueprintId { get; }

        /// <summary>Human-readable display name from the <c>[UtilityDecision]</c> attribute.</summary>
        public string DisplayName { get; }

        /// <summary>
        /// <c>true</c> when the generator could fully enumerate all options and consider calls
        /// in the Build method; <c>false</c> when the body is too dynamic (loops, branches,
        /// or local variables) to statically analyse.
        /// </summary>
        public bool ManifestIsFull { get; }

        /// <summary>Number of Option/CandidateOption calls in the Build method (0 when <see cref="ManifestIsFull"/> is false).</summary>
        public int OptionCount { get; }

        /// <summary>Number of Consider calls across all options (0 when <see cref="ManifestIsFull"/> is false).</summary>
        public int ConsiderCount { get; }

        public UtilityDecisionManifestEntry(
            int    blueprintId,
            string displayName,
            bool   manifestIsFull,
            int    optionCount,
            int    considerCount)
        {
            BlueprintId    = blueprintId;
            DisplayName    = displayName;
            ManifestIsFull = manifestIsFull;
            OptionCount    = optionCount;
            ConsiderCount  = considerCount;
        }
    }
}
