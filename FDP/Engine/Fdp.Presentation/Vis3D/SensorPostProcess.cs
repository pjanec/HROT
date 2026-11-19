using System;
using System.Numerics;
using Fdp.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>
    /// ⭐ <c>CE-1984</c> (📄 docs/designs/sensor-view/DESIGN_Sensor_View.md D-1, D-3, §4b, §6) — THE shared sensor post-process of the 3-D map: the scene is
    /// drawn into <see cref="_scene"/> (window size, with depth), then
    /// <list type="number">
    /// <item>a BRIGHT pass to a quarter-size target: the part above the bloom threshold in rgb, the luminance in alpha;</item>
    /// <item>two blur passes (horizontal, vertical) — the halo; then that target's MIPMAPS, whose last level is the picture's mean luminance (the tube's
    /// automatic gain, read on the GPU — no read-back, D-8);</item>
    /// <item>the FINAL pass to the screen: the sensor's response, the gain, the halo, the sensor resolution, the noise, the palette, the vignette;</item>
    /// <item>the scene's DEPTH is blitted onto the screen's, so the screen-space cards drawn after (CE-4582) are still hidden behind what the scene drew.</item>
    /// </list>
    /// A host sets <see cref="Settings"/> each frame (Hrot's <c>SensorView</c>); <see cref="Sensor"/> = <see cref="SceneSensor.None"/> ⇒ the canvas draws
    /// as it always did. 0 B per steady frame: the targets are made on first use and on a resize only. ⭐ Under the hidden-window mode the frame's own
    /// target is resumed after ours (<see cref="Fdp.Presentation.Raylib.HiddenWindow.Resume"/>).
    /// </summary>
    public sealed class SensorPostProcess : IScenePostProcess
    {
        /// <summary>What the host asks for this frame.</summary>
        public SensorPostSettings Settings;

        /// <inheritdoc/>
        public SceneSensor Sensor => Available ? Settings.Mode : SceneSensor.None;

        /// <inheritdoc/>
        public float AirC => Settings.AirC;

        /// <summary>False when the programs did not compile (logged once): the view then stays normal.</summary>
        public bool Available => !_failed;

        /// <summary>A rail's probe: the picture's mean luminance as the last frame's 1×1 target holds it (a GPU read-back — allocates; never per frame).</summary>
        public float ProbeMean()
        {
            if (_chain.Length == 0 || _chain[^1].Id == 0) return float.NaN;
            var img = Raylib.LoadImageFromTexture(_chain[^1].Texture);
            var c = Raylib.GetImageColor(img, 0, 0);
            Raylib.UnloadImage(img);
            return c.A / 255f;
        }

        /// <summary>Frames processed (a rail; telemetry).</summary>
        public int Frames { get; private set; }

        private RenderTexture2D _scene, _q1, _q2;
        private RenderTexture2D[] _chain = Array.Empty<RenderTexture2D>();   // the halving chain to 1×1: the picture's mean
        private Shader _bright, _blur, _final;
        private IntPtr _window;
        private bool _failed, _loaded;
        private int _frame;
        private int _blurDir, _blurTexel, _bMode, _bLevel, _bSpan;
        private int _fLevel, _fSpan;
        private int _fHalo, _fMode, _fPalette, _fGain, _fNoise, _fSeed, _fLines, _fFlags, _fSize, _fTarget, _fMip;

        private const int GlReadFramebuffer = 0x8CA8, GlDrawFramebuffer = 0x8CA9, GlDepthBufferBit = 0x100;

        /// <summary>rlgl 5's <c>rlBlitFramebuffer</c> with its real arguments — ⚠ Raylib-cs 7.0.2 declares it with NONE (<c>Rlgl.BlitFramebuffer()</c>), which would
        /// pass garbage; the native function takes nine ints and blits with GL_NEAREST.</summary>
        [System.Runtime.InteropServices.DllImport(Raylib.NativeLibName, EntryPoint = "rlBlitFramebuffer", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern void BlitFramebuffer(int srcX, int srcY, int srcWidth, int srcHeight, int dstX, int dstY, int dstWidth, int dstHeight, int bufferMask);

        /// <inheritdoc/>
        public void BeginScene(Vector2 size)
        {
            int w = Math.Max(16, (int)size.X), h = Math.Max(16, (int)size.Y);
            if (!Ensure(w, h)) return;
            Raylib.BeginTextureMode(_scene);
            Raylib.ClearBackground(Color.Black);
        }

        /// <inheritdoc/>
        public void EndScene(Vector2 size)
        {
            if (!_loaded) return;
            Raylib.EndTextureMode();
            int w = _scene.Texture.Width, h = _scene.Texture.Height, qw = _q1.Texture.Width, qh = _q1.Texture.Height;

            // ① bright + luminance, quarter size
            Rlgl.DrawRenderBatchActive();
            Rlgl.DisableColorBlend();   // the passes write their values as they are (the luminance rides in alpha)
            Raylib.BeginTextureMode(_q1);
            Raylib.ClearBackground(Color.Black);
            Raylib.BeginShaderMode(_bright);
            SetInt(_bright, _bMode, (int)Settings.Mode);
            SetFloat(_bright, _bLevel, Settings.LevelC);
            SetFloat(_bright, _bSpan, MathF.Max(0.5f, Settings.SpanC));
            Raylib.DrawTexturePro(_scene.Texture, new Rectangle(0, 0, w, -h), new Rectangle(0, 0, qw, qh), Vector2.Zero, 0f, Color.White);
            Raylib.EndShaderMode();
            Raylib.EndTextureMode();
            // ② the halo: horizontal into q2, vertical back into q1
            // the halo: two rounds (a tube's bloom is wide), horizontal into q2, vertical back into q1
            for (int round = 0; round < 2; round++)
            {
                Blur(_q1, _q2, new Vector2(1f + round, 0f), qw, qh);
                Blur(_q2, _q1, new Vector2(0f, 1f + round), qw, qh);
            }
            // the mean: halve q1 down to 1×1, each step a bilinear 2×2 average (robust on every driver — mipmaps were not)
            var from = _q1;
            for (int i = 0; i < _chain.Length; i++)
            {
                var to = _chain[i];
                Raylib.BeginTextureMode(to);
                Raylib.DrawTexturePro(from.Texture, new Rectangle(0, 0, from.Texture.Width, -from.Texture.Height),
                                      new Rectangle(0, 0, to.Texture.Width, to.Texture.Height), Vector2.Zero, 0f, Color.White);
                Raylib.EndTextureMode();
                from = to;
            }
            Rlgl.DrawRenderBatchActive();
            Rlgl.EnableColorBlend();
            var halo = _q1.Texture;
            var mean = _chain.Length > 0 ? _chain[^1].Texture : halo;

            // back to the frame's own target (the window, or the hidden-window mode's)
            Fdp.Presentation.Raylib.HiddenWindow.Resume();
            uint frameFbo = Fdp.Presentation.Raylib.HiddenWindow.CurrentFramebuffer;

            // ④ the scene's depth onto the frame's, so cards drawn after are still hidden behind the scene (CE-4582)
            Rlgl.DrawRenderBatchActive();
            Rlgl.BindFramebuffer(GlReadFramebuffer, _scene.Id);
            Rlgl.BindFramebuffer(GlDrawFramebuffer, frameFbo);
            BlitFramebuffer(0, 0, w, h, 0, 0, w, h, GlDepthBufferBit);
            Rlgl.BindFramebuffer(GlReadFramebuffer, frameFbo);
            Rlgl.BindFramebuffer(GlDrawFramebuffer, frameFbo);

            // ③ the final picture
            var s = Settings;
            Raylib.BeginShaderMode(_final);
            // the halo on texture unit 1, bound by hand: raylib's SetShaderValueTexture slot is cleared by the batch flush the scene texture's
            // draw can trigger, and the sampler then reads an empty unit (measured: no halo, a constant automatic gain)
            Rlgl.ActiveTextureSlot(1);
            Rlgl.EnableTexture(halo.Id);
            Rlgl.ActiveTextureSlot(2);
            Rlgl.EnableTexture(mean.Id);
            Rlgl.ActiveTextureSlot(0);
            SetInt(_final, _fHalo, 1);
            SetInt(_final, _fMip, 2);
            SetInt(_final, _fMode, (int)s.Mode);
            SetInt(_final, _fPalette, s.Palette);
            SetFloat(_final, _fGain, s.Gain);
            SetFloat(_final, _fNoise, s.Noise ? s.NoiseSigma : 0f);
            SetFloat(_final, _fSeed, (_frame++ % 997) * 0.61803f);
            SetFloat(_final, _fLines, s.SensorResolution ? s.SensorLines : 0f);
            SetInt(_final, _fFlags, (s.Halo ? 1 : 0) | (s.Vignette ? 2 : 0));
            SetVec2(_final, _fSize, new Vector2(w, h));
            SetFloat(_final, _fTarget, s.TargetMean);
            SetFloat(_final, _fLevel, s.LevelC);
            SetFloat(_final, _fSpan, MathF.Max(0.5f, s.SpanC));
            Raylib.DrawTexturePro(_scene.Texture, new Rectangle(0, 0, w, -h), new Rectangle(0, 0, w, h), Vector2.Zero, 0f, Color.White);
            Raylib.EndShaderMode();   // flushes the batch with unit 1 still bound
            Rlgl.ActiveTextureSlot(2);
            Rlgl.DisableTexture();
            Rlgl.ActiveTextureSlot(1);
            Rlgl.DisableTexture();
            Rlgl.ActiveTextureSlot(0);
            Frames++;
        }

        private void Blur(RenderTexture2D from, RenderTexture2D to, Vector2 dir, int qw, int qh)
        {
            Raylib.BeginTextureMode(to);
            Raylib.BeginShaderMode(_blur);
            SetVec2(_blur, _blurDir, dir);
            SetVec2(_blur, _blurTexel, new Vector2(1f / qw, 1f / qh));
            Raylib.DrawTexturePro(from.Texture, new Rectangle(0, 0, qw, -qh), new Rectangle(0, 0, qw, qh), Vector2.Zero, 0f, Color.White);
            Raylib.EndShaderMode();
            Raylib.EndTextureMode();
        }

        /// <summary>The targets and programs for this GL context and size (a new window = a new context, as <see cref="CardOcclusion"/>).</summary>
        private unsafe bool Ensure(int w, int h)
        {
            if (!Raylib.IsWindowReady()) return _loaded = false;
            var window = (IntPtr)Raylib.GetWindowHandle();
            if (window != _window) { _window = window; _failed = false; _shadersReady = false; _scene = _q1 = _q2 = default; _chain = Array.Empty<RenderTexture2D>(); }
            if (_failed) return _loaded = false;
            if (!_shadersReady && !LoadShaders()) { _failed = true; return _loaded = false; }
            if (_scene.Id == 0 || _scene.Texture.Width != w || _scene.Texture.Height != h)
            {
                if (_scene.Id != 0)
                {
                    Raylib.UnloadRenderTexture(_scene); Raylib.UnloadRenderTexture(_q1); Raylib.UnloadRenderTexture(_q2);
                    foreach (var c in _chain) Raylib.UnloadRenderTexture(c);
                }
                _scene = Raylib.LoadRenderTexture(w, h);
                _q1 = Raylib.LoadRenderTexture(Math.Max(4, w / 4), Math.Max(4, h / 4));
                _q2 = Raylib.LoadRenderTexture(Math.Max(4, w / 4), Math.Max(4, h / 4));
                Raylib.SetTextureFilter(_scene.Texture, TextureFilter.Bilinear);
                Raylib.SetTextureFilter(_q2.Texture, TextureFilter.Bilinear);
                Raylib.SetTextureFilter(_q1.Texture, TextureFilter.Bilinear);
                var sizes = new System.Collections.Generic.List<RenderTexture2D>();
                // first to the largest power of two at or under the quarter target (every source texel still feeds a sample), then exact halvings:
                // ⚠ halving an ODD size with bilinear SAMPLES instead of averaging (measured: 5×3 → 2×1 missed a fire in the middle entirely)
                int cw = 1 << (int)MathF.Floor(MathF.Log2(_q1.Texture.Width)), ch = 1 << (int)MathF.Floor(MathF.Log2(_q1.Texture.Height));
                while (true)
                {
                    var c = Raylib.LoadRenderTexture(cw, ch);
                    Raylib.SetTextureFilter(c.Texture, TextureFilter.Bilinear);
                    sizes.Add(c);
                    if (cw == 1 && ch == 1) break;
                    cw = Math.Max(1, cw / 2); ch = Math.Max(1, ch / 2);
                }
                _chain = sizes.ToArray();
            }
            return _loaded = true;
        }

        private bool _shadersReady;

        private bool LoadShaders()
        {
            _bright = Raylib.LoadShaderFromMemory(PassVertex, BrightFragment);
            _blur = Raylib.LoadShaderFromMemory(PassVertex, BlurFragment);
            _final = Raylib.LoadShaderFromMemory(PassVertex, FinalFragment);
            uint def = Rlgl.GetShaderIdDefault();
            if (_bright.Id == 0 || _bright.Id == def || _blur.Id == 0 || _blur.Id == def || _final.Id == 0 || _final.Id == def)
            {
                Console.Error.WriteLine("[SensorPostProcess] a sensor program did not compile — the sensor views stay off");
                return false;
            }
            _blurDir = Raylib.GetShaderLocation(_blur, "dir");
            _bMode = Raylib.GetShaderLocation(_bright, "mode");
            _bLevel = Raylib.GetShaderLocation(_bright, "levelC");
            _bSpan = Raylib.GetShaderLocation(_bright, "spanC");
            _fLevel = Raylib.GetShaderLocation(_final, "levelC");
            _fSpan = Raylib.GetShaderLocation(_final, "spanC");
            _blurTexel = Raylib.GetShaderLocation(_blur, "texel");
            _fHalo = Raylib.GetShaderLocation(_final, "halo");
            _fMode = Raylib.GetShaderLocation(_final, "mode");
            _fPalette = Raylib.GetShaderLocation(_final, "palette");
            _fGain = Raylib.GetShaderLocation(_final, "gain");
            _fNoise = Raylib.GetShaderLocation(_final, "noise");
            _fSeed = Raylib.GetShaderLocation(_final, "seed");
            _fLines = Raylib.GetShaderLocation(_final, "lines");
            _fFlags = Raylib.GetShaderLocation(_final, "flags");
            _fSize = Raylib.GetShaderLocation(_final, "size");
            _fTarget = Raylib.GetShaderLocation(_final, "targetMean");
            _fMip = Raylib.GetShaderLocation(_final, "meanTex");
            return _shadersReady = true;
        }

        private static unsafe void SetFloat(Shader s, int loc, float v) => Raylib.SetShaderValue(s, loc, &v, ShaderUniformDataType.Float);
        private static unsafe void SetInt(Shader s, int loc, int v) => Raylib.SetShaderValue(s, loc, &v, ShaderUniformDataType.Int);
        private static unsafe void SetVec2(Shader s, int loc, Vector2 v) => Raylib.SetShaderValue(s, loc, &v, ShaderUniformDataType.Vec2);

        /// <summary>The bloom threshold on the scene's luminance: a self-luminous thing (drawn near-white, <see cref="SceneSensors.Emissive"/>) is above it,
        /// a lit surface (the night-vision scene peaks ≈ 0.7) below it.</summary>
        public const float BloomThreshold = 0.88f;

        /// <summary>raylib's own textured-quad vertex program (GLSL 330), so the passes take <c>fragTexCoord</c> as every texture draw does.</summary>
        private const string PassVertex = @"#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;
uniform mat4 mvp;
out vec2 fragTexCoord;
out vec4 fragColor;
void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}";

        // The spectral response: an image intensifier sees red and near-infrared best (a Gen-3 tube peaks ~ 600–900 nm); the same weights in the bright
        // pass and the final pass.
        private const string Response = "const vec3 NV_RESPONSE = vec3(0.50, 0.40, 0.10);\n";

        private const string Decode = "float decodeC(vec3 rgb) { return (rgb.r * 255.0 + rgb.g) / 255.0 * 160.0 - 40.0; }\n";

        private const string BrightFragment = "#version 330\nin vec2 fragTexCoord;\nuniform sampler2D texture0;\nuniform int mode;\nuniform float levelC;\nuniform float spanC;\nout vec4 finalColor;\n" + Response + Decode + @"
void main()
{
    vec3 c = texture(texture0, fragTexCoord).rgb;
    float lum = dot(c, NV_RESPONSE);
    float bright = max(max(c.r, max(c.g, c.b)) - 0.88, 0.0) / 0.12;
    if (mode == 2)   // thermal: the picture's value, and what is hotter than the span's top (a fire) blooms
    {
        float v = (decodeC(c) - levelC) / spanC;
        lum = clamp(v, 0.0, 1.0);
        bright = clamp(v - 1.0, 0.0, 1.0);
    }
    // the light the tube's automatic gain feels: a self-luminous source is far brighter than the 8-bit scene can hold (a flash ~10³× the
    // moonlit ground), so its saturated pixels count ×10; stored /11 to stay in 0..1 (the final pass multiplies back)
    finalColor = vec4(vec3(bright), (lum + 10.0 * bright) / 11.0);
}";

        private const string BlurFragment = @"#version 330
in vec2 fragTexCoord;
uniform sampler2D texture0;
uniform vec2 dir;
uniform vec2 texel;
out vec4 finalColor;
void main()
{
    // a 9-tap Gaussian (σ ≈ 2 texels of the quarter-size target = 8 screen pixels)
    vec4 sum = texture(texture0, fragTexCoord) * 0.2270270270;
    vec2 o1 = dir * texel * 1.3846153846, o2 = dir * texel * 3.2307692308;
    sum += (texture(texture0, fragTexCoord + o1) + texture(texture0, fragTexCoord - o1)) * 0.3162162162;
    sum += (texture(texture0, fragTexCoord + o2) + texture(texture0, fragTexCoord - o2)) * 0.0702702703;
    finalColor = sum;
}";

        private const string FinalFragment = "#version 330\nin vec2 fragTexCoord;\nuniform sampler2D texture0;\nuniform sampler2D halo;\nuniform float levelC;\nuniform float spanC;\n" + Response + Decode + @"
uniform int mode;          // 1 night vision, 2 thermal
uniform int palette;       // NV: 0 green P43, 1 white P45 · thermal: 0 white-hot, 1 black-hot, 2 ironbow
uniform float gain;        // the host's gain (night vision: < 1 below the tube's floor)
uniform float noise;       // σ of the sensor noise, 0 = off
uniform float seed;        // per frame
uniform float lines;       // the sensor's lines, 0 = full resolution
uniform int flags;         // 1 halo, 2 vignette
uniform vec2 size;         // the target, pixels
uniform float targetMean;  // the automatic gain's target mean luminance
uniform sampler2D meanTex; // 1×1: the picture's mean luminance in alpha
out vec4 finalColor;

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

vec3 scene(vec2 uv, vec2 cell)
{
    if (lines <= 0.0) return texture(texture0, uv).rgb;
    vec2 d = 0.5 / cell;   // a soft sensor: four taps half a sensor pixel apart
    return 0.25 * (texture(texture0, uv + vec2(d.x, d.y)).rgb + texture(texture0, uv + vec2(-d.x, d.y)).rgb
                 + texture(texture0, uv + vec2(d.x, -d.y)).rgb + texture(texture0, uv + vec2(-d.x, -d.y)).rgb);
}

vec3 ironbow(float v)
{
    v = clamp(v, 0.0, 1.0);
    vec3 a = vec3(0.04, 0.03, 0.12), b = vec3(0.43, 0.08, 0.55), c = vec3(0.90, 0.35, 0.16), d = vec3(0.98, 0.78, 0.24), e = vec3(1.0, 1.0, 0.92);
    if (v < 0.3) return mix(a, b, v / 0.3);
    if (v < 0.6) return mix(b, c, (v - 0.3) / 0.3);
    if (v < 0.85) return mix(c, d, (v - 0.6) / 0.25);
    return mix(d, e, (v - 0.85) / 0.15);
}

void main()
{
    vec2 uv = fragTexCoord;
    vec2 cell = lines > 0.0 ? vec2(lines * size.x / size.y, lines) : size;
    vec3 c = scene(uv, cell);
    vec4 h = (flags & 1) != 0 ? texture(halo, uv) : vec4(0.0);
    float v;
    if (mode == 1)
    {
        // leaves reflect near-infrared: a green-dominant surface reads brighter than its visible luminance
        float nir = max(c.g - max(c.r, c.b), 0.0) * 1.2;
        float lum = dot(c, NV_RESPONSE) + nir;
        // the automatic gain: the picture's mean (the halo target's last mip) brought toward the target — a flash in view pulls the rest down
        float mean = texture(meanTex, vec2(0.5)).a * 11.0;
        float abc = targetMean > 0.0 ? clamp(targetMean / max(mean, 0.01), 0.25, 6.0) : 1.0;   // off: unity gain
        v = lum * abc * gain + h.r * 1.4;
    }
    else v = (decodeC(c) - levelC) / spanC + h.r * 0.8;   // ⭐ CE-1985 — thermal: the scene pass wrote the °C; the gain is the CPU's level / span
    if (noise > 0.0)
    {
        vec2 g = floor(uv * cell);
        float n = hash(g + seed * 17.0) - 0.5;
        float sparkle = step(1.0 - noise * 0.02, hash(g * 1.7 + seed * 31.0));   // scintillation: a few bright specks as the light falls
        v += n * 2.0 * noise + sparkle * 0.8;
    }
    v = clamp(v, 0.0, 1.0);
    vec3 col;
    if (mode == 1) col = palette == 1 ? v * vec3(0.94, 0.97, 1.0) : vec3(0.02, 0.05, 0.02) + v * vec3(0.55, 0.95, 0.45);
    else col = palette == 2 ? ironbow(v) : vec3(palette == 1 ? 1.0 - v : v);
    if ((flags & 2) != 0)
    {
        vec2 q = (uv - 0.5) * vec2(size.x / size.y, 1.0);
        col *= smoothstep(0.95, 0.45, length(q));
    }
    finalColor = vec4(col, 1.0);
}";
    }

    /// <summary>⭐ <c>CE-1984</c> — what a host asks the sensor post-process for, per frame (DESIGN_Sensor_View D-11).</summary>
    public struct SensorPostSettings
    {
        public SceneSensor Mode;
        /// <summary>Night vision: 0 green (P43), 1 white (P45). Thermal: 0 white-hot, 1 black-hot, 2 ironbow.</summary>
        public int Palette;
        /// <summary>The host's gain (night vision: 1 above the tube's floor, less below it).</summary>
        public float Gain;
        public bool Noise;
        /// <summary>σ of the noise when on (night vision: grows as the light falls).</summary>
        public float NoiseSigma;
        public bool Halo;
        public bool SensorResolution;
        /// <summary>The sensor's lines when <see cref="SensorResolution"/> is on.</summary>
        public float SensorLines;
        public bool Vignette;
        /// <summary>The automatic gain's target mean luminance.</summary>
        public float TargetMean;
        /// <summary>⭐ CE-1985 — thermal: the air's °C at the view, and the gain: the °C shown black (<see cref="LevelC"/>) and the range up to white.</summary>
        public float AirC, LevelC, SpanC;
    }
}
