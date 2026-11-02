using System;
using System.Collections.Generic;

namespace Fdp.Presentation.Editing
{
    /// <summary>One parameter in scope of an editing surface: its <paramref name="Name"/> and its declared <paramref name="TypeId"/> (<c>"float"</c>, <c>"AreaLink"</c>, <c>"ForceId"</c> — <c>ParamTypes</c> maps it to a CLR type).</summary>
    public readonly record struct ParamInScope(string Name, string TypeId);

    /// <summary>
    /// ⭐⭐ CE-4210 P2 (MA-1, MA-2; docs/DESIGN_Macro_Authoring_Editor.md §3) — what an editing surface knows about the PARAMETERS a field may be fed from, brokered the way
    /// <see cref="IMapPickContext"/> brokers picks: the host that HAS the world passes one to <see cref="ComponentEditDrawer"/> (⛔ the silent-default rule — a host with a scope that
    /// passes null shows no bind button, and nothing says so); null ⇒ nothing changes anywhere.
    /// <para>The drawer decides WHERE a binding is written from the node (<see cref="ParamBindModel"/>): a reference leaf writes its own <c>^param</c> form and a property of a
    /// <c>BindableNode</c> writes that node's <c>Bind</c> map — neither goes through here. Only the third home, a scalar / enum field of a COMPONENT or a params struct, is
    /// written by the host: <see cref="Bind"/> / <see cref="Unbind"/> edit the entity's <c>ParamBindings</c> under <c>"&lt;Component&gt;.&lt;field path&gt;"</c>.</para>
    /// </summary>
    public interface IParamBindContext
    {
        /// <summary>The parameters in scope — what a field may be bound to (the resident header's <c>Params</c>). Empty ⇒ nothing is bindable.</summary>
        IReadOnlyList<ParamInScope> Params { get; }

        /// <summary>The param the scalar field at <paramref name="path"/> (<c>"Speed"</c>, <c>"Pose.Heading"</c> — relative to the surface's root, no <c>$.</c>) is bound to in the third home (<c>ParamBindings</c>); null when none.</summary>
        string? BoundTo(string path);

        /// <summary>Binds the scalar field at <paramref name="path"/> to <paramref name="param"/> (builds a NEW <c>ParamBindings</c>; one undoable edit).</summary>
        void Bind(string path, string param);

        /// <summary>Removes the field's binding (the literal stays).</summary>
        void Unbind(string path);

        /// <summary>The validator's reason the binding at <paramref name="path"/> is wrong (SR-149/162: the name is not declared, the type does not fit), or null. Used for the third home; the drawer checks the others itself.</summary>
        string? Problem(string path);

        /// <summary>True when this context can declare a NEW parameter (<see cref="NewParam"/>) — the popup then offers <i>New parameter…</i>.</summary>
        bool CanDeclareParams => false;

        /// <summary>Declares a parameter of <paramref name="fieldType"/> (default = the field's current value), named after <paramref name="suggestedName"/> (made unique), through the params editor's source; returns it, or null when it could not.</summary>
        ParamInScope? NewParam(Type fieldType, string suggestedName, object? currentValue) => null;
    }
}
