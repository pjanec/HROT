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
        /// ⭐ <c>BehaviorParams.FromJson</c> does NOT count as a second mechanism and that is the point
        /// of its shape: it is a FACTORY that returns the same <see cref="ParseParamsDelegate"/>, so
        /// the split exists without the ingress learning a second path.
        /// </para>
        /// </summary>
        [Fact]
        public void BehaviorDefinition_CarriesExactlyOneParameterSupplyDelegate()
        {
            var delegateMembers = typeof(BehaviorDefinition)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => typeof(Delegate).IsAssignableFrom(p.PropertyType))
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
        [Fact]
        public void FromJson_WithNoResolver_WritesTheDeserializedDto()
        {
            var parse = BehaviorParams.FromJson<DemoParams>();

            byte* buffer = stackalloc byte[Marshal.SizeOf<DemoParams>()];
            parse("{\"Count\":7,\"Speed\":2.5}", buffer, null!, default, null);

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
        public void FromJson_RunsTheResolverOverTheDeserializedValue()
        {
            var parse = BehaviorParams.FromJson<DemoParams>(
                static (ref DemoParams dto, EntityRepository w, Entity s, IHostVariableAccess? h) =>
                {
                    Assert.Equal(7, dto.Count);      // ⭐ the resolver sees the authored value…
                    dto.Speed = dto.Count * 10f;     // …and derives from it
                });

            byte* buffer = stackalloc byte[Marshal.SizeOf<DemoParams>()];
            parse("{\"Count\":7}", buffer, null!, default, null);

            Assert.Equal(70f, ((DemoParams*)buffer)->Speed);
        }

        /// <summary>
        /// ⚠ <b>An absent payload is <c>default</c>, not a failure.</b> Defaults are baked and scenario
        /// JSON only overlays them (architect-approved <c>2026-06-06</c>), so a behaviour with nothing
        /// to override supplies no JSON at all — and that must not look like a parse error.
        /// </summary>
        [Fact]
        public void FromJson_WithNoPayload_WritesTheDefault()
        {
            var parse = BehaviorParams.FromJson<DemoParams>();

            byte* buffer = stackalloc byte[Marshal.SizeOf<DemoParams>()];
            *(DemoParams*)buffer = new DemoParams { Count = 99, Speed = 99f };   // pre-dirty the region
            parse("", buffer, null!, default, null);

            Assert.Equal(0, ((DemoParams*)buffer)->Count);
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
        public void FromJson_MalformedPayload_Throws_SoIngressCanKeepTheOldBehaviour()
        {
            var parse = BehaviorParams.FromJson<DemoParams>();

            byte* buffer = stackalloc byte[Marshal.SizeOf<DemoParams>()];
            Assert.ThrowsAny<Exception>(() => parse("{ not json", buffer, null!, default, null));
        }

        // ── §3.4: the host argument is present and always null for a root ────

        /// <summary>
        /// ⭐ <b>The host parameter exists NOW so it is never added twice.</b> ⚠ Asserted on the
        /// delegate's signature rather than on behaviour, because there is no behaviour yet:
        /// <see cref="IHostVariableAccess"/> is declared and deliberately unimplemented, and every
        /// caller passes <c>null</c> until <c>E7a</c>.
        /// </summary>
        [Fact]
        public void ParseParamsDelegate_CarriesTheHostArgument()
        {
            var invoke = typeof(ParseParamsDelegate).GetMethod("Invoke")!;
            var last   = invoke.GetParameters().Last();

            Assert.Equal(typeof(IHostVariableAccess), last.ParameterType);
            Assert.Equal("host", last.Name);
        }

        /// <summary>
        /// ⛔ <b><c>IHostVariableAccess</c> has NO implementation, on purpose.</b> ⚠ If this ever goes
        /// red it means <c>E7a</c> started early — in which case the ingress must start passing a real
        /// instance, and this test is the reminder that the two go together.
        /// </summary>
        [Fact]
        public void IHostVariableAccess_IsDeclaredButNotYetImplemented()
        {
            var implementers = typeof(IHostVariableAccess).Assembly.GetTypes()
                .Where(t => !t.IsInterface && typeof(IHostVariableAccess).IsAssignableFrom(t))
                .Select(t => t.FullName)
                .ToList();

            Assert.True(implementers.Count == 0,
                "IHostVariableAccess gained an implementation without the ingress supplying one: "
                + string.Join(", ", implementers));
        }
    }
}
