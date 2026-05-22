# Kernel 03 — Search, Spatial, Constants

Pre-written algorithms and constant tables. All copy verbatim into the
solution.

---

## File: `NodeEditor.Core/Search/FuzzyMatcher.cs`

```csharp
namespace NodeEditor.Core.Search;

/// <summary>
/// Fuzzy string matcher used by the picker and My Blueprint search.
/// Tiered scoring: exact &gt; prefix &gt; word-start &gt; camelCase &gt;
/// substring &gt; keyword-substring &gt; char-order.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>Score and match positions for a candidate.</summary>
    public readonly record struct Result(int Score, IReadOnlyList<int> MatchPositions)
    {
        public bool HasMatch => Score > 0;
        public static Result NoMatch => new(0, Array.Empty<int>());
    }

    /// <summary>
    /// Score a candidate string against a query, returning matched character
    /// positions in <paramref name="candidate"/> when scoring is non-zero.
    /// </summary>
    /// <param name="query">User search text. Empty = match-all with score 1.</param>
    /// <param name="candidate">String being scored.</param>
    /// <param name="keywords">Optional keyword aliases — boosted as secondary.</param>
    public static Result Score(string query, string candidate, IReadOnlyList<string>? keywords = null)
    {
        if (string.IsNullOrEmpty(query)) return new Result(1, Array.Empty<int>());
        if (string.IsNullOrEmpty(candidate)) return Result.NoMatch;

        var q = query.ToLowerInvariant();
        var c = candidate.ToLowerInvariant();

        // Tier 1: exact match.
        if (c == q)
        {
            return new Result(10000, RangePositions(0, candidate.Length));
        }

        // Tier 2: prefix match.
        if (c.StartsWith(q, StringComparison.Ordinal))
        {
            // Bonus for longer prefix matches relative to candidate length.
            int bonus = (int)(500.0 * q.Length / candidate.Length);
            return new Result(5000 + bonus, RangePositions(0, q.Length));
        }

        // Tier 3 & 4: word-start and camelCase boundary matches.
        if (TryMatchAtBoundaries(q, candidate, out var boundaryScore, out var boundaryPositions))
        {
            return new Result(boundaryScore, boundaryPositions);
        }

        // Tier 5: substring in display name.
        int subIdx = c.IndexOf(q, StringComparison.Ordinal);
        if (subIdx >= 0)
        {
            // Penalty for being deeper in the string.
            int penalty = subIdx * 5;
            return new Result(Math.Max(1500 - penalty, 1100),
                              RangePositions(subIdx, q.Length));
        }

        // Tier 6: substring in keywords.
        if (keywords is { Count: > 0 })
        {
            foreach (var kw in keywords)
            {
                var lk = kw.ToLowerInvariant();
                if (lk.Contains(q, StringComparison.Ordinal))
                {
                    return new Result(1000, Array.Empty<int>()); // can't highlight in candidate
                }
            }
        }

        // Tier 7: fuzzy char-order match.
        var fuzzyResult = ScoreFuzzy(q, c, candidate);
        return fuzzyResult;
    }

    /// <summary>
    /// Try to match query against boundary characters (start of words or
    /// camelCase boundaries). Returns true if at least one boundary
    /// match found and the entire query consumed.
    /// </summary>
    private static bool TryMatchAtBoundaries(
        string q, string candidate, out int score, out IReadOnlyList<int> positions)
    {
        score = 0;
        positions = Array.Empty<int>();

        // Collect boundary indices.
        var boundaries = new List<int> { 0 };
        for (int i = 1; i < candidate.Length; i++)
        {
            var prev = candidate[i - 1];
            var cur = candidate[i];

            if (prev is ' ' or '_' or '-' or '/' or '.' or '\\')
            {
                boundaries.Add(i);
            }
            else if (char.IsLower(prev) && char.IsUpper(cur))
            {
                boundaries.Add(i);
            }
            else if (char.IsLetter(prev) && char.IsDigit(cur))
            {
                boundaries.Add(i);
            }
        }

        // Try matching query chars to consecutive boundary chars.
        var lower = candidate.ToLowerInvariant();
        var matched = new List<int>(q.Length);
        int qi = 0;
        foreach (int b in boundaries)
        {
            if (qi >= q.Length) break;
            if (lower[b] == q[qi])
            {
                matched.Add(b);
                qi++;
            }
        }

        if (qi == q.Length && matched.Count == q.Length)
        {
            // Decide between word-start (3000) and camelCase (2500).
            // If first boundary is at index 0, classify as word-start;
            // otherwise camelCase.
            bool startsAtZero = matched[0] == 0;
            score = startsAtZero ? 3000 : 2500;
            positions = matched;
            return true;
        }

        return false;
    }

    /// <summary>Char-order match: each query char appears in candidate, in order.</summary>
    private static Result ScoreFuzzy(string q, string lower, string original)
    {
        var positions = new List<int>(q.Length);
        int ci = 0;
        for (int qi = 0; qi < q.Length; qi++)
        {
            int found = lower.IndexOf(q[qi], ci);
            if (found < 0) return Result.NoMatch;
            positions.Add(found);
            ci = found + 1;
        }

        // Score: 500 minus distance penalty. Tighter clusters score higher.
        int spread = positions[^1] - positions[0];
        int penalty = spread * 2;
        return new Result(Math.Max(500 - penalty, 100), positions);
    }

    private static IReadOnlyList<int> RangePositions(int start, int count)
    {
        var result = new int[count];
        for (int i = 0; i < count; i++) result[i] = start + i;
        return result;
    }
}
```

