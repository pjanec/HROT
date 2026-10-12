using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ <c>CE-2137</c> (C1, Hrot docs/DESIGN_Orders_Params_Entity_Classes.md §2, R-279 (behaviors)) — a pattern over
    /// <see cref="DISEntityType"/>: per segment (<c>Kind.Domain.Country.Category.Subcategory.Specific.Extra</c>) either any
    /// value or a set of ranges.
    /// <para>Text form, one segment per dot: <c>*</c> (any) · <c>5</c> · <c>20-25</c> · <c>1,3,7-9</c> (a list of numbers and
    /// ranges). Missing trailing segments are <c>*</c>, so <c>1.1.*.1</c> = every land tank of any country.</para>
    /// <para><see cref="Specificity"/> orders masks segment by segment from Kind down: an exact number beats a list or range,
    /// which beats <c>*</c>. Two masks that both match one type and have equal specificity are a tie the caller must refuse.</para>
    /// <para>⛔ Not a query filter: <see cref="QueryBuilder.WithDisType"/> (value + byte mask) stays the fast exact-or-any filter.</para>
    /// </summary>
    public sealed class DisTypeMask : IEquatable<DisTypeMask>
    {
        /// <summary>Segment count: Kind, Domain, Country, Category, Subcategory, Specific, Extra.</summary>
        public const int SegmentCount = 7;

        /// <summary>How tightly one segment is pinned.</summary>
        public enum SegmentKind : byte { Any = 0, Set = 1, Exact = 2 }

        private static readonly string[] s_segmentNames = { "Kind", "Domain", "Country", "Category", "Subcategory", "Specific", "Extra" };

        // null = any; otherwise sorted, merged, non-empty ranges.
        private readonly (int Lo, int Hi)[]?[] _segments;
        private readonly string _text;

        private DisTypeMask((int Lo, int Hi)[]?[] segments)
        {
            _segments = segments;
            _text = Format(segments);
            int spec = 0;
            for (int i = 0; i < SegmentCount; i++) spec = (spec << 2) | (int)KindOf(i);
            Specificity = spec;
        }

        /// <summary>Matches every type.</summary>
        public static DisTypeMask Any { get; } = new(new (int, int)[]?[SegmentCount]);

        /// <summary>The comparable specificity: two bits per segment (<see cref="SegmentKind"/>), Kind most significant.
        /// Higher = more specific.</summary>
        public int Specificity { get; }

        /// <summary>How tightly segment <paramref name="index"/> (0 = Kind … 6 = Extra) is pinned.</summary>
        public SegmentKind KindOf(int index)
        {
            var r = _segments[index];
            if (r is null) return SegmentKind.Any;
            return r.Length == 1 && r[0].Lo == r[0].Hi ? SegmentKind.Exact : SegmentKind.Set;
        }

        /// <summary>The exact value of segment <paramref name="index"/>, or null when it is <c>*</c> or a set.</summary>
        public int? ExactValue(int index) => KindOf(index) == SegmentKind.Exact ? _segments[index]![0].Lo : null;

        /// <summary>The deepest segment that is not <c>*</c> (0 = Kind … 6 = Extra), or -1 for <see cref="Any"/>.</summary>
        public int DeepestPinned
        {
            get
            {
                for (int i = SegmentCount - 1; i >= 0; i--) if (_segments[i] is not null) return i;
                return -1;
            }
        }

        /// <summary>True when <paramref name="type"/> lies in every segment.</summary>
        public bool Matches(DISEntityType type)
        {
            for (int i = 0; i < SegmentCount; i++)
                if (!In(_segments[i], Segment(type, i))) return false;
            return true;
        }

        /// <summary>True when some type matches both masks.</summary>
        public bool Overlaps(DisTypeMask other)
        {
            for (int i = 0; i < SegmentCount; i++)
            {
                var a = _segments[i];
                var b = other._segments[i];
                if (a is null || b is null) continue;
                bool any = false;
                foreach (var x in a)
                {
                    foreach (var y in b)
                        if (x.Lo <= y.Hi && y.Lo <= x.Hi) { any = true; break; }
                    if (any) break;
                }
                if (!any) return false;
            }
            return true;
        }

        /// <summary>Parses the text form; throws <see cref="FormatException"/> naming the bad segment.</summary>
        public static DisTypeMask Parse(string text)
            => TryParse(text, out var mask, out var error) ? mask : throw new FormatException($"DIS mask '{text}': {error}");

        /// <summary>Parses the text form. False on an empty string, more than seven segments, a non-number, a value out of
        /// range for its segment, an empty list item or a backwards range.</summary>
        public static bool TryParse(string? text, out DisTypeMask mask) => TryParse(text, out mask, out _);

        private static bool TryParse(string? text, out DisTypeMask mask, out string error)
        {
            mask = Any;
            error = "";
            if (string.IsNullOrWhiteSpace(text)) { error = "empty"; return false; }
            var parts = text.Trim().Split('.');
            if (parts.Length > SegmentCount) { error = $"more than {SegmentCount} segments"; return false; }
            var segments = new (int, int)[]?[SegmentCount];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!TryParseSegment(parts[i].Trim(), i == 2 ? ushort.MaxValue : byte.MaxValue, out segments[i]))
                {
                    error = $"segment {s_segmentNames[i]} '{parts[i]}' is not *, a number, a range a-b or a list";
                    return false;
                }
            }
            mask = new DisTypeMask(segments);
            return true;
        }

        private static bool TryParseSegment(string s, int max, out (int, int)[]? ranges)
        {
            ranges = null;
            if (s == "*") return true;
            if (s.Length == 0) return false;
            var list = new List<(int Lo, int Hi)>();
            foreach (var item in s.Split(','))
            {
                var bounds = item.Trim().Split('-');
                if (bounds.Length > 2) return false;
                if (!TryNum(bounds[0], max, out int lo)) return false;
                int hi = lo;
                if (bounds.Length == 2 && !TryNum(bounds[1], max, out hi)) return false;
                if (hi < lo) return false;
                list.Add((lo, hi));
            }
            list.Sort((a, b) => a.Lo.CompareTo(b.Lo));
            var merged = new List<(int Lo, int Hi)> { list[0] };
            for (int i = 1; i < list.Count; i++)
            {
                var last = merged[^1];
                if (list[i].Lo <= last.Hi + 1) merged[^1] = (last.Lo, Math.Max(last.Hi, list[i].Hi));
                else merged.Add(list[i]);
            }
            ranges = merged.ToArray();
            return true;
        }

        private static bool TryNum(string s, int max, out int value)
            => int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= max;

        private static bool In((int Lo, int Hi)[]? ranges, int value)
        {
            if (ranges is null) return true;
            foreach (var (lo, hi) in ranges) if (value >= lo && value <= hi) return true;
            return false;
        }

        private static int Segment(DISEntityType t, int i) => i switch
        {
            0 => t.Kind, 1 => t.Domain, 2 => t.Country, 3 => t.Category, 4 => t.Subcategory, 5 => t.Specific, _ => t.Extra,
        };

        /// <summary>The canonical text: ranges sorted and merged, trailing <c>*</c> segments dropped (<see cref="Any"/> = <c>*</c>).</summary>
        public override string ToString() => _text;

        private static string Format((int Lo, int Hi)[]?[] segments)
        {
            int last = SegmentCount - 1;
            while (last > 0 && segments[last] is null) last--;
            var sb = new StringBuilder();
            for (int i = 0; i <= last; i++)
            {
                if (i > 0) sb.Append('.');
                var r = segments[i];
                if (r is null) { sb.Append('*'); continue; }
                for (int j = 0; j < r.Length; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(r[j].Lo.ToString(CultureInfo.InvariantCulture));
                    if (r[j].Hi != r[j].Lo) sb.Append('-').Append(r[j].Hi.ToString(CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }

        public bool Equals(DisTypeMask? other) => other is not null && other._text == _text;
        public override bool Equals(object? obj) => Equals(obj as DisTypeMask);
        public override int GetHashCode() => _text.GetHashCode(StringComparison.Ordinal);
    }
}
