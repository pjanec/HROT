using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Tests.Replication
{
    /// <summary>
    /// ⭐ <c>DESIGN_Entity_Reference.md</c> — the <see cref="EntityRef"/> type: its JSON form (D1), its resolve (D2) and the
    /// one remap plan's two appliers over lists and nested values (D3).
    /// </summary>
    public sealed class EntityRefTests
    {
        private static readonly Dictionary<long, long> Map = new() { [1006] = 1001, [7] = 70, [8] = 80 };

        // ── D1: JSON is the bare number ──────────────────────────────────────────────────────────────────────────

        private sealed class Holder { public EntityRef Target { get; set; } }

        [Theory]
        [InlineData("{\"Target\":1001}", 1001)]
        [InlineData("{\"Target\":null}", 0)]
        [InlineData("{\"Target\":\"1001\"}", 1001)]
        [InlineData("{\"Target\":{\"NetworkId\":1001}}", 1001)]
        [InlineData("{}", 0)]
        public void D1_ReadsEveryForm(string json, long expected)
            => Assert.Equal(expected, JsonSerializer.Deserialize<Holder>(json)!.Target.NetworkId);

        /// <summary>⭐ A file written when the field was a <c>long</c> loads unchanged, and saving writes the same number back.</summary>
        [Fact]
        public void D1_WritesTheBareNumber_SoALongFieldsFileRoundTrips()
            => Assert.Equal("{\"Target\":1001}", JsonSerializer.Serialize(new Holder { Target = new EntityRef(1001) }));

        // ── D2: resolve through the one stale-checked resolver ───────────────────────────────────────────────────

        [Fact]
        public void D2_ResolvesToTheEntityCarryingThatNetworkId_AndOfIsTheInverse()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new NetworkIdentity(1001));

            Assert.Equal(e, new EntityRef(1001).Resolve(repo));
            Assert.True(new EntityRef(1002).Resolve(repo).IsNull);
            Assert.True(EntityRef.None.Resolve(repo).IsNull);
            Assert.Equal(new EntityRef(1001), EntityRef.Of(repo, e));
        }

        // ── D3: the object applier (the scenario genesis intents' shapes) ────────────────────────────────────────

        private struct Entry { public EntityRef NetworkId; public float Score; }

        private sealed class IntentShape
        {
            public EntityRef Single { get; set; }
            public List<EntityRef> Many { get; set; } = new();
            public List<Entry> Entries { get; set; } = new();
            public Entry Inline { get; set; }
            public long NotARef { get; set; }
        }

        /// <summary>
        /// ⭐⭐ One plan rewrites a reference, a list of them, a list of STRUCTS holding them and a nested struct — in place —
        /// and never touches a bare <c>long</c>. 🔴 These are the six <c>Initial*Intent</c> shapes the extractor hand-coded.
        /// </summary>
        [Fact]
        public void D3_RemapObject_ReachesEveryShape_AndOnlyReferences()
        {
            var o = new IntentShape
            {
                Single  = new EntityRef(1006),
                Many    = { new EntityRef(7), new EntityRef(9) },
                Entries = { new Entry { NetworkId = new EntityRef(8), Score = 0.5f } },
                Inline  = new Entry { NetworkId = new EntityRef(7) },
                NotARef = 1006,
            };

            Assert.True(EntityRefRemap.RemapObject(o, Map));

            Assert.Equal(1001, o.Single.NetworkId);
            Assert.Equal(new long[] { 70, 9 }, o.Many.ConvertAll(r => r.NetworkId));
            Assert.Equal(80, o.Entries[0].NetworkId.NetworkId);
            Assert.Equal(0.5f, o.Entries[0].Score);
            Assert.Equal(70, o.Inline.NetworkId.NetworkId);
            Assert.Equal(1006, o.NotARef);
        }

        [Fact]
        public void D3_RemapObject_NothingToRewrite_ReportsNoChange()
            => Assert.False(EntityRefRemap.RemapObject(new IntentShape { Single = new EntityRef(5) }, Map));

        /// <summary>⭐ The JSON applier reaches arrays of references and arrays of objects too; other bytes stay as authored.</summary>
        [Fact]
        public void D3_RemapJson_ReachesArrays()
        {
            const string json = "{\"Many\":[7,9],\"Entries\":[{\"NetworkId\":8,\"Score\":0.50}],\"NotARef\":1006}";
            string? result = EntityRefRemap.CompileJson(typeof(IntentShape))(json, Map);
            Assert.Equal("{\"Many\":[70,9],\"Entries\":[{\"NetworkId\":80,\"Score\":0.50}],\"NotARef\":1006}", result);
        }
    }
}