---

## File: `NodeEditor.Core/Spatial/SpatialIndex.cs`

```csharp
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Spatial;

/// <summary>
/// Simple uniform-grid spatial index for hit-testing and viewport culling.
/// Cells indexed by integer coordinates. Each node is registered with
/// its bounding rect; cells containing any part of the rect store the id.
///
/// Trade-off: cheap rebuild, O(visible) hit-test. Suitable for the editor's
/// modest entity count (target 2000 nodes). Not a quadtree by design —
/// simpler and faster for our scale.
/// </summary>
public sealed class SpatialIndex
{
    private readonly float _cellSize;
    private readonly Dictionary<(int X, int Y), List<NodeId>> _cells = new();
    private readonly Dictionary<NodeId, RectF> _bounds = new();

    /// <summary>Create an index with cells of the given size in canvas units.</summary>
    public SpatialIndex(float cellSize = 256f)
    {
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
        _cellSize = cellSize;
    }

    /// <summary>Number of indexed nodes.</summary>
    public int Count => _bounds.Count;

    /// <summary>Bulk reset: clear and re-populate.</summary>
    public void Rebuild(IEnumerable<(NodeId Id, RectF Bounds)> nodes)
    {
        _cells.Clear();
        _bounds.Clear();
        foreach (var (id, b) in nodes) Insert(id, b);
    }

    /// <summary>Insert or update a node's bounds.</summary>
    public void Insert(NodeId id, RectF bounds)
    {
        // If already present, remove first.
        if (_bounds.ContainsKey(id)) Remove(id);

        _bounds[id] = bounds;
        foreach (var key in CellsCovering(bounds))
        {
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<NodeId>(4);
                _cells[key] = list;
            }
            list.Add(id);
        }
    }

    /// <summary>Remove a node from the index.</summary>
    public bool Remove(NodeId id)
    {
        if (!_bounds.TryGetValue(id, out var bounds)) return false;
        _bounds.Remove(id);
        foreach (var key in CellsCovering(bounds))
        {
            if (_cells.TryGetValue(key, out var list))
            {
                list.Remove(id);
                if (list.Count == 0) _cells.Remove(key);
            }
        }
        return true;
    }

    /// <summary>Get the bounds of an indexed node, or null.</summary>
    public RectF? GetBounds(NodeId id) =>
        _bounds.TryGetValue(id, out var b) ? b : null;

    /// <summary>Enumerate nodes whose bounds intersect the given rect.</summary>
    public IEnumerable<NodeId> Query(RectF area)
    {
        var seen = new HashSet<NodeId>();
        foreach (var key in CellsCovering(area))
        {
            if (!_cells.TryGetValue(key, out var list)) continue;
            foreach (var id in list)
            {
                if (!seen.Add(id)) continue;
                if (_bounds.TryGetValue(id, out var b) && b.Intersects(area))
                    yield return id;
            }
        }
    }

    /// <summary>Enumerate nodes whose bounds fully contain the given point.</summary>
    public IEnumerable<NodeId> QueryPoint(Vector2 p)
    {
        var key = ToCell(p);
        if (!_cells.TryGetValue(key, out var list)) yield break;
        foreach (var id in list)
        {
            if (_bounds.TryGetValue(id, out var b) && b.Contains(p))
                yield return id;
        }
    }

    /// <summary>Enumerate nodes fully enclosed by the given rect (used by box-select default).</summary>
    public IEnumerable<NodeId> QueryFullyEnclosed(RectF area)
    {
        foreach (var id in Query(area))
        {
            if (_bounds.TryGetValue(id, out var b) && area.FullyContains(b))
                yield return id;
        }
    }

    private IEnumerable<(int X, int Y)> CellsCovering(RectF r)
    {
        var (x0, y0) = ToCell(r.Min);
        var (x1, y1) = ToCell(r.Max);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            yield return (x, y);
    }

    private (int X, int Y) ToCell(Vector2 p) => (
        (int)Math.Floor(p.X / _cellSize),
        (int)Math.Floor(p.Y / _cellSize));
}
```

