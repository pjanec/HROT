using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior.Tests.TriggerPoc
{
    /// <summary>The runtime catalogue entry: what <c>ActionSchemaEntry</c> says, minus the editor-only parts.</summary>
    public sealed record PocActionEntry(string Fqn, string ShortName, MethodInfo Method, Type ParamsType, Type StateType);

    /// <summary>
    /// ⭐ P1 — a MINIMAL RUNTIME CATALOGUE: reflect <c>[SharedAiAction]</c> static methods of the given assemblies, read the
    /// param / working-state types from the stateful signature <c>(ref P, ref WS, Entity, EntityRepository) → NodeStatus</c>.
    /// Mirrors what <c>ActionSchemaExporter.ProcessMethod</c> does for the SHARED + stateful arm (Fqn = DeclaringType.FullName + "." + Name).
    /// </summary>
    public static class PocCatalogue
    {
        private static readonly ConcurrentDictionary<string, PocActionEntry> ByName = new();
        private static readonly ConcurrentDictionary<string, PocStepRunner> Runners = new();

        /// <summary>How many generic runners (= delegates) were built. A cache hit does NOT bump it.</summary>
        public static int RunnersBuilt;

        public static void Scan(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (!m.IsDefined(typeof(SharedAiActionAttribute), inherit: false)) continue;
                    var ps = m.GetParameters();
                    // the stateful shape only: (ref P, ref WS, Entity, EntityRepository) → NodeStatus
                    if (ps.Length != 4 || !ps[0].ParameterType.IsByRef || !ps[1].ParameterType.IsByRef
                        || ps[2].ParameterType != typeof(Entity) || ps[3].ParameterType != typeof(EntityRepository)
                        || m.ReturnType != typeof(NodeStatus)) continue;
                    var e = new PocActionEntry($"{type.FullName}.{m.Name}", m.Name, m,
                        ps[0].ParameterType.GetElementType()!, ps[1].ParameterType.GetElementType()!);
                    if (!ByName.TryAdd(e.ShortName, e) && ByName[e.ShortName].Fqn != e.Fqn)
                        throw new InvalidOperationException($"short action name '{e.ShortName}' is ambiguous: {ByName[e.ShortName].Fqn} vs {e.Fqn}");
                    ByName[e.Fqn] = e;
                }
        }

        public static bool TryGet(string act, out PocActionEntry entry)
        {
            EnsureScanned();
            return ByName.TryGetValue(act, out entry!);
        }

        private static bool _scanned;
        private static void EnsureScanned()
        {
            if (_scanned) return;
            lock (ByName) { if (_scanned) return; Scan(typeof(PocCatalogue).Assembly); _scanned = true; }
        }

        /// <summary>The cached runner for an action: built ONCE per action TYPE via MakeGenericType + CreateDelegate (no generated code).</summary>
        public static PocStepRunner RunnerFor(string act)
        {
            if (Runners.TryGetValue(act, out var hit)) return hit;
            if (!TryGet(act, out var e)) throw new InvalidOperationException($"unknown action '{act}'");
            return Runners.GetOrAdd(e.Fqn, _ => Build(e));
        }

        private static PocStepRunner Build(PocActionEntry e)
        {
            System.Threading.Interlocked.Increment(ref RunnersBuilt);
            var delType = typeof(SharedNodeStatefulAction<,>).MakeGenericType(e.ParamsType, e.StateType);
            var del = Delegate.CreateDelegate(delType, e.Method);
            var runnerType = typeof(PocStepRunner<,>).MakeGenericType(e.ParamsType, e.StateType);
            return (PocStepRunner)Activator.CreateInstance(runnerType, e.Fqn, del)!;
        }

        /// <summary>Test seam: forget every cached runner so a rail can count builds from zero.</summary>
        public static void ResetRunnerCache() { Runners.Clear(); RunnersBuilt = 0; }
    }

    /// <summary>The non-generic face of one cached action: tick it (attach + seed on first use) and detach its slot.</summary>
    public abstract class PocStepRunner
    {
        /// <summary>⭐ the slot kind of a step. Hsm ⇒ ingress's behaviour-change sweep (kinds Hsm|Blueprint) frees an orphaned step; BTree would leak it.</summary>
        public static OccurrenceKind Kind = OccurrenceKind.Hsm;

        public string Fqn { get; }
        protected PocStepRunner(string fqn) { Fqn = fqn; }
        public abstract NodeStatus Tick(EntityRepository world, Entity self, int slotKey, string? argsJson);
        public bool Detach(EntityRepository world, Entity self, int slotKey) => OccurrenceStoreAccess.TryDetachSlot(world, self, slotKey);
        public abstract (object Params, object State) Peek(EntityRepository world, Entity self, int slotKey);

        protected static ulong Hash(string s) { ulong h = 1469598103934665603UL; foreach (char c in s) { h ^= c; h *= 1099511628211UL; } return h; }
    }

    public sealed unsafe class PocStepRunner<TP, TS> : PocStepRunner where TP : unmanaged where TS : unmanaged
    {
        private readonly SharedNodeStatefulAction<TP, TS> _fn;
        private readonly ulong _hash;

        public PocStepRunner(string fqn, SharedNodeStatefulAction<TP, TS> fn) : base(fqn) { _fn = fn; _hash = Hash(fqn); }

        public override NodeStatus Tick(EntityRepository world, Entity self, int slotKey, string? argsJson)
        {
            ref TS ws = ref OccurrenceWorkingState.ResolveOrAttach<TP, TS>(world, self, slotKey, _hash, Kind, out bool fresh, out TP* pp);
            if (fresh)
            {
                // ⭐ SEED the slot's params from the step's JSON — the seam's contract is that the CALLER does it.
                //   Reuses BehaviorParams.OverlayFlat (ONE JSON→DTO rule: field initialisers, case-insensitive, strict enums, FixedString).
                try
                {
                    TP p = ParamsDefaults.New<TP>();
                    BehaviorParams.OverlayFlat(ref p, argsJson);
                    *pp = p;
                }
                catch
                {
                    OccurrenceStoreAccess.TryDetachSlot(world, self, slotKey);   // bad args ⇒ no half-seeded slot left behind
                    throw;
                }
            }
            return _fn(ref *pp, ref ws, self, world);
        }

        public override (object Params, object State) Peek(EntityRepository world, Entity self, int slotKey)
        {
            if (!OccurrenceStoreAccess.TryFindSlotReadOnly(world, self, slotKey, out byte* block, out int off, out _))
                throw new InvalidOperationException("no slot");
            int stateBytes = OccurrenceWorkingState.ParamsOffsetOf<TS>();
            return (*(TP*)(block + off + stateBytes), *(TS*)(block + off));
        }
    }

    /// <summary>JSON → steps, parsed once per distinct JSON string (the strings are immutable record items).</summary>
    public static class PocStepList
    {
        private static readonly ConcurrentDictionary<string, PocStepSpec[]> Cache = new();

        public static PocStepSpec[] Get(string json) => Cache.GetOrAdd(json, Parse);

        private static PocStepSpec[] Parse(string json)
        {
            var arr = JsonNode.Parse(json) as JsonArray ?? throw new FormatException("a step list must be a JSON array");
            return arr.Select(n =>
            {
                var o = n as JsonObject ?? throw new FormatException("a step must be an object {act,args}");
                string act = o["act"]?.GetValue<string>() ?? throw new FormatException("a step needs 'act'");
                return new PocStepSpec(act, o["args"]?.ToJsonString() ?? "{}");
            }).ToArray();
        }
    }
}
