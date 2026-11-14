using System.Linq;
using Fdp.Toolkit.Behavior;
using Hrot.AI.Behaviors.Brains.Objectives;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>⭐ <c>CE-2377</c> step 1 — <see cref="BehaviorParamsValidation"/>: unknown behaviour, strict unknown key, the Trigger's validator, a good params. ⛔ No world anywhere.</summary>
public sealed class BehaviorParamsValidationTests
{
    private static BehaviorRegistry Production()
    {
        var r = new BehaviorRegistry();
        CgfBehaviorSetup.LoadFromAiAssembly(r);
        return r;
    }

    private const string GoodWhen = "{\"$type\":\"ScenarioTime\",\"fromSeconds\":1}";

    [Fact]
    public void CE2377_AnUnknownBehaviourNameIsAProblem()
    {
        var p = BehaviorParamsValidation.Validate(Production(), "NoSuchBehaviour_CE2377", "{}");
        Assert.Contains(p, s => s.Contains("unknown behaviour 'NoSuchBehaviour_CE2377'"));
    }

    [Fact]
    public void CE2377_AnEmptyOrNullParamsIsFine()
    {
        var r = Production();
        Assert.Empty(BehaviorParamsValidation.Validate(r, "MoveToLocation", null));
        Assert.Empty(BehaviorParamsValidation.Validate(r, "MoveToLocation", ""));
    }

    [Fact]
    public void CE2377_GoodParamsPass_AndAnUnknownKeyIsNamed()
    {
        var r = Production();
        Assert.Empty(BehaviorParamsValidation.Validate(r, "MoveToLocation", "{\"speed\":2,\"arrivalRadius\":1,\"target\":[102,104,0]}"));
        var p = BehaviorParamsValidation.Validate(r, "MoveToLocation", "{\"speed\":2,\"noSuchKeyCE2377\":1}");
        Assert.Single(p);
        Assert.Contains("noSuchKeyCE2377", p[0]);
    }

    [Fact]
    public void CE2377_TheTriggerValidator_AcceptsAGoodTrigger()
    {
        var json = "{\"when\":" + GoodWhen + ",\"do\":[]}";
        Assert.Empty(BehaviorParamsValidation.Validate(Production(), BehaviorNames.Trigger, json));
    }

    [Fact]
    public void CE2377_TheTriggerValidator_RefusesABadDo_AndAMissingWhen()
    {
        var r = Production();
        var bad = BehaviorParamsValidation.Validate(r, BehaviorNames.Trigger, "{\"when\":" + GoodWhen + ",\"do\":[{\"act\":\"NoSuchAct_CE2377\"}]}");
        Assert.Contains(bad, s => s.Contains("'do' is invalid"));
        var noWhen = BehaviorParamsValidation.Validate(r, BehaviorNames.Trigger, "{\"do\":[]}");
        Assert.Contains(noWhen, s => s.Contains("'when' is required"));
    }
}
