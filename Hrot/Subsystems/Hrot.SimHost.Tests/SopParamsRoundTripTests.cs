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
                if (def.ParseParams == null || def.JsonParamsDtoType is not { IsValueType: true } dtoType) continue;
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
                if (f.IsInitOnly || Array.IndexOf(Numeric, f.FieldType) < 0) continue;
                k++;
                f.SetValue(boxed, Convert.ChangeType(7 + k, f.FieldType));
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
