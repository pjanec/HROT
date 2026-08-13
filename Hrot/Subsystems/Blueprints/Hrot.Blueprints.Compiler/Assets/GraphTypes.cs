using System.Text.Json.Serialization;

namespace Hrot.Blueprints.Core.Assets;

public sealed class Graph
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public GraphKind Kind { get; set; }
    public List<ParameterDecl> Inputs { get; set; } = new();
    public List<ParameterDecl> Outputs { get; set; } = new();

    /// <summary>
    /// BP-80 / Macro_Implementation_Design F5 — the exec continuations a <see cref="GraphKind.Macro"/>
    /// graph offers its call sites. Empty (and meaningless) for every other kind.
    ///
    /// <para>
    /// ⚠⚠ <b>A separate list, deliberately — exec entries must NEVER go in <see cref="Outputs"/>.</b>
    /// <c>Outputs.Count</c> is load-bearing <em>arithmetic</em> at 20 executable sites across 8 files
    /// (<c>Stage5_Schedule</c> ×7, <c>ReturnNodeDrawer</c> ×4, <c>CSharpEmitter</c>, <c>NodePinSchema</c>,
    /// <c>GraphSignatureWindow</c>, <c>LibraryEmitter</c>, <c>Stage0_Rehydrate</c>, <c>Stage2_Validate</c>)
    /// — <c>Outputs.Count == 0</c> selects a NodeStatus return, <c>&gt; 1</c> selects tuple packing, and
    /// <c>ReturnNodePins</c> pairs them positionally. Injecting exec entries there would move every one
    /// of those counts <b>silently</b>.
    /// </para>
    ///
    /// <para>
    /// ⛔ <b>Superseded note.</b> This used to say the exec <em>input</em> stays implicit because
    /// Q25-D3 declared exactly one per macro. <b>Q26-A3 replaced that</b>: see
    /// <see cref="ExecInputs"/>, the symmetric list on the entry side.
    /// </para>
    /// </summary>
    public List<ExecOutDecl> ExecOutputs { get; set; } = new();

    /// <summary>
    /// BP-74 / Q26-A3 — the exec <b>entries</b> a <see cref="GraphKind.Macro"/> graph offers its call
    /// sites. Empty (and meaningless) for every other kind.
    ///
    /// <para>
    /// ⛔ <b>Q26-A3 supersedes Q25-D3.</b> A macro used to declare exactly one, implicit, exec-in.
    /// It now declares <c>N</c> — precisely so that a selection entered from two places can be
    /// COLLAPSED into a two-entry macro instead of being refused.
    /// </para>
    ///
    /// <para>
    /// ⚠⚠ <b>A new list, for exactly the reason <see cref="ExecOutputs"/> is one.</b>
    /// <c>Inputs.Count</c> is load-bearing arithmetic at 16 executable sites across 7 files —
    /// <c>InstanceEmitter</c> ×5, <c>Stage2_Validate</c> ×4 (including <c>BP1652</c>'s arity check),
    /// <c>CSharpEmitter</c> ×2, <c>NodePinSchema</c> ×2, <c>Stage0_Rehydrate</c>,
    /// <c>Stage5_Schedule</c>, and <c>Stage2_5_ExpandMacros</c> itself. Putting exec entries in
    /// <see cref="Inputs"/> would move every one of them silently, including the splice.
    /// </para>
    ///
    /// <para>
    /// 📌 <b>Empty means today's behaviour</b>: <c>ExecInputs.Count == 0</c> ⇒ the single implicit
    /// entry, exactly as before, so every existing asset round-trips byte-identically.
    /// </para>
    /// </summary>
    public List<ExecInDecl> ExecInputs { get; set; } = new();

    public List<Node> Nodes { get; set; } = new();
    public List<Link> Links { get; set; } = new();

    /// <summary>
    /// BP-57 / Q27 — variables scoped to <b>this graph</b> and reset on every invocation.
    ///
    /// <para>
    /// ⭐ <b>A local is NOT a <c>State</c> field.</b> Q27-E makes the scoping literal: the local is
    /// re-initialised from <see cref="VariableDecl.DefaultValueJson"/> on entry, so invocation N+1
    /// never sees invocation N's value — which is the whole reason the feature exists rather than
    /// "just add an instance variable".
    /// </para>
    ///
    /// <para>
    /// ⚠⚠ <b>Q27-A3: the STORAGE is a compiler choice and is deliberately invisible here.</b> A graph
    /// that cannot suspend gets a plain C# local; one that CAN gets a graph-scoped blackboard slot
    /// reset in the entry block, because a suspension returns out of the method and a stack local
    /// would not survive it. Both mean the same thing to a designer, and neither is spelled on this
    /// declaration. See <c>LocalStorage</c>.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b><see cref="VariableDecl"/> is reused wholesale, and two of its members are meaningless
    /// here.</b> <c>IsEditable</c> and <c>IsExposedOnSpawn</c> describe an instance's inspector surface;
    /// a local has no instance to be exposed on. They are ignored rather than a narrower decl being
    /// introduced, because the narrower type would duplicate <c>Id</c>/<c>Name</c>/<c>Type</c>/
    /// <c>DefaultValueJson</c>/<c>Category</c>/<c>Tooltip</c> — six of eight members — and would cost
    /// Batch 38 the type picker and every existing row view, which are written against
    /// <see cref="VariableDecl"/>.
    /// </para>
    ///
    /// <para>
    /// ⛔ <b>A <see cref="GraphKind.Macro"/> graph may not declare one</b> (<c>BP1664</c>). A macro is
    /// spliced, so after expansion it does not exist as a graph and its nodes are the host's — there is
    /// nothing for a macro-local to be scoped to. Unreal ships macro locals and they leak into the
    /// host's scope without resetting; this refuses the construct rather than reproducing it.
    /// </para>
    ///
    /// <para>📌 Empty is today's behaviour, and an asset without the field round-trips unchanged.</para>
    /// </summary>
    public List<VariableDecl> LocalVariables { get; set; } = new();

    /// <summary>
    /// BP-220 — a copy of this graph carrying new <see cref="Nodes"/>/<see cref="Links"/> and
    /// <b>every other member preserved</b>.
    ///
    /// <para>
    /// ⚠⚠ <b>The point is that the copy is written ONCE, here, instead of at each call site.</b>
    /// <c>Stage3_Normalize</c> rebuilt <c>Graph</c> field-by-field at two places and both copied 9 of
    /// 10 members — silently dropping <see cref="Comments"/>. Batch 29 then had to remember
    /// <see cref="ExecOutputs"/> at both sites by hand, and nothing would have failed if it had been
    /// missed at one. A copy that must be REMEMBERED is a copy that will be forgotten; this is the
    /// same class of defect as the denormalised <c>Pin.LinkedToIds</c> mirror, one level up.
    /// </para>
    ///
    /// <para>
    /// <c>Graph_CopyShape_PreservesEveryMember</c> walks this type's properties by reflection and
    /// fails on the NEXT member added without being handled here — which is exactly when the
    /// knowledge is needed, rather than several batches later.
    /// </para>
    /// </summary>
    public Graph WithNodesAndLinks(List<Node> nodes, List<Link> links) => new()
    {
        Id             = Id,
        Name           = Name,
        Kind           = Kind,
        Inputs         = Inputs,
        Outputs        = Outputs,
        ExecOutputs    = ExecOutputs,
        ExecInputs     = ExecInputs,
        LocalVariables = LocalVariables,
        Comments       = Comments,
        EditorMetadata = EditorMetadata,
        Nodes          = nodes,
        Links          = links,
    };

    /// <summary>
    /// Unreal-style comment boxes ("Add Comment" on the canvas). Pure editor annotation —
    /// the compiler never reads this list; it exists only so comments round-trip through
    /// save/reload. See <see cref="GraphComment"/>.
    /// </summary>
    public List<GraphComment> Comments { get; set; } = new();
    public GraphMetadata EditorMetadata { get; set; } = new();
}