---

## File: `NodeEditor.Core/TimingConstants.cs`

```csharp
namespace NodeEditor.Core;

/// <summary>
/// Centralized timing constants. All animation durations and thresholds
/// live here so they're tweakable without spelunking through render code.
/// </summary>
public static class TimingConstants
{
    /// <summary>Mouse-down to drag threshold in pixels.</summary>
    public const float DragThresholdPixels = 4f;

    /// <summary>Snap-to-pin radius in canvas pixels during wire drag.</summary>
    public const float SnapToPinRadius = 14f;

    /// <summary>Tooltip appears after this delay.</summary>
    public static readonly TimeSpan TooltipDelay = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan TooltipFade = TimeSpan.FromMilliseconds(80);

    /// <summary>Camera animation when frame-to-target.</summary>
    public static readonly TimeSpan FrameAnimDuration = TimeSpan.FromMilliseconds(180);

    /// <summary>Wire connect animation.</summary>
    public static readonly TimeSpan WireConnectSnap = TimeSpan.FromMilliseconds(120);

    /// <summary>Wire disconnect recoil animation.</summary>
    public static readonly TimeSpan WireDisconnectRecoil = TimeSpan.FromMilliseconds(120);

    /// <summary>Reroute insertion scale-in.</summary>
    public static readonly TimeSpan RerouteScaleIn = TimeSpan.FromMilliseconds(100);

    /// <summary>Node creation fade-in.</summary>
    public static readonly TimeSpan NodeCreateFadeIn = TimeSpan.FromMilliseconds(100);

    /// <summary>Node deletion fade-out.</summary>
    public static readonly TimeSpan NodeDeleteFadeOut = TimeSpan.FromMilliseconds(80);

    /// <summary>Wire flow animation loop period (debug viz).</summary>
    public static readonly TimeSpan WireFlowLoop = TimeSpan.FromMilliseconds(400);

    /// <summary>Executing-node pulse period (debug viz).</summary>
    public static readonly TimeSpan ExecutingPulse = TimeSpan.FromMilliseconds(500);

    /// <summary>After-glow on recently-executed wire/node.</summary>
    public static readonly TimeSpan RecentlyExecutedFade = TimeSpan.FromMilliseconds(800);

    /// <summary>Pan inertia after release.</summary>
    public static readonly TimeSpan PanInertia = TimeSpan.FromMilliseconds(250);

    /// <summary>Popup open/close animation.</summary>
    public static readonly TimeSpan PopupOpen = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan PopupClose = TimeSpan.FromMilliseconds(80);

    /// <summary>Toast notification visible duration.</summary>
    public static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(3);

    /// <summary>Hot reload badge fade window.</summary>
    public static readonly TimeSpan ReloadBadgeFade = TimeSpan.FromSeconds(2);

    /// <summary>Minimum/maximum camera zoom factor.</summary>
    public const float MinZoom = 0.25f;
    public const float MaxZoom = 3.0f;

    /// <summary>Below this zoom, render simplified.</summary>
    public const float LowZoomThreshold = 0.5f;
}
```

