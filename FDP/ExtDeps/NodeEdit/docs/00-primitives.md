# Kernel 00 — Primitives

These types go into `NodeEditor.Primitives`. Zero dependencies. Copy each
inline code block into its own `.cs` file with the indicated filename.

---

## File: `NodeEditor.Primitives/NodeId.cs`

```csharp
namespace NodeEditor.Primitives;

/// <summary>
/// Unique identifier for a node in a graph. Wraps a <see cref="Guid"/>
/// to provide type safety; never expose raw Guids in the public API.
/// </summary>
public readonly record struct NodeId(Guid Value)
{
    /// <summary>The empty (default-constructed) NodeId.</summary>
    public static NodeId Empty => default;

    /// <summary>Generate a new, random NodeId.</summary>
    public static NodeId NewId() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => $"Node({Value:N}[..8])"[..16];
}
```

---

## File: `NodeEditor.Primitives/PinId.cs`

```csharp
namespace NodeEditor.Primitives;

/// <summary>Unique identifier for a pin on a node.</summary>
public readonly record struct PinId(Guid Value)
{
    public static PinId Empty => default;
    public static PinId NewId() => new(Guid.NewGuid());
    public override string ToString() => $"Pin({Value:N}[..8])"[..16];
}
```

---

## File: `NodeEditor.Primitives/LinkId.cs`

```csharp
namespace NodeEditor.Primitives;

/// <summary>Unique identifier for a link (wire) between two pins.</summary>
public readonly record struct LinkId(Guid Value)
{
    public static LinkId Empty => default;
    public static LinkId NewId() => new(Guid.NewGuid());
    public override string ToString() => $"Link({Value:N}[..8])"[..16];
}
```

---

## File: `NodeEditor.Primitives/GraphId.cs`

```csharp
namespace NodeEditor.Primitives;

/// <summary>Unique identifier for a single graph (event graph, function body, macro body, …).</summary>
public readonly record struct GraphId(Guid Value)
{
    public static GraphId Empty => default;
    public static GraphId NewId() => new(Guid.NewGuid());
    public override string ToString() => $"Graph({Value:N}[..8])"[..16];
}
```

---

## File: `NodeEditor.Primitives/CommentId.cs`

```csharp
namespace NodeEditor.Primitives;

/// <summary>Unique identifier for a comment box.</summary>
public readonly record struct CommentId(Guid Value)
{
    public static CommentId Empty => default;
    public static CommentId NewId() => new(Guid.NewGuid());
    public override string ToString() => $"Comment({Value:N}[..8])"[..16];
}
```

---

## File: `NodeEditor.Primitives/RerouteId.cs`

```csharp
namespace NodeEditor.Primitives;

/// <summary>
/// Virtual identifier referring to a single reroute waypoint inside a link's
/// waypoint list. Reroutes are not standalone entities; they're nested
/// inside <c>ILinkModel.Waypoints</c>. This struct is used by selection
/// and command APIs.
/// </summary>
public readonly record struct RerouteRef(LinkId LinkId, int WaypointIndex)
{
    public override string ToString() => $"Reroute({LinkId}, #{WaypointIndex})";
}
```

---

## File: `NodeEditor.Primitives/TypeKey.cs`

```csharp
namespace NodeEditor.Primitives;

/// <summary>
/// String-keyed type identifier. The editor does not know specific types;
/// the host owns the type namespace and provides type info via
/// <c>ITypeSystem</c>. Standard convention: full CLR-style name, e.g.
/// "System.Single", "MyHost.Combat.DamageInfo".
/// </summary>
public readonly record struct TypeKey(string Id)
{
    public static TypeKey Empty => new(string.Empty);
    public bool IsEmpty => string.IsNullOrEmpty(Id);
    public override string ToString() => Id;
}
```

---

## File: `NodeEditor.Primitives/NodeKindKey.cs`

```csharp
namespace NodeEditor.Primitives;

/// <summary>
/// String-keyed node-kind identifier. The host owns the catalog of node
/// kinds; the editor only references kinds by key. Standard convention:
/// "DomainArea.NodeName", e.g. "Math.Multiply", "Control.Branch".
/// </summary>
public readonly record struct NodeKindKey(string Id)
{
    public static NodeKindKey Empty => new(string.Empty);
    public bool IsEmpty => string.IsNullOrEmpty(Id);
    public override string ToString() => Id;
}
```

