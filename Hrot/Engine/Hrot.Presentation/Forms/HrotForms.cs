using System;
using Fdp.Core;
using Fdp.Presentation.Forms;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Presentation.Forms;

/// <summary>
/// ⭐ U-23 (docs/UX/DESIGN_Details_Forms.md §3, D3) — the engine's DECLARED forms, registered once into the one <see cref="FormCatalogue"/>; the Details bundle
/// draws a tab per form an entity is offered (every host, read-only on a recording — §2.4). ⛔ No form writes a component: each field names its attribute.
/// </summary>
public static class HrotForms
{
    private static bool _registered;
    private static readonly object Gate = new();

    /// <summary>Registers the forms (idempotent; the bundle calls it before it lists them).</summary>
    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered) return;
            _registered = true;
            FormCatalogue.Register(Unit);
            FormCatalogue.Register(CrewAndCargo);
            FormCatalogue.Register(Shape);
            FormCatalogue.Register(Scenario);
        }
    }

    // ── the side: Blue / Red / Neutral (ForceId 1 / 2 / 0) ──────────────────────────────────────────────────────────────────

    private static readonly string[] Sides = { "Blue", "Red", "Neutral" };
    private static readonly ForceId[] SideForces = { ForceId.Friend, ForceId.Hostile, ForceId.Neutral };
    private static readonly string[] Affiliations = { "FORCE_FRIENDLY", "FORCE_OPPOSING", "FORCE_NEUTRAL" };   // what the Affiliation attribute takes (AttributeCompilerFactory)

    private static double SideIndex(ForceId f) => f switch { ForceId.Friend => 0, ForceId.Hostile => 1, ForceId.Neutral => 2, _ => -1 };

    /// <summary>A 32-byte fixed string as a 64-byte one — both are null-terminated UTF-8 buffers, so a copy of the 32 bytes (0 B; a field reads it every frame).</summary>
    private static FixedString64 Widen(in FixedString32 s)
    {
        FixedString64 r = default;
        System.Runtime.CompilerServices.Unsafe.CopyBlockUnaligned(ref System.Runtime.CompilerServices.Unsafe.As<FixedString64, byte>(ref r),
            ref System.Runtime.CompilerServices.Unsafe.As<FixedString32, byte>(ref System.Runtime.CompilerServices.Unsafe.AsRef(in s)), 32);
        return r;
    }

    private static readonly string[] Stances = { "Standing", "Crouched", "Prone" };

    /// <summary>
    /// ⭐ D3 — the <b>Unit</b> form (§3): name · side · posture · strength · playable side / slot; ⚙ the entity's network id and TKB template.
    /// ⏳ Not yet (each needs a field kind or an attribute this commit does not add): the type's name (a derived, cached text), rounds per mount (<c>Ammo.*</c>
    /// with a sub-index), the owner node, the loaded round.
    /// </summary>
    public static readonly FormDecl Unit = new()
    {
        Id = "form.unit", Title = "Unit", Order = 10, Requires = new[] { typeof(EntityInfo) },
        Fields = new[]
        {
            new FormField { Label = "Name", Kind = FieldKind.Text, Attribute = "Name", ReadText = (w, e) => w.GetComponentRO<EntityInfo>(e).Name },
            new FormField
            {
                Label = "Side", Kind = FieldKind.Choice, Attribute = "Affiliation", Choices = Sides,
                ReadNumber = (w, e) => SideIndex(w.GetComponentRO<EntityInfo>(e).ForceId),
                ChoiceValue = i => AttributeValue.FromString(i >= 0 && i < Affiliations.Length ? Affiliations[i] : null),
            },
            new FormField
            {
                Label = "Posture", Kind = FieldKind.Choice, Attribute = "Stance", Choices = Stances,
                Has = (w, e) => w.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>() && w.HasComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(e),
                ReadNumber = (w, e) => (int)w.GetComponentRO<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(e).TargetStance,
                ChoiceValue = i => AttributeValue.FromInt(i),
            },
            new FormField
            {
                Label = "Strength", Attribute = "Health.Current", Min = 0,
                Has = (w, e) => w.IsComponentTypeRegistered<Health>() && w.HasComponent<Health>(e),
                ReadNumber = (w, e) => w.GetComponentRO<Health>(e).Current,
            },
            new FormField
            {
                Label = "Playable for", Kind = FieldKind.Choice, Attribute = "Playable.Side", Choices = Sides,
                Has = (w, e) => w.IsComponentTypeRegistered<Hrot.Common.Playable>() && w.HasComponent<Hrot.Common.Playable>(e),
                ReadNumber = (w, e) => SideIndex(w.GetComponentRO<Hrot.Common.Playable>(e).Side),
                ChoiceValue = i => AttributeValue.FromInt(i >= 0 && i < SideForces.Length ? (int)SideForces[i] : 0),
            },
            new FormField
            {
                Label = "Slot", Kind = FieldKind.Text, Attribute = "Playable.SlotName",
                Has = (w, e) => w.IsComponentTypeRegistered<Hrot.Common.Playable>() && w.HasComponent<Hrot.Common.Playable>(e),
                ReadText = (w, e) => Widen(w.GetComponentRO<Hrot.Common.Playable>(e).SlotName),
            },
            new FormField
            {
                Label = "Entity id", Expert = true,
                Has = (w, e) => w.IsComponentTypeRegistered<NetworkIdentity>() && w.HasComponent<NetworkIdentity>(e),
                ReadNumber = (w, e) => w.GetComponentRO<NetworkIdentity>(e).Value,
            },
            new FormField
            {
                Label = "TKB template", Expert = true,
                Has = (w, e) => w.IsComponentTypeRegistered<TkbIdentity>() && w.HasComponent<TkbIdentity>(e),
                ReadNumber = (w, e) => (long)w.GetComponentRO<TkbIdentity>(e).TkbType,
            },
        },
    };

    private static bool HasSpawn(EntityRepository w, Entity e) => w.IsComponentTypeRegistered<Hrot.Common.SpawnArea>() && w.HasComponent<Hrot.Common.SpawnArea>(e);
    private static bool HasWeather(EntityRepository w, Entity e) => w.IsComponentTypeRegistered<Fdp.Toolkit.Weather.WeatherConditions>() && w.HasComponent<Fdp.Toolkit.Weather.WeatherConditions>(e);
    private static ref readonly Fdp.Toolkit.Weather.WeatherConditions Weather(EntityRepository w, Entity e) => ref w.GetComponentRO<Fdp.Toolkit.Weather.WeatherConditions>(e);

    private static readonly string[] Precipitations = { "None", "Drizzle", "Rain", "Snow", "Sleet", "Hail" };   // Weather.PrecipitationKind

    private static FormField WeatherNumber(string label, string attribute, string unit, Func<EntityRepository, Entity, double> read, double min = double.MinValue, double max = double.MaxValue, bool expert = false)
        => new() { Label = label, Attribute = attribute, Unit = unit, Min = min, Max = max, Expert = expert, Has = HasWeather, ReadNumber = read };

    /// <summary>
    /// ⭐ D4 — the <b>Crew &amp; cargo</b> form (§3; §4.0a part 4): offered for a carrier (<c>PassengerBuffer</c>). One row per OCCUPIED seat — its role
    /// (Driver · Gunner · Commander · Weapon station · Passenger) and who sits there, with <i>Take off</i>; then the free seats as a count; <i>Put aboard…</i>
    /// picks a unit. ⭐ Both actions are <see cref="Hrot.Common.Orders.EmbarkOrders"/> — the ONE embark path (the map menu, the ORBAT drag): the vehicle's owner
    /// answers. ⛔ No write path of its own.
    /// </summary>
    public static readonly FormDecl CrewAndCargo = new()
    {
        Id = "form.crew", Title = "Crew & cargo", Order = 20,
        Requires = new[] { typeof(Fdp.Toolkit.Behavior.Components.PassengerBuffer) },
        Fields = Array.Empty<FormField>(),
        Lists = new[]
        {
            new FormList
            {
                Label = "Seats",
                Fill = FillSeats,
                Footer = FreeSeatsText,
                RemoveLabel = "Take off",
                Remove = (w, it) => Hrot.Common.Orders.EmbarkOrders.Disembark(w, it.Item),
                AddLabel = "Put aboard…",
                Add = PutAboard,
            },
        },
    };

    private static readonly string[] RoleWords = { "Passenger", "Driver", "Gunner", "Commander", "Weapon station" };
    private static readonly string[] FreeWords = BuildFreeWords();
    private static string[] BuildFreeWords()
    {
        var w = new string[Fdp.Toolkit.Behavior.Components.PassengerBuffer.Capacity + 1];
        w[0] = "no seat free";
        for (int i = 1; i < w.Length; i++) w[i] = i == 1 ? "1 seat free" : i + " seats free";
        return w;
    }

    private static int SeatCountOf(EntityRepository w, Entity e)
        => w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.CarrierSeats>() && w.HasComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>(e)
            ? Math.Min((int)w.GetComponentRO<Fdp.Toolkit.Behavior.Components.CarrierSeats>(e).SeatCount, Fdp.Toolkit.Behavior.Components.PassengerBuffer.Capacity)
            : Fdp.Toolkit.Behavior.Components.PassengerBuffer.UndeclaredSeats;

    private static string RoleOf(EntityRepository w, Entity e, int seat)
    {
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.CarrierSeats>() || !w.HasComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>(e)) return RoleWords[0];
        int r = (int)w.GetComponentRO<Fdp.Toolkit.Behavior.Components.CarrierSeats>(e).Roles[seat];
        return r >= 0 && r < RoleWords.Length ? RoleWords[r] : RoleWords[0];
    }

    private static void FillSeats(EntityRepository w, Entity e, System.Collections.Generic.List<FormListItem> into)
    {
        ref readonly var buf = ref w.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(e);
        int seats = SeatCountOf(w, e);
        bool named = w.IsComponentTypeRegistered<EntityInfo>();
        for (int i = 0; i < seats; i++)
        {
            var p = buf.Passengers[i];
            if (p.IsNull || !w.IsAlive(p)) continue;
            var who = named && w.HasComponent<EntityInfo>(p) ? w.GetComponentRO<EntityInfo>(p).Name : new FixedString64("(unnamed)");
            into.Add(new FormListItem(RoleOf(w, e, i), who, p));
        }
    }

    private static string? FreeSeatsText(EntityRepository w, Entity e)
    {
        ref readonly var buf = ref w.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(e);
        int seats = SeatCountOf(w, e), free = 0;
        for (int i = 0; i < seats; i++) if (buf.Passengers[i].IsNull) free++;
        return FreeWords[Math.Min(free, FreeWords.Length - 1)];
    }

    private static string? PutAboard(EntityRepository w, Entity vehicle, Entity unit)
    {
        if (unit == vehicle) return "a vehicle cannot board itself";
        if (!Hrot.Common.Orders.EmbarkOrders.CanOffer(w, unit)) return "this cannot be put aboard (already aboard, a vehicle or a weapon)";
        return Hrot.Common.Orders.EmbarkOrders.Embark(w, unit, vehicle) ? null : "not sent";
    }

    /// <summary>
    /// ⭐ D5 — the <b>Shape</b> form (§3): offered for a spawn area or a weather region. Spawn area: side, facing. Weather region: the authored scalars of
    /// <c>WeatherConditions</c> (the global weather's form is the Scenario form's). ⏳ Not yet: kind / name / point count / length (read-only), a route's legs
    /// (CE-4284, a list — not a scalar attribute), the wind levels and cloud layers (lists, not attributes yet).
    /// </summary>
    public static readonly FormDecl Shape = new()
    {
        Id = "form.shape", Title = "Shape", Order = 30,
        Offers = (w, e) => HasSpawn(w, e) || HasWeather(w, e),
        Fields = Join(new[]
        {
            new FormField
            {
                Label = "Spawn side", Kind = FieldKind.Choice, Attribute = "SpawnArea.Side", Choices = Sides, Has = HasSpawn,
                ReadNumber = (w, e) => SideIndex(w.GetComponentRO<Hrot.Common.SpawnArea>(e).Side),
                ChoiceValue = i => AttributeValue.FromInt(i >= 0 && i < SideForces.Length ? (int)SideForces[i] : 0),
            },
            new FormField { Label = "Facing", Attribute = "SpawnArea.FacingDeg", Unit = "°", Min = 0, Max = 360, Has = HasSpawn, ReadNumber = (w, e) => w.GetComponentRO<Hrot.Common.SpawnArea>(e).FacingDeg },
            // the region's weather: the ONE weather row set (WeatherRows), shared with the Scenario form
        }, WeatherRows()),
    };

    /// <summary>The authored weather scalars as form rows — ONE set, used by the Shape form (a weather region) and the Scenario form (the global weather).</summary>
    private static FormField[] WeatherRows() => new[]
    {
            WeatherNumber("Temperature", "Weather.GroundTemperatureC", "°C", (w, e) => Weather(w, e).GroundTemperatureC, -80, 70),
            WeatherNumber("Pressure", "Weather.SeaLevelPressureHpa", "hPa", (w, e) => Weather(w, e).SeaLevelPressureHpa, 800, 1100),
            WeatherNumber("Humidity", "Weather.RelativeHumidity", "(0-1)", (w, e) => Weather(w, e).RelativeHumidity, 0, 1),   // stored 0…1 (W-7)
            WeatherNumber("Visibility", "Weather.VisibilityM", "m", (w, e) => Weather(w, e).VisibilityM, 0),
            new FormField
            {
                Label = "Precipitation", Kind = FieldKind.Choice, Attribute = "Weather.Precipitation", Choices = Precipitations, Has = HasWeather,
                ReadNumber = (w, e) => Weather(w, e).Precipitation, ChoiceValue = i => AttributeValue.FromInt(i),
            },
            WeatherNumber("Rate", "Weather.PrecipitationMmPerHour", "mm/h", (w, e) => Weather(w, e).PrecipitationMmPerHour, 0),
            WeatherNumber("Fog top", "Weather.FogTopM", "m", (w, e) => Weather(w, e).FogTopM, 0),
            WeatherNumber("Fog visibility", "Weather.FogVisibilityM", "m", (w, e) => Weather(w, e).FogVisibilityM, 0),
            WeatherNumber("Edge fade", "Weather.FeatherM", "m", (w, e) => Weather(w, e).FeatherM, 0),
            WeatherNumber("Lapse rate", "Weather.LapseRateKPerKm", "K/km", (w, e) => Weather(w, e).LapseRateKPerKm, expert: true),
    };

    private static FormField[] Join(FormField[] a, FormField[] b)
    {
        var r = new FormField[a.Length + b.Length];
        a.CopyTo(r, 0); b.CopyTo(r, a.Length);
        return r;
    }

    // ── the Scenario form: nothing selected — the World Environment (its clock and the global weather) ───────────────────────

    private static Entity WorldEnvironment(EntityRepository w)
    {
        Fdp.Toolkit.Weather.WeatherField.GlobalOf(w, out var env);
        return env;
    }

    private static long _startTicks = long.MinValue;
    private static FixedString64 _startText;

    /// <summary>The start as people read it (<c>2026-06-01 06:00 UTC</c>), cached by its ticks — a frame reading it allocates nothing.</summary>
    private static FixedString64 StartText(long ticks)
    {
        if (ticks != _startTicks)
        {
            _startTicks = ticks;
            _startText = new FixedString64(ticks <= 0 ? "(not set)" : new DateTime(ticks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture));
        }
        return _startText;
    }

    private static bool HasClock(EntityRepository w, Entity e) => w.IsComponentTypeRegistered<Fdp.Toolkit.Weather.ScenarioClock>() && w.HasComponent<Fdp.Toolkit.Weather.ScenarioClock>(e);

    /// <summary>
    /// ⭐ D5 — the <b>Scenario</b> form (§3), drawn when nothing is selected: Time (the start, read-only here; the display offset) and the GLOBAL weather (the
    /// same rows as a region's). ⏳ Not yet: name / description (the file header — a document step, not an attribute), *Capture picture*, editing the start
    /// (a date field kind), Terrain (read-only), Parameters.
    /// </summary>
    public static readonly FormDecl Scenario = new()
    {
        Id = "form.scenario", Title = "Scenario", Order = 5,
        WorldTarget = WorldEnvironment,
        Fields = Join(new[]
        {
            new FormField { Label = "Start", Kind = FieldKind.Text, Has = HasClock, ReadText = (w, e) => StartText(w.GetComponentRO<Fdp.Toolkit.Weather.ScenarioClock>(e).StartUtcTicks) },
            new FormField { Label = "Clock offset", Attribute = "ScenarioUtcOffsetMinutes", Unit = "min", Min = -720, Max = 840, Has = HasClock,
                            ReadNumber = (w, e) => w.GetComponentRO<Fdp.Toolkit.Weather.ScenarioClock>(e).DisplayUtcOffsetMinutes },
        }, WeatherRows()),
    };
}