---

## File: `NodeEditor.Core/DefaultTypeColors.cs`

```csharp
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core;

/// <summary>
/// Default type-key → color mapping used as fallback when the host's
/// type system doesn't specify one. Colors are RGBA in 0–1 range.
/// </summary>
public static class DefaultTypeColors
{
    private static readonly Dictionary<string, Vector4> _map = new(StringComparer.Ordinal)
    {
        // Booleans
        ["System.Boolean"] = ToRgba(0xE7, 0x4C, 0x3C, 0xFF),

        // Integers
        ["System.Byte"]    = ToRgba(0x5D, 0xAD, 0xE2, 0xFF),
        ["System.Int16"]   = ToRgba(0x5D, 0xAD, 0xE2, 0xFF),
        ["System.Int32"]   = ToRgba(0x5D, 0xAD, 0xE2, 0xFF),
        ["System.Int64"]   = ToRgba(0x5D, 0xAD, 0xE2, 0xFF),

        // Floats
        ["System.Single"]  = ToRgba(0xA6, 0xE2, 0x2E, 0xFF),
        ["System.Double"]  = ToRgba(0xA6, 0xE2, 0x2E, 0xFF),

        // Strings
        ["System.String"]  = ToRgba(0xE8, 0x4F, 0x8E, 0xFF),

        // Vectors / math
        ["System.Numerics.Vector2"]     = ToRgba(0xF1, 0xC4, 0x0F, 0xFF),
        ["System.Numerics.Vector3"]     = ToRgba(0xF1, 0xC4, 0x0F, 0xFF),
        ["System.Numerics.Vector4"]     = ToRgba(0xF1, 0xC4, 0x0F, 0xFF),
        ["System.Numerics.Quaternion"]  = ToRgba(0xF1, 0xC4, 0x0F, 0xFF),

        // Color
        ["NodeEditor.Color"]            = ToRgba(0xFF, 0x6B, 0x9D, 0xFF),

        // Guid
        ["System.Guid"]                 = ToRgba(0x2C, 0x3E, 0x50, 0xFF),
    };

    /// <summary>
    /// Get the default color for a type key. Returns mid-blue as fallback
    /// for unrecognized types (treated as generic struct).
    /// </summary>
    public static Vector4 GetColor(TypeKey key)
    {
        return _map.TryGetValue(key.Id, out var c)
            ? c
            : ToRgba(0x54, 0x99, 0xC7, 0xFF); // generic struct fallback
    }

    /// <summary>Exec pin/wire color (white).</summary>
    public static Vector4 ExecColor => new(1, 1, 1, 1);

    private static Vector4 ToRgba(byte r, byte g, byte b, byte a) =>
        new(r / 255f, g / 255f, b / 255f, a / 255f);
}
```

---

## File: `NodeEditor.Core/DefaultTheme.cs`

