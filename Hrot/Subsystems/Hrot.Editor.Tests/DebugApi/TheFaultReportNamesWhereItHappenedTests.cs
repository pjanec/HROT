using System;
using System.Text.Json.Nodes;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// <b><c>CE-190</c> — a 500 from the debug API must say WHERE the exception happened.</b>
/// </summary>
/// <remarks>
/// <para>🔒 <b>User, <c>2026-09-05</c>:</b> <i>"ex.message in mcp response is not enough, can we add source
/// tracking (where the exception happened?)"</i></para>
///
/// <para>⭐ These pin the properties an MCP caller depends on to locate a failure without a debugger: the
/// exception TYPE, the throw SITE, and — the one that actually decides usefulness here — that a fault raised
/// on the main thread reports the site of the BUG rather than the site of the <c>await</c>.</para>
/// </remarks>
public sealed class TheFaultReportNamesWhereItHappenedTests
{
    // ── the throwers ──────────────────────────────────────────────────────────
    // Named methods, so a rail can assert the site names the method that actually threw.

    private static void ThrowsNullReference()
        => throw new NullReferenceException("Object reference not set to an instance of an object.");

    private static void ThrowsWithInnerCause()
    {
        try { ThrowsNullReference(); }
        catch (Exception inner) { throw new InvalidOperationException("outer wrapper", inner); }
    }

    private static Exception Caught(Action act)
    {
        try { act(); }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("the thrower did not throw — this rail is vacuous");
    }

    // ── ① the one-line string, which is all some callers ever see ─────────────

    /// <summary>
    /// <c>ai-debug-mcp/src/index.mjs:215</c> does <c>const msg = envelope?.error</c> and that string becomes
    /// the <c>McpToolError</c> message. ⇒ it must carry the type and the location on its own; the bare
    /// <c>ex.Message</c> it replaced named none of the three.
    /// </summary>
    [Fact]
    public void OneLineNamesTheTypeAndTheSite_NotJustTheMessage()
    {
        string line = DebugApiFault.OneLine(Caught(ThrowsNullReference));

        Assert.Contains("System.NullReferenceException", line);          // the TYPE
        Assert.Contains("Object reference not set", line);               // the message survives
        Assert.Contains(nameof(ThrowsNullReference), line);              // the METHOD that threw

        // The whole point: strictly more than the message alone.
        Assert.NotEqual("Object reference not set to an instance of an object.", line);
    }

    // ── ② the structured field, which reaches the agent verbatim ──────────────

    /// <summary>
    /// <c>index.mjs:296</c> spreads the whole envelope into the tool's error payload, so this object arrives
    /// at the caller with no node-side change.
    /// </summary>
    [Fact]
    public void DescribeCarriesTypeSiteAndFrames()
    {
        JsonObject fault = DebugApiFault.Describe(Caught(ThrowsNullReference));

        Assert.Equal("System.NullReferenceException", (string?)fault["type"]);
        Assert.Contains(nameof(ThrowsNullReference), (string?)fault["site"]);

        var frames = Assert.IsType<JsonArray>(fault["frames"]);
        Assert.NotEmpty(frames);
        Assert.Contains(nameof(ThrowsNullReference), frames[0]!.ToString());
    }

    // ── ③ the case that decides whether this is useful at all ─────────────────

