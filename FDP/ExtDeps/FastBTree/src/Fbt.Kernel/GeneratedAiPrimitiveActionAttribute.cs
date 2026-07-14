using System;

namespace Fbt.Kernel
{
    /// <summary>
    /// Marks the <c>TickCore</c> of a Blueprint-compiled <b>AiPrimitive</b> as a discoverable
    /// AI behavior action, so the editor's reflection-based action schema exporter surfaces it
    /// in the node palette / action pickers — the same way hand-written <see cref="BTreeActionAttribute"/>
    /// / <see cref="SharedAiActionAttribute"/> methods are surfaced.
    ///
    /// <para>
    /// This is deliberately a <b>distinct</b> attribute (not <see cref="BTreeActionAttribute"/> or
    /// <see cref="SharedAiActionAttribute"/>) for two reasons:
    /// <list type="bullet">
    ///   <item>The FastBTree / Shared-AI source generators scan for those attributes to emit runtime
    ///     registrars; a generated <c>TickCore</c> already registers its own thunk into the
    ///     <c>ActionRegistry</c> via the Blueprint registrar, so it must NOT be re-processed.</item>
    ///   <item>It lets the editor tell a blueprint-authored AiPrimitive apart from a hardcoded action
    ///     (surfacing it under its own <c>Source</c>/category) without any string/name heuristic.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// It carries the primitive's hosting flags (mirroring the compiler's <c>AiPrimitiveHosting</c>
    /// set) so the exporter can map them to the correct host graphs and mark conditions. The action's
    /// parameter DTO type is read from <c>TickCore</c>'s first <c>ref</c> parameter (<c>ref Params</c>),
    /// exactly as for the existing attributes — the attribute carries no <c>DtoType</c>.
    /// </para>
    ///
    /// <para>
    /// Placed in Fbt.Kernel so it is referenced by both the generated <c>Hrot.AI.Behaviors</c> assembly
    /// and the editor's schema exporter (both already reference Fbt.Kernel), alongside the other AI
    /// action attributes.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class GeneratedAiPrimitiveActionAttribute : Attribute
    {
        /// <summary>Hosted as a BTree action leaf (returns NodeStatus).</summary>
        public bool BTreeAction { get; }

        /// <summary>Hosted as a BTree condition leaf (Success ⇒ true).</summary>
        public bool BTreeCondition { get; }

        /// <summary>Hosted as an HSM activity action.</summary>
        public bool HsmAction { get; }

        /// <summary>Hosted as an HSM transition guard.</summary>
        public bool HsmGuard { get; }

        /// <summary>Invocable from a Blueprint (data-flow) graph.</summary>
        public bool BlueprintCall { get; }

        public GeneratedAiPrimitiveActionAttribute(
            bool bTreeAction    = false,
            bool bTreeCondition = false,
            bool hsmAction      = false,
            bool hsmGuard       = false,
            bool blueprintCall  = false)
        {
            BTreeAction    = bTreeAction;
            BTreeCondition = bTreeCondition;
            HsmAction      = hsmAction;
            HsmGuard       = hsmGuard;
            BlueprintCall  = blueprintCall;
        }
    }
}
