using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.CGF.Configuration;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2079</c> — the JSON an SOP order SENDS is read by the behaviour it starts, for EVERY production behaviour of
    /// every kind. 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.6 ("a round-trip rail per kind").
    /// <para>The failure this pins is the silent one: <c>SopActions</c> serialises the behaviour's authored params type with
    /// <see cref="BehaviorParams.JsonOptions"/>, and each kind parses differently (curated: the whole object into
    /// <c>TAuthored</c>; a generated BTree/HSM: one key per variable, CASE-SENSITIVE; a blueprint: case-insensitive). A key
    /// the parser does not recognise is IGNORED — the behaviour then runs on its defaults and nothing throws. ⇒ the test
    /// sends two different values and requires two different blocks.</para>
    /// </summary>
    public sealed unsafe class SopParamsRoundTripTests
    {
        private static readonly Type[] Numeric = { typeof(float), typeof(double), typeof(int) };

        [Fact]
        public void CE2079_EveryBehaviourReadsTheParamsAnSopOrderSends()
        {
            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            using var world = new EntityRepository();
            var self = world.CreateEntity();

            var exercised = new List<(string Name, byte Tier, bool Generated)>();
            var ignored   = new List<string>();
            var skipped   = new List<string>();
            foreach (string name in registry.GetRegisteredNames())
            {
                if (!registry.TryGetId(name, out int id) || !registry.TryGetDefinition(id, out var def)) continue;
                // ⭐ CE-2135 P4 (R-2004) — a class contract too (a blueprint may name its C# twin's DTO: DangerCrossingBp →
                //   DangerCrossingParams), so long as it can be made empty; ⛔ value types only left the blueprint kind unexercised.
                if (def.ParseParams == null || def.JsonParamsDtoType is not { } dtoType
                    || !(dtoType.IsValueType || dtoType.GetConstructor(Type.EmptyTypes) != null)) continue;
                int bytes = RootParamsAccess.RootParamsBytes(def);
                if (bytes <= 0) continue;

                object plain = Activator.CreateInstance(dtoType)!;
                object changed = Activator.CreateInstance(dtoType)!;
                if (!SetSentinels(changed)) continue;   // no top-level number to vary

                byte[] a = new byte[bytes], b = new byte[bytes];
                try
                {
                    Parse(def, Json(plain), a, world, self);
                }
                catch (Exception ex) { skipped.Add($"{name}: {ex.GetType().Name}"); continue; }   // needs world state this rail has not built — not a drift
                Parse(def, Json(changed), b, world, self);

                if (a.AsSpan().SequenceEqual(b)) ignored.Add($"{name} ⇐ {Json(changed)}");
                else exercised.Add((name, def.BrainTier, def.ManagedBlackboardVariables != null));
            }

            Assert.True(ignored.Count == 0, "the sent params were IGNORED by: " + string.Join("; ", ignored));
            // anti-vacuity, per kind: a hand-written one, a generated BTree/HSM, and a blueprint were all exercised
            Assert.True(exercised.Any(e => !e.Generated && e.Tier != BehaviorConstants.BrainTierBlueprint),
                "no hand-written behaviour was exercised; skipped: " + string.Join("; ", skipped));
            Assert.Contains(exercised, e => e.Generated && e.Tier != BehaviorConstants.BrainTierBlueprint);
            Assert.Contains(exercised, e => e.Tier == BehaviorConstants.BrainTierBlueprint);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3190</c> — an HSM whose params DTO holds its HOSTED blueprints' DTOs, one per field: the scenario's own JSON
        /// reaches each hosted blueprint. The rail above skips it (no top-level number), so this one parses
        /// <c>scenarios/hsm-channel-e2e</c>'s <c>behaviorParams</c> through the REGISTERED behaviour and reads each slice back at the
        /// seed offset the runtime looks up for that site (<see cref="HsmParamBindings.SeedOffsetFor(uint, ushort, Guid)"/>).
        /// </summary>
        [Fact]
        public void CE3190_AnHsmHostingBlueprints_SeedsEachFromItsOwnFieldOfTheNamedDto()
        {
            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            Assert.True(registry.TryGetId("HsmChannelE2E", out int id));
            Assert.True(registry.TryGetDefinition(id, out var def));
            Assert.Equal("Hrot.AI.Behaviors.Brains.HsmChannelE2EParams", def!.JsonParamsDtoType?.FullName);

            // the scenario's own params text — the shape a mission sends, kept honest by reading the file
            string scenario = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "scenarios", "hsm-channel-e2e", "scenario.json"));
            string? json = null;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                         scenario, "\"behaviorName\":\\s*\"HsmChannelE2E\",\\s*\"behaviorParams\":\\s*(\"(?:[^\"\\\\]|\\\\.)*\")"))
            { json = JsonSerializer.Deserialize<string>(m.Groups[1].Value); break; }
            Assert.NotNull(json);

            using var world = new EntityRepository();
            byte[] block = new byte[RootParamsAccess.RootParamsBytes(def)];
            Parse(def, json!, block, world, world.CreateEntity());

            var blob = def.HsmDefinition!;
            ushort State(string stableId) => blob.Metadata!.StateStableIds.First(kv => kv.Value == Guid.Parse(stableId)).Key;
            int driving = HsmParamBindings.SeedOffsetFor(blob.MachineId, State("61000000-0000-0000-0000-00000000000a"), Guid.Empty);
            int firing  = HsmParamBindings.SeedOffsetFor(blob.MachineId, State("61000000-0000-0000-0000-00000000000b"), Guid.Empty);
            int guard   = HsmParamBindings.SeedOffsetFor(blob.MachineId, State("61000000-0000-0000-0000-00000000000a"),
                                                         Guid.Parse("00000150-0000-0000-0000-000000000001"));

            // each hosted blueprint reads its slice as its own params: Drive {float DestX; float DestY}, Fire {long TargetNetworkId},
            // Guard {bool EngageTarget} (BlueprintDemoParams.cs)
            Assert.Equal((560f, 296f), (BitConverter.ToSingle(block, driving), BitConverter.ToSingle(block, driving + 4)));
            Assert.Equal(1002L, BitConverter.ToInt64(block, firing));
            Assert.Equal(0, block[guard]);
            Assert.Equal(3, new[] { driving, firing, guard }.Distinct().Count());   // three slices, not one window read three ways
        }

        private static string RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "HROT.sln"))) return dir;
                dir = System.IO.Directory.GetParent(dir)?.FullName;
            }
            throw new InvalidOperationException("HROT.sln not found above " + AppContext.BaseDirectory);
        }

        /// <summary>⭐ The PRODUCTION serialiser itself (a boxed value serialises as its runtime type).</summary>
        private static string Json(object dto) => SopActions.ToJson(dto);

        private static void Parse(BehaviorDefinition def, string json, byte[] into, EntityRepository world, Entity self)
        {
            fixed (byte* p = into) def.ParseParams!(json, p, into.Length, world, self);
        }

        private static bool SetSentinels(object boxed)
        {
            bool any = false;
            int k = 0;
            foreach (var f in boxed.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (f.IsInitOnly) continue;
                if (Array.IndexOf(Numeric, f.FieldType) >= 0)
                {
                    k++;
                    f.SetValue(boxed, Convert.ChangeType(7 + k, f.FieldType));
                    any = true;
                    continue;
                }
                // ⭐ one level into a value-type field (a PickableGeoPoint's Latitude / Longitude): a behaviour that reads only its
                //   order's Location (AirPlatformStrike — a Strike order whose numbers are the fire mission's) is still exercised
                if (!f.FieldType.IsValueType || f.FieldType.IsPrimitive || f.FieldType.IsEnum) continue;
                object inner = f.GetValue(boxed)!;
                bool set = false;
                foreach (var g in f.FieldType.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (g.IsInitOnly || Array.IndexOf(Numeric, g.FieldType) < 0) continue;
                    k++;
                    g.SetValue(inner, Convert.ChangeType(7 + k, g.FieldType));
                    set = true;
                }
                if (!set) continue;
                f.SetValue(boxed, inner);
                any = true;
            }
            // ⭐ hand-written contracts are PROPERTY-shaped ([JsonPropertyName] on get/set), generated ones field-shaped
            foreach (var p in boxed.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!p.CanWrite || !p.CanRead || p.GetIndexParameters().Length > 0
                    || Array.IndexOf(Numeric, p.PropertyType) < 0
                    || p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null) continue;
                k++;
                p.SetValue(boxed, Convert.ChangeType(7 + k, p.PropertyType));
                any = true;
            }
            return any;
        }
    }
}
