using System.Numerics;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>
    /// ⭐ CE-1033 S1 — the 3-D map's one shader (<c>docs/DESIGN_Map_3D_Mode.md</c> M5): vertex colour × material colour, lit by
    /// one sun (ambient + sun·N, two-sided so the terrain's winding does not matter), faded into the sky colour by distance fog.
    /// GLSL 330 — the version Raylib's desktop GL path and Mesa's software GL both take. Created on first use, inside a GL context.
    /// </summary>
    public sealed class LitShader
    {
        private const string Vertex = @"#version 330
in vec3 vertexPosition;
in vec3 vertexNormal;
in vec4 vertexColor;
uniform mat4 mvp;
uniform mat4 matModel;
uniform mat4 matNormal;
out vec3 fragPos;
out vec3 fragNormal;
out vec4 fragColor;
out vec3 localUnit;
out vec3 localMetres;
out vec3 localNormal;
void main()
{
    fragPos = vec3(matModel * vec4(vertexPosition, 1.0));
    fragNormal = vec3(matNormal * vec4(vertexNormal, 0.0));
    fragColor = vertexColor;
    // the mesh's own frame, in HROT axes (raylib-local (x, y, z) is HROT-local (x, -z, y), HrotToRaylib) — as a unit position, in metres
    // (each axis scaled by the length of its model-matrix column), and its normal: what a host's surface function draws on
    vec3 scale = vec3(length(matModel[0].xyz), length(matModel[1].xyz), length(matModel[2].xyz));
    vec3 metres = vertexPosition * scale;
    localUnit = vec3(vertexPosition.x, -vertexPosition.z, vertexPosition.y);
    localMetres = vec3(metres.x, -metres.z, metres.y);
    localNormal = vec3(vertexNormal.x, -vertexNormal.z, vertexNormal.y);
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}";

        /// <summary>The default surface: the colour as it is.</summary>
        public const string PlainSurface = "vec3 surface(vec3 base, vec3 unit, vec3 metres, vec3 n) { return base; }";

        private const string FragmentHead = @"#version 330
in vec3 fragPos;
in vec3 fragNormal;
in vec4 fragColor;
in vec3 localUnit;
in vec3 localMetres;
in vec3 localNormal;
uniform vec4 colDiffuse;
uniform vec3 viewPos;
uniform vec3 sunDir;
uniform vec3 fogColor;
uniform float fogDensity;
uniform float ambient;
uniform float sunLight;
out vec4 finalColor;
";

        private const string FragmentMain = @"
void main()
{
    vec3 n = normalize(fragNormal);
    if (!gl_FrontFacing) n = -n;
    float diffuse = max(dot(n, -sunDir), 0.0);
    vec4 base = fragColor * colDiffuse;
    vec3 albedo = surface(base.rgb, localUnit, localMetres, normalize(localNormal));
    vec3 lit = albedo * (ambient + sunLight * diffuse);
    float d = length(viewPos - fragPos) * fogDensity;
    float visible = clamp(exp(-d * d), 0.0, 1.0);
    finalColor = vec4(mix(fogColor, lit, visible), base.a);
}";

        private static LitShader? _shared;
        private static nint _sharedWindow;
        private static string _surface = PlainSurface;

        /// <summary>
        /// ⭐ CE-1101 (docs/DESIGN_Map_3D_Mode.md §6r S-1) — the host's ONE surface function, GLSL
        /// <c>vec3 surface(vec3 base, vec3 unit, vec3 metres, vec3 n)</c> with any uniforms it needs: it gets each fragment's colour and its
        /// place in the mesh's own frame (HROT axes; a unit position −0.5..0.5, the same in metres, and the normal) and returns the colour to
        /// light. ⚠ It must return <c>base</c> unless the host has set its own uniforms for the draw — every layer draws through it. A new
        /// function recompiles the shader on next use.
        /// </summary>
        public static void UseSurface(string glsl)
        {
            if (glsl == _surface) return;
            _surface = glsl;
            _shared = null;
        }

        /// <summary>The surface function the shader is (or will be) compiled with.</summary>
        public static string Surface => _surface;

        /// <summary>The shared instance, compiled on first use (needs the window's GL context). ⭐ A GL program dies with its context and
        /// its stale id still reads as valid — measured `2026-10-11`: a second 3-D frame rail in one test run drew only the sky. The
        /// window handle is checked, but GLFW can reuse the address, so code that opens a NEW window calls <see cref="Reset"/>.</summary>
        public static LitShader Shared
        {
            get
            {
                nint window = CurrentWindow();
                if (_shared == null || window != _sharedWindow)
                {
                    _shared = new LitShader();
                    _sharedWindow = window;
                }
                return _shared;
            }
        }

        private static unsafe nint CurrentWindow()
        {
            try { return (nint)Raylib.GetWindowHandle(); }
            catch (DllNotFoundException) { return 0; }
            catch (EntryPointNotFoundException) { return 0; }
        }

        /// <summary>Forget the shared instance, so the next <see cref="Shared"/> compiles in the CURRENT window's context — call after
        /// opening a new window in the same process (the frame rails do); the old program died with its context.</summary>
        public static void Reset() { _shared = null; _sharedWindow = 0; }

        /// <summary>True once <see cref="Shared"/> exists — the camera only feeds a shader somebody draws with.</summary>
        public static bool IsCreated => _shared != null;

        public Shader Shader { get; }
        public Material Material { get; }
        /// <summary>False when the GL driver refused the program — Raylib then falls back to its default shader (unlit).</summary>
        public bool IsValid { get; }

        private readonly int _viewPos, _sunDir, _fogColor, _fogDensity, _ambient, _sunLight;

        /// <summary>Sky colour the fog fades into (also the sky gradient's horizon, <see cref="MapCamera3D"/>).</summary>
        public static readonly Color SkyHorizon = new(196, 214, 230, 255);
        public static readonly Color SkyTop = new(120, 160, 205, 255);

        private unsafe LitShader()
        {
            var shader = Raylib.LoadShaderFromMemory(Vertex, FragmentHead + _surface + FragmentMain);
            IsValid = Raylib.IsShaderValid(shader);
            shader.Locs[(int)ShaderLocationIndex.MatrixModel] = Raylib.GetShaderLocation(shader, "matModel");
            shader.Locs[(int)ShaderLocationIndex.MatrixNormal] = Raylib.GetShaderLocation(shader, "matNormal");
            shader.Locs[(int)ShaderLocationIndex.VectorView] = Raylib.GetShaderLocation(shader, "viewPos");
            _viewPos = shader.Locs[(int)ShaderLocationIndex.VectorView];
            _sunDir = Raylib.GetShaderLocation(shader, "sunDir");
            _fogColor = Raylib.GetShaderLocation(shader, "fogColor");
            _fogDensity = Raylib.GetShaderLocation(shader, "fogDensity");
            _ambient = Raylib.GetShaderLocation(shader, "ambient");
            _sunLight = Raylib.GetShaderLocation(shader, "sunLight");
            Shader = shader;

            var material = Raylib.LoadMaterialDefault();
            material.Shader = shader;
            Material = material;

            // The sun: high, from the south-west (HROT), so walls facing it and walls facing away differ.
            Raylib.SetShaderValue(shader, _sunDir, Vector3.Normalize(HrotToRaylib.Position(new Vector3(0.45f, 0.6f, -1f))), ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(shader, _fogColor, new Vector3(SkyHorizon.R, SkyHorizon.G, SkyHorizon.B) / 255f, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(shader, _ambient, 0.42f, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(shader, _sunLight, 0.58f, ShaderUniformDataType.Float);
        }

        /// <summary>⭐ CE-1146 (docs/DESIGN_Map_3D_Mode.md §6aa) — per frame: the sun's direction, the light (ambient + sun) and the colour
        /// the fog fades into (the sky's horizon). <see cref="SceneAtmosphere.Default"/> reproduces the look the shader always had.</summary>
        public void ApplyAtmosphere(in SceneAtmosphere a)
        {
            var travel = HrotToRaylib.Position(-a.ToSun);   // the light travels AWAY from the sun
            Raylib.SetShaderValue(Shader, _sunDir, travel.LengthSquared() > 0f ? Vector3.Normalize(travel) : new Vector3(0f, -1f, 0f), ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(Shader, _fogColor, new Vector3(a.SkyHorizon.R, a.SkyHorizon.G, a.SkyHorizon.B) / 255f, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(Shader, _ambient, a.Ambient, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(Shader, _sunLight, a.SunLight, ShaderUniformDataType.Float);
        }

        /// <summary>Per frame: where the eye is (Raylib coordinates) and how far the fog lets you see.</summary>
        public void ApplyFrame(Vector3 eyeRaylib, float fogDensity)
        {
            Raylib.SetShaderValue(Shader, _viewPos, eyeRaylib, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(Shader, _fogDensity, fogDensity, ShaderUniformDataType.Float);
        }

        /// <summary>The shared material with its diffuse colour set to <paramref name="color"/>. ⚠ The colour lives in the shared
        /// material map, so it holds until the next call — draw with it immediately.</summary>
        public unsafe Material Tinted(Color color)
        {
            var m = Material;
            m.Maps[(int)MaterialMapIndex.Albedo].Color = color;
            return m;
        }
    }
}
