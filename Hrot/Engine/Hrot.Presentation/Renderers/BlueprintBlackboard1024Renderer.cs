using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Blueprints.Components;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// The 1024-byte tier's inspector renderer. ⭐ <c>O3a</c> / task <c>B3</c>: the body lives in
/// <see cref="BlueprintBlackboardRendererBase{TTier}"/> — this class exists because
/// <see cref="ImGuiRendererAttribute"/> needs a concrete component type.
/// </summary>
[ImGuiRenderer(typeof(BlueprintBlackboard1024))]
public sealed class BlueprintBlackboard1024Renderer
    : BlueprintBlackboardRendererBase<BlueprintBlackboard1024>
{
}
