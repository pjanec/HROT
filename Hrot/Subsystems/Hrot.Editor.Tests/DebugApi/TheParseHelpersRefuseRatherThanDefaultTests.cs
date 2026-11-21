using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// <b><c>CE-191</c> — a debug-API parse helper must REFUSE a value it cannot read, never substitute one.</b>
/// </summary>
/// <remarks>
/// <para>🔒 <b>User, <c>2026-09-05</c>:</b> the debug API's swallowed exceptions <i>"need to be surfaced as
/// error response"</i>.</para>
///
/// <para>🔴 <b>The shape of the defect these pin.</b> Each helper below used to answer a malformed input
/// with a <i>plausible</i> value — <c>0f</c> for a number, a shorter list for an array — and the endpoint
/// then reported <c>ok:true</c>. That is strictly worse than an exception: the caller is told the operation
/// succeeded, and the wrongness only shows up later, somewhere else, as a node at the origin or a delete
/// that removed the wrong set.</para>
///
/// <para>⚠ <b>Every rail here also pins the NON-regression half</b> — an ABSENT optional key must still be
/// legal. The failure mode of over-correcting this is an API that refuses bodies it used to accept, which
/// would be a worse outcome than the bug.</para>
///
/// <para>⭐ Reaches the private helpers by reflection, the convention this folder's
/// <c>TheCommandRouteCoversTheWholeUnionTests</c> already uses, so production accessibility is not widened
/// for the sake of a test.</para>
/// </remarks>
public sealed class TheParseHelpersRefuseRatherThanDefaultTests
{
    private static readonly Assembly EditorAsm = typeof(Hrot.Editor.DebugApi.DebugApiService).Assembly;

    private static MethodInfo Method(string typeName, string method)
    {
        Type t = EditorAsm.GetType($"Hrot.Editor.DebugApi.{typeName}", throwOnError: true)!;
        MethodInfo? m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(m != null, $"{typeName}.{method} not found — the rail is looking at the wrong symbol.");
        return m!;
    }

    // ── ① DebugApiService.Authoring — TryReadFloat ────────────────────────────

    /// <summary>
    /// 🔴 Was <c>catch { return 0f; }</c>. A node asked for at (300, 240) with a malformed <c>x</c> was
    /// created at the ORIGIN, and the response said it worked.
    /// </summary>
    [Fact]
    public void AnUnparseableFloatIsRefused_NotSilentlyZero()
    {
        MethodInfo m = Method("DebugApiService", "TryReadFloat");

        var body = new JsonObject { ["x"] = "three hundred" };   // a string where a number belongs
        object?[] args = { body, "x", 0f, null };

        bool ok = (bool)m.Invoke(null, args)!;

        Assert.False(ok);
        Assert.Equal(0f, (float)args[2]!);                       // the value is untouched...
        var error = (string?)args[3];
        Assert.NotNull(error);                                   // ...but it is REPORTED, which is the fix
        Assert.Contains("'x'", error!);
        Assert.Contains("number", error);
    }

    /// <summary>⛔ The non-regression half: an absent coordinate is legal and still means 0.</summary>
    [Fact]
    public void AnAbsentFloatIsStillLegalAndZero()
    {
        MethodInfo m = Method("DebugApiService", "TryReadFloat");

        object?[] args = { new JsonObject(), "x", 0f, null };
        bool ok = (bool)m.Invoke(null, args)!;

        Assert.True(ok);
        Assert.Equal(0f, (float)args[2]!);
        Assert.Null(args[3]);
    }

    // ── ② DebugApiService.Authoring — TryReadGuidList ─────────────────────────

    /// <summary>
    /// 🔴 <b>The worst of the set.</b> The only caller is the REMOVE route, and it used to drop
    /// unparseable ids — so a body naming five nodes with one typo <b>deleted the other four</b> and
    /// answered <c>ok:true</c>. ⭐ The caller's own next guard already says <i>"a partial delete would be
    /// worse than a refusal"</i>; that rule simply could not see an id that failed to PARSE.
    /// </summary>
    [Fact]
    public void OneBadGuidRefusesTheWholeList_NoPartialDelete()
    {
        MethodInfo m = Method("DebugApiService", "TryReadGuidList");

        var good = Guid.NewGuid();
        var body = new JsonObject
        {
            ["nodes"] = new JsonArray(good.ToString(), "not-a-guid", Guid.NewGuid().ToString()),
        };

        object?[] args = { body, "nodes", null, null };
        bool ok = (bool)m.Invoke(null, args)!;

        Assert.False(ok);

        var error = (string?)args[3];
        Assert.NotNull(error);
        Assert.Contains("nodes[1]", error!);          // it names WHICH element
        Assert.Contains("partial delete", error);     // and the consequence it refused

        // ⭐ The decisive assertion: the two VALID ids were not quietly kept and acted on.
        var list = (List<Guid>?)args[2];
        Assert.True(list == null || list.Count < 3,
            "a refused list must not be handed back as a usable, shorter set");
    }

    /// <summary>⛔ Non-regression: a well-formed list still parses, and an absent key is still legal.</summary>
    [Fact]
    public void AWellFormedGuidListStillParses()
    {
        MethodInfo m = Method("DebugApiService", "TryReadGuidList");

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var body = new JsonObject { ["nodes"] = new JsonArray(a.ToString(), b.ToString()) };

        object?[] args = { body, "nodes", null, null };
        Assert.True((bool)m.Invoke(null, args)!);
        Assert.Null(args[3]);
        Assert.Equal(new List<Guid> { a, b }, (List<Guid>)args[2]!);

        object?[] absent = { new JsonObject(), "nodes", null, null };
        Assert.True((bool)m.Invoke(null, absent)!);
        Assert.Empty((List<Guid>)absent[2]!);
    }

    // ── ③ GraphCommandJson — F ────────────────────────────────────────────────

    /// <summary>
    /// 🔴 Was <c>catch { return 0f; }</c>, and it feeds <c>Vec2</c> and <c>Vec4</c> — so a malformed
    /// coordinate silently became the origin and a malformed colour channel silently became black or
    /// fully transparent.
    /// ⭐⭐ <b>This one was an inconsistency, not a policy:</b> every other reader in that file —
    /// <c>Bool</c>, <c>Int</c>, <c>Ints</c>, <c>Guid_</c>, <c>Guids</c> — already threw
    /// <c>CommandJsonException</c> on exactly this input.
    /// </summary>
    [Fact]
    public void AnUnparseableFloatChannelThrowsLikeEveryOtherReaderInThatFile()
    {
        MethodInfo m = Method("GraphCommandJson", "F");

        var o = new JsonObject { ["x"] = "left-ish" };

        var ex = Assert.Throws<TargetInvocationException>(() => m.Invoke(null, new object?[] { o, "x" }));
        Assert.NotNull(ex.InnerException);
        Assert.Equal("CommandJsonException", ex.InnerException!.GetType().Name);
        Assert.Contains("'x'", ex.InnerException.Message);
        Assert.Contains("number", ex.InnerException.Message);
    }

    /// <summary>⛔ Non-regression: an absent channel is still 0 — these are genuinely optional.</summary>
    [Fact]
    public void AnAbsentFloatChannelIsStillZero()
    {
        MethodInfo m = Method("GraphCommandJson", "F");
        Assert.Equal(0f, (float)m.Invoke(null, new object?[] { new JsonObject(), "x" })!);
    }
}
