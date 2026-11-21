using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>G1</c> — the rails from <c>DESIGN_Parameter_Model.md</c> §8, not invented ones.</b>
    /// The two this item can be held to: <b>one supply mechanism</b> and <b>parse-before-commit</b>.
    /// </summary>
    public unsafe class ParameterSupplyRailsTests
    {
        private struct DemoParams
        {
            public int   Count;
            public float Speed;
        }

        // ── §8: ONE supply mechanism ─────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ <b>Exactly one parameter-resolution path exists.</b>
        ///
        /// <para>
        /// ⛔ The rail this states is ruling 9's: a second <c>Overrides</c>-style applier alongside the
        /// resolver would be two implementations of one concept. ⚠ Stated by REFLECTION over
        /// <see cref="BehaviorDefinition"/> rather than by grep, because the thing that would break it
        /// is a new member on that type — and a grep for a name nobody has chosen yet cannot see one.
        /// </para>
        ///
        /// <para>
        /// ⭐ <c>BehaviorParams.FromBlockResolver</c> does NOT count as a second mechanism and that is the point
        /// of its shape: it is a FACTORY that returns the same <see cref="ParseParamsDelegate"/>, so
        /// the split exists without the ingress learning a second path.
        /// </para>
        /// </summary>
        [Fact]
        public void BehaviorDefinition_CarriesExactlyOneParameterSupplyDelegate()
        {
            // ⭐ CE-427: a SUPPLY path is a delegate that consumes the authored JSON. BakeDefaults is a
            //   delegate too, but it takes no JSON — it is the BAKE stage the supply path runs over
            //   (§3.2 "defaults are baked, scenario JSON overlays them"), never a second way to supply.
            var delegateMembers = typeof(BehaviorDefinition)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => typeof(Delegate).IsAssignableFrom(p.PropertyType))
                .Where(p => p.PropertyType.GetMethod("Invoke")!.GetParameters()
                              .Any(a => a.ParameterType == typeof(string)))
                .Select(p => p.Name)
                .ToList();

            Assert.True(delegateMembers.Count == 1,
                "a behaviour must have ONE parameter-supply path. Found: "
                + string.Join(", ", delegateMembers));
            Assert.Equal(nameof(BehaviorDefinition.ParseParams), delegateMembers[0]);
        }

        // ── §3.1: the split — deserialize, then resolve ──────────────────────

        /// <summary>
        /// ⭐ <b>The identity case needs no hand-written resolver.</b> 📄 §3.1: <i>"one shape by
        /// default — the authored DTO is an auto-generated mirror"</i>, so the resolve step IS the
        /// deserialize. ⛔ Before <c>G1</c> there was no generic deserializer at all: every behaviour
        /// hand-rolled both halves into one opaque delegate.
        /// </summary>
        // ⭐ CE-416 ③ (2026-10-02): these four rails used to pin BehaviorParams.FromJson, retired with zero production
        //   callers. Each claim is RE-HOMED onto the surviving supply, FromBlockResolver — the identity resolver below is
        //   the "no hand-written resolver" case.
        private static readonly ResolveBlock<DemoParams, DemoParams> Identity =
            static (in DemoParams authored, ref DemoParams block, EntityRepository w, Entity s) => block = authored;

        [Fact]
        public void FromBlockResolver_IdentityResolver_WritesTheDeserializedDto()
        {
            var parse = BehaviorParams.FromBlockResolver(Identity);

            byte* buffer = stackalloc byte[Marshal.SizeOf<DemoParams>()];
            parse("{\"Count\":7,\"Speed\":2.5}", buffer, Marshal.SizeOf<DemoParams>(), null!, default);

            var written = *(DemoParams*)buffer;
            Assert.Equal(7, written.Count);
            Assert.Equal(2.5f, written.Speed);
        }

        /// <summary>
        /// ⭐⭐ <b>The two halves are separable, which is what "split" means.</b> The resolver sees the
        /// DESERIALIZED value and may rewrite it — the divergence case (§3.1: geo point vs cartesian,
        /// network id vs <c>Entity</c>, derived fields) — without owning the JSON.
        /// </summary>
        [Fact]
        public void FromBlockResolver_RunsTheResolverOverTheDeserializedValue()
        {
            var parse = BehaviorParams.FromBlockResolver<DemoParams, DemoParams>(
                static (in DemoParams authored, ref DemoParams block, EntityRepository w, Entity s) =>
                {
                    Assert.Equal(7, authored.Count);         // ⭐ the resolver sees the authored value…
                    block.Speed = authored.Count * 10f;      // …and derives from it
                });

            byte* buffer = stackalloc byte[Marshal.SizeOf<DemoParams>()];
            parse("{\"Count\":7}", buffer, Marshal.SizeOf<DemoParams>(), null!, default);

            Assert.Equal(70f, ((DemoParams*)buffer)->Speed);
        }

        /// <summary>
        /// ⚠ <b>An absent payload is <c>default(TAuthored)</c>, not a failure.</b> Defaults are baked and scenario
        /// JSON only overlays them (architect-approved <c>2026-06-06</c>), so a behaviour with nothing
        /// to override supplies no JSON at all — and that must not look like a parse error. ⭐ The resolver decides
        /// what an absent intent means; the block it receives is the BAKED one, untouched until it writes.
        /// </summary>
        [Fact]
        public void FromBlockResolver_WithNoPayload_HandsTheResolverTheDefault_OverTheBakedBlock()
        {
            var parse = BehaviorParams.FromBlockResolver<DemoParams, DemoParams>(
                static (in DemoParams authored, ref DemoParams block, EntityRepository w, Entity s) =>
                {
                    Assert.Equal(0, authored.Count);         // absent ⇒ default(TAuthored)
                    Assert.Equal(99, block.Count);           // ⭐ the baked block, not cleared
                });

            byte* buffer = stackalloc byte[Marshal.SizeOf<DemoParams>()];
            *(DemoParams*)buffer = new DemoParams { Count = 99, Speed = 99f };   // the bake
            parse("", buffer, Marshal.SizeOf<DemoParams>(), null!, default);

            Assert.Equal(99, ((DemoParams*)buffer)->Count);
        }

        // ── §8: parse-before-commit ──────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ <b>A malformed payload THROWS rather than writing a zeroed region.</b>
        ///
        /// <para>
        /// ⛔ This is the rail, and it is easy to get backwards. Swallowing here would look tidier and
        /// would be wrong: <c>BehaviorIngressSystem</c> parses into a stack shadow and commits only on
        /// success, so a throw is exactly what leaves the entity <b>100% on its old behaviour</b>. A
        /// helper that returned quietly would hand the ingress a successful-looking all-zero params
        /// region — the failure the shadow copy exists to prevent.
        /// </para>
        /// </summary>
        [Fact]
        public void FromBlockResolver_MalformedPayload_Throws_SoIngressCanKeepTheOldBehaviour()
        {
            var parse = BehaviorParams.FromBlockResolver(Identity);

            byte* buffer = stackalloc byte[Marshal.SizeOf<DemoParams>()];
            Assert.ThrowsAny<Exception>(() => parse("{ not json", buffer, Marshal.SizeOf<DemoParams>(), null!, default));
        }

        // ── CE-445: the host accessor is RETIRED ─────────────────────────────

        /// <summary>
        /// ⛔ <c>CE-445</c> — <b><c>IHostVariableAccess</c> is gone, and so is the <c>host</c> argument.</b> 🔒 User,
        /// <c>2026-09-30</c>: <i>"Retire it."</i> 📐 Nothing fed its name map in production and nothing read through it;
        /// a sub-behaviour's input is exactly its bound host variable (<c>DESIGN_Parameter_Model.md</c> §P.6).
        /// <para>⚠ Inverse-edit red-proof: re-add the interface or the parameter and this reds.</para>
        /// </summary>
        [Fact]
        public void CE445_TheHostAccessorAndItsArgument_AreRetired()
        {
            var asm = typeof(ParseParamsDelegate).Assembly;
            Assert.Null(asm.GetType("Fdp.Toolkit.Behavior.IHostVariableAccess"));
            Assert.Null(asm.GetType("Fdp.Toolkit.Behavior.HsmHostVariableAccess"));
            Assert.Null(asm.GetType("Fdp.Toolkit.Behavior.HostedParamResolvers"));

            foreach (var d in new[] { typeof(ParseParamsDelegate), typeof(ResolveStageDelegate) })
            {
                var last = d.GetMethod("Invoke")!.GetParameters().Last();
                Assert.Equal(typeof(Fdp.Core.Entity), last.ParameterType);
            }
        }
    }
}
