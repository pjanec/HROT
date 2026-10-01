using System;
using System.Collections.Generic;
using System.Globalization;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-415</c> — the one reader of a persisted <c>System.Numerics</c> vector / quaternion value.</b>
///
/// <para>
/// 🔴 <b>The defect.</b> Neither compiler converter had a vector arm: <see cref="DefaultLiteral.TryToCSharp"/> refused
/// every vector default (<c>BP1674</c>), and <c>Stage3_Normalize.FormatDefaultLiteral</c> returned <c>null</c> for a vector
/// PIN default — which a channel command's struct initialiser then simply OMITTED. ⇒ <c>Loco1</c>'s authored
/// <c>Destination</c> and <c>EnumDemo</c>'s <c>TargetPos</c> compiled to <c>Vector3.Zero</c>, silently.
/// </para>
///
/// <para>
/// ⭐ <b>Two spellings exist on disk</b>, and both must read:
/// <list type="bullet">
/// <item><c>[x, y, z]</c> — invariant culture, what the editor writes today (<c>BlueprintPinModel.FormatValue</c>).</item>
/// <item><c>&lt;x  y  z&gt;</c> — the LEGACY <c>Vector3.ToString()</c> form, in the AUTHOR's culture. 📐 <c>Loco1</c> stores
/// <c>&lt;-0,5  0  0&gt;</c>: a decimal COMMA and non-breaking-space separators (cs-CZ). An en-US author wrote
/// <c>&lt;1.5, 2, 3&gt;</c>. ⇒ the legacy form is split on WHITESPACE (NBSP included), a trailing list separator is dropped
/// from each token, and a remaining comma is the decimal separator.</item>
/// </list>
/// </para>
///
/// <para>⭐ Public on the <c>MacroExpander</c> precedent so the editor reads with the same rules — the editor's own
/// splitter split <c>-0,5</c> into two numbers.</para>
/// </summary>
public static class VectorLiteral
{
    /// <summary>The component count of a vector/quaternion type id, or 0 when it is not one.</summary>
    public static int ArityOf(string? typeId) => typeId switch
    {
        "System.Numerics.Vector2"    => 2,
        "System.Numerics.Vector3"    => 3,
        "System.Numerics.Vector4"    => 4,
        "System.Numerics.Quaternion" => 4,
        _                            => 0,
    };

    /// <summary>
    /// Reads exactly <paramref name="arity"/> finite components from <paramref name="text"/>. Returns false (and a
    /// reason) for any other count or an unreadable number — ⛔ never a partially-filled or zero vector.
    /// </summary>
    public static bool TryParse(string? text, int arity, out float[] components, out string reason)
    {
        components = Array.Empty<float>();
        reason = "";
        string s = (text ?? "").Trim();
        if (s.Length == 0) { reason = "empty value"; return false; }

        var tokens = new List<string>();
        if (s[0] == '<' && s[s.Length - 1] == '>')
        {
            // Legacy Vector3.ToString(), author's culture: components are WHITESPACE-separated.
            foreach (var raw in s.Substring(1, s.Length - 2).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim().TrimEnd(',', ';');
                if (t.Length == 0) continue;
                if (t.IndexOf(',') >= 0 && t.IndexOf('.') < 0) t = t.Replace(',', '.');   // decimal comma
                tokens.Add(t);
            }
        }
        else
        {
            if (s[0] == '[' && s[s.Length - 1] == ']') s = s.Substring(1, s.Length - 2);
            foreach (var raw in s.Split(','))
            {
                string t = raw.Trim();
                if (t.Length > 0) tokens.Add(t);
            }
        }

        if (tokens.Count != arity)
        {
            reason = $"expected {arity} components as [x, y{(arity > 2 ? ", z" : "")}{(arity > 3 ? ", w" : "")}], got {tokens.Count}";
            return false;
        }

        var result = new float[arity];
        for (int i = 0; i < arity; i++)
        {
            string t = tokens[i].TrimEnd('f', 'F');
            if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out result[i])
                || float.IsNaN(result[i]) || float.IsInfinity(result[i]))
            {
                reason = $"component {i + 1} ('{tokens[i]}') is not a finite number";
                return false;
            }
        }
        components = result;
        return true;
    }

    /// <summary>
    /// The C# expression for a vector/quaternion value — <c>new global::System.Numerics.Vector3(1F, 2F, 3F)</c>.
    /// Returns false when <paramref name="typeId"/> is not a vector type or <paramref name="text"/> does not parse.
    /// </summary>
    public static bool TryToCSharp(string? typeId, string? text, out string csharp, out string reason)
    {
        csharp = "";
        int arity = ArityOf(typeId);
        if (arity == 0) { reason = $"type '{typeId}' is not a vector"; return false; }
        if (!TryParse(text, arity, out var c, out reason)) return false;

        var inv = CultureInfo.InvariantCulture;
        var args = new string[arity];
        for (int i = 0; i < arity; i++) args[i] = c[i].ToString("R", inv) + "F";
        csharp = $"new global::{typeId}({string.Join(", ", args)})";
        return true;
    }
}
