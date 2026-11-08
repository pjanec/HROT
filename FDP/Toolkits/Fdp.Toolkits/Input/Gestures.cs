using System;

namespace Fdp.Toolkit.Input;

/// <summary>⭐ B-10 K7 (docs/UX/DESIGN_Input_And_Commands.md §6b) — the mouse button of a map gesture.</summary>
public enum GestureButton : byte { Left, Right, Middle }

/// <summary>A click (press + release in place) or a drag (press, move, release).</summary>
public enum GestureKind : byte { Click, Drag }

/// <summary>The modifiers held during a gesture.</summary>
[Flags]
public enum GestureModifiers : byte { None = 0, Shift = 1, Ctrl = 2, Alt = 4 }

/// <summary>
/// ⭐ B-10 K7 — what a gesture lands ON. The BACKEND classifies it (it holds the world and the selection); a binding declares a MASK of the targets it covers.
/// </summary>
[Flags]
public enum GestureTarget : byte
{
    /// <summary>Nothing — open ground / empty space.</summary>
    Ground = 1,
    /// <summary>An entity that is not selected and not hostile to the selection.</summary>
    Entity = 2,
    /// <summary>An entity that is part of the selection.</summary>
    Selected = 4,
    /// <summary>An entity hostile to the selection (RTS: the Attack target).</summary>
    Hostile = 8,
    /// <summary>Any entity.</summary>
    AnyEntity = Entity | Selected | Hostile,
    /// <summary>Anything.</summary>
    Any = Ground | AnyEntity,
}

/// <summary>
/// ⭐ B-10 K7 — one user input on the map that is NOT a key: a mouse button on a target, with modifiers. As the INPUT of a lookup
/// <see cref="Target"/> is a single value (what was hit); as a DECLARATION it is the mask of targets the binding covers.
/// </summary>
public readonly record struct MapGesture(GestureButton Button, GestureKind Kind, GestureModifiers Modifiers, GestureTarget Target);

/// <summary>
/// ⭐ B-10 K7 — one row of the keymap for a gesture: this gesture, in this scope (<see cref="Qualifier"/> = the hat's name for <see cref="InputScope.Hat"/>),
/// runs this action. ⭐ The scope chain is the key rows' own: a hat's row sits in front of the map's.
/// </summary>
public readonly record struct GestureEntry(MapGesture Gesture, InputScope Scope, string? Qualifier, ActionRef Action);

/// <summary>Two gestures bound to different actions where both apply at once.</summary>
public readonly record struct GestureConflict(GestureEntry First, GestureEntry Second)
{
    /// <inheritdoc/>
    public override string ToString() =>
        $"{First.Gesture.Modifiers} {First.Gesture.Button} {First.Gesture.Kind} on {First.Gesture.Target} in {First.Scope}{(First.Qualifier is { } q ? " (" + q + ")" : "")}: '{First.Action}' and '{Second.Action}'";
}
