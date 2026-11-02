using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Fdp.Toolkit.Orchestration;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkits.Tests.Replication
{
    /// <summary>⭐ S0 item 6 — the ONE storage mapping every type-switch site routes through (<c>SemanticValueTypes</c>).</summary>
    public sealed class SemanticValueTypesTests
    {
        [Fact]
        public void StorageTypeOf_MapsTheThreeSemanticTypes_AndLeavesEveryOtherTypeAlone()
        {
            Assert.Equal(typeof(Vector3), SemanticValueTypes.StorageTypeOf(typeof(WorldPoint)));
            Assert.Equal(typeof(Quaternion), SemanticValueTypes.StorageTypeOf(typeof(WorldRotation)));
            Assert.Equal(typeof(float), SemanticValueTypes.StorageTypeOf(typeof(Bearing)));
            Assert.Equal(typeof(long), SemanticValueTypes.StorageTypeOf(typeof(TkbTypeId)));   // ⭐ CE-4200 SV-2
            Assert.Equal(typeof(int), SemanticValueTypes.StorageTypeOf(typeof(NodeId)));       // ⭐ CE-4200 SV-3
            Assert.Equal(typeof(Vector3), SemanticValueTypes.StorageTypeOf(typeof(Vector3)));
            Assert.Equal(typeof(string), SemanticValueTypes.StorageTypeOf(typeof(string)));
        }

        [Fact]
        public void ToStorage_And_FromStorage_RoundTrip_EachSemanticValue()
        {
            Assert.Equal(new Vector3(1, 2, 3), SemanticValueTypes.ToStorage(new WorldPoint(1, 2, 3)));
            Assert.Equal(new WorldPoint(1, 2, 3), SemanticValueTypes.FromStorage(new Vector3(1, 2, 3), typeof(WorldPoint)));
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f);
            Assert.Equal(q, SemanticValueTypes.ToStorage(new WorldRotation(q)));
            Assert.Equal(new WorldRotation(q), SemanticValueTypes.FromStorage(q, typeof(WorldRotation)));
            Assert.Equal(45f, SemanticValueTypes.ToStorage(new Bearing(45f)));
            Assert.Equal(new Bearing(45f), SemanticValueTypes.FromStorage(45f, typeof(Bearing)));
            Assert.Equal(101L, SemanticValueTypes.ToStorage(new TkbTypeId(101)));              // ⭐ CE-4200 SV-2
            Assert.Equal(7, SemanticValueTypes.ToStorage(new NodeId(7)));                      // ⭐ CE-4200 SV-3
            Assert.Equal(new TkbTypeId(101), SemanticValueTypes.FromStorage(101L, typeof(TkbTypeId)));
            Assert.Equal(new NodeId(7), SemanticValueTypes.FromStorage(7, typeof(NodeId)));
        }

        [Fact]
        public void AnythingElse_PassesThroughUnchanged()
        {
            Assert.Null(SemanticValueTypes.ToStorage(null));
            Assert.Equal(7, SemanticValueTypes.ToStorage(7));
            Assert.Equal(new Vector3(1, 2, 3), SemanticValueTypes.FromStorage(new Vector3(1, 2, 3), typeof(Vector3)));
            Assert.Equal(new WorldPoint(1, 2, 3), SemanticValueTypes.FromStorage(new WorldPoint(1, 2, 3), typeof(WorldPoint)));
        }

        // ⭐ CE-4209 — the netstandard2.0 side cannot reference Fdp.Core, so it names the semantic types by STRING. A rename would break
        //   them silently; this reads those sources and resolves every Fdp.Core.<Name> literal against the real assembly.
        private static readonly string[] StringNamingSites =
        {
            "Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs",
            "Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/StaticTypeRegistry.cs",
            "FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/MiniEditors/PinDefaultValueEditorRegistry.cs",
        };

        private static System.Collections.Generic.HashSet<string> FdpCoreLiterals(string relPath)
        {
            var root = ContentLayout.TryFindSourceTree(AppContext.BaseDirectory);
            Assert.NotNull(root);
            var text = string.Join("\n", File.ReadAllLines(Path.Combine(root!, relPath)).Where(l => !l.TrimStart().StartsWith("//")));   // code lines only: a quoted name in a doc comment is not a use
            var found = Regex.Matches(text, "\"(Fdp\\.Core\\.[A-Za-z0-9]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
            Assert.NotEmpty(found);
            return found;
        }

        [Fact]
        public void EveryFdpCoreNameLiteral_OnTheStringSide_ResolvesToARealType()
        {
            var asm = typeof(WorldPoint).Assembly;
            foreach (var site in StringNamingSites)
                foreach (var name in FdpCoreLiterals(site))
                    Assert.True(asm.GetType(name) != null, $"{site} names \"{name}\" but Fdp.Core has no such type (renamed?)");
        }

        [Fact]
        public void EverySemanticNameLiteral_InTheValidatorAndThePinEditors_IsASemanticType()
        {
            var asm = typeof(WorldPoint).Assembly;
            foreach (var site in new[] { StringNamingSites[0], StringNamingSites[2] })
                foreach (var name in FdpCoreLiterals(site))
                    Assert.True(SemanticValueTypes.IsSemantic(asm.GetType(name)!), $"{site} treats \"{name}\" as a semantic value type, but SemanticValueTypes.IsSemantic says it is not");
        }

        [Fact]
        public void TheStaticTypeRegistry_HasAKeyForEverySemanticType()
        {
            var keys = FdpCoreLiterals(StringNamingSites[1]);
            foreach (var t in new[] { typeof(WorldPoint), typeof(WorldRotation), typeof(Bearing), typeof(TkbTypeId), typeof(NodeId) })
            {
                Assert.True(SemanticValueTypes.IsSemantic(t));
                Assert.True(keys.Contains(t.FullName!), $"StaticTypeRegistry has no \"{t.FullName}\" key");
            }
        }
    }
}
