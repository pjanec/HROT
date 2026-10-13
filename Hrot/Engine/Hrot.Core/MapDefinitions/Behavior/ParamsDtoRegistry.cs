using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Hrot.Map.Definitions.Behavior.Orders;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>⭐ <c>CE-2134</c> — one behaviour's declared params contract (<see cref="BehaviorAttribute"/>).</summary>
    public sealed record BehaviorDeclaration(string Name, Type ParamsType, BehaviorCategory Categories, string DeclaredOn);

    /// <summary>⭐ <c>CE-3185</c> (F9) — what an authored data struct is FOR; one DTO may serve several.</summary>
    [Flags]
    public enum DtoPurpose : byte
    {
        None       = 0,
        /// <summary>A blackboard / blueprint variable type (every registered STRUCT is one).</summary>
        Variable   = 1,
        /// <summary>A params DTO: a behaviour's public JSON contract (<c>[ParamsDto]</c>, <c>[Order]</c>, or named by a behaviour).</summary>
        Params     = 2,
        /// <summary>An order's DTO (<c>[Order]</c>).</summary>
        Order      = 4,
        /// <summary>A unit memory (<c>[UnitMemory]</c>).</summary>
        UnitMemory = 8,
    }

    /// <summary>⭐ <c>CE-3185</c> (F1, R-2000) — where a DTO is AUTHORED, a locator for "open source / open asset", never an identity
    /// (the identity is the CLR type FQN). A generated DTO's origin is its asset (<see cref="AssetId"/>); a hand-written one's is
    /// the file + line the compiler recorded on its <see cref="DtoAttribute"/>. <see cref="None"/> when the type carries no
    /// <see cref="DtoAttribute"/> (a DTO a behaviour names without marking it — the CE-3185 rail fails that).</summary>
    public readonly record struct DtoOrigin(string? SourceFile, int SourceLine, string? AssetId)
    {
        public static DtoOrigin None => default;
        public bool IsAsset => !string.IsNullOrEmpty(AssetId);
        public bool IsKnown => IsAsset || !string.IsNullOrEmpty(SourceFile);

        /// <summary>The origin one attribute records: its asset when it names one, else its file + line.</summary>
        public static DtoOrigin Of(DtoAttribute a)
            => !string.IsNullOrEmpty(a.AssetId) ? new DtoOrigin(null, 0, a.AssetId)
             : string.IsNullOrEmpty(a.SourceFile) ? None : new DtoOrigin(a.SourceFile, a.SourceLine, null);
    }

    /// <summary>⭐ <c>CE-2134</c> — one registered DTO: the behaviours that take it, and its order name when it is an
    /// <c>[Order]</c> DTO (a string — menus read it and never the AI assembly's <c>OrderKind</c>, §2.3l). ⭐ <c>CE-3185</c> — plus
    /// its <see cref="Purposes"/> and its <see cref="Origin"/>.</summary>
    public sealed record ParamsDtoInfo(Type Type, string? OrderName, IReadOnlyList<string> BehaviorNames)
    {
        public DtoPurpose Purposes { get; init; }
        public DtoOrigin Origin { get; init; }
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-2134</c> (B2, <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §5b) — THE ONE SCAN of the params contract:
    /// every <see cref="BehaviorAttribute"/> (on methods, types and assemblies) and — ⭐ <c>CE-3185</c> (§13.7, F9) — every type that
    /// carries a <see cref="DtoAttribute"/> (<c>[Dto]</c>, <c>[ParamsDto]</c>, <c>[Order]</c>, <c>[UnitMemory]</c>) in the
    /// LOADED assemblies: one registry for every authored data struct, each with its purposes and origin. Its readers — the runtime binding (<c>BehaviorSchemaDiscovery</c>), the mission pickers
    /// (<see cref="Tkb.BehaviorCatalog"/>) and the blueprint palette — no longer scan for themselves.
    /// <para>⚠ It sees only loaded assemblies. <c>Hrot.Core</c> cannot reference the AI behaviours assembly (a cycle), so
    /// <see cref="Default"/> first tries to load each of <see cref="KnownBehaviourAssemblies"/> by name; a host that does not ship
    /// one simply has no declarations from it. A later assembly load invalidates the cached scan.</para>
    /// <para>⛔ Load check: two declarations of one behaviour name, or one with no <c>Params</c> (P1), THROW — one producer per
    /// behaviour (R-132).</para>
    /// </summary>
    public sealed class ParamsDtoRegistry
    {
        /// <summary>Assemblies that carry behaviour declarations but that <c>Hrot.Core</c> cannot reference; loaded by name, best effort.</summary>
        public static readonly IReadOnlyList<string> KnownBehaviourAssemblies = new[] { "Hrot.AI.Behaviors" };

        private static readonly object Gate = new();
        private static ParamsDtoRegistry? _default;
        private static bool _hooked;
        private static long _generation;   // bumped on every assembly load (CE-3185: a scan that raced one is not cached)

        private readonly Dictionary<string, BehaviorDeclaration> _byName;

        private readonly Dictionary<Type, ParamsDtoInfo> _byType;

        private ParamsDtoRegistry(Dictionary<string, BehaviorDeclaration> byName, IReadOnlyList<ParamsDtoInfo> all)
        {
            _byName = byName;
            Behaviors = byName.Values.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
            All = all;
            Dtos = all.Where(d => (d.Purposes & DtoPurpose.Params) != 0).ToList();
            _byType = all.ToDictionary(d => d.Type);
        }

        /// <summary>Every declared behaviour, by name (ordinal).</summary>
        public IReadOnlyList<BehaviorDeclaration> Behaviors { get; }

        /// <summary>Every PARAMS DTO — named by a behaviour, <c>[ParamsDto]</c> or <c>[Order]</c> — by type name.</summary>
        public IReadOnlyList<ParamsDtoInfo> Dtos { get; }

        /// <summary>⭐ <c>CE-3185</c> — every registered DTO (every <see cref="DtoAttribute"/> carrier plus every params DTO), by type name.</summary>
        public IReadOnlyList<ParamsDtoInfo> All { get; }

        /// <summary>⭐ <c>CE-3185</c> — the registry's entry for <paramref name="type"/>.</summary>
        public bool TryGetDto(Type type, out ParamsDtoInfo info) => _byType.TryGetValue(type, out info!);

        public bool TryGetBehavior(string name, out BehaviorDeclaration declaration)
            => _byName.TryGetValue(name, out declaration!);

        /// <summary>The behaviour names whose categories include <paramref name="category"/>, by name.</summary>
        public IReadOnlyList<string> NamesWithCategory(BehaviorCategory category)
            => Behaviors.Where(d => (d.Categories & category) == category && category != BehaviorCategory.None)
                        .Select(d => d.Name).ToList();

        /// <summary>The process-wide registry over the loaded assemblies (rebuilt after another assembly loads).</summary>
        /// <remarks>⛔ <c>CE-3185</c> — the scan runs OUTSIDE <see cref="Gate"/>. Scanning loads assemblies (<c>GetTypes</c>, attribute
        /// construction) and every load runs the <c>AssemblyLoad</c> hook, which takes <see cref="Gate"/>: holding it across the scan
        /// could let a load on another thread wait for the lock while this thread waited for that load. ⚠ Suspected, not proven: a
        /// <c>Hrot.Presentation.Tests</c> run hung 17 min once the scan widened to every <c>Fdp.Core</c> referencer, and did not reproduce. A scan that raced a load is returned but not cached
        /// (<see cref="_generation"/>).</remarks>
        public static ParamsDtoRegistry Default
        {
            get
            {
                bool hook;
                long generation;
                lock (Gate)
                {
                    if (_default != null) return _default;
                    hook = !_hooked;
                    if (hook)
                    {
                        _hooked = true;
                        AppDomain.CurrentDomain.AssemblyLoad += (_, _) => { lock (Gate) { _default = null; _generation++; } };
                    }
                    generation = _generation;
                }
                if (hook)
                    foreach (var name in KnownBehaviourAssemblies) TryLoad(name);
                var scanned = Scan(LatestPerName(AppDomain.CurrentDomain.GetAssemblies()));
                lock (Gate)
                {
                    if (_default != null) return _default;
                    if (_generation == generation) _default = scanned;
                    return scanned;
                }
            }
        }

        /// <summary>
        /// ⭐ <see cref="Default"/>, but with <paramref name="pinned"/> standing for its assembly name instead of the last-loaded
        /// copy. ⛔ A caller that registers definitions from ONE copy of a hot-reloadable assembly must bind contracts from that
        /// same copy — otherwise <c>MoveOrder</c> from the newer copy is not the <c>MoveOrder</c> its definitions were built
        /// with (measured: <c>CE2142_EveryClassOrderBehaviour_TakesItsOrdersDto</c> red 3/3 after the hot-reload rails).
        /// Returns <see cref="Default"/> itself when it already holds <paramref name="pinned"/> — the production case.
        /// </summary>
        public static ParamsDtoRegistry Pinning(Assembly pinned)
        {
            if (pinned is null) throw new ArgumentNullException(nameof(pinned));
            var all = Default;   // also loads the known behaviour assemblies on first use
            string name = pinned.GetName().Name ?? pinned.FullName ?? "";
            var live = LatestPerName(AppDomain.CurrentDomain.GetAssemblies()).ToList();
            if (live.Contains(pinned)) return all;
            return Scan(live.Select(a => (a.GetName().Name ?? a.FullName ?? "") == name ? pinned : a));
        }

        /// <summary>Scans exactly <paramref name="assemblies"/> (a rail scans a known set).</summary>
        public static ParamsDtoRegistry Scan(IEnumerable<Assembly> assemblies)
        {
            var declarations = new List<(BehaviorAttribute, string)>();
            var dtoTypes = new List<Type>();   // ⭐ CE-3185 — every DtoAttribute carrier ([Dto], [ParamsDto], [Order], [UnitMemory])
            foreach (var asm in assemblies.Distinct())
            {
                if (asm.IsDynamic || !CanDeclare(asm)) continue;
                foreach (var a in asm.GetCustomAttributes<BehaviorAttribute>()) declarations.Add((a, $"assembly {asm.GetName().Name}"));

                foreach (var t in LoadableTypes(asm))
                {
                    foreach (var a in t.GetCustomAttributes<BehaviorAttribute>(inherit: false)) declarations.Add((a, t.FullName ?? t.Name));
                    if (t.IsDefined(typeof(DtoAttribute), inherit: false)) dtoTypes.Add(t);
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                                                   | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        object[] attrs;
                        try { attrs = m.GetCustomAttributes(typeof(BehaviorAttribute), inherit: false); }
                        catch { continue; }
                        foreach (BehaviorAttribute a in attrs) declarations.Add((a, $"{t.FullName}.{m.Name}"));
                    }
                }
            }
            return FromDeclarations(declarations, dtoTypes);
        }

        /// <summary>
        /// The registry over explicit declarations (each with where it was found) and the types that carry a <see cref="DtoAttribute"/>
        /// — what <see cref="Scan"/> builds from reflection; a rail builds one by hand. ⛔ Throws on the load-check failures: a
        /// behaviour with no <c>Params</c>, a behaviour declared twice, or ⭐ <c>CE-3185</c> two <c>[ParamsDto]</c> / <c>[Order]</c> types
        /// with one simple name (menus, order names and the UI show the simple name).
        /// </summary>
        public static ParamsDtoRegistry FromDeclarations(IEnumerable<(BehaviorAttribute Declaration, string DeclaredOn)> declarations,
            IEnumerable<Type> dtoTypes)
        {
            var byName = new Dictionary<string, BehaviorDeclaration>(StringComparer.Ordinal);
            foreach (var (a, on) in declarations)
            {
                if (a.Params is null)
                    throw new InvalidOperationException(
                        $"CE-2134: [Behavior(\"{a.Name}\")] on {on} names no Params — every behaviour names exactly one params DTO; " +
                        "one that takes nothing names NoParams (docs/DESIGN_Orders_Params_Entity_Classes.md §1 P1).");
                if (byName.TryGetValue(a.Name, out var first))
                    throw new InvalidOperationException(
                        $"CE-2134: behaviour '{a.Name}' is declared twice, on {first.DeclaredOn} and on {on} — one declaration per behaviour (R-132).");
                byName[a.Name] = new BehaviorDeclaration(a.Name, a.Params, a.Categories, on);
            }

            var marked = dtoTypes.Distinct().ToList();
            var types = byName.Values.Select(d => d.ParamsType).Concat(marked).Distinct().ToList();
            var all = new List<ParamsDtoInfo>(types.Count);
            foreach (var t in types)
            {
                var attrs = t.GetCustomAttributes<DtoAttribute>(inherit: false).ToArray();
                var order = attrs.OfType<OrderAttribute>().FirstOrDefault();
                var purposes = DtoPurpose.None;
                foreach (var a in attrs)
                    purposes |= a switch
                    {
                        ParamsDtoAttribute  => DtoPurpose.Params,
                        OrderAttribute      => DtoPurpose.Params | DtoPurpose.Order,
                        UnitMemoryAttribute => DtoPurpose.UnitMemory,
                        _                   => DtoPurpose.None,
                    };
                var names = byName.Values.Where(d => d.ParamsType == t).Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                if (names.Count > 0) purposes |= DtoPurpose.Params;
                if (t.IsValueType) purposes |= DtoPurpose.Variable;
                var origin = attrs.Select(DtoOrigin.Of).Where(o => o.IsAsset).Concat(attrs.Select(DtoOrigin.Of).Where(o => o.IsKnown))
                                  .FirstOrDefault();
                all.Add(new ParamsDtoInfo(t, order?.Name, names) { Purposes = purposes, Origin = origin });
            }

            // ⭐ CE-3185 (F1) — the UI and the order names show a params DTO's SIMPLE name, so two marked ones may not share it.
            var clash = all.Where(i => i.Type.IsDefined(typeof(ParamsDtoAttribute), false) || i.Type.IsDefined(typeof(OrderAttribute), false))
                           .GroupBy(i => i.Type.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (clash != null)
                throw new InvalidOperationException(
                    $"CE-3185: two params DTOs are named '{clash.Key}' ({string.Join(", ", clash.Select(i => i.Type.FullName))}) — a params " +
                    "DTO's simple name is what menus and order names show; rename one (docs/DESIGN_Orders_Params_Entity_Classes.md §13.7).");

            return new ParamsDtoRegistry(byName, all.OrderBy(i => i.Type.FullName, StringComparer.Ordinal).ToList());
        }

        /// <summary>⭐ One instance per assembly NAME, the last loaded: a host that hot-reloads the AI behaviours (the editor) holds
        /// several copies of it, and each would otherwise declare every behaviour again (measured: "declared twice" on every
        /// editor rail). ⭐ <see cref="AppDomain.GetAssemblies"/> lists in load order, so the last is the live one.</summary>
        private static IEnumerable<Assembly> LatestPerName(IEnumerable<Assembly> assemblies)
        {
            var byName = new Dictionary<string, Assembly>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var a in assemblies)
            {
                if (a.IsDynamic) continue;
                string name = a.GetName().Name ?? a.FullName ?? "";
                if (!byName.ContainsKey(name)) order.Add(name);
                byName[name] = a;
            }
            return order.Select(n => byName[n]);
        }

        /// <summary>Only <c>Hrot.Core</c>, <c>Fdp.Core</c> and assemblies that reference either can carry these attributes — the
        /// rest are skipped unscanned (the framework, the test runners), which is what keeps the scan cheap. ⭐ <c>CE-3185</c> — widened to
        /// <c>Fdp.Core</c> (where <see cref="DtoAttribute"/> lives), or <c>Fdp.Toolkits</c>' DTOs (<c>ThreatCountParams</c>) stay invisible.</summary>
        private static bool CanDeclare(Assembly asm)
        {
            var core = typeof(BehaviorAttribute).Assembly;
            var fdp = typeof(DtoAttribute).Assembly;
            if (asm == core || asm == fdp) return true;
            string coreName = core.GetName().Name!, fdpName = fdp.GetName().Name!;
            try { return asm.GetReferencedAssemblies().Any(n => n.Name == coreName || n.Name == fdpName); }
            catch { return false; }
        }

        private static IEnumerable<Type> LoadableTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).Cast<Type>(); }
            catch { return Array.Empty<Type>(); }
        }

        private static void TryLoad(string name)
        {
            try { Assembly.Load(new AssemblyName(name)); }
            catch { /* not shipped with this host: it has no declarations from it */ }
        }
    }
}
