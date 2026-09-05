using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Tests.Builders;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// FC-2/LV-5 -- debugger/watch visibility for fixed-list variables:
/// <list type="bullet">
///   <item>the generated registrar's <c>StateFields</c> now INCLUDES list fields (qualified
///   nested wrapper CLR type, runtime <c>Marshal.OffsetOf</c> offset/size);</item>
///   <item><see cref="BlueprintDebugSession.MarshalFromBytes"/> renders the wrapper bytes as
///   <c>List&lt;Elem&gt;[N] Count=k {e0, e1, …}</c>, discovering Count/Items/element type and
///   capacity purely by reflection (works on ALC-loaded generated types), with the shown
///   element window F2-clamped to <c>min(max(Count,0), N)</c>.</item>
/// </list>
/// </summary>
public sealed class ListVariableWatchTests
{
    // A hand-authored twin of the generated wrapper shape (same names, layout, and attributes
    // the compiler emits) so the formatter is testable without an ALC load.
    [InlineArray(4)]
    private struct __Buf_System_Int32_4 { private int _e0; }

    [StructLayout(LayoutKind.Sequential)]
    private struct __List_System_Int32_4
    {
        public int Count;
        public __Buf_System_Int32_4 Items;
    }

    private static byte[] ListBytes(int count, params int[] items)
    {
        var bytes = new byte[Marshal.SizeOf<__List_System_Int32_4>()];
        BitConverter.GetBytes(count).CopyTo(bytes, 0);
        for (int i = 0; i < items.Length; i++)
            BitConverter.GetBytes(items[i]).CopyTo(bytes, 4 + i * 4);
        return bytes;
    }

    [Fact]
    public void TryFormatFixedList_RendersHeaderCountAndClampedElements()
    {
        Assert.True(BlueprintDebugSession.TryFormatFixedList(
            ListBytes(2, 5, 7), typeof(__List_System_Int32_4), out var s));
        Assert.Equal("List<Int32>[4] Count=2 {5, 7}", s);
    }

    [Fact]
    public void TryFormatFixedList_GarbageCount_NeverReadsOutOfRange()
    {
        // Overflowing Count (stale/garbage bytes): window clamps to capacity, raw Count shown.
        Assert.True(BlueprintDebugSession.TryFormatFixedList(
            ListBytes(99, 1, 2, 3, 4), typeof(__List_System_Int32_4), out var over));
        Assert.Equal("List<Int32>[4] Count=99 {1, 2, 3, 4}", over);

        // Negative Count: zero elements shown.
        Assert.True(BlueprintDebugSession.TryFormatFixedList(
            ListBytes(-5, 1, 2), typeof(__List_System_Int32_4), out var neg));
        Assert.Equal("List<Int32>[4] Count=-5 {}", neg);
    }

    [Fact]
    public void TryFormatFixedList_NonWrapperTypes_Refused()
    {
        Assert.False(BlueprintDebugSession.TryFormatFixedList(
            new byte[8], typeof(long), out _));
        Assert.False(BlueprintDebugSession.TryFormatFixedList(
            new byte[8], typeof(Guid), out _));
    }

    [Fact]
    public void MarshalFromBytes_RoutesWrapperTypeToListRendering()
    {
        var raw = BlueprintDebugSession.MarshalFromBytes(
            ListBytes(1, 42), typeof(__List_System_Int32_4));
        Assert.Equal("List<Int32>[4] Count=1 {42}", raw);
    }

    // ---- end-to-end: real generated assembly + registrar StateFields --------

    private static CompileOptions Options() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private delegate void SpanAction(Span<byte> bytes);

    [Fact]
    public void GeneratedListVariable_DescriptorVisible_And_WatchRendersLiveState()
    {
        var asset = BlueprintAssetBuilder.Instance("ListWatchBp")
            .WithVariable("MyList", typeof(int), "0")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Variables[0].Type = new BlueprintTypeRef { TypeId = "System.Int32", Capacity = 4, InitialLength = 2 };

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var assembly = fixture.CompileAndLoad(asset);

        // LV-5: the staged definition's StateFields now carries the list field, typed as the
        // generated nested wrapper, at the runtime-derived offset.
        Assert.True(fixture.Registry.TryGetById(
            BlueprintIdHash.Compute(asset.AssetId), out var def));
        Assert.True(def!.StateFields.TryGetValue("MyList", out var fd));
        Assert.Equal("__List_System_Int32_4", fd.ClrType.Name);

        var bpClass = assembly.GetTypes().Single(t => t.Name.EndsWith("_Bp") && t.GetNestedType("State") != null);
        var state = bpClass.GetNestedType("State")!;
        int listOffset = (int)Marshal.OffsetOf(state, "MyList");
        Assert.Equal(listOffset, fd.OffsetBytes);

        // Live state: InitDefault seeds Count=2, poke Items[0..1], then render through the
        // exact slice-by-descriptor path the watch uses.
        var bytes = new byte[Marshal.SizeOf(state) + 64];
        var init = (SpanAction)Delegate.CreateDelegate(typeof(SpanAction), bpClass.GetMethod("InitDefault")!);
        init(bytes);
        BitConverter.GetBytes(11).CopyTo(bytes, listOffset + 4);
        BitConverter.GetBytes(22).CopyTo(bytes, listOffset + 8);

        var raw = BlueprintDebugSession.MarshalFromBytes(
            bytes[fd.OffsetBytes..(fd.OffsetBytes + fd.SizeBytes)], fd.ClrType);
        Assert.Equal("List<Int32>[4] Count=2 {11, 22}", raw);
    }
}
