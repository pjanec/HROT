using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Serialization;

namespace Hrot.Map.Common.Scenario;

/// <summary>
/// ⭐⭐ CE-4099 AUTOSAVE (📄 docs/blueprints/Architect_Question_108_Run_Mode_Picker.md §4c A4 + §4c.1, option A accepted by the user) — the node's half:
/// <see cref="Take"/> on the FRAME thread (a keyframe of every type with singletons, the save header — it reads only resident singletons — and the read-only
/// CONTENT singletons the save reads), then <see cref="Write"/> on a WORKER: a private sandbox rebuilt from the capture (<see cref="KeyframeSandbox"/>) and
/// the ONE save (<see cref="ScenarioSaveCore.Write"/>) into it. 📐 Byte-identical to the live save on uc-urban-battle, pc-defend-house and
/// showcase-bridgehead (rail <c>CE4099_A0_*</c>, which runs THIS code).
/// </summary>
/// <remarks>
/// ⚠ The serializer's translators read the <see cref="BehaviorRegistry"/>, which a behaviour hot reload writes on the frame thread (plain dictionaries).
/// <see cref="Write"/> reads <see cref="BehaviorRegistry.Revision"/> at the capture and after the write: if it moved, the result is discarded
/// (<see cref="RacedHotReloadException"/>) — an autosave that fails only logs; the next interval takes it.
/// </remarks>
public static class ScenarioAutosaveCore
{
    /// <summary>The keyframe's type set: every registered type (the Recording set drops NoReplay SAVED components — <c>VariantForms</c>) with singletons.</summary>
    public static readonly CaptureOptions Options = new(KeyframeTypes.All, Singletons: true);

    /// <summary>What the frame thread hands the worker. Nothing in it is shared with the live world but the read-only content objects.</summary>
    public sealed record Capture(WorldKeyframe Keyframe, Type[] Types, ScenarioHeader Header, IReadOnlyList<object> Content, long RegistryRevision);

    /// <summary>FRAME THREAD (~20–30 ms on the curated scenarios): the keyframe, the header and the content singletons.</summary>
    public static Capture Take(EntityRepository world, string docType, BehaviorRegistry? registry)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        var content = new List<object>(1);
        // ⭐ §4c.1 — the doors section decides "as authored" against the terrain's AUTHORED door states (TerrainObjectsSection.Write); content, not state (R-2037)
        if (world.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() && world.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() is { } terrain)
            content.Add(terrain);
        return new Capture(WorldKeyframes.Capture(world, Options), KeyframeSandbox.TypesOf(world),
                           ScenarioSaveCore.HeaderOf(world, docType), content, registry?.Revision ?? 0);
    }

    /// <summary>WORKER: the scenario JSON of <paramref name="capture"/>, exactly as <see cref="ScenarioSaveCore.Write"/> writes it.</summary>
    /// <exception cref="RacedHotReloadException">the behaviour registry changed while it ran.</exception>
    public static string ToJson(ScenarioSerializer serializer, Capture capture, BehaviorRegistry? registry, ScenarioBehaviorRemapper? behaviorRemapper = null)
    {
        if (serializer == null) throw new ArgumentNullException(nameof(serializer));
        if (capture == null) throw new ArgumentNullException(nameof(capture));
        string json;
        using (var sandbox = KeyframeSandbox.Rebuild(capture.Types, capture.Keyframe, capture.Content))
        {
            var dom = ScenarioSaveCore.BuildDom(serializer, sandbox, capture.Header, onlyGuids: null, behaviorRemapper);
            json = JsonAestheticFormatter.FlattenNumericArrays(JsonSerializer.Serialize(dom, FdpJsonOptionsRegistry.DefaultRelaxed));
        }
        if ((registry?.Revision ?? 0) != capture.RegistryRevision) throw new RacedHotReloadException();
        return json;
    }

    /// <summary>WORKER: <see cref="ToJson"/> written to <paramref name="filePath"/>.</summary>
    public static void Write(ScenarioSerializer serializer, Capture capture, string filePath, BehaviorRegistry? registry, ScenarioBehaviorRemapper? behaviorRemapper = null)
        => File.WriteAllText(filePath, ToJson(serializer, capture, registry, behaviorRemapper));

    /// <summary>A behaviour hot reload ran while the worker extracted: the result is not trusted.</summary>
    public sealed class RacedHotReloadException : Exception
    {
        public RacedHotReloadException() : base("a behaviour hot reload ran during the autosave extraction — discarded; the next interval takes it") { }
    }
}
