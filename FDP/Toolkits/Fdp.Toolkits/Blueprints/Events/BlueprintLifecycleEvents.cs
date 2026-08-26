using Fdp.Core;

namespace Fdp.Toolkit.Blueprints.Events;

/// <summary>
/// Requests attaching an Instance blueprint to an entity at runtime.
/// Consumed by <see cref="Systems.BlueprintEventIngressSystem"/> in the Input phase.
///
/// <para>
/// ⭐⭐⭐ <b>Batch 70 — a CLASS, not a struct, because it carries <see cref="ParamsJson"/>.</b>
/// <c>DESIGN_Parameter_Model.md</c> §3.3 rules that Instances reuse the behaviour parameter pipeline,
/// and that pipeline is fed by a JSON string. 📌 The shape is the one <c>AssignBehaviorEvent</c>
/// already set: <i>"must be a class (not a struct) because it carries managed string fields"</i>,
/// published via <c>PublishManaged</c>.
/// </para>
///
/// <para>
/// ⛔ <b>At runtime attach, this event's params JSON is the ONLY source of params</b> — a caller with
/// nothing to pass leaves it null and the blueprint's declared defaults stand. (Save→reload is a separate
/// path: <c>BlueprintStateTranslator</c> snapshots the resolved param bytes into
/// <c>BlueprintAssignmentDto.Params</c> and <c>BlueprintMaterializationSystem</c> re-applies them — MX-031/032.)
/// </para>
/// </summary>
[EventId(BlueprintConstants.EventId_AttachInstanceBlueprint)]
public sealed class AttachInstanceBlueprintEvent
{
    public Entity Entity;
    public int BlueprintId;

    /// <summary>
    /// Params for the attaching blueprint, as a JSON object keyed by parameter name.
    /// null/empty is valid and means "defaults only".
    /// </summary>
    public string? ParamsJson;
}

/// <summary>
/// Requests detaching an Instance blueprint from an entity at runtime.
/// Consumed by <see cref="Systems.BlueprintEventIngressSystem"/> in the Input phase.
///
/// <para>
/// ⭐ <b>Stays a struct.</b> A detach carries no params and therefore no managed field; converting it
/// would move it to the managed bus for no reason and cost an allocation per event. ⇒ the ingress
/// system drains this one with <c>Read&lt;T&gt;</c> and the other two with <c>ReadManaged&lt;T&gt;</c>.
/// </para>
/// </summary>
[EventId(BlueprintConstants.EventId_RemoveInstanceBlueprint)]
public struct RemoveInstanceBlueprintEvent
{
    public Entity Entity;
    public int BlueprintId;
}

/// <summary>
/// Requests replacing one Instance blueprint with another on an entity.
/// Applied as: detach old → attach new (remove-before-add ordering).
/// Consumed by <see cref="Systems.BlueprintEventIngressSystem"/> in the Input phase.
///
/// <para>
/// ⭐ <b>A class for the same reason Attach is</b> — its add half attaches a blueprint, so it must be
/// able to carry that blueprint's params. ⚠ The ingress system reads this stream TWICE in one frame
/// (old id in phase 1, new id in phase 2); <c>ReadManaged&lt;T&gt;</c> returns the read buffer
/// directly and only <c>SwapBuffers</c> clears it, so it is non-consuming exactly like
/// <c>Read&lt;T&gt;</c> and the two-phase drain is preserved.
/// </para>
/// </summary>
[EventId(BlueprintConstants.EventId_ReplaceInstanceBlueprint)]
public sealed class ReplaceInstanceBlueprintEvent
{
    public Entity Entity;
    public int OldBlueprintId;
    public int NewBlueprintId;

    /// <summary>Params for the NEW blueprint. See <see cref="AttachInstanceBlueprintEvent.ParamsJson"/>.</summary>
    public string? ParamsJson;
}
