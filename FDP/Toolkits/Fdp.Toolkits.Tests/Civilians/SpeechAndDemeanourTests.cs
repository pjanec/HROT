using System;
using Fdp.Core;
using Fdp.Toolkit.Civilians;
using Xunit;

namespace Fdp.Toolkit.Tests.Civilians
{
    /// <summary>
    /// ⭐ <c>CE-3264</c> / <c>CE-3265</c> (G1 / G2; docs/DESIGN_Civilians_And_Crowd_Control.md §7.18) — what a man is SAYING and how a civilian
    /// SEEMS, through their one helper each. New data, no suite before.
    /// </summary>
    public sealed class SpeechAndDemeanourTests : IDisposable
    {
        private readonly EntityRepository _world = new();

        public SpeechAndDemeanourTests()
        {
            _world.RegisterComponent<Utterance>();
            _world.RegisterComponent<Demeanour>();
        }

        public void Dispose() => _world.Dispose();

        /// <summary>SP-1 / CB-27 — a command addressed to a man is what he reads while it lasts; the newest wins; it ends at its time.
        /// ✅ Red-proof: <c>Addressed</c> ignoring the target ⇒ the bystander is "addressed" too ⇒ red.</summary>
        [Fact]
        public void CE3264_ACommandAddressedToHim_IsWhatHeReads_WhileItLasts_TheNewestWins()
        {
            Entity officer = _world.CreateEntity(), other = _world.CreateEntity(), man = _world.CreateEntity(), bystander = _world.CreateEntity();
            Assert.True(Utterances.Say(_world, officer, UtteranceKind.CmdDropIt, man, now: 10, seconds: 3));
            Assert.True(Utterances.Say(_world, other, UtteranceKind.Insult, Entity.Null, now: 10, seconds: 5));

            Assert.Equal(UtteranceKind.CmdDropIt, Utterances.Of(_world, officer, 11, out var to));
            Assert.Equal(man, to);
            Assert.True(Utterances.Addressed(_world, man, 11, out var who, out var kind));
            Assert.Equal((officer, UtteranceKind.CmdDropIt), (who, kind));
            Assert.False(Utterances.Addressed(_world, bystander, 11, out _, out _));

            Utterances.Say(_world, other, UtteranceKind.CmdDown, man, now: 11, seconds: 3);   // a later command to him
            Assert.True(Utterances.Addressed(_world, man, 11.5, out who, out kind));
            Assert.Equal((other, UtteranceKind.CmdDown), (who, kind));

            Assert.Equal(UtteranceKind.UtterNone, Utterances.Of(_world, officer, 13, out _));   // over at 10 + 3
            Assert.False(Utterances.Addressed(_world, man, 14.5, out _, out _));
        }

        /// <summary>MD-1 — fear and anger in quarters; written only when a quarter or the mode CHANGES (the egress then sends only real news).
        /// ✅ Red-proof: <c>Set</c> writing every call ⇒ the unchanged write reports a change ⇒ red.</summary>
        [Fact]
        public void CE3265_TheMood_InQuarters_IsWrittenOnlyWhenItChanges()
        {
            Assert.Equal((byte)0, Demeanours.Quarter(0.1f));
            Assert.Equal((byte)1, Demeanours.Quarter(0.2f));
            Assert.Equal((byte)2, Demeanours.Quarter(0.5f));
            Assert.Equal((byte)4, Demeanours.Quarter(1.3f));

            var man = _world.CreateEntity();
            Assert.True(Demeanours.Set(_world, man, CivilianMode.Protest, 0.2f, 0.6f));
            Assert.False(Demeanours.Set(_world, man, CivilianMode.Protest, 0.24f, 0.55f));   // the same quarters
            Assert.True(Demeanours.Set(_world, man, CivilianMode.Protest, 0.4f, 0.55f));     // fear crossed a quarter
            Assert.True(Demeanours.Set(_world, man, CivilianMode.Riot, 0.4f, 0.55f));        // the mode changed
            Assert.True(Demeanours.Of(_world, man, out var d));
            Assert.Equal((CivilianMode.Riot, (byte)2, (byte)2), (d.Mode, d.Fear, d.Anger));
        }
    }
}
