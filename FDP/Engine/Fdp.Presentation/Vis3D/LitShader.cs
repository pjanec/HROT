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

        /// <summary>
        /// ⭐ CE-4555 (docs/DESIGN_Map_3D_Mode.md §6ak G-1) — the INSTANCED vertex program: the same outputs as <see cref="Vertex"/>, but the model matrix,
        /// the colour (what <c>colDiffuse</c> is per draw) and five data vectors come per INSTANCE (<see cref="LitInstanceBatch"/>), so every copy of one
        /// mesh is one draw call. The data reach the surface function as <c>instData0..4</c> (flat), where a host maps them onto its own parameters
        /// (<c>BodySurface</c>'s <c>#ifdef INSTANCED</c>). The fragment program is the SAME text as the per-draw one (<see cref="FragmentSource"/>).
        /// Attribute locations are explicit, above raylib's defaults (0–5): the transform 6–9, the colour 10, the data 11–15.
        /// </summary>
        private const string InstancedVertex = @"#version 330
in vec3 vertexPosition;
in vec3 vertexNormal;
in vec4 vertexColor;
layout(location = 6) in mat4 instanceTransform;
layout(location = 10) in vec4 instanceColour;
layout(location = 11) in vec4 instanceData0;
layout(location = 12) in vec4 instanceData1;
layout(location = 13) in vec4 instanceData2;
layout(location = 14) in vec4 instanceData3;
layout(location = 15) in vec4 instanceData4;
uniform mat4 mvp;
out vec3 fragPos;
out vec3 fragNormal;
out vec4 fragColor;
out vec3 localUnit;
out vec3 localMetres;
out vec3 localNormal;
flat out vec4 instData0;
flat out vec4 instData1;
flat out vec4 instData2;
flat out vec4 instData3;
flat out vec4 instData4;
void main()
{
    fragPos = vec3(instanceTransform * vec4(vertexPosition, 1.0));
    fragNormal = vec3(transpose(inverse(instanceTransform)) * vec4(vertexNormal, 0.0));
    fragColor = vertexColor * instanceColour;
    vec3 scale = vec3(length(instanceTransform[0].xyz), length(instanceTransform[1].xyz), length(instanceTransform[2].xyz));
    vec3 metres = vertexPosition * scale;
    localUnit = vec3(vertexPosition.x, -vertexPosition.z, vertexPosition.y);
    localMetres = vec3(metres.x, -metres.z, metres.y);
    localNormal = vec3(vertexNormal.x, -vertexNormal.z, vertexNormal.y);
    instData0 = instanceData0; instData1 = instanceData1; instData2 = instanceData2; instData3 = instanceData3; instData4 = instanceData4;
    gl_Position = mvp * vec4(fragPos, 1.0);
}";

        /// <summary>⭐ CE-4555 — the per-instance attribute locations of <see cref="InstancedVertex"/> (the transform takes four).</summary>
        public const int InstanceTransformLocation = 6, InstanceColourLocation = 10, InstanceDataLocation = 11, InstanceDataVectors = 5;

        /// <summary>
        /// ⭐ CE-4555 — the fragment program's text: ONE source for both programs. The instanced one gets <c>#define INSTANCED</c> and the five flat
        /// <c>instData</c> inputs ahead of the host's surface function, so a surface written for both (<c>BodySurface</c>) reads its parameters
        /// from the instance there and from its uniforms in the per-draw program.
        /// </summary>
        private static string FragmentSource(string surface, bool instanced)
            => "#version 330\n"
             + (instanced ? "#define INSTANCED 1\nflat in vec4 instData0;\nflat in vec4 instData1;\nflat in vec4 instData2;\nflat in vec4 instData3;\nflat in vec4 instData4;\n" : "")
             + FragmentHead + surface + FragmentMain;

        private const string FragmentHead = @"
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
// a roof's top and a storey's floor — the same upward faces (CE-1341). User, 2026-10-30: a gray noisy surface that fits the interior floors and
// still looks good enough as the roof ⇒ a screed: ~3 m blotches, a fine grain, and a tiny aggregate speckle near
// the eye — ⛔ was rows of roof tiles, which read wrong on a floor
vec3 screed(vec3 albedo, vec2 uv, float near)
{
    float blotch = grassNoise(uv * 0.35);
    float grain = grassNoise(uv * 4.0) * 0.6 + grassNoise(uv * 11.0) * 0.4;
    float speck = grassHash(floor(uv * 40.0));
    return albedo * (0.86 + 0.16 * blotch + 0.16 * (grain - 0.5) + near * 0.10 * (speck - 0.5));
}
// ⭐ CE-4377 R-1 — a drawn PITCHED roof (never a floor, so CE-1341's screed does not apply): rows of 30 × 24 cm tiles up the slope,
// half-staggered, each row's lower lip in shadow, a per-tile tint
vec3 tiles(vec3 albedo, vec2 uv)
{
    float row = floor(uv.y / 0.24);
    float x = uv.x / 0.30 + 0.5 * mod(row, 2.0);
    vec2 f = vec2(fract(x), fract(uv.y / 0.24));
    float lip = smoothstep(0.0, 0.25, f.y);
    float edge = smoothstep(0.0, 0.06, f.x) * smoothstep(1.0, 0.94, f.x);
    return albedo * (0.62 + 0.38 * lip) * (0.85 + 0.15 * edge) * (0.9 + 0.2 * grassHash(vec2(floor(x), row)));
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
    if (groundDetail <= 0.0 || code > 250.5 || code < 239.5) return albedo;
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
    else if (code > 244.5) c = sloped ? screed(albedo, uv, near) : concrete(albedo * 0.95, uv);                 // 245 roof / floor (its sides: concrete)
    else if (code > 243.5) c = mix(albedo, planks(albedo, uv), fine);                                     // 244 wood
    else if (code > 242.5) c = albedo * (0.82 + 0.3 * grassNoise(uv * 1.7));                              // 243 earth
    else if (code > 241.5) c = albedo * (0.72 + 0.45 * grassNoise(uv * 6.0) * grassNoise(uv * 2.0 + 7.0)); // 242 hedge
    else if (code > 240.5) c = sloped ? screed(albedo, uv, near) : mix(albedo, brick(albedo, uv), fine); // 241 brick building's roof / floor
    else c = mix(albedo, tiles(albedo, uv), clamp(1.0 - dist / 160.0, 0.0, 1.0));                        // 240 a drawn pitched roof (CE-4377): rows read further than bricks
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
            var shader = Raylib.LoadShaderFromMemory(Vertex, FragmentSource(_surface, instanced: false));
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
        /// the fog fades into (the sky's horizon). <see cref="SceneAtmosphere.Default"/> reproduces the look the shader always had.
        /// ⭐ CE-4555 — the instanced program (<see cref="Instanced"/>) gets the same values.</summary>
        public void ApplyAtmosphere(in SceneAtmosphere a)
        {
            var travel = HrotToRaylib.Position(-a.ToSun);   // the light travels AWAY from the sun
            _sunTravel = travel.LengthSquared() > 0f ? Vector3.Normalize(travel) : new Vector3(0f, -1f, 0f);
            _fog = new Vector3(a.SkyHorizon.R, a.SkyHorizon.G, a.SkyHorizon.B) / 255f;
            _ambientValue = a.Ambient;
            _sunLightValue = a.SunLight;
            SetAtmosphere(Shader, _sunDir, _fogColor, _ambient, _sunLight);
            if (_instancedBuilt && InstancedIsValid) SetAtmosphere(_instanced, _iSunDir, _iFogColor, _iAmbient, _iSunLight);
        }

        private void SetAtmosphere(Shader s, int sunDir, int fogColor, int ambient, int sunLight)
        {
            Raylib.SetShaderValue(s, sunDir, _sunTravel, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(s, fogColor, _fog, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(s, ambient, _ambientValue, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(s, sunLight, _sunLightValue, ShaderUniformDataType.Float);
        }

        /// <summary>Per frame: where the eye is (Raylib coordinates), how far the fog lets you see, and ⭐ CE-1146 how far from the eye it
        /// starts (0 = at the eye — first person; the free camera starts it at what it looks at). ⭐ CE-4555 — the instanced program too.</summary>
        public void ApplyFrame(Vector3 eyeRaylib, float fogDensity, float fogStart = 0f)
        {
            _eye = eyeRaylib; _fogDensityValue = fogDensity; _fogStartValue = fogStart;
            SetFrame(Shader, _viewPos, _fogDensity, _fogStart);
            if (_instancedBuilt && InstancedIsValid) SetFrame(_instanced, _iViewPos, _iFogDensity, _iFogStart);
        }

        private void SetFrame(Shader s, int viewPos, int fogDensity, int fogStart)
        {
            Raylib.SetShaderValue(s, viewPos, _eye, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(s, fogDensity, _fogDensityValue, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(s, fogStart, _fogStartValue, ShaderUniformDataType.Float);
        }

        // ⭐ CE-4555 — the last atmosphere and frame values, so the instanced program, compiled on first use mid-frame, starts with this frame's
        private Vector3 _sunTravel = Vector3.Normalize(HrotToRaylib.Position(new Vector3(0.45f, 0.6f, -1f))), _fog = new Vector3(SkyHorizon.R, SkyHorizon.G, SkyHorizon.B) / 255f, _eye;
        private float _ambientValue = 0.42f, _sunLightValue = 0.58f, _fogDensityValue, _fogStartValue;

        private Shader _instanced;
        private bool _instancedBuilt;
        private int _iViewPos, _iSunDir, _iFogColor, _iFogDensity, _iFogStart, _iAmbient, _iSunLight, _iMvp, _iVertexColor;

        /// <summary>⭐ CE-4555 (§6ak G-1) — false when the GL driver refused the instanced program; <see cref="LitInstanceBatch"/> then draws each
        /// instance with the per-draw program instead (the same picture, one call each).</summary>
        public bool InstancedIsValid { get; private set; }

        /// <summary>⭐ CE-4555 — the instanced program (<see cref="InstancedVertex"/> + the SAME fragment source), compiled on first use, with this
        /// frame's atmosphere and eye already set. Unlit uniforms it does not use (<c>depthBias</c>, <c>groundDetail</c>) are 0, <c>colDiffuse</c> white —
        /// the colour comes per instance.</summary>
        public Shader Instanced
        {
            get
            {
                if (_instancedBuilt) return _instanced;
                _instancedBuilt = true;
                var s = Raylib.LoadShaderFromMemory(InstancedVertex, FragmentSource(_surface, instanced: true));
                InstancedIsValid = Raylib.IsShaderValid(s);
                _instanced = s;
                if (!InstancedIsValid) return s;
                _iViewPos = Raylib.GetShaderLocation(s, "viewPos");
                _iSunDir = Raylib.GetShaderLocation(s, "sunDir");
                _iFogColor = Raylib.GetShaderLocation(s, "fogColor");
                _iFogDensity = Raylib.GetShaderLocation(s, "fogDensity");
                _iFogStart = Raylib.GetShaderLocation(s, "fogStart");
                _iAmbient = Raylib.GetShaderLocation(s, "ambient");
                _iSunLight = Raylib.GetShaderLocation(s, "sunLight");
                _iMvp = Raylib.GetShaderLocation(s, "mvp");
                _iVertexColor = Raylib.GetShaderLocationAttrib(s, "vertexColor");
                Raylib.SetShaderValue(s, Raylib.GetShaderLocation(s, "depthBias"), 0f, ShaderUniformDataType.Float);
                Raylib.SetShaderValue(s, Raylib.GetShaderLocation(s, "groundDetail"), 0f, ShaderUniformDataType.Float);
                Raylib.SetShaderValue(s, Raylib.GetShaderLocation(s, "colDiffuse"), Vector4.One, ShaderUniformDataType.Vec4);
                SetAtmosphere(s, _iSunDir, _iFogColor, _iAmbient, _iSunLight);
                SetFrame(s, _iViewPos, _iFogDensity, _iFogStart);
                return s;
            }
        }

        /// <summary>⭐ CE-4555 — the instanced program's <c>mvp</c> uniform location and its <c>vertexColor</c> attribute location (−1 when unused).</summary>
        internal int InstancedMvpLocation => _iMvp;
        internal int InstancedVertexColorLocation => _iVertexColor;

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
