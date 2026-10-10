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
void main()
{
    fragPos = vec3(matModel * vec4(vertexPosition, 1.0));
    fragNormal = vec3(matNormal * vec4(vertexNormal, 0.0));
    fragColor = vertexColor;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}";

        private const string Fragment = @"#version 330
in vec3 fragPos;
in vec3 fragNormal;
in vec4 fragColor;
uniform vec4 colDiffuse;
uniform vec3 viewPos;
uniform vec3 sunDir;
uniform vec3 fogColor;
uniform float fogDensity;
uniform float ambient;
out vec4 finalColor;
void main()
{
    vec3 n = normalize(fragNormal);
    if (!gl_FrontFacing) n = -n;
    float diffuse = max(dot(n, -sunDir), 0.0);
    vec4 base = fragColor * colDiffuse;
    vec3 lit = base.rgb * (ambient + (1.0 - ambient) * diffuse);
    float d = length(viewPos - fragPos) * fogDensity;
    float visible = clamp(exp(-d * d), 0.0, 1.0);
    finalColor = vec4(mix(fogColor, lit, visible), base.a);
}";

        private static LitShader? _shared;

        /// <summary>The shared instance, compiled on first use (needs the window's GL context).</summary>
        public static LitShader Shared => _shared ??= new LitShader();

        /// <summary>True once <see cref="Shared"/> exists — the camera only feeds a shader somebody draws with.</summary>
        public static bool IsCreated => _shared != null;

        public Shader Shader { get; }
        public Material Material { get; }
        /// <summary>False when the GL driver refused the program — Raylib then falls back to its default shader (unlit).</summary>
        public bool IsValid { get; }

        private readonly int _viewPos, _sunDir, _fogColor, _fogDensity, _ambient;

        /// <summary>Sky colour the fog fades into (also the sky gradient's horizon, <see cref="MapCamera3D"/>).</summary>
        public static readonly Color SkyHorizon = new(196, 214, 230, 255);
        public static readonly Color SkyTop = new(120, 160, 205, 255);

        private unsafe LitShader()
        {
            var shader = Raylib.LoadShaderFromMemory(Vertex, Fragment);
            IsValid = Raylib.IsShaderValid(shader);
            shader.Locs[(int)ShaderLocationIndex.MatrixModel] = Raylib.GetShaderLocation(shader, "matModel");
            shader.Locs[(int)ShaderLocationIndex.MatrixNormal] = Raylib.GetShaderLocation(shader, "matNormal");
            shader.Locs[(int)ShaderLocationIndex.VectorView] = Raylib.GetShaderLocation(shader, "viewPos");
            _viewPos = shader.Locs[(int)ShaderLocationIndex.VectorView];
            _sunDir = Raylib.GetShaderLocation(shader, "sunDir");
            _fogColor = Raylib.GetShaderLocation(shader, "fogColor");
            _fogDensity = Raylib.GetShaderLocation(shader, "fogDensity");
            _ambient = Raylib.GetShaderLocation(shader, "ambient");
            Shader = shader;

            var material = Raylib.LoadMaterialDefault();
            material.Shader = shader;
            Material = material;

            // The sun: high, from the south-west (HROT), so walls facing it and walls facing away differ.
            Raylib.SetShaderValue(shader, _sunDir, Vector3.Normalize(HrotToRaylib.Position(new Vector3(0.45f, 0.6f, -1f))), ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(shader, _fogColor, new Vector3(SkyHorizon.R, SkyHorizon.G, SkyHorizon.B) / 255f, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(shader, _ambient, 0.42f, ShaderUniformDataType.Float);
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
