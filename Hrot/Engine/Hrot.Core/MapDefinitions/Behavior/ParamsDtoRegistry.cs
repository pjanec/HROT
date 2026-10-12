using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hrot.Map.Definitions.Behavior.Orders;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>⭐ <c>CE-2134</c> — one behaviour's declared params contract (<see cref="BehaviorAttribute"/>).</summary>
    public sealed record BehaviorDeclaration(string Name, Type ParamsType, BehaviorCategory Categories, string DeclaredOn);

    /// <summary>⭐ <c>CE-2134</c> — one params DTO: the behaviours that take it, and its order name when it is an
    /// <c>[Order]</c> DTO (a string — menus read it and never the AI assembly's <c>OrderKind</c>, §2.3l).</summary>
    public sealed record ParamsDtoInfo(Type Type, string? OrderName, IReadOnlyList<string> BehaviorNames);

    /// <summary>
    /// ⭐⭐⭐ <c>CE-2134</c> (B2, <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §5b) — THE ONE SCAN of the params contract:
    /// every <see cref="BehaviorAttribute"/> (on methods, types and assemblies) and every <see cref="OrderAttribute"/> DTO in the
    /// LOADED assemblies. Its readers — the runtime binding (<c>BehaviorSchemaDiscovery</c>), the mission pickers
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

        private readonly Dictionary<string, BehaviorDeclaration> _byName;

        private ParamsDtoRegistry(Dictionary<string, BehaviorDeclaration> byName, IReadOnlyList<ParamsDtoInfo> dtos)
        {
            _byName = byName;
            Behaviors = byName.Values.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
            Dtos = dtos;
        }

        /// <summary>Every declared behaviour, by name (ordinal).</summary>
        public IReadOnlyList<BehaviorDeclaration> Behaviors { get; }

        /// <summary>Every params DTO a behaviour declares, plus every <c>[Order]</c> DTO, by type name.</summary>
        public IReadOnlyList<ParamsDtoInfo> Dtos { get; }

        public bool TryGetBehavior(string name, out BehaviorDeclaration declaration)
            => _byName.TryGetValue(name, out declaration!);

        /// <summary>The behaviour names whose categories include <paramref name="category"/>, by name.</summary>
        public IReadOnlyList<string> NamesWithCategory(BehaviorCategory category)
            => Behaviors.Where(d => (d.Categories & category) == category && category != BehaviorCategory.None)
                        .Select(d => d.Name).ToList();

        /// <summary>The process-wide registry over the loaded assemblies (rebuilt after another assembly loads).</summary>
        public static ParamsDtoRegistry Default
        {
            get
            {
                lock (Gate)
                {
                    if (!_hooked)
                    {
                        _hooked = true;
                        foreach (var name in KnownBehaviourAssemblies) TryLoad(name);
                        AppDomain.CurrentDomain.AssemblyLoad += (_, _) => { lock (Gate) _default = null; };
                    }
                    return _default ??= Scan(LatestPerName(AppDomain.CurrentDomain.GetAssemblies()));
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
            var orderDtos = new List<Type>();
            foreach (var asm in assemblies.Distinct())
            {
                if (asm.IsDynamic || !CanDeclare(asm)) continue;
                foreach (var a in asm.GetCustomAttributes<BehaviorAttribute>()) declarations.Add((a, $"assembly {asm.GetName().Name}"));

                foreach (var t in LoadableTypes(asm))
                {
                    foreach (var a in t.GetCustomAttributes<BehaviorAttribute>(inherit: false)) declarations.Add((a, t.FullName ?? t.Name));
                    if (t.IsDefined(typeof(OrderAttribute), inherit: false)) orderDtos.Add(t);
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
            return FromDeclarations(declarations, orderDtos);
        }

        /// <summary>
        /// The registry over explicit declarations (each with where it was found) and <c>[Order]</c> DTO types — what
        /// <see cref="Scan"/> builds from reflection; a rail builds one by hand. ⛔ Throws on the load-check failures.
        /// </summary>
        public static ParamsDtoRegistry FromDeclarations(IEnumerable<(BehaviorAttribute Declaration, string DeclaredOn)> declarations,
            IEnumerable<Type> orderDtos)
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

            var orders = new Dictionary<Type, string>();
            foreach (var t in orderDtos)
                if (t.GetCustomAttribute<OrderAttribute>(inherit: false) is { } order) orders[t] = order.Name;

            var dtos = byName.Values.Select(d => d.ParamsType).Concat(orders.Keys).Distinct()
                .Select(t => new ParamsDtoInfo(t, orders.TryGetValue(t, out var o) ? o : null,
                    byName.Values.Where(d => d.ParamsType == t).Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal).ToList()))
                .OrderBy(i => i.Type.FullName, StringComparer.Ordinal)
                .ToList();
            return new ParamsDtoRegistry(byName, dtos);
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

        /// <summary>Only <c>Hrot.Core</c> and assemblies that reference it can carry these attributes — the rest are skipped
        /// unscanned (the framework, FDP, the test runners), which is what keeps the scan cheap.</summary>
        private static bool CanDeclare(Assembly asm)
        {
            var core = typeof(BehaviorAttribute).Assembly;
            if (asm == core) return true;
            string coreName = core.GetName().Name!;
            try { return asm.GetReferencedAssemblies().Any(n => n.Name == coreName); }
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
