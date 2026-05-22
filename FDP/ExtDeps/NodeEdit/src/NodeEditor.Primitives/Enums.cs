namespace NodeEditor.Primitives;

/// <summary>Direction of a pin relative to its owner node.</summary>
public enum PinDirection
{
    /// <summary>Input pin (left side of node).</summary>
    Input,

    /// <summary>Output pin (right side of node).</summary>
    Output,
}

/// <summary>Kind of pin: control-flow execution vs. typed data.</summary>
public enum PinKind
{
    /// <summary>Execution pin; no type; triangle glyph; white wire.</summary>
    Exec,

    /// <summary>Data pin; typed; circle glyph; colored wire.</summary>
    Data,
}

/// <summary>Visual shape used to render a pin glyph.</summary>
public enum PinShape
{
    Circle,
    Diamond,
    Square,
    Pentagon,
    Triangle,
}

/// <summary>Coarse node category used to pick header color and icon.</summary>
public enum NodeCategory
{
    Function,
    Event,
    Pure,
    VariableGet,
    VariableSet,
    FlowControl,
    Macro,
    Comment,
    Custom,
}

/// <summary>Bit flags describing node runtime/edit state.</summary>
[Flags]
public enum NodeState
{
    Normal             = 0,
    Disabled           = 1 << 0,
    Error              = 1 << 1,
    Warning            = 1 << 2,
    Executing          = 1 << 3,
    RecentlyExecuted   = 1 << 4,
}

/// <summary>Container-kind for typed pins (for shape selection).</summary>
public enum ContainerKind
{
    Single,
    Array,
    Map,
    Set,
}

/// <summary>Mouse buttons abstracted across input backends.</summary>
public enum MouseButton
{
    Left,
    Right,
    Middle,
    X1,
    X2,
}

/// <summary>Modifier keys held during an input event.</summary>
[Flags]
public enum KeyModifiers
{
    None   = 0,
    Ctrl   = 1 << 0,
    Shift  = 1 << 1,
    Alt    = 1 << 2,
    Super  = 1 << 3,
}
