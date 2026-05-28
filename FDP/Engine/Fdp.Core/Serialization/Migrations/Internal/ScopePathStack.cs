using System.Text;
using System.Text.RegularExpressions;

namespace Fdp.Core.Serialization.Migrations.Internal;

/// <summary>
/// LIFO stack of path segments that tracks the current JSONPath as migrators
/// push and pop scopes via <see cref="MigrationContext"/>.
/// </summary>
internal sealed class ScopePathStack
{
    // Each frame represents one pushed segment string (already in canonical form).
    private readonly Stack<string> _frames = new();

    // Regex matching a plain dotted identifier.
    private static readonly Regex s_identifierRegex =
        new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Builds the current JSONPath from the stack frames.
    /// Returns <c>"$"</c> when no frames are active.
    /// </summary>
    public string CurrentPath
    {
        get
        {
            if (_frames.Count == 0)
                return "$";

            // Stack is LIFO — enumerate in reverse for path order.
            var sb = new StringBuilder("$");
            foreach (string seg in _frames.Reverse())
                sb.Append(seg);

            return sb.ToString();
        }
    }

    /// <summary>Pushes a named key segment. Returns a disposable that pops it.</summary>
    public IDisposable PushItem(string key) => PushRaw(CanonicalSegment(key));

    /// <summary>Pushes a numeric array index segment. Returns a disposable that pops it.</summary>
    public IDisposable PushIndex(int index) => PushRaw($"[{index}]");

    /// <summary>
    /// Pushes a pre-built multi-segment suffix string as a single frame.
    /// Returns a disposable that pops it.
    /// </summary>
    public IDisposable PushSuffix(string suffix) => PushRaw(suffix);

    // ---------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------

    private IDisposable PushRaw(string segment)
    {
        _frames.Push(segment);
        return new PopToken(this);
    }

    private static string CanonicalSegment(string key)
    {
        if (s_identifierRegex.IsMatch(key))
            return "." + key;

        // Bracketed form with escaping.
        var sb = new StringBuilder("['");
        foreach (char c in key)
        {
            if (c == '\'') sb.Append("\\'");
            else if (c == '\\') sb.Append("\\\\");
            else sb.Append(c);
        }
        sb.Append("']");
        return sb.ToString();
    }

    private sealed class PopToken : IDisposable
    {
        private readonly ScopePathStack _owner;
        private bool _disposed;

        public PopToken(ScopePathStack owner) => _owner = owner;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_owner._frames.Count > 0)
                    _owner._frames.Pop();
            }
        }
    }
}