---

## File: `NodeEditor.Primitives/Enums.cs`

```csharp
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
```

---

## File: `NodeEditor.Primitives/EditorKey.cs`

```csharp
namespace NodeEditor.Primitives;

/// <summary>
/// Abstract key codes used by <c>IInputSource</c>. Mapped from the host
/// input backend (raylib, SDL, win32, …) by an adapter.
/// </summary>
public enum EditorKey
{
    Unknown,

    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    Tab, Space, Enter, Escape, Backspace, Delete,
    Home, End, PageUp, PageDown,
    Left, Right, Up, Down, Insert, CapsLock,

    LeftBracket, RightBracket, Comma, Period, Slash,
    Minus, Equals, Apostrophe,
}
```

---

## File: `NodeEditor.Primitives/IdGenerator.cs`

```csharp
using System.Security.Cryptography;
using System.Text;

namespace NodeEditor.Primitives;

/// <summary>
/// Centralized ID generation. Provides random and deterministic-from-string
/// variants. Use deterministic for content-based identity (e.g. "the
/// breakpoint at this node") and random for new mutable entities.
/// </summary>
public static class IdGenerator
{
    /// <summary>Generates a new random NodeId.</summary>
    public static NodeId NewNodeId() => NodeId.NewId();

    /// <summary>Generates a new random PinId.</summary>
    public static PinId NewPinId() => PinId.NewId();

    /// <summary>Generates a new random LinkId.</summary>
    public static LinkId NewLinkId() => LinkId.NewId();

    /// <summary>Generates a new random GraphId.</summary>
    public static GraphId NewGraphId() => GraphId.NewId();

    /// <summary>Generates a new random CommentId.</summary>
    public static CommentId NewCommentId() => CommentId.NewId();

    /// <summary>
    /// Generates a deterministic Guid from an input string by SHA-256
    /// hashing. Same string ⇒ same Guid. Useful when an ID must be
    /// reconstructable from stable content.
    /// </summary>
    public static Guid Deterministic(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(input), hash);
        // Take first 16 bytes; set version + variant per RFC 4122 v5 conventions.
        Span<byte> guidBytes = stackalloc byte[16];
        hash[..16].CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // version 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // variant
        return new Guid(guidBytes);
    }

    /// <summary>Deterministic NodeId from input string.</summary>
    public static NodeId DeterministicNodeId(string input) => new(Deterministic(input));

    /// <summary>Deterministic PinId from input string.</summary>
    public static PinId DeterministicPinId(string input) => new(Deterministic(input));

    /// <summary>Deterministic LinkId from input string.</summary>
    public static LinkId DeterministicLinkId(string input) => new(Deterministic(input));
}
```

---

## File: `NodeEditor.Primitives/RectF.cs`

```csharp
using System.Numerics;

namespace NodeEditor.Primitives;

/// <summary>
/// Axis-aligned rectangle in canvas (or screen) coordinates. Immutable.
/// <c>Min</c> is the top-left corner, <c>Size</c> is non-negative.
/// </summary>
public readonly record struct RectF(Vector2 Min, Vector2 Size)
{
    public Vector2 Max => Min + Size;
    public Vector2 Center => Min + Size * 0.5f;
    public float Width => Size.X;
    public float Height => Size.Y;

    public bool Contains(Vector2 p) =>
        p.X >= Min.X && p.X <= Max.X &&
        p.Y >= Min.Y && p.Y <= Max.Y;

    public bool Intersects(RectF other) =>
        !(Max.X < other.Min.X || other.Max.X < Min.X ||
          Max.Y < other.Min.Y || other.Max.Y < Min.Y);

    public bool FullyContains(RectF other) =>
        other.Min.X >= Min.X && other.Min.Y >= Min.Y &&
        other.Max.X <= Max.X && other.Max.Y <= Max.Y;

    public RectF Expand(float amount) => new(
        Min - new Vector2(amount, amount),
        Size + new Vector2(amount * 2, amount * 2));

    public static RectF FromMinMax(Vector2 min, Vector2 max) =>
        new(min, max - min);

    public static RectF FromCenterSize(Vector2 center, Vector2 size) =>
        new(center - size * 0.5f, size);

    public static RectF Empty => default;
}
```

---

That's the full Primitives layer. Total: ~280 lines across 9 files.
