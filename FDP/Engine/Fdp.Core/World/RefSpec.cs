using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Fdp.Core
{
    /// <summary>How an authored value was written (<see cref="RefSpec.Form"/>).</summary>
    public enum RefForm : byte
    {
        /// <summary>Not authored by reference — the value is the literal answer (a number, coordinates).</summary>
        None = 0,
        /// <summary>A stable entity GUID — <i>this exact entity</i> (<c>EntityGuid</c>, CE-4153).</summary>
        Exact = 1,
        /// <summary>A <c>RefKey</c> — <i>whatever entity has that key here</i> (<c>"@BlueHQ"</c>, <c>"@Outer/Inner"</c>).</summary>
        Key = 2,
        /// <summary>An area authored as a circle (<c>AreaLink</c> only).</summary>
        Circle = 3,
        /// <summary>An area authored as a rotated rectangle (<c>AreaLink</c> only).</summary>
        Rectangle = 4,
    }

    /// <summary>
    /// ⭐⭐ HOW a person wrote a reference — kept beside the resolved answer in every authored type (<c>EntityLink</c>, <c>PointLink</c>,
    /// <c>AreaLink</c>) so resolution FILLS the answer and KEEPS the form, and a save writes the form back.
    /// 📄 <c>docs/DESIGN_Scenario_Rules.md</c> §9.8.8 SR-140.
    /// <para>
    /// 36 bytes, fully unmanaged (saved, recorded, snapshotted and sent on a brain hand-over as is — SR-139): a
    /// <see cref="FixedString32"/> key (≤ 31 UTF-8 bytes, no <c>@</c>) OVERLAID with a <see cref="Guid"/> (one or the other), then the
    /// <see cref="RefForm"/>. All zero = not authored by reference. ⛔ A key longer than 31 bytes is refused LOUDLY (never truncated).
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 36)]
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct RefSpec : IEquatable<RefSpec>
    {
        /// <summary>The key, when <see cref="Form"/> is <see cref="RefForm.Key"/> (no leading <c>@</c>).</summary>
        [FieldOffset(0)] public readonly FixedString32 Key;
        /// <summary>The entity's stable GUID, when <see cref="Form"/> is <see cref="RefForm.Exact"/> (overlays <see cref="Key"/>).</summary>
        [FieldOffset(0)] public readonly Guid Exact;
        /// <summary>How the value was written.</summary>
        [FieldOffset(32)] public readonly RefForm Form;

        private RefSpec(RefForm form, FixedString32 key) { Exact = default; Key = key; Form = form; }
        private RefSpec(RefForm form, Guid exact) { Key = default; Exact = exact; Form = form; }

        /// <summary>Not authored by reference.</summary>
        public static RefSpec None => default;

        /// <summary><c>true</c> when the value is a literal (no reference form).</summary>
        public bool IsNone => Form == RefForm.None;

        /// <summary>By key — <paramref name="key"/> with or without the leading <c>@</c>; ⛔ empty or over 31 UTF-8 bytes throws.</summary>
        public static RefSpec ByKey(string key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (key.StartsWith('@')) key = key.Substring(1);
            if (key.Length == 0) throw new ArgumentException("a reference key cannot be empty", nameof(key));
            int bytes = Encoding.UTF8.GetByteCount(key);
            if (bytes > FixedString32.MaxLength)
                throw new ArgumentException($"the reference key '@{key}' is {bytes} UTF-8 bytes; a key is at most {FixedString32.MaxLength}", nameof(key));
            return new RefSpec(RefForm.Key, new FixedString32(key));
        }

        /// <summary>This exact entity — its stable GUID.</summary>
        public static RefSpec ByExact(Guid entityGuid)
            => entityGuid == Guid.Empty ? throw new ArgumentException("an exact reference needs a GUID", nameof(entityGuid))
                                        : new RefSpec(RefForm.Exact, entityGuid);

        /// <summary>A shape form with no text (<see cref="RefForm.Circle"/> / <see cref="RefForm.Rectangle"/>).</summary>
        public static RefSpec Shape(RefForm shape)
            => shape is RefForm.Circle or RefForm.Rectangle ? new RefSpec(shape, default(Guid))
                                                            : throw new ArgumentException($"{shape} is not a shape form", nameof(shape));

        /// <summary>The key text without <c>@</c> (empty unless <see cref="RefForm.Key"/>).</summary>
        public string KeyText => Form == RefForm.Key ? Key.ToString() : "";

        /// <summary>The authored text — <c>"@BlueHQ"</c>, a GUID <c>"D"</c> string, or <c>""</c> (none / a shape).</summary>
        public override string ToString() => Form switch
        {
            RefForm.Key   => "@" + Key.ToString(),
            RefForm.Exact => Exact.ToString("D"),
            _             => "",
        };

        /// <summary>
        /// Parses the authored TEXT of a reference: <c>"@Key"</c> ⇒ <see cref="RefForm.Key"/>, a GUID ⇒ <see cref="RefForm.Exact"/>;
        /// <c>false</c> for anything else (the caller decides whether that is a number, an error, …).
        /// </summary>
        public static bool TryParse(string? text, out RefSpec spec)
        {
            spec = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text[0] == '@') { spec = ByKey(text); return true; }
            if (Guid.TryParse(text, out var g) && g != Guid.Empty) { spec = ByExact(g); return true; }
            return false;
        }

        public bool Equals(RefSpec other) => Form == other.Form && Form switch
        {
            RefForm.Key   => Key.Equals(other.Key),
            RefForm.Exact => Exact == other.Exact,
            _             => true,
        };
        public override bool Equals(object? obj) => obj is RefSpec r && Equals(r);
        public override int GetHashCode() => Form switch
        {
            RefForm.Key   => HashCode.Combine(Form, Key.GetHashCode()),
            RefForm.Exact => HashCode.Combine(Form, Exact),
            _             => Form.GetHashCode(),
        };
        public static bool operator ==(RefSpec a, RefSpec b) => a.Equals(b);
        public static bool operator !=(RefSpec a, RefSpec b) => !a.Equals(b);
    }
}
