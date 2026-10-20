using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Explicit execution phases for EQS tests.
    /// Tests execute in enum order; top-K reduction occurs between FilterExpensive and ScoreCheap.
    /// </summary>
    public enum EqsTestPhase : byte
    {
        /// <summary>Fast data-driven filters (faction, FOV). No allocations. Reject with EntityId = -1L.</summary>
        FilterCheap = 0,
        /// <summary>Slow filters (navmesh reachability). Reject with EntityId = -1L.</summary>
        FilterExpensive = 1,
        /// <summary>Fast scoring (distance falloff). Additive to EqsResult.Score.</summary>
        ScoreCheap = 2,
        /// <summary>Slow scoring (accurate LOS, path cost). Additive to EqsResult.Score.</summary>
        ScoreExpensive = 3,
    }

    /// <summary>
    /// Generates the initial set of EQS candidates (entity-shaped or positional).
    /// Must operate on the provided span with zero heap allocation.
    /// </summary>
    public interface IEqsGenerator
    {
        /// <summary>
        /// Fills <paramref name="candidates"/> with initial results and returns the valid count.
        /// Entity-shaped results store <c>entity.PackedValue</c> in EntityId.
        /// Positional results set EntityId = 0.
        /// <para>Return a NEGATIVE value when the query cannot be evaluated yet (e.g. a context entity
        /// it needs has not arrived on this node): the solver then publishes NOTHING, so a reader keeps
        /// waiting instead of seeing an empty result that would read as "nothing there".</para>
        /// </summary>
        int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates);
    }

    /// <summary>
    /// Filters or scores a batch of EQS candidates in-place.
    /// All operations must be zero-allocation.
    /// </summary>
    public interface IEqsTest
    {
        /// <summary>The phase in which this test executes.</summary>
        EqsTestPhase Phase { get; }

        /// <summary>
        /// Executes the test over <paramref name="candidates"/>.
        /// Filters reject by setting EntityId = -1L.
        /// Scorers accumulate into EqsResult.Score additively.
        /// </summary>
        void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates);
    }

    /// <summary>
    /// Compiled representation of an EQS query blueprint. Struct to allow stack allocation.
    /// Tests are split by phase; null arrays are treated as empty (no tests in that phase).
    /// </summary>
    public struct EqsQueryTemplate
    {
        /// <summary>FNV-1a 32-bit hash of the template AssetId GUID.</summary>
        public uint BlueprintId;

        /// <summary>Produces the initial candidate span.</summary>
        public IEqsGenerator Generator;

        /// <summary>Fast filter tests. Run before FilterExpensive.</summary>
        public IEqsTest[]? FilterCheap;

        /// <summary>Slow filter tests. Run before top-K reduction.</summary>
        public IEqsTest[]? FilterExpensive;

        /// <summary>Fast scoring tests. Run after top-K reduction.</summary>
        public IEqsTest[]? ScoreCheap;

        /// <summary>Slow scoring tests. Run last.</summary>
        public IEqsTest[]? ScoreExpensive;

        /// <summary>Maximum candidates the generator may populate. Must be &lt;= EqsResultPool.MaxTopK * some factor.</summary>
        public int MaxCandidates;

        /// <summary>
        /// ⭐⭐ <c>CE-2164</c> (R-3004) — when the filters reject EVERY candidate, keep them all: the filters become a preference, and the
        /// score phases rank the whole set. ⭐ For a query that must always answer — a retreat: 📌 live, on open ground no point within
        /// 60 m was hidden from the enemy, the retreat answered nothing, and a squad (and a lone soldier's <c>FallBack</c>) stood in the
        /// open being shot. ⚠ Only when nothing passes: a candidate that passes always beats one that does not.
        /// </summary>
        public bool KeepAllWhenFilteredOut;

        /// <summary>
        /// FNV-1a 64-bit hash over the fully-qualified type names of the Generator and all Tests.
        /// Compared each tick to SensorEvalState.CurrentStructureHash to detect hot-reload changes.
        /// </summary>
        public ulong StructureHash;

        /// <summary>
        /// Computes and returns a 64-bit FNV-1a hash covering the type names of all generators
        /// and tests in this template. Zero-allocation; uses stackalloc for intermediate state.
        /// </summary>
        public ulong ComputeStructureHash()
        {
            const ulong FnvOffset = 14695981039346656037UL;
            const ulong FnvPrime  = 1099511628211UL;
            ulong hash = FnvOffset;

            void HashTypeName(System.Type? t)
            {
                if (t == null) return;
                foreach (char c in t.FullName ?? t.Name)
                {
                    hash ^= (ulong)(byte)c;
                    hash *= FnvPrime;
                }
                // Separator byte
                hash ^= (ulong)'|';
                hash *= FnvPrime;
            }

            HashTypeName(Generator?.GetType());
            if (FilterCheap    != null) foreach (var t in FilterCheap)    HashTypeName(t?.GetType());
            if (FilterExpensive != null) foreach (var t in FilterExpensive) HashTypeName(t?.GetType());
            if (ScoreCheap     != null) foreach (var t in ScoreCheap)     HashTypeName(t?.GetType());
            if (ScoreExpensive != null) foreach (var t in ScoreExpensive)  HashTypeName(t?.GetType());
            return hash;
        }
    }

    /// <summary>
    /// Registry allowing the solver to look up a compiled template by BlueprintId.
    /// </summary>
    /// <summary>
    /// ⭐ <c>CE-3143</c> — a template's FILTER phases (cheap, then expensive) over generated candidates: a rejected candidate is marked
    /// <c>EntityId = -1</c>. ONE pipeline for the solver and the map's EQS verdict (<c>EqsSensorGizmo</c>), so the map shows exactly what
    /// the AI concluded. ⚠ The filter tests are pure (they read the view, mark candidates); the SCORE phases are not run here — one of
    /// them submits raycasts (<c>AccurateLineOfSightTest</c>).
    /// </summary>
    public static class EqsFilters
    {
        /// <summary>Runs <paramref name="template"/>'s filter tests over <paramref name="candidates"/>; returns their cost (<see cref="EqsCost"/>).</summary>
        public static int Run(in EqsQueryTemplate template, Entity entity, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            int cost = 0;
            // ⭐ CE-2164 (R-3004) — remember who was a candidate, to give them all back if the filters leave nobody
            Span<long> before = template.KeepAllWhenFilteredOut && candidates.Length <= MaxKeptOnStack
                ? stackalloc long[candidates.Length] : Span<long>.Empty;
            for (int i = 0; i < before.Length; i++) before[i] = candidates[i].EntityId;
            if (template.FilterCheap != null)
                foreach (var test in template.FilterCheap)
                {
                    test.ExecuteBatch(entity, ref sensor, view, candidates);
                    cost += EqsCost.Charged(test, candidates.Length);   // ⭐ CE-3279 (c) — after the batch: a test may count its own work
                }
            if (template.FilterExpensive != null)
                foreach (var test in template.FilterExpensive)
                {
                    test.ExecuteBatch(entity, ref sensor, view, candidates);
                    cost += EqsCost.Charged(test, candidates.Length);   // ⭐ CE-3279 (c) — after the batch: a test may count its own work
                }
            if (!before.IsEmpty)
            {
                bool any = false;
                foreach (var c in candidates) if (c.EntityId != -1L) { any = true; break; }
                if (!any)
                    for (int i = 0; i < before.Length; i++) candidates[i].EntityId = before[i];
            }
            return cost;
        }

        /// <summary>The most candidates <see cref="EqsQueryTemplate.KeepAllWhenFilteredOut"/> remembers (on the stack); a larger
        /// span filters hard.</summary>
        public const int MaxKeptOnStack = 1024;
    }

    [ComponentId(GlobalComponentIds.IEqsTemplateRegistry)]
    public interface IEqsTemplateRegistry
    {
        /// <summary>
        /// Returns true and sets <paramref name="template"/> if a template with
        /// the given <paramref name="blueprintId"/> is registered.
        /// </summary>
        bool TryGetTemplate(uint blueprintId, out EqsQueryTemplate template);
    }

    /// <summary>
    /// Attribute marking a class as an EQS query template for the source generator.
    /// The <c>AssetId</c> GUID is hashed to produce the <c>BlueprintId</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class EqsTemplateAttribute : Attribute
    {
        /// <summary>GUID string of the template asset (used to compute BlueprintId).</summary>
        public string AssetId { get; }

        public EqsTemplateAttribute(string assetId)
        {
            AssetId = assetId ?? throw new ArgumentNullException(nameof(assetId));
        }
    }

    /// <summary>
    /// Optional abstract base for EQS templates. Provides no runtime behaviour;
    /// templates may directly implement the <c>Build</c> pattern without inheriting this.
    /// </summary>
    public abstract class EqsTemplateBase
    {
        // Subclasses should provide: public static EqsQueryTemplate Build() { ... }
        // The purity analyser (Phase 6, TASK-EQS-020) will enforce this at compile time.
    }

    /// <summary>
    /// Marker interface for the Roslyn source generator Build() overload.
    /// Implementations may be no-ops; the generator uses this signature to call Build() at
    /// registration time without injecting runtime-service dependencies.
    /// </summary>
    public interface IEqsTemplateBuilder { }

    /// <summary>
    /// No-op implementation passed by the generated EqsRegistrar class when calling Build().
    /// </summary>
    public sealed class EqsTemplateBuilder : IEqsTemplateBuilder { }
}
