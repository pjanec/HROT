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
uniform float depthBias;
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
    gl_Position.z -= depthBias * gl_Position.w;   // CE-1196: a ground layer drawn a few depth steps toward the eye (0 for the rest)
}";

        /// <summary>
        /// ⭐ <c>CE-1196</c> (docs/DESIGN_Map_3D_Mode.md §6z CA-4) — the FLAT program: vertex colour × the material / batch colour, unlit and
        /// unfogged (what Raylib's own shader draws), moved <c>depthBias</c> toward the eye like <see cref="SetDepthBias"/>. The gizmo fills
        /// lying on the ground draw with it, so they keep their colours and still never fight the ground.
        /// </summary>
        private const string FlatVertex = @"#version 330
in vec3 vertexPosition;
in vec4 vertexColor;
uniform mat4 mvp;
uniform float depthBias;
out vec4 fragColor;
void main()
{
    fragColor = vertexColor;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
    gl_Position.z -= depthBias * gl_Position.w;
}";

        private const string FlatFragment = @"#version 330
in vec4 fragColor;
uniform vec4 colDiffuse;
out vec4 finalColor;
void main() { finalColor = fragColor * colDiffuse; }";

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
uniform float fogStart;
uniform float ambient;
uniform float sunLight;
uniform float groundDetail;
out vec4 finalColor;
";

        private const string FragmentMain = @"