/// <summary>
/// BP-74 / Q26-A3 — one declared exec <b>entry</b> of a <see cref="GraphKind.Macro"/> graph. Projects
/// to an exec-<b>Out</b> pin on the macro's <c>EventEntryNode</c> (the input boundary) and to an
/// exec-<b>In</b> pin on every <see cref="MacroCallNode"/> targeting it, paired <b>positionally</b> in
/// declaration order — <c>Stage2_5_ExpandMacros</c>' splice rule 1 rewires <c>execIn[k]</c> to the
/// successor of <c>execOut[k]</c>, so the order is load-bearing on both sides.
///
/// <para>
/// ⚠ <b>Properties, not fields</b> — same trap as <see cref="ExecOutDecl"/>: System.Text.Json does not
/// serialise fields without <c>IncludeFields</c>, so a field-based shape would round-trip as <c>{}</c>.
/// </para>
/// </summary>
public sealed class ExecInDecl
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Tooltip { get; set; }
}

/// <summary>
/// BP-80 — one declared exec continuation of a <see cref="GraphKind.Macro"/> graph. Projects to an
/// exec-<b>In</b> pin on the macro's <c>ReturnNode</c> (the output boundary) and to an exec-<b>Out</b>
/// pin on every <see cref="MacroCallNode"/> targeting it, paired <b>positionally</b> in declaration
/// order — <c>Stage2_5_ExpandMacros</c>' splice rule 2 rewires <c>execIn[k]</c> to <c>execOut[k]</c>,
/// so the order is load-bearing on both sides.
///
/// <para>
/// ⚠ <b>Properties, not fields.</b> The design note wrote this as a field-only record; System.Text.Json
/// does not serialise fields unless <c>IncludeFields</c> is set (it is not — see the same trap called
/// out on <see cref="GraphComment"/> and <see cref="LinkWaypoint"/>), so a field-based shape would
/// round-trip as <c>{}</c> and every declared exec-out would vanish on save/reload.
/// </para>
/// </summary>
public sealed class ExecOutDecl
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Tooltip { get; set; }
}

/// <summary>
/// ⚠ Serialised as a <b>string</b> (<c>BlueprintJsonServices</c>), so adding a member is additive on
/// disk -- no existing asset changes and no migration is needed.
/// </summary>
public enum GraphKind
{
    Function,
    Event,
    Construction,

