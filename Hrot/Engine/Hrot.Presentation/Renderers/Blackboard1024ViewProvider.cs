using System;
using Fdp.Toolkit.Behavior.Components;
using StructEdit.Core.UnionSupport;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// StructEdit plugin that projects the raw 1024-byte <see cref="Blackboard1024.Memory"/>
/// buffer as the active doctrine's <c>HeavyDtoType</c> in the component editor.
/// Registered on startup via <c>ComponentReflector.AddBufferViewProvider</c> when
/// a doctrine registry with heavy DTO types is available.
/// </summary>
public sealed class Blackboard1024ViewProvider : IBufferViewProvider
{
    /// <inheritdoc/>
    /// <remarks>
    /// Only intercepts the <c>$.Memory</c> fixed-buffer field of <see cref="Blackboard1024"/>
    /// when the caller has supplied a <c>"HeavyDtoType"</c> key via <see cref="EditContext"/>.
    /// </remarks>
    public bool CanCreateView(BufferViewRequest request)
        => request.ComponentType == typeof(Blackboard1024)
        && request.BufferPath.Value == "$.Memory"
        && request.ExternalContext?.Get<Type>("HeavyDtoType") != null;

    /// <inheritdoc/>
    /// <remarks>
    /// Projects the unmanaged bytes directly into the heavy DTO struct layout using
    /// <see cref="BufferViewRequest.ProjectBufferAs"/>, which creates
    /// <c>NativeFieldBinding</c> objects mapped to the exact field offsets.
    /// UI edits perform zero-allocation writes directly into the buffer.
    /// </remarks>
    public BufferViewResult CreateView(BufferViewRequest request)
    {
        var dtoType = request.ExternalContext!.Get<Type>("HeavyDtoType")!;
        return request.ProjectBufferAs(dtoType, "Heavy Parameters");
    }
}
