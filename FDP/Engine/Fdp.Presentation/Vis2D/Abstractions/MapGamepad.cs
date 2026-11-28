namespace Fdp.Toolkit.Vis2D.Abstractions;

/// <summary>
/// ⭐ SD-S2a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-102) — a gamepad axis of <see cref="IInputProvider.GamepadAxis"/>. Values match the
/// Raylib-cs <c>GamepadAxis</c> enum, as <see cref="MapMouseButton"/> matches its mouse buttons (a direct cast, no table).
/// </summary>
public enum MapGamepadAxis
{
    /// <summary>The left stick, −1 left … +1 right.</summary>
    LeftX = 0,
    /// <summary>The left stick, −1 up … +1 down.</summary>
    LeftY = 1,
    /// <summary>The right stick, −1 left … +1 right.</summary>
    RightX = 2,
    /// <summary>The right stick, −1 up … +1 down.</summary>
    RightY = 3,
    /// <summary>The left trigger, −1 released … +1 full.</summary>
    LeftTrigger = 4,
    /// <summary>The right trigger, −1 released … +1 full.</summary>
    RightTrigger = 5,
}

/// <summary>
/// ⭐ SD-S2a (§3.13c D-102) — a gamepad button of <see cref="IInputProvider.GamepadButtonDown"/>. Values match the Raylib-cs <c>GamepadButton</c> enum
/// (the face buttons by position: an Xbox pad's Y / B / A / X are <see cref="RightFaceUp"/> / <see cref="RightFaceRight"/> / <see cref="RightFaceDown"/> /
/// <see cref="RightFaceLeft"/>).
/// </summary>
public enum MapGamepadButton
{
    /// <summary>None.</summary>
    Unknown = 0,
    /// <summary>The d-pad up.</summary>
    LeftFaceUp = 1,
    /// <summary>The d-pad right.</summary>
    LeftFaceRight = 2,
    /// <summary>The d-pad down.</summary>
    LeftFaceDown = 3,
    /// <summary>The d-pad left.</summary>
    LeftFaceLeft = 4,
    /// <summary>Y (Xbox) / triangle.</summary>
    RightFaceUp = 5,
    /// <summary>B (Xbox) / circle.</summary>
    RightFaceRight = 6,
    /// <summary>A (Xbox) / cross.</summary>
    RightFaceDown = 7,
    /// <summary>X (Xbox) / square.</summary>
    RightFaceLeft = 8,
    /// <summary>The left bumper (LB).</summary>
    LeftTrigger1 = 9,
    /// <summary>The left trigger as a button (LT).</summary>
    LeftTrigger2 = 10,
    /// <summary>The right bumper (RB).</summary>
    RightTrigger1 = 11,
    /// <summary>The right trigger as a button (RT).</summary>
    RightTrigger2 = 12,
    /// <summary>Back / View / Select.</summary>
    MiddleLeft = 13,
    /// <summary>The guide button.</summary>
    Middle = 14,
    /// <summary>Start / Menu.</summary>
    MiddleRight = 15,
    /// <summary>The left stick pressed.</summary>
    LeftThumb = 16,
    /// <summary>The right stick pressed.</summary>
    RightThumb = 17,
}
