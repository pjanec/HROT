using System.Numerics;
using Fdp.Toolkit.Vis3D;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>⭐ CE-1101 (docs/DESIGN_Map_3D_Mode.md §6r) — what the shader paints on a body part.</summary>
public enum SurfaceKind { Plain = 0, Woodland = 1, Fleck = 2, Face = 3, Desert = 4 }

/// <summary>
/// ⭐ CE-1101 (docs/DESIGN_Map_3D_Mode.md §6r) — the figures' procedural surface: camouflage and faces painted by the 3-D map's shader
/// (<see cref="LitShader.UseSurface"/>) instead of drawn as extra boxes. Camouflage is 3-D value noise in the part's own METRES (S-2), so a
/// patch is the same size on every part and moves with it; a face is painted on the head's face side (S-3). Each uniform is set only when it
/// changes, and <see cref="Plain"/> restores the shader's own output for everything else.
/// </summary>
public sealed class BodySurface
{
    /// <summary>The surface function (<see cref="LitShader.UseSurface"/>): plain unless <c>bodyPattern</c> says otherwise.
    /// ⭐ CE-4555 (§6ak D-5) — in the INSTANCED program the same parameters come from the instance (<see cref="Pack"/> writes them, the
    /// <c>#ifdef INSTANCED</c> block reads them); the face's axes come from a table of <see cref="AxesOf"/> by <see cref="FaceOn"/>.</summary>
    public static readonly string Glsl = InstancedParameters() + UniformGlsl;

    private static string InstancedParameters()
    {
        static string V(Vector3 v) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"vec3({v.X:R}, {v.Y:R}, {v.Z:R})");
        var axis = new string[4]; var up = new string[4];
        for (int i = 0; i < 4; i++) { var (a, u) = AxesOf((FaceOn)i); axis[i] = V(a); up[i] = V(u); }
        return "\n#ifdef INSTANCED\n"
             + "const vec3 bodyFaceAxes[4] = vec3[4](" + string.Join(", ", axis) + ");\n"
             + "const vec3 bodyFaceUps[4] = vec3[4](" + string.Join(", ", up) + ");\n"
             + "#define bodyPattern int(instData0.x + 0.5)\n"
             + "#define patternSeed instData0.y\n"
             + "#define faceAxis bodyFaceAxes[int(instData0.z + 0.5)]\n"
             + "#define faceUp bodyFaceUps[int(instData0.z + 0.5)]\n"
             + "#define tone1 instData1.rgb\n#define tone2 instData2.rgb\n#define tone3 instData3.rgb\n"
             + "#define faceMood vec3(instData1.w, instData2.w, instData3.w)\n"
             + "#define faceLook instData4\n"
             + "#else\n"
             + "uniform int bodyPattern;\nuniform vec3 tone1;\nuniform vec3 tone2;\nuniform vec3 tone3;\nuniform float patternSeed;\n"
             + "uniform vec3 faceAxis;\nuniform vec3 faceUp;\nuniform vec4 faceLook;\nuniform vec3 faceMood;\n"
             + "#endif\n";
    }

    /// <summary>
    /// ⭐ CE-4555 — instance data (<see cref="CamouflageData"/>, <see cref="FaceData"/>, <see cref="PlainData"/>) set as THIS surface's uniforms on the
    /// per-draw program: the batch's per-draw path (a driver that refused the instanced program, and the rail that compares the two pictures) draws
    /// exactly what the instanced program draws.
    /// </summary>
    public void Apply(LitShader shader, in LitInstanceData d)
    {
        var kind = (SurfaceKind)(int)(d.D0.X + 0.5f);
        Kind(shader, kind);
        if (kind == SurfaceKind.Plain) return;
        SetRaw(shader, _tone1, ref _t1, new Vector3(d.D1.X, d.D1.Y, d.D1.Z));
        SetRaw(shader, _tone2, ref _t2, new Vector3(d.D2.X, d.D2.Y, d.D2.Z));
        SetRaw(shader, _tone3, ref _t3, new Vector3(d.D3.X, d.D3.Y, d.D3.Z));
        if (kind != SurfaceKind.Face)
        {
            if (d.D0.Y != _s) { _s = d.D0.Y; Raylib.SetShaderValue(shader.Shader, _seed, _s, ShaderUniformDataType.Float); }
            return;
        }
        var mood = new Vector3(d.D1.W, d.D2.W, d.D3.W);
        if (mood != _mood) { _mood = mood; Raylib.SetShaderValue(shader.Shader, _faceMood, mood, ShaderUniformDataType.Vec3); }
        var (axis, up) = AxesOf((FaceOn)(int)(d.D0.Z + 0.5f));
        if (axis != _axis) { _axis = axis; Raylib.SetShaderValue(shader.Shader, _faceAxis, axis, ShaderUniformDataType.Vec3); }
        if (up != _up) { _up = up; Raylib.SetShaderValue(shader.Shader, _faceUp, up, ShaderUniformDataType.Vec3); }
        if (d.D4 != _look) { _look = d.D4; Raylib.SetShaderValue(shader.Shader, _faceLook, d.D4, ShaderUniformDataType.Vec4); }
    }

    private static void SetRaw(LitShader shader, int loc, ref Vector3 last, Vector3 v)
    {
        if (v == last) return;
        last = v;
        Raylib.SetShaderValue(shader.Shader, loc, v, ShaderUniformDataType.Vec3);
    }

    /// <summary>⭐ CE-4555 — nothing painted, as instance data.</summary>
    public static LitInstanceData PlainData => default;

    /// <summary>⭐ CE-4555 (§6ak D-5) — camouflage <paramref name="kind"/> as instance data (the same parameters <see cref="Camouflage"/> sets as uniforms).</summary>
    public static LitInstanceData CamouflageData(SurfaceKind kind, Color t1, Color t2, Color t3, float seed) => new()
    {
        D0 = new Vector4((int)kind, seed, 0f, 0f),
        D1 = new Vector4(t1.R / 255f, t1.G / 255f, t1.B / 255f, 0f),
        D2 = new Vector4(t2.R / 255f, t2.G / 255f, t2.B / 255f, 0f),
        D3 = new Vector4(t3.R / 255f, t3.G / 255f, t3.B / 255f, 0f),
    };

    /// <summary>⭐ CE-4555 (§6ak D-5) — a face as instance data (the same parameters <see cref="Face"/> sets as uniforms).</summary>
    public static LitInstanceData FaceData(FaceOn on, in FaceLook look, Color hair, Vector3 mood = default) => new()
    {
        D0 = new Vector4((int)SurfaceKind.Face, 0f, (int)(on == FaceOn.None ? FaceOn.Front : on), 0f),
        D1 = new Vector4(hair.R / 255f, hair.G / 255f, hair.B / 255f, mood.X),
        D2 = new Vector4(look.EyeColour.R / 255f, look.EyeColour.G / 255f, look.EyeColour.B / 255f, mood.Y),
        D3 = new Vector4(look.Lips.R / 255f, look.Lips.G / 255f, look.Lips.B / 255f, mood.Z),
        D4 = new Vector4(look.EyeSpacing, look.MouthHalfWidth, (float)look.Beard, look.BrowHalfHeight),
    };

    private const string UniformGlsl = @"
