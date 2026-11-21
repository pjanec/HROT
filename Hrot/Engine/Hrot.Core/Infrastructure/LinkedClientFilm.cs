using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hrot.Common.Infrastructure;

/// <summary>⭐ <c>CE-11018</c> (DESIGN_Linked_Client_Director.md §2b) — what a film job is: a reel played to frames, or one still.</summary>
public enum FilmJobKind : byte { Reel = 1, Still = 2 }

/// <summary>⭐ <c>CE-11018</c> (Q113-B) — where a job's scene frames come from: <see cref="Auto"/> = the linked client when one is offered and ready, else the
/// map; <see cref="Map"/> = always the node's own 3-D map (a take with a 2-D view needs it).</summary>
public enum FilmFrames : byte { Auto = 0, Map = 1 }

/// <summary>⭐ <c>CE-11018</c> (Q113-E, built by <c>CE-11021</c>) — whose sound a job's movie carries: HROT's own mix, or the linked client's track.</summary>
public enum FilmSound : byte { Hrot = 0, Client = 1 }

/// <summary>
/// ⭐⭐ <c>CE-11018</c> (§2b, §2c) — one film job, from <c>Begin</c> to <c>End</c>: its id (every <see cref="FilmFrame"/> carries it), its kind, what it films (the reel's
/// name or the still's), the film's frame rate, the frame size and supersampling, the folder its frames are written to, the sound source and the frame source.
/// </summary>
public sealed record FilmJob(Guid JobId, FilmJobKind Kind, string What, int Fps, int Width, int Height, int Supersample, string Dir,
                             FilmSound Sound = FilmSound.Hrot, FilmFrames Frames = FilmFrames.Auto);

/// <summary>
/// ⭐ <c>CE-11018</c> (Q113-F, D-3) — the camera of one frame, in world metres (engine space: X east, Y north, Z up): the eye, the unit view direction, the unit
/// up direction (orthogonal to <see cref="Forward"/>; banked when the view is), and the VERTICAL field of view in degrees. It is the pose the node's own map
/// would have rendered this frame (its film layer placed it), so the client sees exactly what the map would have shown.
/// </summary>
public readonly record struct FilmCamera(Vector3 Position, Vector3 Forward, Vector3 Up, float FovDeg);

/// <summary>⭐ <c>CE-11018</c> — a frame's content: a <see cref="Scene"/> to render; the <see cref="Repeat"/> of the previous scene frame (slow motion past one
/// frame per step — HROT copies the picture; the client only keeps its sound in step); a <see cref="Silence"/> frame under a title card (the card is the
/// node's own — Q113 D-4 — the client renders no picture, only silence for its track).</summary>
public enum FilmFrameKind : byte { Scene = 0, Repeat = 1, Silence = 2 }

/// <summary>⭐ <c>CE-11018</c> — where a subtitle stands on the frame.</summary>
public enum FilmSubtitlePosition : byte { Bottom = 0, Top = 1, Middle = 2 }

/// <summary>⭐ <c>CE-11018</c> — the subtitle burned over a scene frame: its text, position and opacity (0…1) this frame.</summary>
public sealed record FilmSubtitle(string Text, FilmSubtitlePosition Position, float Alpha);

/// <summary>⭐ <c>CE-11018</c> — the kind of a film graphic.</summary>
public enum FilmGraphicKind : byte { Ring = 0, Arrow = 1, Route = 2, Area = 3, Label = 4 }

/// <summary>⭐ <c>CE-11018</c> — one film graphic on a scene frame, already resolved: its WORLD points this frame (metres), radius (m), text, colour (0xRRGGBB - opacity is <c>Alpha</c>) and
/// opacity (0…1). Drawn over the scene, under the subtitle, in list order.</summary>
public sealed record FilmGraphic(FilmGraphicKind Kind, IReadOnlyList<Vector3> Points, float Radius, string? Text, uint Colour, float Alpha);

/// <summary>
/// ⭐⭐ <c>CE-11018</c> (§2b, §2c; the gateway maps it onto DD-022's <c>FrameRequest</c>) — one film frame for the linked client:
/// <list type="bullet">
///   <item><see cref="JobId"/> + <see cref="Index"/> — the job and the film frame number (0, 1, 2 … every frame of the movie in order, repeats and silence included);</item>
///   <item><see cref="Kind"/> — a scene to render, a repeat, or silence (see <see cref="FilmFrameKind"/>);</item>
///   <item><see cref="SimTime"/> — the world's sim time this frame shows (s); <see cref="FilmRate"/> — sim seconds per film second now (the shot's speed: 1 = real
///     time, 0.5 slow motion); <see cref="Step"/> — the director's steps in this reel item so far (the world is held still between frames: frame N is taken after
///     step N);</item>
///   <item><see cref="Camera"/> — the frame's camera (a scene frame; default for silence); <see cref="Width"/> × <see cref="Height"/> px, rendered
///     <see cref="Supersample"/> times larger and scaled down; <see cref="Path"/> — the PNG to write (a scene frame only);</item>
///   <item><see cref="Subtitle"/>, <see cref="Graphics"/>, <see cref="Fade"/> (0 = none … 1 = black over the picture) — burned over the scene;</item>
///   <item><see cref="LayersOn"/> / <see cref="LayersOff"/> — the take's map-layer override this frame (layer names; empty = as configured).</item>
/// </list>
/// </summary>
public sealed record FilmFrame(Guid JobId, int Index, FilmFrameKind Kind, double SimTime, double FilmRate, long Step, FilmCamera Camera,
                               int Width, int Height, int Supersample, string Path, FilmSubtitle? Subtitle, IReadOnlyList<FilmGraphic> Graphics, float Fade,
                               IReadOnlyList<string> LayersOn, IReadOnlyList<string> LayersOff);

/// <summary>⭐ <c>CE-11018</c> — how a job ended: <see cref="Ok"/> (every frame written), <see cref="Failed"/> (with <c>Reason</c>) or <see cref="Stopped"/> by the user.</summary>
public enum FilmJobEnd : byte { Ok = 0, Failed = 1, Stopped = 2 }

/// <summary>⭐ <c>CE-11018</c> — the outcome handed to <c>End</c>: how it ended, and why when it did not end well.</summary>
public sealed record FilmJobOutcome(FilmJobEnd End, string? Reason = null)
{
    public static readonly FilmJobOutcome Ok = new(FilmJobEnd.Ok);
}

/// <summary>⭐ <c>CE-11018</c> (Q113-E) — the linked client's sound track of a job: the WAV file, its total samples (per channel), the sample rate and channel count.
/// The encode muxes it and checks <see cref="Samples"/> against the frames (<c>CE-11021</c>).</summary>
public sealed record FilmTrack(string Path, long Samples, int SampleRate, int Channels);
