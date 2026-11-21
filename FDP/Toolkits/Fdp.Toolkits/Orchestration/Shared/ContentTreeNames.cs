// CE-3340 (4) - netstandard2.0-SAFE ON PURPOSE (constants only, no dependency): this file is compiled into Fdp.Toolkits (net8.0) AND LINKED into
// Hrot.AiEditor.Persistence (netstandard2.0, the Roslyn-generator side) - the same mechanism as OccurrenceSlotKey / BuiltInHsmEvents there.
// It is `internal` on both sides, so no CS0436/CS0433 for a project that references both.
namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// The ONE spelling of the content tree's behaviour segment names. <c>ContentLayout</c> (runtime, the path authority) and
    /// <c>BehaviorGroups</c> (the generators' picker-group rule, which cannot reference Fdp.Toolkits) both read it - never a literal
    /// <c>"assets/behaviors/BTrees/"</c> in either. See docs/DESIGN_Content_Layout.md section 5 (D-7).
    /// </summary>
    internal static class ContentTreeNames
    {
        public const string Assets = "assets";
        public const string Behaviors = "behaviors";
        public const string BTrees = "BTrees";
        public const string Hsms = "HSMs";
        public const string Blueprints = "Blueprints";

        /// <summary><c>assets/behaviors/&lt;folder&gt;/</c> - the forward-slash prefix of a behaviour kind's root (what a generator matches a path against).</summary>
        public static string BehaviorKindRootPrefix(string folder) => Assets + "/" + Behaviors + "/" + folder + "/";
    }
}
