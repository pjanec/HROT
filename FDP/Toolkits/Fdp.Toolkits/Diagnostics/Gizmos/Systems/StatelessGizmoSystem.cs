using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.World;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    /// <summary>
    /// ECS system that executes all registered <see cref="IStatelessGizmo"/> projectors
    /// for every matching, live entity each frame. Runs in
    /// <see cref="SystemPhase.PostSimulation"/>.
    ///
    /// <para>
    /// Unlike <see cref="DataDrivenGizmoSystem"/>, this system holds no per-entity state.
    /// One projector instance handles every entity that matches its component mask.
    /// </para>
    ///
    /// <para>
    /// Selection and visibility follow the same delegate pattern as
    /// <see cref="DataDrivenGizmoSystem"/>: when <c>isSelectedPredicate</c> is
    /// <c>null</c>, all matching entities are drawn; when non-null, only entities for
    /// which the predicate returns <c>true</c> are drawn.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class StatelessGizmoSystem : IEcsModuleSystem
    {
        private readonly StatelessGizmoRegistry _registry;
        private readonly IDebugDrawBuilder _drawBuilder;
        private readonly Func<ISimulationView, Entity, bool>? _isSelectedPredicate;
        // ⚠ NOT readonly: the registry is mutable (Register/RegisterGlobal append), so these are grown
        // in Execute when it has. See EnsureCaches — CE-188.
        private bool[] _globalVisibilityCache;
        private bool[] _globalRulesVisibilityCache;

        // ⭐ CE-2394 — per rule, the entity indices that match it this frame (reused; grown, never shrunk)
        private int[][] _matches = System.Array.Empty<int[]>();
        private int[] _matchCount = System.Array.Empty<int>();

        private void EnsureMatchLists(int ruleCount)
        {
            if (_matches.Length >= ruleCount) return;
            int old = _matches.Length;
            System.Array.Resize(ref _matches, ruleCount);
            System.Array.Resize(ref _matchCount, ruleCount);
            for (int r = old; r < ruleCount; r++) _matches[r] = new int[64];
        }

        /// <summary>Max wall-clock budget in ms for entity iteration. 0 = unlimited.</summary>
        public float MaxGizmoFrameMs { get; set; } = 0f;

        /// <summary>
        /// ⭐ CE-2402 (docs/DESIGN_Evaluation_Cost_And_Cadence.md §10) — how many of a rule's matched entities are drawn FRESH per frame before
        /// the rest are HELD: re-emitted from the picture their projector drew on an earlier frame. 0 (default) = every entity every frame.
        /// With more matched entities than this, a rule refreshes a slice of them per frame (interleaved by entity index) — period
        /// <c>1 + (matched − 1) / FreshEntitiesPerFrame</c>, capped at <see cref="MaxHeldFrames"/> + 1. Needs a <see cref="DebugPrimitiveBuffer"/>.
        /// </summary>
        public int FreshEntitiesPerFrame { get; set; }

        /// <summary>⭐ CE-2402 — the most frames an entity's picture may be held before it is drawn fresh again.</summary>
        public int MaxHeldFrames { get; set; } = 3;

        private readonly DebugPrimitiveBuffer? _buffer;
        private RuleHold?[] _holds = System.Array.Empty<RuleHold?>();
        private int _frame;

        /// <summary>
        /// ⭐ CE-2402 — one rule's pictures, double-buffered: <c>Prev</c> holds what each entity emitted last frame (fresh or held), back to
        /// back; <c>Next</c> is filled this frame and swapped in. Per entity index: where its picture starts, how long it is, the generation it
        /// was drawn for and how many frames ago it was drawn. Valid only when written on the frame just before (a rule hidden or cut by the
        /// budget for a frame starts over fresh). Reused — grown, never shrunk.
        /// </summary>
        private sealed class RuleHold
        {
            public DebugPrimitive[] Prev = new DebugPrimitive[256], Next = new DebugPrimitive[256];
            public int NextCount;
            public int[] PrevStart = System.Array.Empty<int>(), PrevLen = System.Array.Empty<int>(), PrevGen = System.Array.Empty<int>(), PrevAge = System.Array.Empty<int>(), PrevStamp = System.Array.Empty<int>();
            public int[] NextStart = System.Array.Empty<int>(), NextLen = System.Array.Empty<int>(), NextGen = System.Array.Empty<int>(), NextAge = System.Array.Empty<int>(), NextStamp = System.Array.Empty<int>();
            public int WrittenFrame = int.MinValue;   // the frame Prev was written on

            public void EnsureEntities(int n)
            {
                if (PrevStart.Length >= n) return;
                int size = System.Math.Max(n, PrevStart.Length * 2);
                System.Array.Resize(ref PrevStart, size); System.Array.Resize(ref PrevLen, size); System.Array.Resize(ref PrevGen, size);
                System.Array.Resize(ref PrevAge, size); System.Array.Resize(ref PrevStamp, size);
                System.Array.Resize(ref NextStart, size); System.Array.Resize(ref NextLen, size); System.Array.Resize(ref NextGen, size);
                System.Array.Resize(ref NextAge, size); System.Array.Resize(ref NextStamp, size);
            }

            public void Put(int index, int gen, int age, int frame, System.ReadOnlySpan<DebugPrimitive> picture)
            {
                int need = NextCount + picture.Length;
                if (need > Next.Length) System.Array.Resize(ref Next, System.Math.Max(need, Next.Length * 2));
                int start = NextCount;
                for (int k = 0; k < picture.Length; k++)
                {
                    if (picture[k].LifetimeSeconds > 0f) continue;   // the buffer re-injects a lasting primitive itself
                    Next[NextCount++] = picture[k];
                }
                NextStart[index] = start; NextLen[index] = NextCount - start; NextGen[index] = gen; NextAge[index] = age; NextStamp[index] = frame;
            }

            public void Swap(int frame)
            {
                (Prev, Next) = (Next, Prev);
                (PrevStart, NextStart) = (NextStart, PrevStart); (PrevLen, NextLen) = (NextLen, PrevLen); (PrevGen, NextGen) = (NextGen, PrevGen);
                (PrevAge, NextAge) = (NextAge, PrevAge); (PrevStamp, NextStamp) = (NextStamp, PrevStamp);
                NextCount = 0;
                WrittenFrame = frame;
            }
        }

        // ⭐ CE-1480 G5 (docs/DESIGN_Change_Gated_Systems.md §4 G5) — change gating, parametrised by what the rules declare.
        //   ① the MATCH PASS (one walk over every entity slot collecting each rule's matches, 5–6 ms at 5 648 entities) is skipped while neither the
        //     entity set (structural clock), the view, the active rule set nor — when a view filter is on — a position moved: last frame's lists stand.
        //   ② a RULE that declares IStatelessGizmo.Inputs is drawn fresh only when an input moved; otherwise the primitives it drew are re-appended.
        //   Rules that declare nothing: today's path, untouched. HROT_CHANGE_GATE=0 ⇒ every gate answers Full.
        private readonly ChangeGate _matchGate = new();      // no position input (no view filter)
        private readonly ChangeGate _matchGatePos = new();   // + SimTransform: the in-view test reads positions
        private long _matchSig, _lastViewVersion;
        private bool _matchValid;
        private int _lastMaxIndex, _lastOutOfView;
        private GizmoInputs?[] _ruleInputs = System.Array.Empty<GizmoInputs?>();
        private int _ruleInputsFor;
        private RulePicture?[] _pics = System.Array.Empty<RulePicture?>();

        /// <summary>⭐ CE-1480 G5 — false ⇒ every rule is drawn every frame whatever it declares (the ungated reference of the equivalence rails; same as <c>HROT_CHANGE_GATE=0</c> for the rules, per system).</summary>
        public bool HonourDeclaredInputs { get; set; } = true;

        /// <summary>Frames on which the match pass was skipped (rails / diagnostics).</summary>
        public int MatchPassesSkipped { get; private set; }
        /// <summary>Rule pictures re-appended instead of drawn (rails / diagnostics).</summary>
        public int RulePicturesReused { get; private set; }

        /// <summary>
        /// ⭐ CE-1480 G5 — one declared rule's last picture: its gate, the entities (index + generation) it was drawn for, and the primitives, back to back
        /// (a lasting primitive — <c>LifetimeSeconds &gt; 0</c> — is left out: the buffer re-injects it itself). <c>Valid</c> only after a frame that
        /// drew EVERY entity fresh with no input moving, so a held (older) picture can never be frozen into it.
        /// </summary>
        private sealed class RulePicture
        {
            public readonly ChangeGate Gate;
            public DebugPrimitive[] Prims = new DebugPrimitive[64];
            public int PrimCount;
            public int[] Idx = new int[64], Gen = new int[64], NextIdx = new int[64], NextGen = new int[64];
            public int Count, NextCount;
            public bool Valid;

            public RulePicture(GizmoInputs inputs, StatelessGizmoSystem owner)
            {
                Gate = new ChangeGate().Watch(inputs.Components);
                if (inputs.DependsOnView) Gate.WatchExternal(() => owner.ViewFilter?.Version ?? -1L);
                if (inputs.Version != null) Gate.WatchExternal(inputs.Version);
                if (inputs.ReadsLayerState) Gate.WatchExternal(() => owner._buffer?.LayersVersion ?? -1L);
            }

            public void Add(int index, int gen)
            {
                if (NextCount == NextIdx.Length) { System.Array.Resize(ref NextIdx, NextCount * 2); System.Array.Resize(ref NextGen, NextCount * 2); }
                NextIdx[NextCount] = index; NextGen[NextCount] = gen; NextCount++;
            }

            public bool SameAsNext()
            {
                if (Count != NextCount) return false;
                for (int i = 0; i < Count; i++) if (Idx[i] != NextIdx[i] || Gen[i] != NextGen[i]) return false;
                return true;
            }

            public void Capture(System.ReadOnlySpan<DebugPrimitive> picture)
            {
                if (picture.Length > Prims.Length) System.Array.Resize(ref Prims, System.Math.Max(picture.Length, Prims.Length * 2));
                int n = 0;
                for (int k = 0; k < picture.Length; k++)
                {
                    if (picture[k].LifetimeSeconds > 0f) continue;
                    Prims[n++] = picture[k];
                }
                PrimCount = n;
                (Idx, NextIdx) = (NextIdx, Idx); (Gen, NextGen) = (NextGen, Gen);
                Count = NextCount;
                Valid = true;
            }
        }

        /// <summary>
        /// Creates the system.
        /// </summary>
        /// <remarks>
        /// <para>⚠ This used to require that every projector be registered <i>before</i> construction,
        /// so the visibility caches were sized correctly. <b>That precondition was unsatisfiable and
        /// production violated it every run</b>: the registry is mutable, hosts register projectors after
        /// building the system, and an AI hot-reload registers more at any time. The caches are now grown
        /// on demand (<c>CE-188</c>), so registration order no longer matters.</para>
        /// </remarks>
        public StatelessGizmoSystem(
            StatelessGizmoRegistry registry,
            IDebugDrawBuilder drawBuilder,
            Func<ISimulationView, Entity, bool>? isSelectedPredicate = null)
        {
            _registry                   = registry    ?? throw new ArgumentNullException(nameof(registry));
            _drawBuilder                = drawBuilder ?? throw new ArgumentNullException(nameof(drawBuilder));
            _buffer                     = drawBuilder as DebugPrimitiveBuffer;
            _isSelectedPredicate        = isSelectedPredicate;
            _globalVisibilityCache      = new bool[registry.Rules.Count];
            _globalRulesVisibilityCache = new bool[registry.GlobalRules.Count];
            _matchGate.WatchExternal(() => _matchSig);
            _matchGatePos.Watch(typeof(SimTransform)).WatchExternal(() => _matchSig);
        }

        // ---- IEcsModuleSystem -------------------------------------------------------

        /// <summary>
        /// Grows the visibility caches to match the registry, which can gain rules at any time.
        /// </summary>
        /// <remarks>
        /// <para><b>Why grow rather than bounds-check (<c>CE-188</c>).</b> The throw this fixes came from
        /// the global-rules dispatch loop indexing a cache sized at construction. Adding a bounds check
        /// there would have stopped the exception and left every late-registered global gizmo silently
        /// never drawing — trading a loud failure for a quiet one, which is the opposite of what this
        /// codebase needs. Growing the cache makes late registration simply work.</para>
        ///
        /// <para>Allocation happens only on the frames where the registry actually grew; the steady state
        /// is a length comparison.</para>
        /// </remarks>
        private void EnsureCaches()
        {
            int ruleCount = _registry.Rules.Count;
            if (_globalVisibilityCache.Length < ruleCount)
                Array.Resize(ref _globalVisibilityCache, ruleCount);

            int globalRuleCount = _registry.GlobalRules.Count;
            if (_globalRulesVisibilityCache.Length < globalRuleCount)
                Array.Resize(ref _globalRulesVisibilityCache, globalRuleCount);

            // ⭐ CE-1480 G5 — each rule's declared inputs, read once per rule (the registry only ever appends)
            if (_ruleInputsFor < ruleCount)
            {
                Array.Resize(ref _ruleInputs, ruleCount);
                Array.Resize(ref _pics, ruleCount);
                var rs = _registry.Rules;
                for (int i = _ruleInputsFor; i < ruleCount; i++) _ruleInputs[i] = rs[i].Projector.Inputs;
                _ruleInputsFor = ruleCount;
            }
        }

        /// <summary>
        /// ⭐ CE-4388 (docs/DESIGN_Map_View_Filter.md, R-1049) — what the map shows: an entity out of view reaches no rule (except a <c>DrawsOffScreen</c> one), and a
        /// rule coarser than its <c>MaxMetresPerPixel</c> is skipped for the frame. Null or not active ⇒ everything as before. Set by <c>MapInteractionPack.Build</c>.
        /// </summary>
        public IGizmoViewFilter? ViewFilter { get; set; }

        // ⭐ CE-4388 — the frame's active rules as plain arrays (grown with the rule count, never per frame)
        private int[] _activeIndex = System.Array.Empty<int>();
        private BitMask512[] _activeMask = System.Array.Empty<BitMask512>();
        private bool[] _activeOff = System.Array.Empty<bool>();

        /// <summary>⭐ CE-4388 — entities the last frame found out of view (tested at most once each), and rules skipped for the zoom (rails, telemetry).</summary>
        public int EntitiesOutOfView { get; private set; }
        public int RulesSkippedForZoom { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(StatelessGizmoSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only view ({view.GetType().Name}).");

            EnsureCaches();

            var rules = _registry.Rules;
            int ruleCount = rules.Count;

            // Evaluate and dispatch global (entity-less) rules first.
            var globalRules = _registry.GlobalRules;
            int globalRuleCount = globalRules.Count;
            for (int g = 0; g < globalRuleCount && g < _globalRulesVisibilityCache.Length; g++)
                _globalRulesVisibilityCache[g] = globalRules[g].VisibilityPolicy.IsGloballyEnabled(view);

            for (int g = 0; g < globalRuleCount; g++)
            {
                if (!_globalRulesVisibilityCache[g]) continue;
                using var traceRuleG = Fdp.Core.AccessTrace.Scope(globalRules[g].Projector, "gizmo-rule(global): ");   // ⭐ A-1 — one reader per gizmo rule (inside the gizmo system's own Tick scope)
                bool timedG = GizmoRuleTimings.Enabled;   // ⭐ CE-4388 — HROT_FRAME_STATS: a GLOBAL gizmo's time and garbage too (the A/B showed the entity rules are not the system's whole cost)
                long gStart = timedG ? System.Diagnostics.Stopwatch.GetTimestamp() : 0, gBytes = timedG ? System.GC.GetAllocatedBytesForCurrentThread() : 0;
                int gPrims = timedG && _buffer != null ? _buffer.Count : 0;
                globalRules[g].Projector.Draw(view, _drawBuilder);
                if (timedG) GizmoRuleTimings.Record(globalRules[g].Projector.GetType().Name, gStart, 0, System.GC.GetAllocatedBytesForCurrentThread() - gBytes, (_buffer?.Count ?? 0) - gPrims);
            }

            // Pre-evaluate global visibility once per rule, not once per entity.
            for (int r = 0; r < ruleCount && r < _globalVisibilityCache.Length; r++)
                _globalVisibilityCache[r] = rules[r].VisibilityPolicy.IsGloballyEnabled(view);

            // ⭐ CE-4388 (D-3) — the zoom gate: once per rule per frame, folded into the same per-rule cache
            var filter = ViewFilter;
            bool filtering = filter != null && filter.Active;
            float mpp = filtering ? filter!.MetresPerPixel : 0f;
            int skippedForZoom = 0;
            if (mpp > 0f)
                for (int r = 0; r < ruleCount && r < _globalVisibilityCache.Length; r++)
                    if (_globalVisibilityCache[r] && rules[r].MaxMetresPerPixel > 0f && mpp > rules[r].MaxMetresPerPixel) { _globalVisibilityCache[r] = false; skippedForZoom++; }
            RulesSkippedForZoom = skippedForZoom;
            int outOfView = 0;

            bool alwaysDraw = _isSelectedPredicate == null;
            var entityIndex = repo.GetEntityIndex();
            int maxIndex    = entityIndex.MaxIssuedIndex;
            float budget = MaxGizmoFrameMs;

            // ⭐ CE-2394 — ONE pass over the entities collects each visible rule's matches; then each rule draws its own, in the same
            //   order as before (rule by rule, ascending entity index). It walked every entity slot once PER RULE (~20), re-reading each
            //   one's metadata and component mask every time. ⛔ No allocation in the steady state: the match lists are reused and grow
            //   only when a rule matches more entities than ever before. The budget clock is a timestamp, not a Stopwatch object.
            EnsureMatchLists(ruleCount);
            bool timedPass = GizmoRuleTimings.Enabled;   // ⭐ CE-4388 — HROT_FRAME_STATS: the match pass itself (the rules alone did not add up to the system's time)
            long passStart = timedPass ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            // ⭐ CE-4388 — the rules this frame tests, copied ONCE into plain arrays: measured on Windows, the pass read rules[r] through
            //   IReadOnlyList<CompiledStatelessRule> (an interface call returning the whole rule — a 64-byte mask inside — by value) per entity
            //   per rule ≈ 250 000 times a frame, 12 ms on scale-1. A rule hidden for the frame (its policy, its zoom) is not in the list at all.
            if (_activeIndex.Length < ruleCount) { System.Array.Resize(ref _activeIndex, ruleCount); System.Array.Resize(ref _activeMask, ruleCount); System.Array.Resize(ref _activeOff, ruleCount); }
            int active = 0;
            for (int r = 0; r < ruleCount; r++)
            {
                if (r < _globalVisibilityCache.Length && !_globalVisibilityCache[r]) continue;
                var rule = rules[r];
                _activeIndex[active] = r; _activeMask[active] = rule.RequiredMask; _activeOff[active] = rule.DrawsOffScreen;
                active++;
            }

            // ⭐ CE-1480 G5 ① — the match pass is skipped when nothing it reads moved: the entity set (structural clock), the active rule set / filtering
            //   mode (the signature), the view (a position, when a filter is on: the in-view test reads it). Last frame's lists and out-of-view count stand.
            long sig = ruleCount;
            for (int a = 0; a < active; a++) sig = sig * 31 + _activeIndex[a] + 1;
            _matchSig = sig * 2 + (filtering ? 1 : 0);
            bool skipPass = (filtering ? _matchGatePos : _matchGate).Next(view).Kind == ChangeKind.Unchanged
                            && _matchValid && _lastMaxIndex == maxIndex
                            && (!filtering || filter!.Version >= 0 && filter.Version == _lastViewVersion);
            if (filtering) _lastViewVersion = filter!.Version;
            if (skipPass) { MatchPassesSkipped++; outOfView = _lastOutOfView; }
            else for (int r = 0; r < ruleCount; r++) _matchCount[r] = 0;
            for (int i = 0; !skipPass && i <= maxIndex; i++)
            {
                ref readonly var metaSG = ref entityIndex.GetMetadata(i);
                if (!metaSG.IsActive) continue;
                ref var compSG = ref entityIndex.GetComponentMask(i);
                bool tested = false, inView = true;   // ⭐ CE-4388 (D-1) — the view test, at most once per entity, only when a filtered rule matches
                for (int a = 0; a < active; a++)
                {
                    if (!BitMask512.HasAll(in compSG, in _activeMask[a])) continue;
                    int r = _activeIndex[a];
                    if (filtering && !_activeOff[a])
                    {
                        if (!tested) { inView = filter!.InView(view, new Entity(i, metaSG.Generation)); tested = true; if (!inView) outOfView++; }
                        if (!inView) continue;
                    }
                    var list = _matches[r];
                    int n = _matchCount[r];
                    if (n == list.Length) { System.Array.Resize(ref list, list.Length * 2); _matches[r] = list; }
                    list[n] = i;
                    _matchCount[r] = n + 1;
                }
            }

            _matchValid = true; _lastMaxIndex = maxIndex; _lastOutOfView = outOfView;
            EntitiesOutOfView = outOfView;
            if (timedPass) GizmoRuleTimings.Record("(match pass)", passStart, maxIndex + 1, 0);
            long start = budget > 0f ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            long budgetTicks = budget > 0f ? (long)(budget * System.Diagnostics.Stopwatch.Frequency / 1000.0) : 0;
            bool budgetExceeded = false;

            int frame = ++_frame;
            bool holding = FreshEntitiesPerFrame > 0 && _buffer != null;
            if (holding && _holds.Length < ruleCount) System.Array.Resize(ref _holds, ruleCount);

            for (int r = 0; r < ruleCount; r++)
            {
                // Skip rules whose global visibility policy rejects them.
                if (r < _globalVisibilityCache.Length && !_globalVisibilityCache[r])
                    continue;

                if (budgetExceeded) break;

                var rule = rules[r];
                var matched = _matches[r];
                int count = _matchCount[r];

                // ⭐ CE-2402 — the period this rule refreshes its entities at (1 = every entity every frame)
                RuleHold? hold = null;
                int period = 1;
                if (holding && count > FreshEntitiesPerFrame)
                {
                    period = System.Math.Min(1 + (count - 1) / FreshEntitiesPerFrame, System.Math.Max(1, MaxHeldFrames + 1));
                    if (period > 1)
                    {
                        hold = _holds[r] ??= new RuleHold();
                        hold.EnsureEntities(maxIndex + 1);
                    }
                }
                bool prevValid = hold != null && hold.WrittenFrame == frame - 1;
                int slot = frame % period;
                using var traceRule = Fdp.Core.AccessTrace.Scope(rule.Projector, "gizmo-rule: ");   // ⭐ A-1 — one reader per gizmo rule
                bool timed = GizmoRuleTimings.Enabled;   // ⭐ CE-4385 — HROT_FRAME_STATS: this rule's time and garbage
                long ruleStart = timed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0, ruleBytes = timed ? System.GC.GetAllocatedBytesForCurrentThread() : 0;
                int rulePrims = timed && _buffer != null ? _buffer.Count : 0;   // ⭐ CE-4388 — and the primitives it emits

                // ⭐ CE-1480 G5 ② — a rule that DECLARED its inputs: re-append its picture while none moved. Needs the whole frame's draw (no budget cut),
                //   every entity (no selection predicate) and, for a view-dependent rule, a view that publishes a version.
                RulePicture? pic = null; bool reuse = false, capture = false; int picMark = 0;
                var inputs = _ruleInputs[r];
                if (inputs != null && HonourDeclaredInputs && _buffer != null && alwaysDraw && budget <= 0f && ChangeGate.Enabled
                    && (!inputs.DependsOnView || (filter != null && filter.Version >= 0)))
                {
                    pic = _pics[r] ??= new RulePicture(inputs, this);
                    var kind = pic.Gate.Next(view).Kind;
                    pic.NextCount = 0;
                    for (int m = 0; m < count; m++)
                    {
                        int i = matched[m];
                        var gi = entityIndex.GetMetadata(i).Generation;
                        if (!ReferenceEquals(rule.VisibilityPolicy, AlwaysVisiblePolicy.Instance)
                            && !rule.VisibilityPolicy.IsEntityVisible(view, new Entity(i, gi))) continue;
                        pic.Add(i, gi);
                    }
                    if (kind == ChangeKind.Unchanged && pic.Valid && pic.SameAsNext())
                    {
                        for (int k = 0; k < pic.PrimCount; k++) _buffer.AppendRaw(in pic.Prims[k]);
                        RulePicturesReused++;
                        reuse = true;
                    }
                    else if (kind == ChangeKind.Unchanged)
                    {
                        // inputs are still: draw EVERY entity fresh (never a held, older picture) and keep what came out
                        capture = true; hold = null; picMark = _buffer.Count;
                    }
                    else pic.Valid = false;   // something moved: today's path (the slice / hold when on)
                }

                for (int m = 0; !reuse && m < count; m++)
                {
                    int i = matched[m];
                    var gen = entityIndex.GetMetadata(i).Generation;
                    var entity = new Entity(i, gen);

                    if (!alwaysDraw && !_isSelectedPredicate!(view, entity))
                        continue;

                    // ⭐⭐ UXI-23 S4: the per-ENTITY half of IGizmoVisibilityPolicy, finally consumed.
                    // 📄 UX_Feature_Map_Parity.md §3.2f · UX_Feature_Entity_Symbology.md §3.4.
                    //
                    // ⚠ This interface has always declared IsEntityVisible, and DataDrivenGizmoSystem has
                    // always honoured it (:326, :369) — but THIS system called only IsGloballyEnabled, so a
                    // per-entity policy registered here was stored and silently ignored. That made §3.4's
                    // design ("move CullingState out of the projector key and into the policy")
                    // unimplementable: the policy would have done nothing at all.
                    //
                    // ⭐ Cost: placed AFTER the mask match, so it runs once per MATCHED entity rather than
                    // per rule x entity; and the reference compare short-circuits it for AlwaysVisiblePolicy,
                    // which is every projector's default and what reflection registers.
                    if (!ReferenceEquals(rule.VisibilityPolicy, AlwaysVisiblePolicy.Instance)
                        && !rule.VisibilityPolicy.IsEntityVisible(view, entity))
                        continue;

                    if (hold == null)
                    {
                        rule.Projector.Draw(view, entity, _drawBuilder);
                    }
                    else
                    {
                        // ⭐ CE-2402 — HOLD the picture drawn on an earlier frame, unless this entity is due (its slice, its age, or no valid picture)
                        int age = prevValid && hold.PrevStamp[i] == frame - 1 && hold.PrevGen[i] == gen ? hold.PrevAge[i] + 1 : int.MaxValue;
                        if (age < period && i % period != slot)
                        {
                            var held = new System.ReadOnlySpan<DebugPrimitive>(hold.Prev, hold.PrevStart[i], hold.PrevLen[i]);
                            for (int k = 0; k < held.Length; k++) _buffer!.AppendRaw(in held[k]);
                            hold.Put(i, gen, age, frame, held);
                        }
                        else
                        {
                            int mark = _buffer!.Count;
                            rule.Projector.Draw(view, entity, _drawBuilder);
                            int end = _buffer.Count;
                            hold.Put(i, gen, 0, frame, _buffer.GetFrame().Slice(mark, end - mark));
                        }
                    }

                    // Check budget after each entity (only if budget is active).
                    if (budgetTicks > 0 && System.Diagnostics.Stopwatch.GetTimestamp() - start >= budgetTicks)
                    {
                        budgetExceeded = true;
                        break;
                    }
                }

                if (capture) pic!.Capture(_buffer!.GetFrame().Slice(picMark, _buffer.Count - picMark));
                hold?.Swap(frame);
                if (timed) GizmoRuleTimings.Record(rule.Projector.GetType().Name, ruleStart, count, System.GC.GetAllocatedBytesForCurrentThread() - ruleBytes, (_buffer?.Count ?? 0) - rulePrims);
            }
        }
    }
}
