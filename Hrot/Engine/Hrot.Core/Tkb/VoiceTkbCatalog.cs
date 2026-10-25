using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ CE-2372 (AQ105 F, R-2080; docs/designs/audio/DESIGN_FPS_Audio.md §9) — the VOICE clips: one sound type per (voice, line), a line
    /// being what an <see cref="UtteranceKind"/> says ("Moving!", "Police! Show me your hands!") and, for a contact call, the side it names
    /// ("Contact left!"). A voice per side (<see cref="ForceId"/>; a language later). Each is a TKB entry with its <see cref="SoundDto"/>,
    /// hidden from the palette, and a generated placeholder (<see cref="SoundSynth.Voice"/>) until a recording is named in its
    /// <see cref="SoundDto.Files"/>.
    /// <para>⭐ The lines are keyed by the kind's NAME, resolved against the enum at load: a kind behaviors appends later (AQ105 A — the names
    /// are approved) is spoken the moment it exists, and a line whose kind does not exist yet is simply not registered.</para>
    /// </summary>
    public static class VoiceTkbCatalog
    {
        /// <summary>The first voice type id; voice <c>v</c>, line <c>l</c> is <c>Base + v·<see cref="Lines"/> + l</c>.</summary>
        public const long Base = 7400;

        /// <summary>Line slots per voice: a kind's line is its byte value; the contact sides follow at <see cref="ContactSides"/>.</summary>
        public const int Lines = 64;

        /// <summary>The first of the four "Contact front / left / right / rear!" lines.</summary>
        public const int ContactSides = 48;

        /// <summary>A voice's priority (AU-7): above a far shot, below a round passing the ear — a call is what the operator must catch.</summary>
        public const byte PriorityVoice = 4;

        /// <summary>The voices — index = <see cref="ForceId"/> (neutral, blue, red): the placeholder's pitch and formant scale.</summary>
        public static readonly IReadOnlyList<(string Name, float PitchHz, float Formant)> Voices = new[]
        {
            ("neutral", 150f, 1.12f),
            ("blue", 118f, 1.0f),
            ("red", 98f, 0.92f),
        };

        /// <summary>What each kind says, by the kind's name. ⛔ <c>Talk</c> has no line: talk is not heard across the street.</summary>
        public static readonly IReadOnlyList<(string Kind, string Text)> Said = new[]
        {
            ("Insult", "Get out of here!"),
            ("Warning", "Back off!"),
            ("Order", "Disperse! Go home!"),
            ("Chant", "Go home! Go home!"),
            ("Challenge", "Police! Show me your hands!"),
            ("CmdDown", "Get down!"),
            ("CmdFreeze", "Freeze!"),
            ("CmdHandsUp", "Hands up!"),
            ("CmdDropIt", "Drop it!"),
            ("CmdComeOut", "Come out!"),
            ("Announce", "Police! Search warrant!"),
            ("CallClear", "Clear!"),
            ("CallManDown", "Man down!"),
            // ⭐ AQ105 A — the squad's calls (behaviors appends them, CE-2372)
            ("CallContact", "Contact!"),
            ("CallMoving", "Moving!"),
            ("CallCovering", "Covering!"),
            ("CallReloading", "Reloading!"),
            ("CallFragOut", "Frag out!"),
            ("CallGrenade", "Grenade!"),
            ("CallSuppress", "Suppress!"),
            ("CmdMove", "Move!"),
            ("CmdHold", "Hold!"),
            ("CmdFallBack", "Fall back!"),
            ("CallAck", "Copy!"),
        };

        /// <summary>The side a contact call names, relative to the speaker's facing.</summary>
        public enum ContactSide : byte { Front = 0, Left = 1, Right = 2, Rear = 3 }

        private static readonly string[] SideWords = { "front", "left", "right", "rear" };

        /// <summary>Every voice line that exists: its type id, its name, the words and the voice.</summary>
        public static readonly IReadOnlyDictionary<long, (string Name, string Text, int Voice)> All = Build();

        private static Dictionary<long, (string, string, int)> Build()
        {
            var d = new Dictionary<long, (string, string, int)>();
            for (int v = 0; v < Voices.Count; v++)
            {
                foreach (var (kind, text) in Said)
                {
                    if (!Enum.TryParse<UtteranceKind>(kind, out var k) || (int)k >= ContactSides) continue;
                    d[ClipOf(v, (int)k)] = ($"Voice: {Voices[v].Name} {text}", text, v);
                    if (kind == "CallContact")
                        for (int s = 0; s < SideWords.Length; s++)
                            d[ClipOf(v, ContactSides + s)] = ($"Voice: {Voices[v].Name} Contact {SideWords[s]}!", $"Contact {SideWords[s]}!", v);
                }
            }
            return d;
        }

        /// <summary>The type id of <paramref name="voice"/>'s line <paramref name="line"/>.</summary>
        public static long ClipOf(int voice, int line) => Base + voice * Lines + line;

        /// <summary>The voice of a speaker of <paramref name="force"/>.</summary>
        public static int VoiceOf(ForceId force) => (int)force < Voices.Count ? (int)force : 0;

        /// <summary>The clip <paramref name="voice"/> says <paramref name="kind"/> with — for a contact call with a known side, the side's
        /// line; 0 when that kind has no line.</summary>
        public static long ClipFor(int voice, UtteranceKind kind, ContactSide? side = null)
        {
            if (kind == UtteranceKind.UtterNone || (int)kind >= ContactSides) return 0;
            if (side is { } s && All.ContainsKey(ClipOf(voice, ContactSides + (int)s)) && IsContact(kind)) return ClipOf(voice, ContactSides + (int)s);
            long clip = ClipOf(voice, (int)kind);
            return All.ContainsKey(clip) ? clip : 0;
        }

        /// <summary>The kind is the contact call (by name: it is appended by behaviors).</summary>
        public static bool IsContact(UtteranceKind kind) => kind.ToString() == "CallContact";

        /// <summary>How a voice line sounds: its TKB <see cref="SoundDto"/> (a recording named), else the built-in placeholder.</summary>
        public static SoundDto? SoundOf(ITkbDatabase? tkb, long clip)
        {
            if (!All.ContainsKey(clip)) return null;
            if (tkb != null && tkb.TryGetByType(clip, out var t) && t?.GetDescriptor<SoundDto>() is { } s) return s;
            return Placeholder;
        }

        /// <summary>The built-in voice sound: a shout at full level within 5 m, silent at the AI's voice range (40 m) — the operator hears
        /// what the AI hears (AU-4).</summary>
        public static readonly SoundDto Placeholder = new()
        {
            Synth = SoundSynth.Voice, ReferenceM = 5f, RangeM = 40f, Category = SoundCategory.Voices, Priority = PriorityVoice, VolumeDb = -2f,
            PitchJitter = 0.04f,
        };

        /// <summary>Registers the voice types (hidden from the palette). ⚠ Once per database.</summary>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));
            foreach (var (type, (name, _, _)) in All)
            {
                var t = new TkbTemplate(name, type);
                t.AddDescriptor(new TkbMasterDto { CustomName = name, HideFromPalette = true });
                t.AddDescriptor(Placeholder);
                tkb.Register(t);
            }
        }
    }
}