// bodyPattern: 0 plain, 1 woodland, 2 fleck, 3 face, 4 desert · tone1..3: camouflage tones; a face: the hair (brows, beard), the iris, the lips
// faceAxis / faceUp: the side of the head the face is on, and the top of the head (part frame)
// faceLook: eye spacing, mouth half-width, beard (0 none, 1 moustache, 2 beard), brow half-height
// faceMood (CE-1177, U16): brow lift (+ raised, - knitted), mouth open, eye squint; zero = calm
float bodyHash(vec3 p) { p = fract(p * 0.3183099 + vec3(0.11, 0.23, 0.37)); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float bodyNoise(vec3 x)
{
    vec3 i = floor(x); vec3 f = fract(x); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(bodyHash(i), bodyHash(i + vec3(1, 0, 0)), f.x), mix(bodyHash(i + vec3(0, 1, 0)), bodyHash(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(bodyHash(i + vec3(0, 0, 1)), bodyHash(i + vec3(1, 0, 1)), f.x), mix(bodyHash(i + vec3(0, 1, 1)), bodyHash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
float bodyFbm(vec3 p) { return 0.65 * bodyNoise(p) + 0.35 * bodyNoise(p * 2.13 + vec3(7.3)); }
float bodyBox(vec2 p, vec2 c, vec2 h) { vec2 d = abs(p - c) - h; return step(max(d.x, d.y), 0.0); }
vec3 surface(vec3 base, vec3 unit, vec3 metres, vec3 n)
{
    if (bodyPattern == 0) return base;
    if (bodyPattern == 3)
    {
        if (dot(n, faceAxis) < 0.5) return base;
        vec3 side = cross(faceUp, faceAxis);
        vec2 p = vec2(dot(unit, side), dot(unit, faceUp));
        vec2 m = vec2(abs(p.x), p.y);
        vec3 c = base;
        if (faceLook.z > 1.5 && p.y < -0.10 && abs(p.x) < 0.47) c = tone1;                        // a beard
        c = mix(c, base * 0.80, bodyBox(p, vec2(0.0, -0.08), vec2(0.055, 0.12)));             // the nose's shade
        if (faceLook.z > 0.5) c = mix(c, tone1, bodyBox(p, vec2(0.0, -0.215), vec2(faceLook.y + 0.03, 0.035)));   // a moustache
        c = mix(c, faceMood.y > 0.0 ? tone3 * 0.45 : tone3,
                bodyBox(p, vec2(0.0, -0.31), vec2(faceLook.y * (1.0 - 2.0 * faceMood.y), 0.035 + faceMood.y)));   // the mouth (open: dark)
        c = mix(c, tone1, bodyBox(m, vec2(faceLook.x, 0.175 + faceMood.x), vec2(0.09, faceLook.w)));        // the brows
        float eye = max(0.012, 0.055 - faceMood.z);
        c = mix(c, vec3(0.94, 0.94, 0.92), bodyBox(m, vec2(faceLook.x, 0.05), vec2(0.085, eye)));   // the eyes
        c = mix(c, tone2, bodyBox(m, vec2(faceLook.x, 0.05), vec2(0.04, eye)));             // the irises
        return c;
    }
    bool woodland = bodyPattern == 1, desert = bodyPattern == 4;
    vec3 q = metres * (woodland ? 5.5 : desert ? 4.5 : 17.0) + vec3(patternSeed);
    float a = bodyFbm(q), b = bodyFbm(q + vec3(31.7, 11.3, 5.9)), d = bodyFbm(q + vec3(-17.1, 23.9, 41.3));
    // ⭐ CE-1235 (§6q O-4a) — woodland denser (was 0.60 / 0.62 / 0.64) and desert's large soft patches: the pattern reads at room distance
    if (woodland) { if (a > 0.56) return tone1; if (b > 0.58) return tone2; if (d > 0.60) return tone3; return base; }
    if (desert) { if (a > 0.57) return tone1; if (b > 0.60) return tone3; if (d > 0.64) return tone2; return base; }
    if (a > 0.64) return tone1; if (b > 0.66) return tone2; if (d > 0.70) return tone3; return base;
}
";

    private uint _shaderId;
    private int _pattern, _tone1, _tone2, _tone3, _seed, _faceAxis, _faceUp, _faceLook, _faceMood;
    private Vector3 _mood;
    private SurfaceKind _kind = (SurfaceKind)(-1);
    private Vector3 _t1, _t2, _t3, _axis, _up;
    private float _s = float.NaN;
    private Vector4 _look;

    /// <summary>Draws (or skips) since the last <see cref="BeginFrame"/>: how many parts were painted (rails).</summary>
    public int Painted { get; private set; }

    /// <summary>Register the surface with the shader (once per process; recompiles it on next use) and re-read the uniform locations
    /// when the shader instance changed. Call before drawing with <see cref="LitShader.Shared"/>.</summary>
    public void BeginFrame(LitShader shader)
    {
        Painted = 0;
        if (shader.Shader.Id == _shaderId) return;
        _shaderId = shader.Shader.Id;
        var s = shader.Shader;
        _pattern = Raylib.GetShaderLocation(s, "bodyPattern");
        _tone1 = Raylib.GetShaderLocation(s, "tone1");
        _tone2 = Raylib.GetShaderLocation(s, "tone2");
        _tone3 = Raylib.GetShaderLocation(s, "tone3");
        _seed = Raylib.GetShaderLocation(s, "patternSeed");
        _faceAxis = Raylib.GetShaderLocation(s, "faceAxis");
        _faceUp = Raylib.GetShaderLocation(s, "faceUp");
        _faceLook = Raylib.GetShaderLocation(s, "faceLook");
        _faceMood = Raylib.GetShaderLocation(s, "faceMood");
        _mood = new Vector3(float.NaN);
        _kind = (SurfaceKind)(-1);
        _s = float.NaN;
        _t1 = _t2 = _t3 = _axis = _up = new Vector3(float.NaN);
        _look = new Vector4(float.NaN);
        Plain(shader);
    }

    /// <summary>Nothing painted: the shader's own colour (every non-body draw).</summary>
    public void Plain(LitShader shader) => Kind(shader, SurfaceKind.Plain);

    /// <summary>Camouflage <paramref name="kind"/> in three tones over the part's colour, shifted by the man's <paramref name="seed"/>.</summary>
    public void Camouflage(LitShader shader, SurfaceKind kind, Color t1, Color t2, Color t3, float seed)
    {
        Kind(shader, kind);
        Tones(shader, t1, t2, t3);
        if (seed != _s) { _s = seed; Raylib.SetShaderValue(shader.Shader, _seed, seed, ShaderUniformDataType.Float); }
        Painted++;
    }

    /// <summary>A face on the head's <paramref name="on"/> side, in the man's <paramref name="look"/> (his hair for the brows and beard).</summary>
    public void Face(LitShader shader, FaceOn on, in FaceLook look, Color hair, Vector3 mood = default)
    {
        if (mood != _mood) { _mood = mood; Raylib.SetShaderValue(shader.Shader, _faceMood, mood, ShaderUniformDataType.Vec3); }   // ⭐ CE-1177
        Kind(shader, SurfaceKind.Face);
        Tones(shader, hair, look.EyeColour, look.Lips);
        var (axis, up) = AxesOf(on);
        if (axis != _axis) { _axis = axis; Raylib.SetShaderValue(shader.Shader, _faceAxis, axis, ShaderUniformDataType.Vec3); }
        if (up != _up) { _up = up; Raylib.SetShaderValue(shader.Shader, _faceUp, up, ShaderUniformDataType.Vec3); }
        var v = new Vector4(look.EyeSpacing, look.MouthHalfWidth, (float)look.Beard, look.BrowHalfHeight);
        if (v != _look) { _look = v; Raylib.SetShaderValue(shader.Shader, _faceLook, v, ShaderUniformDataType.Vec4); }
        Painted++;
    }

    /// <summary>
    /// ⭐ <c>CE-1177</c> (People Display §5 U16) — a face's expression for <paramref name="mood"/>, the shader's <c>faceMood</c> (brow lift,
    /// mouth open, eye squint, in the head box's unit frame): calm none · afraid raised brows, the mouth open · angry the brows knitted down,
    /// the mouth a tight line · in pain the eyes squeezed, the brows down · alarmed the brows up.
    /// </summary>
    public static Vector3 Expression(Hrot.Common.Diagnostics.Gizmos.MarkKind mood) => mood switch
    {
        Hrot.Common.Diagnostics.Gizmos.MarkKind.Fear => new Vector3(0.05f, 0.035f, 0f),
        Hrot.Common.Diagnostics.Gizmos.MarkKind.Anger => new Vector3(-0.035f, -0.015f, 0.012f),
        Hrot.Common.Diagnostics.Gizmos.MarkKind.Pain => new Vector3(-0.025f, 0f, 0.035f),
        Hrot.Common.Diagnostics.Gizmos.MarkKind.Alarm => new Vector3(0.03f, 0.012f, 0f),
        _ => Vector3.Zero,
    };

    /// <summary>⭐ CE-1101 (§6r S-3) — the head box's face side and the top of the head, in the part's own frame, for each
    /// <see cref="FaceOn"/> (the same turn as <see cref="BlockFigure.FaceFrame"/>).</summary>
    public static (Vector3 Face, Vector3 Up) AxesOf(FaceOn on)
    {
        var frame = BlockFigure.FaceFrame(on == FaceOn.None ? FaceOn.Front : on);
        return (Vector3.TransformNormal(Vector3.UnitX, frame), Vector3.TransformNormal(Vector3.UnitZ, frame));
    }

    private void Kind(LitShader shader, SurfaceKind kind)
    {
        if (kind == _kind) return;
        _kind = kind;
        Raylib.SetShaderValue(shader.Shader, _pattern, (int)kind, ShaderUniformDataType.Int);
    }

    private void Tones(LitShader shader, Color a, Color b, Color c)
    {
        Set(shader, _tone1, ref _t1, a);
        Set(shader, _tone2, ref _t2, b);
        Set(shader, _tone3, ref _t3, c);
    }

    private static void Set(LitShader shader, int loc, ref Vector3 last, Color c)
    {
        var v = new Vector3(c.R, c.G, c.B) / 255f;
        if (v == last) return;
        last = v;
        Raylib.SetShaderValue(shader.Shader, loc, v, ShaderUniformDataType.Vec3);
    }
}
