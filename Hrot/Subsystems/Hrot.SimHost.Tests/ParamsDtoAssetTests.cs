using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2136</c> (B4a, docs/DESIGN_Orders_Params_Entity_Classes.md §12) — a params DTO AUTHORED AS AN ASSET
    /// (<c>Assets/Params/Tests/T41Params.params.json</c>) used by a behaviour end to end: generated at build into a <c>[ParamsDto]</c>
    /// struct, listed by <see cref="ParamsDtoRegistry"/>, published as <c>T41_GeneratedParamsDto</c>'s contract, and its JSON lands flat
    /// over the asset's baked default — the same path as a hand-written DTO (B3a).
    /// </summary>
    public sealed class ParamsDtoAssetTests
    {
        private const string Dto = "Hrot.AI.Behaviors.Params.T41Params";

        private static Type Generated()
        {
            var t = typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly.GetType(Dto);
            Assert.True(t is not null, $"the generator must have emitted {Dto} into Hrot.AI.Behaviors");
            return t!;
        }

        /// <summary>⭐ CE-3194 (R-3002) — the asset's defaults are the generated struct's field INITIALISERS: <c>new T()</c> carries
        /// them (the attribute no longer does). 🔴 Red-proof: drop the parameterless constructor from <c>ParamsDtoEmitCore.Emit</c> → the
        /// build fails (CS8983), and without the initialisers <c>Range</c> reads 0.</summary>
        [Fact]
        public void CE2136_TheGeneratedDto_IsAParamsDto_ListedByTheRegistry_WithItsDefault()
        {
            var t = Generated();
            Assert.NotNull(Attribute.GetCustomAttribute(t, typeof(ParamsDtoAttribute)));
            object made = ParamsDefaults.DefaultOf(t)!;
            Assert.Equal(25f, t.GetField("Range")!.GetValue(made));
            Assert.Equal(new System.Numerics.Vector3(10, 20, 0), t.GetField("Home")!.GetValue(made));
            Assert.Equal(2, t.GetField("Loops")!.GetValue(made));
            Assert.Equal(false, t.GetField("Alerted")!.GetValue(made));

            // ⭐ CE-3194 — what the editor shows (U5b): the DTO's default with the asset's overrides on top, keys case-insensitive.
            object eff = ParamsDefaults.Effective(t, "{\"range\":30}")!;
            Assert.Equal(30f, t.GetField("Range")!.GetValue(eff));
            Assert.Equal(2, t.GetField("Loops")!.GetValue(eff));

            var registry = ParamsDtoRegistry.Scan(new[] { t.Assembly, typeof(ParamsDtoRegistry).Assembly });
            Assert.Contains(registry.Dtos, d => d.Type == t);
        }

        /// <summary>⭐ CE-3185 (P1, F1) — the registry records each params DTO's ORIGIN: a generated one its asset's id (from
        /// <c>T41Params.params.json</c>), a hand-written one its C# file and line. 🔴 Red-proof: drop the <c>AssetId</c> emit from
        /// <c>ParamsDtoEmitCore.Emit</c> → the generated entry's origin is the generated file and this fails.</summary>
        [Fact]
        public void CE3185_TheRegistryRecordsEachDtosOrigin_AssetOrSourceLine()
        {
            var registry = ParamsDtoRegistry.Pinning(typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly);

            Assert.True(registry.TryGetDto(Generated(), out var gen));
            Assert.True(gen.Origin.IsAsset);
            Assert.Equal("ee7e86f5-6718-4210-ae07-c8e13de9d8df", gen.Origin.AssetId);
            Assert.True((gen.Purposes & DtoPurpose.Params) != 0 && (gen.Purposes & DtoPurpose.Variable) != 0);

            Assert.True(registry.TryGetDto(typeof(MoveToLocationParamsJsonDto), out var hand));
            Assert.False(hand.Origin.IsAsset);
            Assert.EndsWith("MoveToLocationParamsJsonDto.cs", hand.Origin.SourceFile!.Replace('\\', '/'));
            Assert.True(hand.Origin.SourceLine > 0);

            Assert.True(registry.TryGetDto(typeof(Hrot.AI.Behaviors.Orders.MoveOrder), out var order));
            Assert.Equal("Move", order.OrderName);
            Assert.Equal(DtoPurpose.Variable | DtoPurpose.Params | DtoPurpose.Order, order.Purposes);
            Assert.EndsWith("OrderDtos.cs", order.Origin.SourceFile!.Replace('\\', '/'));
        }

        /// <summary>The CLR layout of the generated struct is the sequential one the sibling generators computed from the JSON
        /// (float@0, Vector3@4, int@16, bool@20, 24 bytes) — if the two disagreed, a <c>Params.&lt;field&gt;</c> binding would read the
        /// wrong bytes.</summary>
        [Fact]
        public unsafe void CE2136_TheGeneratedStruct_HasTheLayoutTheGeneratorsComputed()
        {
            var t = Generated();
            Assert.Equal(24, (int)typeof(System.Runtime.CompilerServices.Unsafe).GetMethod("SizeOf", Type.EmptyTypes)!
                .MakeGenericMethod(t).Invoke(null, null)!);
            Assert.Equal(new[] { "Range", "Home", "Loops", "Alerted" },
                t.GetFields().Select(f => f.Name).ToArray());
        }

        /// <summary>⭐⭐ CE-3194 (R-3001) end to end: <c>T41_GeneratedParamsDto</c> OVERRIDES only <c>Range</c> (30) in its
        /// <c>ParamsDefaultJson</c>; <c>Home</c> and <c>Loops</c> come from the DTO's initialisers; the order's JSON lands on top.
        /// 🔴 Red-proof: bake the asset JSON over zeroed memory (the pre-CE-3194 <c>Deserialize</c> of a struct with no constructor)
        /// → <c>Home</c> / <c>Loops</c> read 0.</summary>
        [Fact]
        public unsafe void CE2136_ABehaviourTakesTheGeneratedDto_FlatOverItsBakedDefault()
        {
            var t = Generated();
            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            Assert.True(registry.TryGetId("T41_GeneratedParamsDto", out int id));
            Assert.True(registry.TryGetDefinition(id, out var def) && def is not null);
            Assert.Equal(t, def!.JsonParamsDtoType);

            using var repo = new EntityRepository();
            var block = new byte[RootParamsAccess.RootParamsBytes(def)];
            fixed (byte* p = block)
            {
                def.BakeDefaults?.Invoke(p, block.Length);
                def.ParseParams!("{\"range\":40,\"Alerted\":true}", p, block.Length, repo, Entity.Null);
            }
            Assert.Equal(40f, BitConverter.ToSingle(block, 0));                      // sent (case-insensitive)
            Assert.Equal(10f, BitConverter.ToSingle(block, 4));                      // Home.X — the DTO's initialiser (not overridden)
            Assert.Equal(20f, BitConverter.ToSingle(block, 8));
            Assert.Equal(2, BitConverter.ToInt32(block, 16));                        // Loops — the DTO's initialiser (not overridden)
            Assert.Equal(1, block[20]);                                              // Alerted — sent

            // No order JSON: the asset's override (Range 30) over the DTO's initialisers.
            var bare = new byte[block.Length];
            fixed (byte* p = bare)
            {
                def.BakeDefaults?.Invoke(p, bare.Length);
                def.ParseParams!("", p, bare.Length, repo, Entity.Null);
            }
            Assert.Equal(30f, BitConverter.ToSingle(bare, 0));                       // the asset's override
            Assert.Equal(10f, BitConverter.ToSingle(bare, 4));                       // the DTO's initialiser
            Assert.Equal(2, BitConverter.ToInt32(bare, 16));
        }
    }
}
