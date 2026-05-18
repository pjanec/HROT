using System;
using StructEdit.Core;

namespace Fdp.Presentation.Editing;

/// <summary>
/// Plugin interface for custom ImGui editors for specific CLR field types.
/// </summary>
public interface IImGuiFieldDrawer
{
    /// <summary>The CLR type this drawer handles.</summary>
    Type TargetType { get; }

    /// <summary>
    /// Draws input widgets for <paramref name="value"/>.
    /// Returns <see langword="true"/> if the value changed.
    /// </summary>
    bool DrawInput(ref object value, EditNode node);
}
