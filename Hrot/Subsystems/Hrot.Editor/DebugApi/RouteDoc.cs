using System;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE AGENT-FACING CONTRACT OF ONE ENDPOINT, held next to the endpoint</b> — the one source <c>GET /capabilities</c> (JSON) and
    /// <c>GET /help</c> / <c>--help-api</c> (the guide) render (docs/DESIGN_Debug_Api_Http_Only.md §3, R-4223).
    ///
    /// <para>⚠ The summaries, notes, hints and examples are TEACHING content authored by a person; what keeps them true is enforcement:
    /// a route without its doc fails <c>EveryRouteIsDocumentedTests</c>, a handler reading an undocumented key or reading it from another
    /// place than derived fails <c>EveryRouteReadsItsParamsWhereDerivedTests</c>.</para>
    ///
    /// <para>⛔ HISTORY (HN-030): the docs were first moved here from the MCP server's hand-kept JS catalog, which had drifted from the
    /// routes; that server and its generated catalog / skill are removed (CE-4526).</para>
    /// </summary>
    /// <param name="Tool">
    /// ⭐ The route's short unique id (once its MCP tool name — HISTORY). Kept: hints and rails name routes by it.
    /// </param>
    /// <param name="Group">The catalog group heading, e.g. <c>"A — Lifecycle &amp; status"</c>.</param>
    /// <param name="Summary">One line: what the endpoint does.</param>
    /// <param name="Returns">Shape of the payload, in prose or a pseudo-literal.</param>
    /// <param name="Hint">
    /// ⭐ The educating error hint — what a caller who got it wrong should read *(`MX8`)*.
    /// </param>
    /// <param name="Params">
    /// The agent-visible parameters (names, types, meaning: declared). ⭐ WHERE each goes is derived, never authored — a <c>{name}</c> in the
    /// template is the path, else GET / DELETE the query and POST / PUT / PATCH the body (<see cref="ParamLocations"/>, CE-4523); a rail
    /// checks every handler reads its keys there.
    /// </param>
    /// <param name="Notes">The gotchas. ⭐ This is where most of the endpoint's real teaching lives.</param>
    /// <param name="ExampleArgsJson">A JSON object literal of example arguments, or <see langword="null"/> for none.</param>
    /// <param name="ExampleGist">One line saying what the example achieves.</param>
    /// <param name="ManualVerify">⚠ True when the endpoint cannot be verified automatically end-to-end.</param>
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
        bool ManualVerify = false);

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
