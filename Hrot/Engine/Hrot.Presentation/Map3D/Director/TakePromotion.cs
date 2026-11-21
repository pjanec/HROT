using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>What a promote did: the scenario take's path, whether it was written, what became of the bake, and why not when refused.</summary>
/// <param name="Bake"><c>"kept"</c> (it was made for the scenario file as it is now) · <c>"dropped"</c> (made for another version of it) · <c>"none"</c>.</param>
/// <param name="FixedKeys">The keys at a fixed time (no event before them when they were made): on a run whose timing differs they stay at that second.</param>
/// <param name="Conflict">Refused because the scenario already has a take of that name (pass overwrite).</param>
public sealed record PromoteResult(string? From, string? To, bool Written, bool Overwrote, string Bake, IReadOnlyList<string> FixedKeys,
                                   IReadOnlyList<string> Problems, bool Conflict = false)
{
    public static PromoteResult Refused(string why, bool conflict = false, string? from = null, string? to = null)
        => new(from, to, false, false, "none", Array.Empty<string>(), new[] { why }, conflict);
}

/// <summary>
/// ⭐⭐ <c>CE-1304</c> P4 (docs/DESIGN_Director_Timeline_Editor.md §11s; Film Production F-4, workflow ⑧) — PROMOTE: a take adjusted on a recording (stored
/// BESIDE it, TE-1 / §11m D8) is COPIED into its scenario's own takes (<c>&lt;scenarios root&gt;/&lt;scenario&gt;/takes/</c>). ⭐ A copy, no re-anchoring: its keys
/// already hang on events (CR-6), which CR-5 finds again on any run. ⛔ Never over a scenario take of the same name unless <c>overwrite</c>.
/// <para>⭐ <b>The bake rule (§11s D6):</b> kept when it was made for the scenario file AS IT IS NOW (its hash = the scenario's) — a live run of that file
/// repeats the recording's timing (R-3038), so the bake places its cuts exactly; dropped otherwise (it would only be reported stale). With the bake kept the
/// scenario take is the recording take's file, byte for byte.</para>
/// </summary>
public sealed class TakePromotion
{
    private readonly Func<TakeStore?> _from;
    private readonly Func<TakeStore?> _to;
    private readonly Func<string?> _scenario;
    private readonly DirectorTakeSession? _session;

    /// <param name="from">The takes beside the open recording (the host's own store).</param>
    /// <param name="to">The scenarios root's takes; null (or a null answer) = this host's takes ARE the scenario's (the editor): nothing to promote to.</param>
    /// <param name="scenario">The scenario the shown recording names (the default when a request names none).</param>
    /// <param name="session">The take session: a take open with unsaved edits is refused (promote copies the SAVED take).</param>
    public TakePromotion(Func<TakeStore?> from, Func<TakeStore?>? to, Func<string?>? scenario = null, DirectorTakeSession? session = null)
    {
        _from = from ?? throw new ArgumentNullException(nameof(from));
        _to = to ?? (() => null);
        _scenario = scenario ?? (() => null);
        _session = session;
    }

    /// <summary>Whether this host has somewhere to promote to (a recording host: the Replay Browser).</summary>
    public bool Available => _to() != null;

    /// <summary>The scenario the shown recording names.</summary>
    public string? Scenario => _scenario();

    /// <summary>Promotes <paramref name="take"/> of <paramref name="scenario"/> (null: the shown recording's) from this host's takes to the scenario's.</summary>
    public PromoteResult Promote(string? scenario, string take, bool overwrite = false)
    {
        scenario = string.IsNullOrWhiteSpace(scenario) ? _scenario() : scenario;
        if (string.IsNullOrWhiteSpace(scenario)) return PromoteResult.Refused("name the scenario (or open a recording that names one)");
        if (_from() is not { } from || _to() is not { } to)
            return PromoteResult.Refused("nothing to promote to: this host's takes are the scenario's own (promote copies a RECORDING's take — open it in the Replay Browser)");
        if (_session?.Editor is { Dirty: true } open && open.Take.Scenario == scenario && open.Take.Name == take)
            return PromoteResult.Refused($"'{scenario}/{take}' has unsaved edits: save it first (promote copies the saved take)");
        return Copy(from, to, scenario!, take, overwrite);
    }

    /// <summary>⭐ The copy itself (a host-free step: the rails call it with two stores).</summary>
    public static PromoteResult Copy(TakeStore from, TakeStore to, string scenario, string take, bool overwrite)
    {
        var src = from.TakePath(scenario, take);
        var dst = to.TakePath(scenario, take);
        if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.Ordinal))
            return PromoteResult.Refused("the take is already the scenario's own", from: src, to: dst);
        if (!from.HasOwn(scenario, take))   // a take read through from the scenario's own takes (TE-1's reverse) is already promoted
            return PromoteResult.Refused(File.Exists(dst) ? $"'{scenario}/{take}' is the scenario's own take already (no recording take of that name)"
                                                         : $"no take '{take}' for scenario '{scenario}' ({src})", from: src, to: dst);
        var loaded = from.Load(scenario, take);
        if (loaded.Value is not { } value) return PromoteResult.Refused(string.Join("; ", loaded.Problems), from: src, to: dst);
        if (loaded.Problems.Count > 0) return PromoteResult.Refused("the take is not valid: " + string.Join("; ", loaded.Problems), from: src, to: dst);
        bool exists = File.Exists(dst);
        if (exists && !overwrite)
            return PromoteResult.Refused($"scenario '{scenario}' already has a take '{take}' ({dst}) — promote with overwrite to replace it", conflict: true, from: src, to: dst);

        string bake = "none";
        if (value.Baked is { } baked)
        {
            bool fresh = !baked.IsStaleFor(to.ScenarioHash(scenario), "", value.TickHz);
            bake = fresh ? "kept" : "dropped";
        }
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        if (bake == "dropped") to.Save(value with { Baked = null });
        else File.Copy(src, dst, overwrite: true);   // ⭐ a COPY: the recording take's file, byte for byte

        var fixedKeys = value.Keys.Where(k => k.At.Anchor == null).Select(k => k.At.Offset.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " s").ToList();
        return new PromoteResult(src, dst, true, exists, bake, fixedKeys, Array.Empty<string>());
    }
}