```csharp
using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Core;

/// <summary>
/// Default implementation of <see cref="IEditorTheme"/>. A host can use
/// this directly or implement its own.
/// </summary>
public sealed class DefaultTheme : IEditorTheme
{
    public Vector4 BackgroundColor          { get; init; } = Rgb(0x1E, 0x1E, 0x1E);
    public Vector4 GridMinorColor           { get; init; } = Rgb(0x2A, 0x2A, 0x2A);
    public Vector4 GridMajorColor           { get; init; } = Rgb(0x3A, 0x3A, 0x3A);
    public Vector4 SelectionAccent          { get; init; } = Rgb(0xFF, 0xD7, 0x00);
    public Vector4 PrimarySelectionAccent   { get; init; } = Rgb(0xFF, 0xE6, 0x4D);
    public Vector4 ErrorColor               { get; init; } = Rgb(0xFF, 0x44, 0x44);
    public Vector4 WarningColor             { get; init; } = Rgb(0xFF, 0xAA, 0x00);
    public Vector4 TextDefault              { get; init; } = Rgb(0xE0, 0xE0, 0xE0);
    public Vector4 TextMuted                { get; init; } = Rgb(0x80, 0x80, 0x80);

    public float NodeCornerRadius      { get; init; } = 4f;
    public float NodeBorderThickness   { get; init; } = 2f;
    public float NodeHeaderHeight      { get; init; } = 24f;
    public float PinGlyphSize          { get; init; } = 10f;
    public float WireThicknessExec     { get; init; } = 3f;
    public float WireThicknessData     { get; init; } = 2f;

    public Vector4 GetCategoryHeaderColor(NodeCategory category) => category switch
    {
        NodeCategory.Function     => Rgb(0x2E, 0x5C, 0x8A),
        NodeCategory.Event        => Rgb(0xA9, 0x32, 0x26),
        NodeCategory.Pure         => Rgb(0x27, 0xAE, 0x60),
        NodeCategory.VariableGet  => Rgb(0x56, 0x65, 0x73),
        NodeCategory.VariableSet  => Rgb(0x56, 0x65, 0x73),
        NodeCategory.FlowControl  => Rgb(0xD3, 0x54, 0x00),
        NodeCategory.Macro        => Rgb(0x8E, 0x44, 0xAD),
        NodeCategory.Comment      => Rgb(0x7F, 0x8C, 0x8D),
        _                         => Rgb(0x7F, 0x8C, 0x8D),
    };

    private static Vector4 Rgb(byte r, byte g, byte b) =>
        new(r / 255f, g / 255f, b / 255f, 1f);
}
```

---

## File: `NodeEditor.Core/CommandCatalog.cs`

```csharp
namespace NodeEditor.Core;

/// <summary>
/// Canonical command-ID strings. Keep in sync with spec D.0 and the
/// editor's command publication code.
/// </summary>
public static class CommandCatalog
{
    // File / Asset
    public const string Save             = "editor.save";
    public const string SaveAll          = "editor.save-all";
    public const string Reload           = "editor.reload";
    public const string Compile          = "editor.compile";
    public const string QuickReload      = "editor.quick-reload";

    // Edit
    public const string Undo             = "editor.undo";
    public const string Redo             = "editor.redo";
    public const string Cut              = "editor.cut";
    public const string Copy             = "editor.copy";
    public const string Paste            = "editor.paste";
    public const string Duplicate        = "editor.duplicate";
    public const string SelectAll        = "editor.select-all";
    public const string SelectNone       = "editor.select-none";
    public const string InvertSelection  = "editor.invert-selection";
    public const string DeleteSelection  = "editor.delete-selection";

    // View
    public const string FrameAll         = "editor.frame-all";
    public const string FrameSelection   = "editor.frame-selection";
    public const string ZoomIn           = "editor.zoom-in";
    public const string ZoomOut          = "editor.zoom-out";
    public const string ZoomReset        = "editor.zoom-reset";
    public const string ToggleGrid       = "editor.toggle-grid";
    public const string ToggleMinimap    = "editor.toggle-minimap";

    // Navigation
    public const string NextTab          = "editor.next-tab";
    public const string PrevTab          = "editor.prev-tab";
    public const string CloseTab         = "editor.close-tab";
    public const string GoToGraph        = "editor.go-to-graph";
    public const string NextError        = "editor.next-error";
    public const string PrevError        = "editor.prev-error";
    public const string NextBookmark     = "editor.next-bookmark";
    public const string PrevBookmark     = "editor.prev-bookmark";

    // Add/Create
    public const string AddNode             = "editor.add-node";
    public const string AddComment          = "editor.add-comment";
    public const string AddReroute          = "editor.add-reroute";
    public const string CreateFunction      = "editor.create-function";
    public const string CreateCustomEvent   = "editor.create-custom-event";
    public const string CreateVariable      = "editor.create-variable";
    public const string CreateMacro         = "editor.create-macro";

    // Refactor
    public const string CollapseToFunction  = "editor.collapse-to-function";
    public const string CollapseToMacro     = "editor.collapse-to-macro";
    public const string CollapseToComment   = "editor.collapse-to-comment";
    public const string ExpandNode          = "editor.expand-node";
    public const string PromoteToVariable   = "editor.promote-to-variable";
    public const string Rename              = "editor.rename";

    // Find
    public const string FindInGraph         = "editor.find-in-graph";
    public const string FindInAsset         = "editor.find-in-asset";
    public const string FindInProject       = "editor.find-in-project";
    public const string GoToDefinition      = "editor.go-to-definition";
    public const string FindReferences      = "editor.find-references";
    public const string FindNext            = "editor.find-next";
    public const string FindPrev            = "editor.find-prev";

    // Debug
    public const string ToggleBreakpoint    = "editor.toggle-breakpoint";
    public const string ToggleWatch         = "editor.toggle-watch";
    public const string DebugContinue       = "editor.continue";
    public const string DebugStepOver       = "editor.step-over";
    public const string DebugStepInto       = "editor.step-into";
    public const string DebugStepOut        = "editor.step-out";
    public const string ClearAllBreakpoints = "editor.clear-all-breakpoints";

    // Alignment
    public const string AlignLeft          = "editor.align-left";
    public const string AlignRight         = "editor.align-right";
    public const string AlignTop           = "editor.align-top";
    public const string AlignBottom        = "editor.align-bottom";
    public const string AlignCenterH       = "editor.align-center-h";
    public const string AlignCenterV       = "editor.align-center-v";
    public const string DistributeH        = "editor.distribute-h";
    public const string DistributeV        = "editor.distribute-v";
    public const string StraightenConn     = "editor.straighten-connection";
}
```

