namespace Fdp.Toolkit.Blueprints.Partitioning
{
    /// <summary>
    /// Clarity-only runtime twin of the editor's <c>BlackboardVariableRole</c> byte values
    /// (0 = Input, 1 = State). Exists so generated <c>StatefulSlotInfo</c> registrar code can emit
    /// a named enum cast for the Role argument instead of a raw integer literal, without the
    /// low-level runtime assembly (<c>Fdp.Toolkits</c>) taking a dependency on the editor's
    /// persistence enums. Mirrors <see cref="StatefulSlotScope"/> in structure and intent.
    /// </summary>
    public enum StatefulSlotRole : byte
    {
        /// <summary>A parameter / input value. Default.</summary>
        Input = 0,
        /// <summary>Mutable working state.</summary>
        State = 1,
    }
}
