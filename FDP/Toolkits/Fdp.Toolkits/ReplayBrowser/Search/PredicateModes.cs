using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐ CE-4219 ③ (<c>docs/DESIGN_Rules_View_UI.md</c> §7.5a) — which root <see cref="SearchPredicateDto"/> subtype a predicate form edits. ONE enum for every host that offers a predicate form
    /// (the Replay Browser's search panel, the Data Breakpoint Manager's builder); each offers its own SUBSET (<see cref="PredicateModes.All"/> is the whole set).
    /// ⚠ Not the compiler's internal <c>PredicateMode</c> (Scan / State) — a different concept.
    /// </summary>
    public enum SearchPredicateMode
    {
        Component,
        Event,
        Lifecycle,
        Spatial,
        Structural,
        Compound,
        BehaviorParam,
        BlueprintVariable,
        TraceBufferScan,
    }

    /// <summary>
    /// ⭐ CE-4219 ③ — THE mode table: a mode's label, its blank predicate and the inverse (the mode a DTO belongs to). It replaces <c>PredicateBuilderState.CreateDefaultDto / InferMode</c> and
    /// <c>ReplaySearchPanel.DtoForMode</c>'s blank DTOs, which were two copies of one mapping. A host's switch behaviour stays its own (the search panel keeps one DTO per mode so a switch preserves edits;
    /// the breakpoint builder replaces it).
    /// </summary>
    public static class PredicateModes
    {
        /// <summary>Every mode, in display order.</summary>
        public static IReadOnlyList<SearchPredicateMode> All { get; } = (SearchPredicateMode[])Enum.GetValues(typeof(SearchPredicateMode));

        /// <summary>The mode's display label.</summary>
        public static string Label(SearchPredicateMode mode) => mode switch
        {
            SearchPredicateMode.Component         => "Component",
            SearchPredicateMode.Event             => "Event",
            SearchPredicateMode.Lifecycle         => "Lifecycle",
            SearchPredicateMode.Spatial           => "Spatial",
            SearchPredicateMode.Structural        => "Structural",
            SearchPredicateMode.Compound          => "Compound",
            SearchPredicateMode.BehaviorParam     => "Behavior Param",
            SearchPredicateMode.BlueprintVariable => "Blueprint Variable",
            SearchPredicateMode.TraceBufferScan   => "Trace Buffer Scan",
            _                                     => mode.ToString(),
        };

        /// <summary>A blank predicate of the mode's subtype (a fresh instance each call).</summary>
        public static SearchPredicateDto CreateDefault(SearchPredicateMode mode) => mode switch
        {
            SearchPredicateMode.Component         => new PropertyMatchDto(),
            SearchPredicateMode.Event             => new TransientEventPredicateDto(),
            SearchPredicateMode.Lifecycle         => new LifecyclePredicateDto(),
            SearchPredicateMode.Spatial           => new SpatialBoundingPredicateDto(),
            SearchPredicateMode.Structural        => new StructuralPredicateDto(),
            SearchPredicateMode.Compound          => new CompoundPredicateDto(),
            SearchPredicateMode.BehaviorParam     => new BehaviorParamPredicateDto(),
            SearchPredicateMode.BlueprintVariable => new BlueprintVariablePredicateDto(),
            SearchPredicateMode.TraceBufferScan   => new TraceBufferScanPredicateDto(),
            _                                     => new PropertyMatchDto(),
        };

        /// <summary>The mode a predicate DTO belongs to (<see cref="SearchPredicateMode.Component"/> for a subtype no mode names).</summary>
        public static SearchPredicateMode ModeOf(SearchPredicateDto dto) => dto switch
        {
            PropertyMatchDto              => SearchPredicateMode.Component,
            TransientEventPredicateDto    => SearchPredicateMode.Event,
            LifecyclePredicateDto         => SearchPredicateMode.Lifecycle,
            SpatialBoundingPredicateDto   => SearchPredicateMode.Spatial,
            StructuralPredicateDto        => SearchPredicateMode.Structural,
            CompoundPredicateDto          => SearchPredicateMode.Compound,
            BehaviorParamPredicateDto     => SearchPredicateMode.BehaviorParam,
            BlueprintVariablePredicateDto => SearchPredicateMode.BlueprintVariable,
            TraceBufferScanPredicateDto   => SearchPredicateMode.TraceBufferScan,
            _                             => SearchPredicateMode.Component,
        };
    }
}
