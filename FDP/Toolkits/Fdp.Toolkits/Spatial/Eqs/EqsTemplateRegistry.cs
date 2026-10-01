using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// The production <see cref="IEqsTemplateRegistry"/>: every <see cref="EqsTemplateAttribute"/> class
    /// found in the loaded assemblies, keyed by the canonical <see cref="BlueprintIdOf"/> of its AssetId.
    /// </summary>
    /// <remarks>
    /// <para>⭐ <b>Why it exists (<c>CE-465</c>).</b> Until it did, nothing outside tests installed a
    /// registry, so <c>EqsSolverSystem</c> answered every sensor with its empty-result stub. The
    /// <c>[EqsTemplate]</c> source generator registers a <c>BlueprintDefinition</c> (name + hash) and
    /// drops the template itself, so it cannot back a registry.</para>
    ///
    /// <para>⭐ <b>One identity.</b> The key is FNV-1a over the AssetId GUID's 16 bytes — the hash the
    /// blueprint compiler bakes into a <c>SpawnEqsSensor</c> node — so a blueprint and a C# caller reach
    /// the same template. A template whose own <see cref="EqsQueryTemplate.BlueprintId"/> differs (a
    /// hand-typed constant, e.g. <c>FindCoverFromTarget</c>) is ALSO registered under that value, so its
    /// existing C# callers keep resolving.</para>
    ///
    /// <para>📄 <c>docs/designs/eqs-2/EQS_Design_v1.3_final.md</c> §17.</para>
    /// </remarks>
    public sealed class EqsTemplateRegistry : IEqsTemplateRegistry
    {
        /// <summary>One discovered template: its asset identity and the class that builds it.</summary>
        public readonly record struct Entry(Guid AssetId, uint BlueprintId, string Name);

        private readonly Dictionary<uint, EqsQueryTemplate> _byId = new();
        private readonly List<Entry> _entries = new();

        /// <summary>The templates registered here, in registration order.</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>The canonical BlueprintId of a template asset: FNV-1a over the GUID's bytes.</summary>
        public static uint BlueprintIdOf(Guid assetId) => unchecked((uint)BlueprintIdHash.Compute(assetId));

        /// <summary>
        /// Registers <paramref name="template"/> under the canonical id of <paramref name="assetId"/>,
        /// and under the template's own <see cref="EqsQueryTemplate.BlueprintId"/> when that differs.
        /// </summary>
        /// <exception cref="InvalidOperationException">Two templates claim the same id (§6.3).</exception>
        public void Register(Guid assetId, EqsQueryTemplate template, string name)
        {
            uint canonical = BlueprintIdOf(assetId);
            Add(canonical, template, name);
            if (template.BlueprintId != 0 && template.BlueprintId != canonical)
                Add(template.BlueprintId, template, name);
            _entries.Add(new Entry(assetId, canonical, name));
        }

        /// <inheritdoc/>
        public bool TryGetTemplate(uint blueprintId, out EqsQueryTemplate template)
            => _byId.TryGetValue(blueprintId, out template);

        private void Add(uint id, EqsQueryTemplate template, string name)
        {
            if (_byId.ContainsKey(id))
                throw new InvalidOperationException(
                    $"EQS template id collision: 0x{id:X8} ('{name}') is already registered.");
            _byId[id] = template;
        }

        /// <summary>
        /// Builds a registry from every <see cref="EqsTemplateAttribute"/> class in
        /// <paramref name="assemblies"/> that exposes <c>static EqsQueryTemplate Build(IEqsTemplateBuilder)</c>.
        /// </summary>
        public static EqsTemplateRegistry Discover(IEnumerable<Assembly> assemblies)
        {
            var registry = new EqsTemplateRegistry();
            var builder  = new EqsTemplateBuilder();

            foreach (var type in assemblies.SelectMany(LoadableTypes).OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                var attr = type.GetCustomAttribute<EqsTemplateAttribute>(inherit: false);
                if (attr is null || !Guid.TryParse(attr.AssetId, out var assetId)) continue;

                var build = type.GetMethod(
                    "Build", BindingFlags.Public | BindingFlags.Static, binder: null,
                    types: new[] { typeof(IEqsTemplateBuilder) }, modifiers: null);
                if (build is null || build.ReturnType != typeof(EqsQueryTemplate)) continue;

                var template = (EqsQueryTemplate)build.Invoke(null, new object[] { builder })!;
                registry.Register(assetId, template, type.FullName ?? type.Name);
            }

            return registry;
        }

        /// <summary>
        /// The assemblies that can declare templates: this toolkit and every loaded assembly that
        /// references it.
        /// </summary>
        public static IEnumerable<Assembly> CandidateAssemblies()
        {
            var toolkit     = typeof(EqsQueryTemplate).Assembly;
            string toolkitName = toolkit.GetName().Name!;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                if (asm == toolkit
                    || asm.GetReferencedAssemblies().Any(r => string.Equals(r.Name, toolkitName, StringComparison.Ordinal)))
                    yield return asm;
            }
        }

        /// <summary>
        /// Installs a discovered registry as <paramref name="world"/>'s <see cref="IEqsTemplateRegistry"/>
        /// singleton unless one is already present, and returns the one in force.
        /// </summary>
        /// <remarks>The singleton is synced into solver snapshots (<c>EntityRepository.Sync.cs</c>, id 210).</remarks>
        public static IEqsTemplateRegistry InstallDefault(EntityRepository world)
        {
            if (world is null) throw new ArgumentNullException(nameof(world));
            if (world.HasSingletonManaged<IEqsTemplateRegistry>())
                return world.GetSingletonManaged<IEqsTemplateRegistry>()!;

            var registry = Discover(CandidateAssemblies());
            world.SetSingletonManaged<IEqsTemplateRegistry>(registry);
            return registry;
        }

        private static IEnumerable<Type> LoadableTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
        }
    }
}
