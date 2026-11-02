using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.ScenarioRules.Actions
{
    /// <summary>
    /// ⭐ rules S2-d (CE-4160/4161/4162) — the non-generic face of ONE cached action: tick it (attach + seed its slot on first use) and detach the slot.
    /// Built by <see cref="ActionCatalogue.RunnerFor"/>; lifted from the trigger PoC.
    /// </summary>
    public abstract class StepRunner
    {
        /// <summary>
        /// ⭐ CE-4161 — the slot kind of a step. <b>Hsm</b>: ingress's behaviour-change sweep frees kinds Hsm | Blueprint, so a step orphaned by a
        /// replaced task is cleaned. (A BTree-kind lazy slot would leak — <see cref="OccurrenceWorkingState"/> now refuses the root kinds.)
        /// </summary>
        public const OccurrenceKind Kind = OccurrenceKind.Hsm;

        /// <summary>The first step slot's key; a list at nesting <c>depth</c> uses <see cref="SlotKey"/> (CE-4161: a list run from inside a step cannot collide).</summary>
        public const int SlotBase = 0x7C000001;

        /// <summary>The step slot of a list running at <paramref name="depth"/> (0 = the outermost).</summary>
        public static int SlotKey(int depth) => SlotBase ^ depth;

        /// <summary>The action's full name.</summary>
        public string Fqn { get; }

        protected StepRunner(string fqn) { Fqn = fqn; }

        /// <summary>Ticks the action in slot <paramref name="slotKey"/>; on its first call attaches the slot and seeds the params from <paramref name="argsJson"/>.
        /// A reference that cannot be resolved FAILS the step (<see cref="ActionFailure"/>); malformed args THROW (nothing half-seeded is left).</summary>
        public abstract NodeStatus Tick(EntityRepository world, Entity self, int slotKey, string? argsJson);

        /// <summary>⭐ CE-4163 — STRICT check of an args object against the params type: not an object / an unknown property / a value that does not
        /// read (strict enums) THROWS. Used at assignment; the runtime seeding stays the lenient overlay.</summary>
        public abstract void CheckArgs(string? argsJson);

        /// <summary>⭐ rules S2 ⑨ — the action's params carry an <c>int ArgsRef</c>: a step may then bring a nested <c>"params"</c> value (<see cref="ActionList.ExtractParams"/>).</summary>
        public abstract bool TakesArgsRef { get; }

        /// <summary>Frees the step's slot.</summary>
        public bool Detach(EntityRepository world, Entity self, int slotKey) => OccurrenceStoreAccess.TryDetachSlot(world, self, slotKey);

        /// <summary>The slot's current params and working state (a rail's / the inspector's view).</summary>
        public abstract (object Params, object State) Peek(EntityRepository world, Entity self, int slotKey);

        protected static ulong Hash(string s) { ulong h = 1469598103934665603UL; foreach (char c in s) { h ^= c; h *= 1099511628211UL; } return h; }
    }

    /// <summary>⭐ rules S2-f — an action's params may check the raw args object at ASSIGNMENT (<see cref="StepRunner.CheckArgs"/>): a message too long for its
    /// <c>FixedString</c> is refused there, not silently cut. Returns the reason, or <c>null</c> when fine.</summary>
    public interface IActionArgsCheck { string? Check(JsonObject args); }

    /// <summary>One action's runner: its delegate plus the seeding of its slot.</summary>
    public sealed unsafe class StepRunner<TP, TS> : StepRunner where TP : unmanaged where TS : unmanaged
    {
        private readonly SharedNodeStatefulAction<TP, TS> _fn;
        private readonly ulong _hash;
        private readonly bool _holdsRefs = EntityRefRemap.HoldsRefs(typeof(TP));

        public StepRunner(string fqn, SharedNodeStatefulAction<TP, TS> fn) : base(fqn) { _fn = fn; _hash = Hash(fqn); }

        public override NodeStatus Tick(EntityRepository world, Entity self, int slotKey, string? argsJson)
        {
            ref TS ws = ref OccurrenceWorkingState.ResolveOrAttach<TP, TS>(world, self, slotKey, _hash, Kind, out bool fresh, out TP* pp);
            if (fresh)
            {
                ActionFailure.Clear(world, self);   // a stale reason must never be read as THIS step's
                // ⭐ SEED the slot's params from the step's JSON — the seam's contract is that the CALLER does it. ONE JSON→DTO rule:
                //   BehaviorParams.OverlayFlat (field initialisers, case-insensitive, strict enums, FixedString, EntityLink).
                try
                {
                    TP p = ParamsDefaults.New<TP>();
                    BehaviorParams.OverlayFlat(ref p, argsJson);
                    if (_holdsRefs) p = ResolveLinks(world, p);
                    *pp = p;
                }
                catch (UnresolvedReferenceException ex)
                {
                    OccurrenceStoreAccess.TryDetachSlot(world, self, slotKey);
                    ActionFailure.Set(world, self, ex.Message);
                    return NodeStatus.Failure;
                }
                catch
                {
                    OccurrenceStoreAccess.TryDetachSlot(world, self, slotKey);   // bad args ⇒ no half-seeded slot left behind
                    throw;
                }
            }
            return _fn(ref *pp, ref ws, self, world);
        }

        /// <summary>⭐ CE-4162 — an authored <c>"@Key"</c> / GUID link in the params becomes its entity NOW (the world is live), by the one walker S0 loads with.</summary>
        private static TP ResolveLinks(EntityRepository world, TP p)
        {
            object boxed = p;
            try { EntityRefRemap.WalkObject(boxed, new RefResolve(new WorldRefScope(world))); }
            catch (InvalidOperationException ex) { throw new UnresolvedReferenceException(ex.Message); }
            return (TP)boxed;
        }

        public override bool TakesArgsRef => typeof(TP).GetField("ArgsRef", BindingFlags.Public | BindingFlags.Instance)?.FieldType == typeof(int);

        public override void CheckArgs(string? argsJson)
        {
            if (string.IsNullOrWhiteSpace(argsJson)) return;
            var obj = JsonNode.Parse(argsJson!) as JsonObject ?? throw new FormatException($"the args of '{Fqn}' must be a JSON object");
            var known = typeof(TP).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).ToArray();
            foreach (var (key, _) in obj)
                if (!known.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                    throw new FormatException($"unknown property '{key}' for '{Fqn}' (known: {string.Join(", ", known)})");
            TP p = ParamsDefaults.New<TP>();
            BehaviorParams.OverlayFlat(ref p, argsJson);   // a value that does not read (strict enum, wrong type) throws
            // ⭐ an action's own check of the RAW args (a FixedString field truncates silently: only the JSON knows the text was too long)
            if ((object)p is IActionArgsCheck check && check.Check(obj) is { } error) throw new FormatException($"'{Fqn}': {error}");
        }

        public override (object Params, object State) Peek(EntityRepository world, Entity self, int slotKey)
        {
            if (!OccurrenceStoreAccess.TryFindSlotReadOnly(world, self, slotKey, out byte* block, out int off, out _))
                throw new InvalidOperationException("no slot");
            int stateBytes = OccurrenceWorkingState.ParamsOffsetOf<TS>();
            return (*(TP*)(block + off + stateBytes), *(TS*)(block + off));
        }

        private sealed class UnresolvedReferenceException : Exception { public UnresolvedReferenceException(string m) : base(m) { } }
    }
}
