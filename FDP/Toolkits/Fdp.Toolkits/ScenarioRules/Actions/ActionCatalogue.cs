using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;

namespace Fdp.Toolkit.ScenarioRules.Actions
{
    /// <summary>
    /// What the catalogue knows about ONE stateful <c>[SharedAiAction]</c>: the declaring method and the two struct types of its
    /// <c>(ref TParams, ref TState, Entity, EntityRepository) → NodeStatus</c> shape. <see cref="Refusal"/> is non-null when the action
    /// cannot run as a trigger step (CE-4162: its params hold an <see cref="Entity"/>) — listed, never run.
    /// </summary>
    public sealed record ActionEntry(string Fqn, string ShortName, MethodInfo Method, Type ParamsType, Type StateType, string? Refusal);

    /// <summary>
    /// ⭐⭐ rules S2-d (📄 docs/DESIGN_Scenario_Rules.md §00.9, SR-93 ③, CE-4160) — THE ONE runtime catalogue of the stateful shared actions: the
    /// reflection pass that finds every <c>[SharedAiAction]</c> of the shape above in the loaded assemblies, and the cached runner per action TYPE
    /// (<c>MakeGenericType(SharedNodeStatefulAction&lt;,&gt;)</c> + <c>CreateDelegate</c> — no generated code). Lifted from the trigger PoC.
    /// <para>⭐ <b>One discovery, two users</b> (ruling 9): the editor's <c>ActionSchemaExporter</c> walks the assemblies THROUGH
    /// <see cref="Discover"/> — its own per-method schema logic rides on the same pass, so there is no second reflection over the assemblies.</para>
    /// <para>⛔ A short name is unique: a second action with the same short name marks it AMBIGUOUS — looking it up by the short name throws, naming both;
    /// the full name always works. (Listing never throws: a scan over every loaded assembly must not be poisoned by two unrelated test actions.)</para>
    /// </summary>
    public static class ActionCatalogue
    {
        private static readonly object Gate = new();
        private static readonly ConcurrentDictionary<string, ActionEntry> ByFqn = new();
        private static readonly ConcurrentDictionary<string, ActionEntry> ByShort = new();
        private static readonly ConcurrentDictionary<string, List<string>> Ambiguous = new();
        private static readonly ConcurrentDictionary<string, StepRunner> Runners = new();
        private static readonly HashSet<Assembly> Scanned = new();

        /// <summary>How many generic runners (= delegates) were built. A cache hit does NOT bump it.</summary>
        public static int RunnersBuilt;

        // ── discovery ────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ THE reflection pass over one assembly: every declared method of every type (static and instance, public and not), registering the
        /// stateful shared actions and handing EACH method to <paramref name="alsoVisit"/> (the editor exporter's own per-method logic).
        /// A partly loadable assembly is walked for the types that did load.
        /// </summary>
        public static void Discover(Assembly assembly, Action<MethodInfo>? alsoVisit = null)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

            foreach (var type in types)
            {
                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                }
                catch { continue; }

