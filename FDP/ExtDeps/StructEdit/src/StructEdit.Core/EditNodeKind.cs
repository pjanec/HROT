namespace StructEdit.Core;

/// <summary>
/// Enumeration of all node types the library can represent.
/// </summary>
public enum EditNodeKind
{
    /// <summary>Synthetic root when scope selects multiple unrelated paths.</summary>
    SelectionRoot,
    /// <summary>Numeric primitives (int, float, double, byte, …).</summary>
    Scalar,
    Boolean,
    String,
    /// <summary>Any CLR enum; carries available names+values.</summary>
    Enum,
    Guid,
    DateTime,
    /// <summary>Value-type composite.</summary>
    Struct,
    /// <summary>Reference-type composite.</summary>
    Class,
    /// <summary>Immutable record (reconstructed on write).</summary>
    Record,
    /// <summary>C# 12 [InlineArray] attributed struct.</summary>
    InlineArray,
    /// <summary>C# fixed keyword buffer.</summary>
    FixedBuffer,
    /// <summary>List&lt;T&gt;, T[], resizable collections.</summary>
    DynamicArray,
    /// <summary>Overlay projection of a raw byte buffer (union).</summary>
    BufferView,
    /// <summary>Discriminator-driven choice between sub-editors.</summary>
    Union,
    /// <summary>Consumer-installed custom field editor.</summary>
    Custom,
    /// <summary>Type the library cannot reflect (shown read-only or hidden).</summary>
    Unsupported,
}
