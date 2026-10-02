using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Blueprints.Components;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// The 16384-byte tier's inspector renderer. ⭐ <c>O3a</c> / task <c>B3</c>: the body lives in
/// <see cref="BlueprintBlackboardRendererBase{TTier}"/> — this class exists because
/// <see cref="ImGuiRendererAttribute"/> needs a concrete component type.
/// </summary>
[ImGuiRenderer(typeof(BlueprintBlackboard16384))]
public sealed class BlueprintBlackboard16384Renderer
    : BlueprintBlackboardRendererBase<BlueprintBlackboard16384>
{
}