                foreach (var m in methods)
                {
                    try { TryRegister(type, m); }
                    catch (Exception ex) when (ex is TypeLoadException or BadImageFormatException or FileNotFoundException or FileLoadException) { }
                    alsoVisit?.Invoke(m);
                }
            }
            lock (Gate) Scanned.Add(assembly);
        }

        private static void TryRegister(Type declaring, MethodInfo m)
        {
            if (!m.IsStatic || !m.IsDefined(typeof(SharedAiActionAttribute), inherit: false)) return;
            var ps = m.GetParameters();
            if (ps.Length != 4 || !ps[0].ParameterType.IsByRef || !ps[1].ParameterType.IsByRef
                || ps[2].ParameterType != typeof(Entity) || ps[3].ParameterType != typeof(EntityRepository)
                || m.ReturnType != typeof(NodeStatus)) return;

            var pt = ps[0].ParameterType.GetElementType()!;
            var st = ps[1].ParameterType.GetElementType()!;
            string fqn = $"{declaring.FullName}.{m.Name}";
            var entry = new ActionEntry(fqn, m.Name, m, pt, st, FindRefusal(m.Name, pt));

            lock (Gate)
            {
                if (ByFqn.TryGetValue(fqn, out var old) && old.Method != m) Runners.TryRemove(fqn, out _);   // a hot-reloaded assembly: its types are new
                ByFqn[fqn] = entry;
                if (ByShort.TryGetValue(m.Name, out var other) && other.Fqn != fqn)
                {
                    var list = Ambiguous.GetOrAdd(m.Name, _ => new List<string> { other.Fqn });
                    if (!list.Contains(fqn)) list.Add(fqn);
                }
                else ByShort[m.Name] = entry;
            }
        }

        /// <summary>⭐ CE-4162 — an <see cref="Entity"/> field in a params struct is refused: it cannot be authored (the JSON reader drops it
        /// silently) — an authored reference is an <c>EntityLink</c>, which reads and resolves.</summary>
        private static string? FindRefusal(string action, Type paramsType)
        {
            string? where = EntityFieldPath(paramsType, paramsType.Name, 0);
            return where == null ? null
                : $"action '{action}': its params type has an Entity field ({where}) — a trigger step cannot author an Entity (the JSON reader drops it silently). " +
                  "Use an EntityLink: authored as \"@Key\" or a GUID, resolved when the step starts (CE-4162)";
        }

        private static string? EntityFieldPath(Type t, string path, int depth)
        {
            if (depth > 6) return null;
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var ft = f.FieldType;
                string fp = path + "." + f.Name;
                if (ft == typeof(Entity)) return fp;
                if (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum && ft.Namespace != "System" && ft.Namespace?.StartsWith("System.", StringComparison.Ordinal) != true)
                {
                    var inner = EntityFieldPath(ft, fp, depth + 1);
                    if (inner != null) return inner;
                }
            }
            return null;
        }

        /// <summary>Scans every loaded assembly that can declare shared actions (one that references Fbt.Kernel) and was not scanned yet.</summary>
        private static void EnsureScanned()
        {
            var kernel = typeof(SharedAiActionAttribute).Assembly;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;
                lock (Gate) if (Scanned.Contains(a)) continue;
                if (a != kernel && !a.GetReferencedAssemblies().Any(n => n.Name == kernel.GetName().Name)) { lock (Gate) Scanned.Add(a); continue; }
                Discover(a);
            }
        }

        // ── lookup ───────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Every stateful shared action found so far (after a scan of the loaded assemblies).</summary>
        public static IReadOnlyCollection<ActionEntry> All { get { EnsureScanned(); return ByFqn.Values.ToArray(); } }

        /// <summary>The short names more than one action carries, with every full name — what an author must disambiguate.</summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> Ambiguities
        {
            get { EnsureScanned(); return Ambiguous.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.ToArray()); }
        }

        /// <summary>
        /// By short name or full name. A miss rescans the assemblies loaded since the last scan, then answers <c>false</c>.
        /// ⛔ An ambiguous SHORT name throws, naming every full name that carries it.
        /// </summary>
        public static bool TryGet(string act, out ActionEntry entry)
        {
            if (TryLookup(act, out entry)) return true;
            EnsureScanned();
            return TryLookup(act, out entry);
        }

        private static bool TryLookup(string act, out ActionEntry entry)
        {
            if (ByFqn.TryGetValue(act, out entry!)) return true;
            if (Ambiguous.TryGetValue(act, out var fqns))
                throw new InvalidOperationException($"action name '{act}' is ambiguous: {string.Join(" | ", fqns)} — use the full name");
            return ByShort.TryGetValue(act, out entry!);
        }

        // ── runners ──────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The cached runner for an action: built ONCE per action TYPE. ⛔ An unknown action, or one that cannot run as a step, THROWS.</summary>
        public static StepRunner RunnerFor(string act)
        {
            if (!TryGet(act, out var e)) throw new InvalidOperationException($"unknown action '{act}'");
            if (e.Refusal != null) throw new InvalidOperationException(e.Refusal);
            return Runners.GetOrAdd(e.Fqn, _ => Build(e));
        }

        private static StepRunner Build(ActionEntry e)
        {
            System.Threading.Interlocked.Increment(ref RunnersBuilt);
            try
            {
                var delType = typeof(SharedNodeStatefulAction<,>).MakeGenericType(e.ParamsType, e.StateType);
                var del = Delegate.CreateDelegate(delType, e.Method);
                var runnerType = typeof(StepRunner<,>).MakeGenericType(e.ParamsType, e.StateType);
                return (StepRunner)Activator.CreateInstance(runnerType, e.Fqn, del)!;
            }
            catch (ArgumentException ex)   // a params / state type that is not unmanaged
            {
                throw new InvalidOperationException($"action '{e.Fqn}' cannot run as a step: its params / state types must be unmanaged structs ({ex.Message})", ex);
            }
        }

        /// <summary>Test seam: forget every cached runner so a rail can count builds from zero.</summary>
        public static void ResetRunnerCache() { Runners.Clear(); RunnersBuilt = 0; }
    }
}
