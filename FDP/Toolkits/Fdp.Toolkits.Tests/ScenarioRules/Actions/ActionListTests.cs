using System;
using System.Linq;
using Fdp.Toolkit.ScenarioRules.Actions;
using Xunit;
using static Fdp.Toolkit.ScenarioRules.Tests.Actions.ActionKit;

namespace Fdp.Toolkit.ScenarioRules.Tests.Actions
{
    /// <summary>⭐ rules S2-d (CE-4163) — a <c>do</c> list is validated at ASSIGNMENT, naming the step and the act.</summary>
    public sealed class ActionListTests
    {
        [Fact]
        public void AGoodList_Validates_AndComesBackParsed()
        {
            var steps = ActionList.Validate(Steps(Step("TestRecord", "{\"value\":1}"), Step("TestWaitTicks", "{\"ticks\":3}"), Step("TestRich", "{\"mode\":\"Fast\",\"target\":\"@Hq\"}")));
            Assert.Equal(new[] { "TestRecord", "TestWaitTicks", "TestRich" }, new[] { steps[0].Act, steps[1].Act, steps[2].Act });
            Assert.Empty(ActionList.Validate("[]"));
        }

        [Fact]
        public void CE4163_AnUnknownAct_IsRefused_NamingTheStepAndTheAct()
        {
            var ex = Assert.Throws<ActionListException>(() => ActionList.Validate(Steps(Step("TestRecord", "{\"value\":1}"), Step("NoSuchAct"))));   // ← red-proof: skip the lookup ⇒ no throw
            Assert.Equal(1, ex.Step);
            Assert.Equal("NoSuchAct", ex.Act);
            Assert.Contains("step 1", ex.Message);
            Assert.Contains("unknown action", ex.Message);
        }

        [Fact]
        public void CE4163_AnUnknownArgsProperty_IsRefused_NamingTheStepTheActAndTheProperty()
        {
            var ex = Assert.Throws<ActionListException>(() => ActionList.Validate(Steps(Step("TestWaitTicks", "{\"tiks\":3}"))));
            Assert.Equal(0, ex.Step);
            Assert.Contains("TestWaitTicks", ex.Message);
            Assert.Contains("tiks", ex.Message);
        }

        [Fact]
        public void CE4163_ArgsThatDoNotRead_AreRefused()
        {
            var ex = Assert.Throws<ActionListException>(() => ActionList.Validate(Steps(Step("TestRich", "{\"mode\":\"Warp9\"}"))));
            Assert.Equal(0, ex.Step);
            Assert.Throws<ActionListException>(() => ActionList.Validate(Steps(Step("TestWaitTicks", "{\"ticks\":\"many\"}"))));
            Assert.Throws<ActionListException>(() => ActionList.Validate(Steps(Step("TestWaitTicks", "[1]"))));   // args must be an object
        }

        [Fact]
        public void CE4163_AnActionThatCannotRunAsAStep_IsRefusedAtAssignment()
        {
            var ex = Assert.Throws<ActionListException>(() => ActionList.Validate(Steps(Step("TestTakesEntity", "{\"n\":1}"))));
            Assert.Contains("Entity field", ex.Message);                         // CE-4162 surfaces here, not at the first tick
        }

        [Fact]
        public void CE4163_AMalformedList_IsRefused()
        {
            Assert.Throws<ActionListException>(() => ActionList.Validate("{\"act\":\"TestRecord\"}"));              // not an array
            Assert.Throws<ActionListException>(() => ActionList.Validate("[1]"));                                   // a step that is not an object
            Assert.Throws<ActionListException>(() => ActionList.Validate("[{\"args\":{}}]"));                       // no act
            Assert.Throws<ActionListException>(() => ActionList.Validate("[not json"));
            var ex = Assert.Throws<ActionListException>(() => ActionList.Validate("[{\"act\":\"TestRecord\"},{\"x\":1}]"));
            Assert.Equal(1, ex.Step);
        }

        [Fact]
        public void TheStepsOfADistinctJsonString_AreParsedOnce()
        {
            string json = Steps(Step("TestRecord", "{\"value\":1}"));
            Assert.Same(ActionList.Get(json), ActionList.Get(new string(json.AsSpan())));   // same content, a different string instance ⇒ the cache keys by value
        }

        /// <summary>⭐ rules S2 ⑨ — a step's nested <c>"params"</c> (an object, or a string) is lifted OUT of its args into extra items; its <c>ArgsRef</c> is the item's index; the
        /// rewritten list validates and, RUN, the step reads the item its ArgsRef names. ✅ Red-proof: the <c>args["ArgsRef"] = …</c> seeding removed ⇒ both steps read item 0 ⇒ red.</summary>
        [Fact]
        public void ExtractParams_LiftsNestedParamsIntoItems_SeedsArgsRef_AndTheRunnerReadsTheRightItem()
        {
            var (json, extras) = ActionList.ExtractParams(
                Steps(Step("TestRefLength", "{\"n\":5,\"params\":{\"a\":1}}"), Step("TestRecord", "{\"value\":9}"), Step("TestRefLength", "{\"params\":\"hello\"}")), firstItem: 2);
            Assert.Equal(new[] { "{\"a\":1}", "hello" }, extras);
            var steps = ActionList.Validate(json);
            Assert.DoesNotContain("params", steps[0].ArgsJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"ArgsRef\":2", steps[0].ArgsJson);
            Assert.Contains("\"ArgsRef\":3", steps[2].ArgsJson);
            Assert.Equal("{\"value\":9}", steps[1].ArgsJson);                       // a step with no params is untouched

            using var w = NewWorld();
            var e = Unit(w, new[] { "when", json }.Concat(extras).ToArray());
            Assert.Equal(Fbt.NodeStatus.Success, ActionHost.Tick(w, e, 1, out _));
            var log = w.GetComponentRO<ActionTestLog>(e);
            Assert.Equal((3, 7, 9, 5), (log.Count, log.V0, log.V1, log.V2));         // {"a":1} is 7 chars, then the record, then "hello"
        }

        [Fact]
        public void ExtractParams_AListWithNoParams_ComesBackUnchanged_ParamsOnAnActionWithoutArgsRefAreRefused()
        {
            string plain = Steps(Step("TestRecord", "{\"value\":1}"));
            var (same, none) = ActionList.ExtractParams(plain, 2);
            Assert.Same(plain, same);                                                 // the parse cache keys on the very string
            Assert.Empty(none);

            var ex = Assert.Throws<ActionListException>(() => ActionList.ExtractParams(Steps(Step("TestRecord", "{\"value\":1}"), Step("TestRecord", "{\"params\":{\"x\":1}}")), 2));
            Assert.Equal((1, "TestRecord"), (ex.Step, ex.Act));
            Assert.Contains("ArgsRef", ex.Message);
            var both = Assert.Throws<ActionListException>(() => ActionList.ExtractParams(Steps(Step("TestRefLength", "{\"argsRef\":0,\"params\":{}}")), 2));
            Assert.Contains("not both", both.Message);
        }
    }
}
