using System;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE AGENT-FACING CONTRACT OF ONE ENDPOINT, held next to the endpoint.</b>
    /// 📄 <c>MCP_Integration.md</c> § *"Follow-up — GENERATE `tool-catalog.mjs` from the routes"* *(`HN-030`)*.
    ///
    /// <para>⛔⛔ <b>The problem this exists to end.</b> <c>tools/ai-debug-mcp/tool-catalog.mjs</c> was a
    /// HAND-MAINTAINED MIRROR of the HTTP route table — the exact *"hand-authored vs derived"* rot that
    /// <c>GET /capabilities</c> was built to kill *(`Q54-1` / charter `D4`)*. 📌 And it rotted exactly as
    /// predicted: <c>HN-025/026/027</c> shipped <c>/capabilities</c>, <c>/perspectives</c> and
    /// <c>/perspective</c> with no catalog update, and <c>HN-029</c>'s own skill prose then told agents to
    /// call a <c>switch_perspective</c> tool that <b>did not exist</b>. ⚠ Nobody had to forget; the mirror
    /// just drifted.</para>
    ///
    /// <para>⭐⭐ <b>So the catalog is now DERIVED.</b> This record is the source; <c>GET /capabilities</c>
    /// emits it; the JS side transforms that dump into <c>tool-catalog.mjs</c>, and <c>SKILL.md</c> is
    /// generated from there as before. ⇒ ⭐ <b>one route-derived source, three consumers</b> *(the manifest,
    /// the catalog, the skill)*, none hand-authored.</para>
    ///
    /// <para>⚠⚠ <b>What this does NOT do, stated plainly:</b> it does not make the prose write itself. The
    /// summaries, notes, hints and examples are still authored by a person — they are TEACHING content and
    /// cannot be derived from a method signature. ⭐ What changes is that they are authored HERE, beside the
    /// route, and a route without them fails <c>EveryRouteIsDocumentedTests</c>. ⇒ the drift class above
    /// becomes structurally impossible rather than merely detectable.</para>
    /// </summary>
    /// <param name="Tool">
    /// ⭐ The MCP tool name. ⚠ Authored, not derived: 📐 measured, only 11 of 67 tool names follow any
    /// mechanical path rule *(<c>GET /breakpoints/hits</c> → <c>get_breakpoint_status</c>,
    /// <c>POST /sim/step</c> → <c>step</c>)*, so a naming convention would rename most of the surface.
    /// </param>
    /// <param name="Group">The catalog group heading, e.g. <c>"A — Lifecycle &amp; status"</c>.</param>
    /// <param name="Summary">One line: what the endpoint does.</param>
    /// <param name="Returns">Shape of the payload, in prose or a pseudo-literal.</param>
    /// <param name="Hint">
    /// ⭐ The educating error hint — what a caller who got it wrong should read *(`MX8`)*.
    /// </param>
    /// <param name="Params">
    /// The agent-visible parameters. ⚠ Declared, not derived: a body field is read as
    /// <c>ctx.Body?["count"]</c> inside a lambda and a query as <c>ctx.Query("near")</c>, neither of which is
    /// machine-readable. ⭐ Only the 14 PATH params could ever be derived, and declaring them costs nothing
    /// extra once the line is being written anyway.
    /// </param>
    /// <param name="Notes">The gotchas. ⭐ This is where most of the endpoint's real teaching lives.</param>
    /// <param name="ExampleArgsJson">A JSON object literal of example arguments, or <see langword="null"/> for none.</param>
    /// <param name="ExampleGist">One line saying what the example achieves.</param>
    /// <param name="ManualVerify">⚠ True when the endpoint cannot be verified automatically end-to-end.</param>
    /// <param name="NotATool">
    /// ⭐⭐ <b>Deliberately NOT exposed as an MCP tool</b> — the endpoint exists and is documented here, but
    /// the generator emits no tool for it. 📌 Exactly one today: <c>POST /breakpoints/step</c>, which is
    /// <c>continue_from_breakpoint({step:true})</c>. ⛔ Without this flag the *"every route is documented"*
    /// rail could only be satisfied by inventing a tool nobody wants.
    /// </param>
    public sealed record RouteDoc(
        string Tool,
        string Group,
        string Summary,
        string Returns,
        string Hint,
        RouteParam[]? Params = null,
        string[]? Notes = null,
        string? ExampleArgsJson = null,
        string? ExampleGist = null,
        bool ManualVerify = false,
        bool NotATool = false);

    /// <summary>⭐ One agent-visible parameter of an endpoint.</summary>
    /// <param name="Name">The JSON key, exactly as the handler reads it.</param>
    /// <param name="Type">
    /// JSON-schema type: <c>string</c> · <c>number</c> · <c>integer</c> · <c>boolean</c> · <c>object</c> ·
    /// <c>array</c>. ⭐ <see langword="null"/> for a param that deliberately accepts more than one shape —
    /// 📌 <c>patch_attribute.patchJson</c> takes an object OR a JSON string, and pinning a type there would
    /// narrow the tool. ⚠ Found by the round-trip diff, which turned the missing type into the literal
    /// string <c>"undefined"</c>.
    /// </param>
    /// <param name="Required">Whether omitting it is an error.</param>
    /// <param name="Description">What it means — authored.</param>
    /// <param name="DefaultJson">The default as a JSON literal (<c>"1"</c>, <c>"false"</c>, <c>"\"preview\""</c>), or <see langword="null"/> for none.</param>
    /// <param name="EnumJson">
    /// ⭐ The allowed values, as a JSON array literal — e.g. <c>["world","orchestration"]</c>. It reaches the
    /// agent's <c>inputSchema</c>, so an omitted <c>enum</c> silently widens the tool's contract.
    /// </param>
    /// <param name="ItemsJson">⭐ JSON-schema <c>items</c> for an array param, e.g. <c>{"type":"number"}</c>.</param>
    /// <param name="PropertiesJson">⭐ JSON-schema <c>properties</c> for an object param.</param>
    /// <remarks>
    /// ⚠⚠ <b><see cref="EnumJson"/>, <see cref="ItemsJson"/> and <see cref="PropertiesJson"/> exist because a
    /// ROUND-TRIP CHECK found them missing.</b> 📐 The first cut of this record carried only name/type/
    /// required/description/default, and regenerating the catalog from it silently dropped the <c>enum</c> on
    /// five params *(<c>get_event_history.bus</c>, <c>start_recording.mode</c>, <c>step_replay.dir</c>,
    /// <c>get_logs.level</c>, <c>add_annotation.type</c>)*, the <c>items</c> on two and one <c>properties</c>.
    /// ⛔ Nothing would have failed — the tools would just have accepted anything. ⭐ Which is the whole reason
    /// the move was verified by regenerating and DIFFING against the hand-written catalog rather than by
    /// reading it.
    /// </remarks>
    public sealed record RouteParam(
        string Name,
        string? Type,
        bool Required,
        string Description,
        string? DefaultJson = null,
        string? EnumJson = null,
        string? ItemsJson = null,
        string? PropertiesJson = null);
}
