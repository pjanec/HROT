using Fdp.Core;

namespace Fdp.Toolkit.Blueprints.Events;

/// <summary>
/// Requests attaching an Instance blueprint to an entity at runtime.
/// Consumed by <see cref="Systems.BlueprintEventIngressSystem"/> in the Input phase.
/// </summary>
[EventId(BlueprintConstants.EventId_AttachInstanceBlueprint)]
public struct AttachInstanceBlueprintEvent
{
    public Entity Entity;
    public int BlueprintId;
}

/// <summary>
/// Requests detaching an Instance blueprint from an entity at runtime.
/// Consumed by <see cref="Systems.BlueprintEventIngressSystem"/> in the Input phase.
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
/// </summary>
[EventId(BlueprintConstants.EventId_ReplaceInstanceBlueprint)]
public struct ReplaceInstanceBlueprintEvent
{
    public Entity Entity;
    public int OldBlueprintId;
    public int NewBlueprintId;
}
