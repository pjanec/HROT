using System.Text.Json.Nodes;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// The one place that knows which endpoint answers which kind of mistake (<c>MX8</c>).
    ///
    /// <para><b>This promotes a habit the code already had.</b> Several errors already end with the
    /// endpoint that fixes them — <i>"Entity {id} not found. List entities with GET /entities."</i>,
    /// <i>"Unknown component type… GET /components."</i> — but many do not, and the pointer is buried
    /// in prose an agent has to parse out of a sentence. The prose stays, for humans; this is the
    /// machine-readable half.</para>
    ///
    /// <para><b>Why one map and not a string per throw-site.</b> A hint written at each throw is a
    /// hint that drifts: rename an endpoint and the sentences still name the old one. Here, one edit
    /// moves every mention. ⇒ ⛔ never inline a <c>seeEndpoint</c> at a call site — add a category.</para>
    /// </summary>
    internal static class DebugApiHints
    {
        public const string Entity      = "entity";
        public const string Component   = "component";
        public const string Event       = "event";
        public const string Condition   = "condition";
        public const string Behavior    = "behavior";
        public const string MissionTask = "missionTask";
        public const string Variable    = "variable";
        public const string Baseline    = "baseline";
        public const string Breakpoint  = "breakpoint";
        public const string Scenario    = "scenario";
        public const string TkbType     = "tkbType";
        public const string Recording   = "recording";

        /// <summary>MX9 — the panel snapshot: a bad panel id, or a read while capture is off.</summary>
        public const string Panel       = "panel";

        /// <summary>MX2 — blueprint hot-attach: a blueprint name the registry does not know.</summary>
        public const string Blueprint   = "blueprint";

        /// <summary>
        /// The hint for a category: which endpoint to call and what it answers. Returns
        /// <see langword="null"/> for an unknown category, so an un-mapped failure simply carries no
        /// hint rather than a misleading one.
        /// </summary>
        public static JsonNode? For(string? category) => category switch
        {
            Entity      => Hint("GET /entities",         "the live entities and their network ids"),
            Component   => Hint("GET /components",       "the registered component type names"),
            Event       => Hint("GET /commands",         "the publishable event types and their payload shapes"),
            Condition   => Hint("GET /breakpoint-types", "valid condition $type values and their param schemas"),
            Behavior    => Hint("GET /behaviors?tkbType=", "the behaviours valid for an entity type, each with its param schema"),
            MissionTask => Hint("GET /behaviors?tkbType=", "the behaviours a mission task may name, each with its param schema"),
            Variable    => Hint("GET /entities/{id}/variables", "the entity's blueprint variables by asset and path"),
            Baseline    => Hint("POST /diff/capture",    "capture a baseline before comparing against one"),
            Breakpoint  => Hint("GET /breakpoints",      "the registered breakpoints and their ids"),
            Scenario    => Hint("GET /scenarios",        "the scenarios this build can load"),
            TkbType     => Hint("GET /tkb/types",        "the TKB templates and their tkbType ids"),
            Recording   => Hint("POST /recording/start", "start a recording before stopping or replaying one"),
            Panel       => Hint("GET /panels", "which panels are instrumented, and which published a model this frame"),
            Blueprint   => Hint("GET /blueprints", "the blueprints this editor has compiled, by name"),
            _           => null,
        };

        private static JsonNode Hint(string seeEndpoint, string why) => new JsonObject
        {
            ["seeEndpoint"] = seeEndpoint,
            ["why"] = why,
        };
    }
}
