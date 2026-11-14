using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Blueprints.Components;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// The 256-byte tier's inspector renderer. ⭐ <c>O3b</c> / task <c>B4</c>: the body lives in
/// <see cref="BlueprintBlackboardRendererBase{TTier}"/> — this class exists because
/// <see cref="ImGuiRendererAttribute"/> needs a concrete component type.
/// ⭐⭐ Four lines, which is what <c>B3①</c>'s generic base bought.
/// </summary>
[ImGuiRenderer(typeof(BlueprintBlackboard256))]
public sealed class BlueprintBlackboard256Renderer
    : BlueprintBlackboardRendererBase<BlueprintBlackboard256>
{
}
