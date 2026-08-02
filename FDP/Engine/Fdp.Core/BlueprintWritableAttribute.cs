using System;

namespace Fdp.Core
{
    /// <summary>
    /// Marks an ECS component type as blueprint-writable (behavior-owned intent) -- the Blueprint
    /// editor's <c>SetComponent</c> write picker offers ONLY types carrying this attribute.
    ///
    /// <para>
    /// System-output components (e.g. <c>SimTransform</c>, physics-owned state) must NOT carry
    /// this attribute -- their fields are written by their owning system, and a blueprint racing
    /// that system would corrupt determinism. See
    /// <c>docs/blueprints/Blueprint_Component_Access_Design.md</c> (Q#16, write) for the full
    /// constraint model: writes are self-only, write-if-present (no implicit add), and race-free
    /// because <c>BlueprintTickSystem</c> runs <c>[UpdateBefore]</c> the Locomotion/Weapon
    /// dispatchers that own the excluded components.
    /// </para>
    ///
    /// <para>
    /// <b>This is an EDITOR-primary gate.</b> The Blueprint compiler runs as a netstandard2.0
    /// Roslyn analyzer and cannot load/reflect over arbitrary game assemblies to check this
    /// attribute at compile time (the same constraint documented on
    /// <see cref="ComponentIdAttribute"/>'s and <c>FunctionCallNode.TrailingContext</c>'s baked
    /// strings) -- so <c>SetComponentNode</c>'s Stage2 validator (<c>V_ComponentAccessRules</c>)
    /// performs STRUCTURAL checks only (non-empty/well-formed <c>ComponentTypeFqn</c>, self-only)
    /// and never inspects this attribute. The editor's component picker/palette (CA-04) is the
    /// actual enforcement point.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// [ComponentId(GlobalComponentIds.SomeBehaviorOwnedState)]
    /// [BlueprintWritable]
    /// public struct SomeBehaviorOwnedState { ... }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class BlueprintWritableAttribute : Attribute
    {
    }
}
