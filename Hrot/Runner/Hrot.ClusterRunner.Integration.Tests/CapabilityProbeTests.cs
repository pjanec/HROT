using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Signatures;
using Hrot.AI.Behaviors.Brains;
using Hrot.Common.Orders;
using Hrot.MuscleCharacter.Animation.Stance;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>C0</c> (<c>docs/DESIGN_Capability_Components.md</c> §2c, §5) — THE COMPOSITION PROBE. One entity composed the way a simulated device
/// will be — a brain (<see cref="BehaviorState"/>, owned here), a place, health and a side — but NOT a person body (no stance), NOT a command
/// unit, NOT a weapon system and held by no station. A clean ECS handles a new combination of capabilities without any system knowing what
/// the entity "is"; every system that asks a PROXY (a component that only correlates with the capability it needs) treats the probe wrongly.
///
/// <para>⭐ A RATCHET, not a red test: each row asks one capability question; the rows that answer wrongly TODAY are listed in
/// <see cref="ExpectedProxies"/> with the family that owns their fix. The rail fails when a row outside the list goes wrong (a NEW proxy) or
/// a listed row starts answering right (its slice landed — remove it from the list in that slice's commit). C1–C3 empty the list.</para>
///
/// <para>Rows NOT here, on purpose: the station-body rows (no mode lets a non-body be held before C3 — C3 owns a two-seat rail) and the
/// gizmo meta-shape rows (C4 — a frame rail).</para>
/// </summary>
public sealed class CapabilityProbeTests
{
    /// <summary>The rows that answer wrongly today, each with the family (design §4) whose slice fixes it.</summary>
    private static readonly IReadOnlyDictionary<string, string> ExpectedProxies = new Dictionary<string, string>
    {
        // F2 — "has a brain" asked for "is a person / is crewed".
        ["not crewed (heat)"]          = "F2 / C2: HeatSourceSystem.IsCrewed = has BehaviorState (HeatSourceSystem.cs:175-177)",
        ["no side proficiency"]        = "F2 / C2: ProficiencyFillSystem walks With<BehaviorState>.With<EntityInfo> (ProficiencyFillSystem.cs:65)",
    };

    private const int ProbeId = 7700;

    [Fact]
    public void A_brain_that_is_no_person_no_unit_and_no_weapon_is_treated_as_exactly_that_by_every_system()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var repo = h.Repo;
        if (!repo.IsComponentTypeRegistered<SkillAmplifier>()) repo.RegisterComponent<SkillAmplifier>();
        if (!repo.IsComponentTypeRegistered<CombatProficiency>()) repo.RegisterComponent<CombatProficiency>();

        // the world's side amplifier: every Friend brain gets one proficiency tier (what makes ProficiencyFill act at all)
        var amp = repo.CreateEntity();
        repo.AddComponent(amp, new SkillAmplifier { Friend = 1 });

        // THE PROBE — the device's composition
        var probe = repo.CreateEntity();
        repo.AddComponent(probe, new BehaviorState());
        repo.SetAuthority<BehaviorState>(probe, true);
        repo.AddComponent(probe, new EntityInfo { ForceId = ForceId.Friend });
        repo.AddComponent(probe, new SimTransform { Position = new Vector3(10f, 10f, 0f), Rotation = Quaternion.Identity });
        repo.AddComponent(probe, new Health { Current = 1f, Max = 1f });
        // ⭐ C1 — the device is an ITEM (it can be carried / mounted): it declares it through Mountable, not by pretending to be a weapon
        if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Mountable>()) repo.RegisterComponent<Fdp.Toolkit.Combat.Mountable>();
        repo.AddComponent(probe, new Fdp.Toolkit.Combat.Mountable
        {
            MountKinds = (byte)Fdp.Toolkit.Tkb.Domain.MountKinds.Carried, Portable = (byte)Fdp.Toolkit.Tkb.Domain.WeaponPortability.OneMan, CarrySpeedFactor = 1f,
        });
        h.EntityMap.Register(ProbeId, probe);

        // every system of the node runs over it — PumpFrames steps the time controller each frame (EditorHarness.PumpFrames), so
        // WhenTimeAdvances systems run too
        h.PumpFrames(30);

        var rows = new List<(string Name, bool Right)>
        {
            // F1 — an item is manned or taken, never possessed or ordered aboard as a passenger
            ("not possessable as a body", Possessability.Refusal(repo, probe) != null),
            ("not offered Embark",        !EmbarkOrders.CanOffer(repo, probe)),
            // F2 — a brain is not a crew, not a person, not a soldier with a side's training
            ("not crewed (heat)",         !HeatSourceSystem.IsCrewed(repo, probe)),
            ("no side proficiency",       !repo.HasComponent<CombatProficiency>(probe)),
            // guards — rows that answer RIGHT today; a regression here is a new proxy
            ("feels no stress",           !StressModel.Feels(repo, probe)),
            ("has no stance to change",   !StanceRequest.CanCarry(repo, probe)),
            ("still alive and ticked",    repo.IsAlive(probe) && repo.HasComponent<BehaviorState>(probe)),
        };

        var wrong = rows.Where(r => !r.Right).Select(r => r.Name).ToHashSet();
        var newProxies = wrong.Where(n => !ExpectedProxies.ContainsKey(n)).ToList();
        var fixedRows = ExpectedProxies.Keys.Where(n => !wrong.Contains(n)).ToList();

        Assert.True(newProxies.Count == 0,
            "NEW proxy rows (a system treats the probe as something it is not): " + string.Join(", ", newProxies));
        Assert.True(fixedRows.Count == 0,
            "rows now answer RIGHT — their slice landed; remove them from ExpectedProxies: " + string.Join(", ", fixedRows));
    }
}
