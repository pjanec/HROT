using System;
using System.Numerics;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>
    /// ⭐⭐ <c>CE-4582</c> (docs/DESIGN_Map_3D_Mode.md §6am D-1, D-2, D-8) — the unit cards and the place labels of the 3-D map, hidden behind
    /// whatever the 3-D pass drew in front of their ANCHOR: drawn in screen space after the pass, each at its anchor's depth, tested against
    /// the depth buffer the pass left behind (depth test on, depth writes off). Step 1 of §6am — trees in front of a unit cut its card like
    /// any obstacle.
    /// <para>📐 How a card gets its depth with no draw call of its own: inside <see cref="Push(Vector3)"/> rlgl's transform is translated by
    /// <c>depth × <see cref="DepthScale"/></c> in z, so every vertex of the card CARRIES its anchor's depth; a small program
    /// (<see cref="Fragment"/>) writes it as the fragment's depth and places the vertex on the 2-D plane. ⛔ Not the 2-D projection's own depth
    /// (the design's first wording): rlgl adds 1/20 000 to the 2-D depth after every shape within a batch (up to ~0.4 of the whole range), which
    /// would swamp the precision a perspective depth needs; here that drift is divided by <see cref="DepthScale"/> (≈ one depth step).</para>
    /// <para>0 B per frame: arithmetic and rlgl's matrix stack; the program is loaded once per GL context.</para>
    /// </summary>
    public sealed class CardOcclusion : GizmoMap.Presentation.ICardOcclusion
    {
        /// <summary>The z a unit of window depth becomes in the vertex: large against rlgl's 2-D depth drift (≤ ~0.4), small enough that a
        /// float still resolves ~one 24-bit depth step (ulp at 1e7 = 1 ⇒ 1e-7).</summary>
        public const float DepthScale = 1e7f;

        /// <summary>The switch (Units ▸ Hide labels behind terrain). Null ⇒ on.</summary>
        public Func<bool>? Enabled { get; set; }

        /// <summary>A card's anchor in the world (HROT, z up) by network id — the point its card hangs from (lifted to the body's top). Null ⇒ unknown.</summary>
        public Func<long, Vector3?>? AnchorOf { get; set; }

        /// <summary>True between <see cref="Begin"/> and <see cref="End"/>.</summary>
        public bool Active { get; private set; }

        /// <summary>Cards and labels drawn depth-tested in the current / last scope (a rail; telemetry).</summary>
        public int Tested { get; private set; }

        private MapCamera3D? _camera;
        private int _pushed;

        /// <summary>Opens the scope for <paramref name="camera"/>'s frame: false (nothing changes) when switched off or the program is unavailable.</summary>
        public bool Begin(MapCamera3D camera)
        {
            End();
            Tested = 0;
            if (Enabled != null && !Enabled()) return false;
            if (!EnsureShader()) return false;
            _camera = camera;
            Rlgl.DrawRenderBatchActive();   // what was drawn before is on top as before
            Rlgl.EnableDepthTest();
            Rlgl.DisableDepthMask();        // a card hides nothing: it is only hidden
            Raylib.BeginShaderMode(_shader);
            Active = true;
            return true;
        }

        /// <inheritdoc/>
        public void End()
        {
            while (_pushed > 0) Pop();
            if (!Active) return;
            Raylib.EndShaderMode();         // flushes what the scope drew, with the test on
            Rlgl.DisableDepthTest();
            Rlgl.EnableDepthMask();
            Active = false;
            _camera = null;
        }

        /// <inheritdoc/>
        public bool Push(long networkId)
        {
            var at = AnchorOf?.Invoke(networkId);
            if (at is { } world) return Push(world);
            PushDepth(0f);   // unknown: on top (depth 0 passes everything)
            return false;
        }

        /// <summary>Everything drawn until <see cref="Pop"/> is tested at the depth of <paramref name="world"/> (HROT).</summary>
        public bool Push(Vector3 world)
        {
            if (!Active || _camera == null) { PushDepth(0f); return false; }
            PushDepth(DepthOf(_camera, world));
            Tested++;
            return true;
        }

        /// <inheritdoc/>
        public void Pop()
        {
            if (_pushed == 0) return;
            Rlgl.PopMatrix();
            _pushed--;
        }

        private void PushDepth(float depth)
        {
            Rlgl.PushMatrix();
            Rlgl.Translatef(0f, 0f, depth * DepthScale);
            _pushed++;
        }

        /// <summary>
        /// The window depth (0 near … 1 far) the camera's projection gives <paramref name="world"/> — the value the 3-D pass wrote for a surface
        /// there: <c>f (z − n) / ((f − n) z)</c>, z the distance along the view axis, n / f the camera's clip planes (the lens shift of CE-4084
        /// moves x and y only). In front of the near plane ⇒ 0 (never hidden); beyond the far plane ⇒ 1. Pure — a rail checks it without GL.
        /// </summary>
        public static float DepthOf(MapCamera3D camera, Vector3 world)
        {
            float z = Vector3.Dot(world - camera.Eye, camera.Forward);
            float n = camera.NearClip, f = camera.FarClip;
            if (z <= n) return 0f;
            if (z >= f) return 1f;
            return f * (z - n) / ((f - n) * z);
        }

        // ── the program: raylib's default one, plus the depth the vertex carries ────────────────────────────────────────────────────────
        /// <summary>The vertex program: the depth rides in the vertex's z (<see cref="DepthScale"/>); the vertex itself is put on the 2-D plane.</summary>
        public const string Vertex = @"#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;
uniform mat4 mvp;
uniform float depthScale;
out vec2 fragTexCoord;
out vec4 fragColor;
out float fragDepth;
void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    fragDepth = clamp((vertexPosition.z + 1.0) / depthScale, 0.0, 1.0);
    gl_Position = mvp * vec4(vertexPosition.xy, -0.5, 1.0);
}";

        /// <summary>The fragment program: raylib's default colour, the carried depth as the fragment's.</summary>
        public const string Fragment = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
in float fragDepth;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
out vec4 finalColor;
void main()
{
    finalColor = texture(texture0, fragTexCoord) * colDiffuse * fragColor;
    gl_FragDepth = fragDepth;
}";

        private static Shader _shader;
        private static IntPtr _shaderWindow;
        private static bool _shaderFailed;

        /// <summary>Loads the program for this GL context (a new window = a new context, as <c>EnsureWireSphere</c>). False ⇒ draw as before.</summary>
        private static unsafe bool EnsureShader()
        {
            if (!Raylib.IsWindowReady()) return false;
            var window = (IntPtr)Raylib.GetWindowHandle();
            if (window == _shaderWindow && _shader.Id != 0) return true;
            if (window == _shaderWindow && _shaderFailed) return false;
            _shaderWindow = window;
            _shader = Raylib.LoadShaderFromMemory(Vertex, Fragment);
            _shaderFailed = _shader.Id == 0 || _shader.Id == Rlgl.GetShaderIdDefault();
            if (_shaderFailed) { Console.Error.WriteLine("[CardOcclusion] the card depth program did not compile — cards are drawn on top"); return false; }
            int loc = Raylib.GetShaderLocation(_shader, "depthScale");
            float scale = DepthScale;
            Raylib.SetShaderValue(_shader, loc, &scale, ShaderUniformDataType.Float);
            return true;
        }
    }
}