    /// <summary>
    /// <b>Every fault raised on the main thread arrives wrapped.</b> <c>MainThreadJobQueue</c> completes its
    /// jobs with <c>TrySetException</c>, so awaiting one surfaces an <see cref="AggregateException"/>.
    /// ⛔ Reporting the aggregate's own site would point at the await — true, and useless. ⭐ The report must
    /// name the inner throw.
    /// </summary>
    [Fact]
    public void ASingleInnerAggregateReportsTheRealThrowSite_NotTheAwait()
    {
        var wrapped = new AggregateException(Caught(ThrowsNullReference));

        string line = DebugApiFault.OneLine(wrapped);
        JsonObject fault = DebugApiFault.Describe(wrapped);

        Assert.Contains(nameof(ThrowsNullReference), line);
        Assert.Contains("System.NullReferenceException", line);
        Assert.DoesNotContain("AggregateException:", line);   // not reported AS the aggregate

        Assert.Equal("System.NullReferenceException", (string?)fault["type"]);
        Assert.Contains(nameof(ThrowsNullReference), (string?)fault["site"]);

        // ⚠ The wrapper is not discarded — that a call crossed a Task boundary changes where you look.
        Assert.Contains("AggregateException", (string?)fault["wrappedIn"]);
    }

    // ── ④ the inner chain ─────────────────────────────────────────────────────

    /// <summary>The origin is the INNERMOST cause; the chain above it is still reported.</summary>
    [Fact]
    public void TheOriginIsTheInnermostCauseAndTheChainIsKept()
    {
        JsonObject fault = DebugApiFault.Describe(Caught(ThrowsWithInnerCause));

        // Reported AS the root cause, not as the wrapper the catch block happened to see.
        Assert.Equal("System.NullReferenceException", (string?)fault["type"]);
        Assert.Contains(nameof(ThrowsNullReference), (string?)fault["site"]);

        var inner = Assert.IsType<JsonArray>(fault["inner"]);
        Assert.NotEmpty(inner);
    }

    // ── ⑤ it must never be the thing that breaks the error path ───────────────

    /// <summary>
    /// An exception that was never thrown has no stack at all. ⛔ A fault reporter that throws while
    /// reporting a fault would turn a diagnosable 500 into a hung request — so degrade, never fail.
    /// </summary>
    [Fact]
    public void AnUnthrownExceptionDegradesInsteadOfThrowing()
    {
        var never = new InvalidOperationException("never thrown");

        string line = DebugApiFault.OneLine(never);
        JsonObject fault = DebugApiFault.Describe(never);

        Assert.Contains("System.InvalidOperationException", line);
        Assert.Contains("never thrown", line);
        Assert.Equal("System.InvalidOperationException", (string?)fault["type"]);
        // site may legitimately be null here — the absence of a stack is not an error.
    }

    /// <summary>
    /// <b>The fault must survive the wire.</b>
    /// </summary>
    /// <remarks>
    /// 📌 <b>Written because building this hit the landmine.</b> A first attempt to print the fault used
    /// <c>JsonNode.ToJsonString(options)</c> and threw <i>"JsonSerializerOptions instance must specify a
    /// TypeInfoResolver setting before being marked as read-only"</i> — the trap already documented at
    /// <c>DebugApiHost.cs:225</c>. ⭐ The HTTP path is safe because it goes through
    /// <see cref="System.Text.Json.JsonSerializer"/>, which attaches the resolver — ⛔ but "safe by a
    /// distinction two call sites deep" is exactly the kind of thing that silently stops being true.
    /// ⚠ A fault reporter that cannot be serialized converts a diagnosable 500 into a dead request, so this
    /// serializes the WHOLE envelope the way the host writes it and checks the location survives.
    /// </remarks>
    [Fact]
    public void TheFaultSerializesOnTheWirePathWithItsSiteIntact()
    {
        var envelope = new Hrot.Editor.DebugApi.ApiResponse(
            false,
            Error: DebugApiFault.OneLine(Caught(ThrowsNullReference)),
            Fault: DebugApiFault.Describe(Caught(ThrowsNullReference)));

        // The same shape DebugApiHost._jsonOptions uses.
        string json = System.Text.Json.JsonSerializer.Serialize(
            envelope,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            });

        Assert.Contains("\"fault\":", json);
        Assert.Contains("\"site\":", json);
        Assert.Contains(nameof(ThrowsNullReference), json);
        Assert.Contains("\"ok\":false", json);
    }
}
