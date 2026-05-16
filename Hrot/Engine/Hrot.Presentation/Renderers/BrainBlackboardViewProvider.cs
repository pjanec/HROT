using System;
using Fdp.Toolkit.Behavior.Components;
using StructEdit.Core.UnionSupport;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// StructEdit plugin that projects the raw <see cref="BrainBlackboard.BehaviorParameters"/>
/// fixed buffer as the active behavior's <c>ParamsDtoType</c> in the component editor.
/// Registered on startup via <c>ComponentReflector.AddBufferViewProvider</c> when
/// a behavior registry with typed parameters is available.
/// </summary>
public sealed class BrainBlackboardViewProvider : IBufferViewProvider
{
    /// <inheritdoc/>
    /// <remarks>
    /// Only intercepts the <c>$.BehaviorParameters</c> fixed-buffer field of <see cref="BrainBlackboard"/>
    /// when the caller has supplied a <c>"ParamsDtoType"</c> key via <see cref="EditContext"/>.
    /// </remarks>
    public bool CanCreateView(BufferViewRequest request)
        => request.ComponentType == typeof(BrainBlackboard)
        && request.BufferPath.Value == "$.BehaviorParameters"
        && request.ExternalContext?.Get<Type>("ParamsDtoType") != null;

    /// <inheritdoc/>
    /// <remarks>
    /// Projects the unmanaged bytes directly into the DTO struct layout using
    /// <see cref="BufferViewRequest.ProjectBufferAs"/>, which creates
    /// <c>NativeFieldBinding</c> objects mapped to the exact field offsets.
    /// UI edits perform zero-allocation writes directly into the buffer.
    /// </remarks>
    public BufferViewResult CreateView(BufferViewRequest request)
    {
        var dtoType = request.ExternalContext!.Get<Type>("ParamsDtoType")!;
        return request.ProjectBufferAs(dtoType, "Active Parameters");
    }
}
