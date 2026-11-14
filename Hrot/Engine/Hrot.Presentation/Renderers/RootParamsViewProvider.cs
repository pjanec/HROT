using System;
using Fdp.Toolkit.Blueprints.Partitioning;
using StructEdit.Core.UnionSupport;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// ⭐⭐⭐ <c>P4</c>-③ — the StructEdit plugin that projects a behaviour's ROOT PARAMS as its typed
/// <c>BlackboardLayoutType</c>, from the occurrence slot that holds them.
///
/// <para>🔴 <b>What it replaces.</b> <c>BrainBlackboardViewProvider</c> projected
/// <c>BrainBlackboard.$.BehaviorParameters</c> — a buffer <c>P3</c> stopped filling while leaving the
/// component attached, so the component editor bound its fields to <b>permanently zero</b> bytes and
/// every edit wrote into memory nothing reads. 📄 <c>CE-312</c>, §30.22.</para>
///
/// <para>⭐⭐ <b>Two context keys, and the second is the whole change.</b> The caller supplies the
/// DTO type <i>and</i> the slot's base offset; this provider adds nothing of its own. ⛔ StructEdit
/// still learns nothing about slots or keys — <see cref="BufferViewRequest.ProjectBufferAs"/> merely
/// composes one more addend. 📄 §30.7, which ruled exactly this shape.</para>
/// </summary>
public sealed class RootParamsViewProvider : IBufferViewProvider
{
    /// <summary>Context key carrying the DTO type to project. Unchanged from the retired provider.</summary>
    public const string LayoutTypeKey = "BlackboardLayoutType";

    /// <summary>
    /// Context key carrying the root params slot's byte offset within the tier component's buffer.
    /// ⚠ A valid payload offset is always <c>&gt; 0</c> — the header and slot table occupy the first
    /// bytes — so <c>0</c> unambiguously means "absent", and this provider then declines.
    /// </summary>
    public const string OffsetKey = "RootParamsOffset";

    /// <inheritdoc/>
    public bool CanCreateView(BufferViewRequest request)
    {
        if (request.BufferPath.Value != "$.Memory") return false;
        if (!IsTierComponent(request.ComponentType)) return false;
        if (request.ExternalContext?.Get<Type>(LayoutTypeKey) == null) return false;
        return request.ExternalContext.Get<int>(OffsetKey) > 0;
    }

    /// <inheritdoc/>
    public BufferViewResult CreateView(BufferViewRequest request)
    {
        var dtoType = request.ExternalContext!.Get<Type>(LayoutTypeKey)!;
        int offset  = request.ExternalContext.Get<int>(OffsetKey);
        return request.ProjectBufferAs(dtoType, "Active Parameters", offset);
    }

    /// <summary>
    /// ⭐ Asks the tier TABLE rather than listing the four component types. ⛔ A hand-written list is
    /// the shape that goes stale when a fifth tier is added — which is exactly when a silently
    /// non-matching provider is hardest to notice.
    /// </summary>
    private static bool IsTierComponent(Type componentType)
    {
        var tiers = BlueprintTierTable.Ascending;
        for (int i = 0; i < tiers.Count; i++)
            if (tiers[i].ComponentType == componentType) return true;
        return false;
    }
}