// CE-1328 — the ground's grass: value noise on the WORLD position (metres), so the ground slides past a walking eye
float grassHash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float grassNoise(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(grassHash(i), grassHash(i + vec2(1.0, 0.0)), u.x), mix(grassHash(i + vec2(0.0, 1.0)), grassHash(i + vec2(1.0, 1.0)), u.x), u.y);
}
vec3 grass(vec3 albedo, vec2 p, float near)
{
    float patches = grassNoise(p * 0.08);                                 // ~12 m: lighter / drier patches
    float tufts = grassNoise(p * 0.7) * 0.6 + grassNoise(p * 2.3) * 0.4;  // ~1 m tufts
    float blades = grassNoise(p * vec2(9.0, 23.0));                       // fine streaks
    float shade = 0.80 + 0.30 * tufts + near * 0.18 * (blades - 0.5);
    vec3 dry = albedo * vec3(1.18, 1.08, 0.72);
    return mix(albedo, dry, smoothstep(0.55, 0.8, patches) * 0.6) * shade;
}
// CE-1333 — the terrain's materials, drawn from the WORLD position (metres): the code rides in the vertex colour's alpha
// (TerrainLayer3D.MaterialCode), read only while the terrain draws (groundDetail > 0). A wall's pattern runs along the wall (u) and up (v).
vec3 brick(vec3 albedo, vec2 uv)
{
    float row = floor(uv.y / 0.085);
    float x = uv.x / 0.25 + 0.5 * mod(row, 2.0);
    vec2 f = vec2(fract(x), fract(uv.y / 0.085));
    float mortar = max(step(f.x, 0.05), step(f.y, 0.13));
    float tint = 0.82 + 0.32 * grassHash(vec2(floor(x), row));
    return mix(albedo * tint, vec3(0.70, 0.68, 0.64), mortar);
}
vec3 concrete(vec3 albedo, vec2 uv)
{
    vec2 f = vec2(fract(uv.x / 3.0), fract(uv.y / 2.9));
    float joint = max(step(f.x, 0.006), step(f.y, 0.008));
    float stain = 0.90 + 0.14 * grassNoise(uv * 0.6) + 0.06 * grassNoise(uv * vec2(5.0, 0.4));   // patches, and rain streaks down
    return albedo * stain * (1.0 - 0.28 * joint);
}
vec3 sandbags(vec3 albedo, vec2 uv)
{
    float row = floor(uv.y / 0.14);
    float x = uv.x / 0.55 + 0.5 * mod(row, 2.0);
    vec2 f = vec2(fract(x), fract(uv.y / 0.14));
    float bulge = pow(sin(3.14159 * f.x), 0.35) * pow(sin(3.14159 * f.y), 0.55);
    return albedo * (0.58 + 0.5 * bulge) * (0.92 + 0.16 * grassHash(vec2(floor(x), row)));
}
vec3 tiles(vec3 albedo, vec2 uv)
{
    float row = floor(uv.y / 0.30);
    float x = uv.x / 0.22 + 0.5 * mod(row, 2.0);
    vec2 f = vec2(fract(x), fract(uv.y / 0.30));
    float gap = step(f.x, 0.05);
    return albedo * (0.74 + 0.34 * f.y) * (1.0 - 0.3 * gap) * (0.93 + 0.12 * grassHash(vec2(floor(x), row)));
}
vec3 planks(vec3 albedo, vec2 uv)
{
    float row = floor(uv.y / 0.15);
    float gap = step(fract(uv.y / 0.15), 0.06);
    float grain = 0.88 + 0.18 * grassNoise(vec2(uv.x * 1.5, uv.y * 40.0)) + 0.1 * grassHash(vec2(row, 3.0));
    return albedo * grain * (1.0 - 0.4 * gap);
}
vec3 materialDetail(vec3 albedo, float code, vec3 world, vec3 n, float dist)
{
    if (groundDetail <= 0.0 || code > 250.5 || code < 240.5) return albedo;
    // the face's own frame: t runs level along it, b up its slope (a wall: straight up; a flat roof: north) — a pattern lies ON the face
    vec3 t = length(n.xz) < 0.05 ? vec3(1.0, 0.0, 0.0) : normalize(vec3(-n.z, 0.0, n.x));
    vec3 b = normalize(cross(n, t));
    vec2 uv = vec2(dot(world, t), abs(dot(world, b)));
    bool sloped = n.y > 0.3;   // a roof's top, flat or pitched
    float near = clamp(1.0 - dist / 90.0, 0.0, 1.0);
    float fine = clamp(1.0 - dist / 70.0, 0.0, 1.0);   // small courses (bricks, bags, planks) fade before they shimmer
    vec3 c = albedo;
    if (code > 249.5) c = n.y > 0.6 ? grass(albedo, world.xz, near) : albedo;                             // 250 open ground
    else if (code > 248.5) c = n.y > 0.6 ? grass(albedo * 0.9, world.xz * 1.3, near) : albedo;            // 249 forest floor
    else if (code > 247.5) c = mix(albedo, brick(albedo, uv), fine);                                      // 248 brick
    else if (code > 246.5) c = concrete(albedo, uv);                                                      // 247 concrete
    else if (code > 245.5) c = mix(albedo, sandbags(albedo, uv), fine);                                   // 246 sandbags
    else if (code > 244.5) c = sloped ? mix(albedo, tiles(albedo, uv), near) : concrete(albedo * 0.95, uv);  // 245 roof (its sides: concrete)
    else if (code > 243.5) c = mix(albedo, planks(albedo, uv), fine);                                     // 244 wood
    else if (code > 242.5) c = albedo * (0.82 + 0.3 * grassNoise(uv * 1.7));                              // 243 earth
    else if (code > 241.5) c = albedo * (0.72 + 0.45 * grassNoise(uv * 6.0) * grassNoise(uv * 2.0 + 7.0)); // 242 hedge
    else c = sloped ? mix(albedo, tiles(albedo, uv), near) : mix(albedo, brick(albedo, uv), fine);        // 241 brick building's roof
    return mix(albedo, c, groundDetail);
}
void main()
{
    vec3 n = normalize(fragNormal);
    if (!gl_FrontFacing) n = -n;
    float diffuse = max(dot(n, -sunDir), 0.0);
    vec4 base = fragColor * colDiffuse;
    vec3 albedo = surface(base.rgb, localUnit, localMetres, normalize(localNormal));
    albedo = materialDetail(albedo, floor(base.a * 255.0 + 0.5), fragPos, n, length(viewPos - fragPos));
    vec3 lit = albedo * (ambient + sunLight * diffuse);
    float d = max(length(viewPos - fragPos) - fogStart, 0.0) * fogDensity;   // CE-1146: the free camera's fog starts at what it looks at
    float visible = clamp(exp(-d * d), 0.0, 1.0);
    finalColor = vec4(mix(fogColor, lit, visible), groundDetail > 0.0 ? 1.0 : base.a);   // CE-1333: the terrain's alpha is its material code
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

        private readonly int _viewPos, _sunDir, _fogColor, _fogDensity, _fogStart, _ambient, _sunLight, _depthBias, _flatDepthBias, _groundDetail;

        /// <summary>⭐ <c>CE-1196</c> — the flat program (<see cref="FlatVertex"/>); <see cref="FlatIsValid"/> false when the driver refused it.</summary>
        public Shader Flat { get; }
        public bool FlatIsValid { get; }

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
            _fogStart = Raylib.GetShaderLocation(shader, "fogStart");
            _ambient = Raylib.GetShaderLocation(shader, "ambient");
            _sunLight = Raylib.GetShaderLocation(shader, "sunLight");
            _depthBias = Raylib.GetShaderLocation(shader, "depthBias");
            _groundDetail = Raylib.GetShaderLocation(shader, "groundDetail");
            Shader = shader;
            Raylib.SetShaderValue(shader, _depthBias, 0f, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(shader, _groundDetail, 0f, ShaderUniformDataType.Float);

            var flat = Raylib.LoadShaderFromMemory(FlatVertex, FlatFragment);
            FlatIsValid = Raylib.IsShaderValid(flat);
            _flatDepthBias = Raylib.GetShaderLocation(flat, "depthBias");
            Flat = flat;

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

        /// <summary>Per frame: where the eye is (Raylib coordinates), how far the fog lets you see, and ⭐ CE-1146 how far from the eye it
        /// starts (0 = at the eye — first person; the free camera starts it at what it looks at).</summary>
        public void ApplyFrame(Vector3 eyeRaylib, float fogDensity, float fogStart = 0f)
        {
            Raylib.SetShaderValue(Shader, _viewPos, eyeRaylib, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(Shader, _fogDensity, fogDensity, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(Shader, _fogStart, fogStart, ShaderUniformDataType.Float);
        }

        /// <summary>⭐ <c>CE-1196</c> (§6z CA-4) — moves what this program draws next <paramref name="ndc"/> toward the eye in clip depth
        /// (<see cref="MapCamera3D.GroundBiasNdc"/> for a layer lying on the ground; 0 for everything else). ⚠ It holds until set again —
        /// a layer that sets it puts it back to 0 after its draw.</summary>
        public void SetDepthBias(float ndc) => Raylib.SetShaderValue(Shader, _depthBias, ndc, ShaderUniformDataType.Float);

        /// <summary>⭐ <c>CE-1328</c> / <c>CE-1333</c> — paints the terrain's materials on what is drawn next (1 = full, 0 = off): grass on the
        /// open ground, brick, concrete panels, sandbags, roof tiles, planks, earth, hedge — world-anchored, so they slide past a moving eye.
        /// While it is on, the vertex colour's ALPHA is read as the material code (<c>TerrainLayer3D.MaterialCode</c>) and the output is
        /// opaque. ⚠ It holds until set again — the terrain layer sets it for its draw and puts it back to 0.</summary>
        public void SetGroundDetail(float amount) => Raylib.SetShaderValue(Shader, _groundDetail, amount, ShaderUniformDataType.Float);

        /// <summary>⭐ <c>CE-1196</c> — the same for the <see cref="Flat"/> program.</summary>
        public void SetFlatDepthBias(float ndc) => Raylib.SetShaderValue(Flat, _flatDepthBias, ndc, ShaderUniformDataType.Float);

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