    /// <summary>
    /// A reusable exec/data template spliced into its call sites. ⚠ <b>Never a compilation target:</b>
    /// Stage 5 skips it, and it is deliberately not tick-eligible -- <c>InstanceEmitter</c> picks a
    /// tick graph from <c>IrGraphKind.Function</c> graphs only, and a macro produces no IrGraph at all.
    /// </summary>
    Macro,
}

public sealed class Pin
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Direction { get; set; } = "";
    public BlueprintTypeRef TypeRef { get; set; } = new();
    public bool IsExec { get; set; }
    public List<Guid> LinkedToIds { get; set; } = new();

    /// <summary>
    /// Inline default value for this pin, stored as a JSON-compatible string
    /// (e.g. "42" for int, "3.14" for float, "true" for bool, "hello" for string).
    /// Null when no default has been set. Written to disk only when non-null.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultValue { get; set; }
}

public sealed class Link
{
    public Guid FromNodeId { get; set; }
    public Guid FromPinId { get; set; }
    public Guid ToNodeId { get; set; }
    public Guid ToPinId { get; set; }

    /// <summary>
    /// Reroute waypoints along the wire.  Empty list = straight wire (default).
    /// Serialized as a JSON array; omitted from output only when null (we default to new() so
    /// it will appear as [] in JSON when no waypoints have been added — consistent with other list fields).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<LinkWaypoint>? Waypoints { get; set; }
}

/// <summary>
/// A single reroute point on a Blueprint wire.  Uses float properties (not fields) so
/// System.Text.Json serializes correctly even without <c>IncludeFields = true</c>.
/// Mirrors the shape of <see cref="NodeMetadata"/> (X/Y float properties).
/// </summary>
public sealed class LinkWaypoint
{
    public float X { get; set; }
    public float Y { get; set; }
}

/// <summary>
/// Asset-level projection of an Unreal-style comment box (NodeEdit's
/// <c>NodeEditor.Core.Interfaces.ICommentModel</c>). Position/size are stored as flat
/// float properties (X/Y/W/H) — same shape convention as <see cref="NodeMetadata"/> and
/// <see cref="LinkWaypoint"/> — rather than a <c>System.Numerics.Vector2</c>, whose X/Y are
/// FIELDS and would serialize to <c>{}</c> unless <c>IncludeFields</c> were enabled.
/// Color is stored as four float channels (RGBA, matching <c>System.Numerics.Vector4</c>
/// component order) for the same reason.
/// <para>
/// Pure editor annotation: the compiler stages never read this type. "Move with contents"
/// is computed geometrically by the NodeEdit canvas at drag-time (nodes whose bounds are
/// fully contained by the comment rect) — there is no persisted child-node-id list to keep
/// in sync, so none is stored here.
/// </para>
/// </summary>
public sealed class GraphComment
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public float ColorR { get; set; } = 0.29f;
    public float ColorG { get; set; } = 0.56f;
    public float ColorB { get; set; } = 0.88f;
    public float ColorA { get; set; } = 1f;
    public int ZOrder { get; set; }
    public bool MoveWithContents { get; set; } = true;
}

public sealed class AssetMetadata
{
    public string? Description { get; set; }
    public string? Category { get; set; }
    public RecipeMetadata? Recipe { get; set; }

    /// <summary>
    /// Compiler mode override for this asset. Default <see cref="Compiler.CompilerMode.Debug"/>
    /// emits only <c>NodeEnter</c> probes (suitable for breakpoints/stepping).
    /// Set to <see cref="Compiler.CompilerMode.Trace"/> to emit <c>PinValueChanged&lt;T&gt;</c>
    /// probes for watch expressions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Compiler.CompilerMode CompilerMode { get; set; } = Compiler.CompilerMode.Debug;
}

public sealed class RecipeMetadata
{
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Difficulty { get; set; } = "Beginner";
    public List<string> ConceptsTaught { get; set; } = new();
}

public sealed class GraphMetadata
{
    public float ViewportX { get; set; }
    public float ViewportY { get; set; }
    public float ViewportZoom { get; set; }
}

public sealed class NodeMetadata
{
    public float X { get; set; }
    public float Y { get; set; }
    public string? Comment { get; set; }

    /// <summary>
    /// BP-17 -- author-supplied node header text, overriding the title generated from the node's
    /// kind and configuration. Null (and omitted from JSON) means "use the generated title", so
    /// existing assets round-trip byte-identically and a node whose configuration later changes
    /// still re-titles itself.
    /// <para>
    /// Purely presentational: nothing in the compiler reads it.
    /// </para>
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomTitle { get; set; }

    /// <summary>
    /// BP-18 -- whether the node draws with its body collapsed to the header. Editor-only view
    /// state; false (default, omitted from JSON) is the normal expanded node.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Collapsed { get; set; }
}

public sealed class Header
{
    // SubsystemType and SchemaVersion removed -- $meta envelope carries this since Phase 2 (D-021).
}

public enum NodeStatus { Success, Failure, Running }