---

## File: `NodeEditor.Core/Expression/ExpressionEvaluator.cs`

```csharp
using System.Globalization;

namespace NodeEditor.Core.Expression;

/// <summary>
/// Tiny safe expression evaluator used by inline drag-float/int text-edit
/// mode. Whitelist: + - * / % ^, constants pi/tau/e, functions sin cos tan
/// asin acos atan sqrt abs floor ceil round min max clamp deg rad,
/// suffix `deg` and `rad`. Recursive-descent parser.
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>Result of an evaluation attempt.</summary>
    public readonly record struct Result(bool Success, double Value, string? Error)
    {
        public static Result Ok(double v) => new(true, v, null);
        public static Result Fail(string e) => new(false, double.NaN, e);
    }

    /// <summary>Evaluate an expression. Returns failure with message on parse error.</summary>
    public static Result Evaluate(string expr)
    {
        if (string.IsNullOrWhiteSpace(expr))
            return Result.Fail("Empty expression.");

        var parser = new Parser(expr);
        try
        {
            var v = parser.ParseFullExpression();
            return Result.Ok(v);
        }
        catch (FormatException ex)
        {
            return Result.Fail(ex.Message);
        }
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) { _s = s; _i = 0; }

        public double ParseFullExpression()
        {
            var v = ParseExpr();
            SkipWs();
            if (_i < _s.Length)
                throw new FormatException($"Unexpected character at position {_i}: '{_s[_i]}'");
            return v;
        }

        // expr := term (('+'|'-') term)*
        private double ParseExpr()
        {
            var v = ParseTerm();
            while (true)
            {
                SkipWs();
                if (Peek('+')) { _i++; v += ParseTerm(); }
                else if (Peek('-')) { _i++; v -= ParseTerm(); }
                else break;
            }
            return v;
        }

        // term := power (('*'|'/'|'%') power)*
        private double ParseTerm()
        {
            var v = ParsePower();
            while (true)
            {
                SkipWs();
                if (Peek('*')) { _i++; v *= ParsePower(); }
                else if (Peek('/')) { _i++; v /= ParsePower(); }
                else if (Peek('%')) { _i++; v %= ParsePower(); }
                else break;
            }
            return v;
        }

        // power := unary ('^' power)?     (right-associative)
        private double ParsePower()
        {
            var v = ParseUnary();
            SkipWs();
            if (Peek('^')) { _i++; v = Math.Pow(v, ParsePower()); }
            return v;
        }

        // unary := '-' unary | primary
        private double ParseUnary()
        {
            SkipWs();
            if (Peek('-')) { _i++; return -ParseUnary(); }
            if (Peek('+')) { _i++; return ParseUnary(); }
            return ParsePrimary();
        }

        // primary := number | identifier | '(' expr ')'  with optional suffix 'deg' or 'rad'
        private double ParsePrimary()
        {
            SkipWs();
            double v;

            if (Peek('('))
            {
                _i++;
                v = ParseExpr();
                SkipWs();
                if (!Peek(')')) throw new FormatException("Missing ')'");
                _i++;
            }
            else if (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
            {
                v = ParseNumber();
            }
            else if (_i < _s.Length && char.IsLetter(_s[_i]))
            {
                v = ParseIdentifier();
            }
            else
            {
                throw new FormatException($"Unexpected character at position {_i}");
            }

            // Suffix
            SkipWs();
            if (MatchKeyword("deg")) v = v * Math.PI / 180.0;
            else if (MatchKeyword("rad")) { /* no-op */ }
            return v;
        }

        private double ParseNumber()
        {
            int start = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
                _i++;
            // Scientific notation: e[+-]?digits
            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                _i++;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
            }
            var slice = _s.AsSpan(start, _i - start);
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new FormatException($"Invalid number '{slice.ToString()}'");
            return v;
        }

        private double ParseIdentifier()
        {
            int start = _i;
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_'))
                _i++;
            var name = _s.Substring(start, _i - start).ToLowerInvariant();

            // Constants
            if (name == "pi")  return Math.PI;
            if (name == "tau") return Math.PI * 2;
            if (name == "e")   return Math.E;

            // Suffix keywords: caller's ParsePrimary handles deg/rad. If we
            // see them here as primary, treat as 0 (shouldn't happen if
            // parser balanced).
            // Functions: name '(' args ')'
            SkipWs();
            if (Peek('('))
            {
                _i++;
                var args = new List<double>();
                SkipWs();
                if (!Peek(')'))
                {
                    args.Add(ParseExpr());
                    SkipWs();
                    while (Peek(','))
                    {
                        _i++;
                        args.Add(ParseExpr());
                        SkipWs();
                    }
                }

                if (!Peek(')')) throw new FormatException("Missing ')'");
                _i++;
                return CallFunction(name, args);
            }

            throw new FormatException($"Unknown identifier '{name}'");
        }

        private static double CallFunction(string name, List<double> args)
        {
            return (name, args.Count) switch
            {
                ("sin", 1)   => Math.Sin(args[0]),
                ("cos", 1)   => Math.Cos(args[0]),
                ("tan", 1)   => Math.Tan(args[0]),
                ("asin", 1)  => Math.Asin(args[0]),
                ("acos", 1)  => Math.Acos(args[0]),
                ("atan", 1)  => Math.Atan(args[0]),
                ("sqrt", 1)  => Math.Sqrt(args[0]),
                ("abs", 1)   => Math.Abs(args[0]),
                ("floor", 1) => Math.Floor(args[0]),
                ("ceil", 1)  => Math.Ceiling(args[0]),
                ("round", 1) => Math.Round(args[0]),
                ("min", 2)   => Math.Min(args[0], args[1]),
                ("max", 2)   => Math.Max(args[0], args[1]),
                ("clamp", 3) => Math.Clamp(args[0], args[1], args[2]),
                ("deg", 1)   => args[0] * 180.0 / Math.PI,
                ("rad", 1)   => args[0] * Math.PI / 180.0,
                _ => throw new FormatException($"Unknown function '{name}'/{args.Count}"),
            };
        }

        private bool MatchKeyword(string kw)
        {
            SkipWs();
            if (_i + kw.Length > _s.Length) return false;
            for (int k = 0; k < kw.Length; k++)
                if (char.ToLowerInvariant(_s[_i + k]) != kw[k]) return false;
            // Must not be followed by identifier char.
            int after = _i + kw.Length;
            if (after < _s.Length && (char.IsLetterOrDigit(_s[after]) || _s[after] == '_'))
                return false;
            _i = after;
            return true;
        }

        private bool Peek(char c) => _i < _s.Length && _s[_i] == c;

        private void SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }
    }
}
```
