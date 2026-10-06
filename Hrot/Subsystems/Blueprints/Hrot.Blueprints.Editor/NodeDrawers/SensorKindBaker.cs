using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// ⭐ <c>CE-3078</c> (D1) — bakes a sensor kind's types from the toolkit's <see cref="SensorKindRegistry"/> into the
/// <see cref="SensorKindDecl"/> the sensor nodes carry. The ONLY reader of the registry on the blueprint side: the compiler
/// (netstandard2.0) cannot reference it, so every kind fact rides in the asset (the <c>GetComponent</c> CA-01 precedent).
/// 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.10a.
/// </summary>
public static class SensorKindBaker
{
    /// <summary>The decl for <paramref name="kind"/>; null when the kind is not registered.</summary>
    public static SensorKindDecl? Bake(SensorModality kind)
    {
        if (!SensorKindRegistry.TryGet(kind, out var info)) return null;
        var elementFqn = info.ElementType.FullName!;
        var decl = new SensorKindDecl
        {
            Kind               = (byte)kind,
            KindName           = KindName(kind),
            Family             = info.Family.ToString(),
            ResultComponentFqn = info.ResultComponent.FullName!,
            ElementTypeFqn     = elementFqn,
        };
        foreach (var f in info.ElementType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            decl.ElementFields.Add(new ComponentFieldDecl { Name = f.Name, TypeId = PinTypeId(f.FieldType) });

        // ⭐ N4 (Q3) — the triggers as DATA, and whether the answer carries its TIME (BecomesStale needs it).
        decl.Triggers = info.Triggers.Select(t => new SensorTriggerDecl { Name = t.Name, ElementField = t.ElementField, Shape = t.Shape.ToString() }).ToList();
        decl.HasAnswerTime = info.ResultComponent.GetField("LastUpdateTimeSeconds", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.FieldType == typeof(float);

        // ⭐ N3 (Q2) — a kind a behaviour spawns: its settings fields (the in-pins), their Default, the Ensure method.
        if (!string.IsNullOrEmpty(info.EnsureMethod))
        {
            var settings = info.SettingsType;
            decl.EnsureMethodFqn = info.EnsureMethod;
            decl.SettingsTypeFqn = settings.FullName!;
            decl.SettingsFields  = new List<ComponentFieldDecl>();
            foreach (var f in settings.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                decl.SettingsFields.Add(new ComponentFieldDecl { Name = f.Name, TypeId = PinTypeId(f.FieldType) });
            var d = (System.Reflection.MemberInfo?)settings.GetProperty("Default", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                 ?? settings.GetField("Default", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (d is System.Reflection.PropertyInfo { PropertyType: var pt } && pt == settings
                || d is System.Reflection.FieldInfo { FieldType: var ft } && ft == settings)
                decl.SettingsDefaultFqn = settings.FullName + ".Default";
        }
        return decl;
    }

    /// <summary>An enum pin is spelled <c>global::Ns.Enum</c> (a bare FQN is <c>BP1500</c>) — the <c>NodePinSchema</c> rule.</summary>
    private static string PinTypeId(Type type)
        => type.IsEnum ? "global::" + (type.FullName ?? type.Name).Replace('+', '.') : type.FullName ?? type.Name;

    /// <summary>The kind as an author reads it (<c>"EqsQuery"</c> for kind 0, which carries no modality).</summary>
    public static string KindName(SensorModality kind)
        => kind == SensorKindRegistry.EqsQuery ? "EqsQuery" : kind.ToString();
}

/// <summary>⭐ <c>CE-3078</c> — palette entries for the per-kind sensor nodes, one per registered kind (D5).</summary>
public static class SensorPaletteEntries
{
    /// <summary>The palette category of the sensor nodes (beside the ranked EQS nodes).</summary>
    public const string Category = "EQS";

    /// <summary>N2 — "Read Sensor Result: {kind}" for every registered kind, ranked included (D5).</summary>
    public static IEnumerable<NodeKindDescriptor> ReadEntries()
    {
        foreach (var info in SensorKindRegistry.All)
        {
            var kind = info.Kind;
            if (kind == SensorKindRegistry.EqsQuery) continue;   // a query sensor is read through its variable (ReadEqsResult)
            var name = SensorKindBaker.KindName(kind);
            yield return new NodeKindDescriptor
            {
                Kind        = $"Sensor.Read.{name}",
                DisplayName = $"Read Sensor Result: {name}",
                Category    = Category,
                Tooltip     = $"Read entry Index of the unit's {name} sensor ({info.Family} family): IsReady, Count, AnswerTick and one pin per field of {info.ElementType.Name}.",
                Icon        = "icons/eqs_read.svg",
                // Re-bake per placed node so each node owns its decl (never one list shared across nodes).
                CreateInstance = () => new ReadSensorResultNode { Id = Guid.NewGuid(), Decl = SensorKindBaker.Bake(kind) },
            };
        }
    }

    /// <summary>The triggers a node of this decl may offer — <c>BecomesStale</c> only when the answer carries its time (Stage2 BP2076).</summary>
    public static IEnumerable<SensorTriggerDecl> UsableTriggers(SensorKindDecl decl)
        => (decl.Triggers ?? new List<SensorTriggerDecl>()).Where(t => t.Name != "BecomesStale" || decl.HasAnswerTime);

    /// <summary>N4 — "When Sensor Result: {kind}" for every registered unit kind, preset to the kind's first OWN trigger
    /// (the first non-header one — e.g. DangerArea's NextAreaChanged), rising edge.</summary>
    public static IEnumerable<NodeKindDescriptor> WhenEntries()
    {
        foreach (var info in SensorKindRegistry.All)
        {
            var kind = info.Kind;
            if (kind == SensorKindRegistry.EqsQuery) continue;
            var name = SensorKindBaker.KindName(kind);
            yield return new NodeKindDescriptor
            {
                Kind        = $"Sensor.When.{name}",
                DisplayName = $"When Sensor Result: {name}",
                Category    = Category,
                Tooltip     = $"React to the unit's {name} sensor: {string.Join(", ", info.Triggers.Select(t => t.Name))}.",
                Icon        = "icons/when.svg",
                CreateInstance = () =>
                {
                    var decl = SensorKindBaker.Bake(kind)!;
                    var trigger = UsableTriggers(decl).FirstOrDefault(t => t.Shape != "Header") ?? UsableTriggers(decl).First();
                    return new WhenNode
                    {
                        Id = Guid.NewGuid(), Mode = WhenMode.SensorResult, Edges = WhenEdge.RisingEdge,
                        SensorResult = new SensorResultPayload { Decl = decl, Trigger = trigger.Name },
                        Pins =
                        [
                            new Pin { Id = Guid.NewGuid(), Name = "In",      Direction = "In",  IsExec = true },
                            new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true },
                            new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true },
                        ],
                    };
                },
            };
        }
    }

    /// <summary>N3 — "Spawn Sensor: {kind}" for every kind a behaviour spawns (an Ensure method, Q2) — D3.</summary>
    public static IEnumerable<NodeKindDescriptor> SpawnEntries()
    {
        foreach (var info in SensorKindRegistry.All)
        {
            if (string.IsNullOrEmpty(info.EnsureMethod)) continue;
            var kind = info.Kind;
            var name = SensorKindBaker.KindName(kind);
            yield return new NodeKindDescriptor
            {
                Kind        = $"Sensor.Spawn.{name}",
                DisplayName = $"Spawn Sensor: {name}",
                Category    = Category,
                Tooltip     = $"Create this run's {name} sensor (find-or-create, one per Key; it goes when the run ends), configured by one pin per field of {info.SettingsType.Name} — an unwired pin keeps its default.",
                Icon        = "icons/eqs_spawn.svg",
                CreateInstance = () => new SpawnSensorNode { Id = Guid.NewGuid(), Decl = SensorKindBaker.Bake(kind) },
            };
        }
    }
}
