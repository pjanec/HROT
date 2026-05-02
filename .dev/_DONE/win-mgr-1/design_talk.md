
i would like to discuss the extension of fdp.runner (see window manager source) and the extension of imgui toolkit (see Icons source). Let's starts with the icons.

Extending the ImGui toolkit to support a robust colored icon system is a great starting point. From a clean architecture perspective, building reusable, stateless immediate-mode GUI components ensures our UI remains maintainable and decoupled from the business logic.

Here is how we should structure the extension for the icons based on the requirements:

**1\. Texture Atlas Management** The framework must support rendering colored icons from a consolidated texture atlas, such as the `famfamfam-silk` icon set. The proposal suggests using a string-based coordinate system (e.g., `'b12'` to represent the second row and 12th column) to address individual icons on this checkerboard texture. _Architectural note: While string coordinates are flexible, I highly recommend abstracting these behind strongly typed constants or an enum mapping at the API boundary to prevent magic-string typos and ensure compile-time safety._

**2\. Core Rendering API** To provide flexibility for standard layouts and custom overlays, the toolkit needs two primary rendering functions:

-   An inline render function that draws the icon at the current layout position and automatically advances the X position cursor behind it.-   An absolute/relative render function that draws the icon at a specific set of coordinates without affecting the current layout cursor.

**3\. Interactive Widgets** Following the immediate-mode paradigm, we need to build specialized public functions for interactive elements:

-   **Clickable Icon Buttons:** This function must handle interaction logic and respond to mouse events by visually indicating hovered, pressed, and depressed states.-   **Toggle Icons:** A stateful variant that takes its current toggle state (e.g., a boolean) as an input parameter and visually reflects whether it is toggled on or off.

**Integration & Extensibility** Establishing this clean, modular icon API will immediately benefit other proposed UI extensions. For example, the framework is expected to support a status bar where various clients and subsystems can register their own sections. Because our icon widgets will be decoupled and standard, these subsystems will be able to effortlessly render custom colored icons and respond to user clicks within their reserved status bar sections.

We will also need these icons (like custom pin and cross/close icons) for the `fdp.runner` generic window manager implementation. How would you like to approach the architecture for the window manager and subsystem injection next?

how can we add support for texture atlas?

To implement a robust texture atlas system for the `famfamfam-silk` colored icons, we should decouple the asset management from the immediate-mode rendering logic. Here is the architectural approach to achieve this cleanly within the `FDP.Toolkit.ImGui` ecosystem:

**1\. Asset Management & UV Resolution (****IconAtlas** **Registry)** First, we need a dedicated class to load and own the single atlas texture in VRAM. To support the requested checkerboard string coordinates (where 'b12' represents the second row and 12th column), we must parse these strings and convert them into UV coordinates.

-   Implement a parser that translates the character component to a row index (e.g., 'a' = 0, 'b' = 1) and the numeric component to a column index.-   Multiply these indices by the fixed icon dimension (typically 16x16 for famfamfam-silk) to yield the source `Rectangle` (UV coordinates).-   _Best Practice:_ To avoid string allocations on the 60 FPS hot path, compute the UVs and pass them to the ImGui backend, rather than doing string parsing inside the render loop.

**2\. Core Rendering Extensions** We must extend our ImGui utility classes to provide the requested foundational rendering functions. Using `rlImGui.Image` (which bridges Raylib textures to ImGui), we will implement two primary drawing modes:

-   **Inline Rendering:** Create a function that calculates the UV vectors, calls the image render API, and then automatically invokes `ImGui.SameLine()` so the layout cursor immediately advances behind the X position of the icon.-   **Absolute Rendering:** Create a variant that uses `ImGui.SetCursorScreenPos()` or the `ImDrawList` directly to draw the icon at a specific, absolute coordinate without disrupting the standard layout flow.

**3\. Interactive Widget Abstractions** Following immediate-mode GUI patterns, we will build stateful wrappers around ImGui's internal button logic to create our requested interactive controls:

-   **Clickable Icon Button:** Build a public wrapper around `ImGui.ImageButton()`. This native ImGui function already handles hit-testing, but we will augment it with custom style pushes (`ImGui.PushStyleColor`) to visually indicate hovered, pressed, and depressed states.-   **Toggle Icon:** Implement a function that takes its current toggle state (e.g., a boolean) as an input parameter. We can visually reflect the toggled state by altering the image's tint color (e.g., darkening it or lowering the alpha when disabled) or by rendering a secondary visual overlay.

By isolating the UV coordinate math and texture binding within this dedicated utility class, any subsystem injected into the framework will be able to effortlessly render icons using simple string identifiers, keeping the UI layout code clean and maintainable.

pls show some code skeletons demonstrating your suggested approach

Here are the code skeletons demonstrating the decoupled architecture for the colored icon system. To maintain a clean boundary, we encapsulate the `famfamfam-silk` texture and the string coordinate parser (e.g., converting 'b12' into UV vectors) into a dedicated registry.

```
using System;
using System.Numerics;
using Raylib_cs;

namespace FDP.Toolkit.ImGui.Icons
{
    /// <summary>
    /// Manages the consolidated texture atlas and resolves checkerboard coordinates to UVs.
    /// </summary>
    public class IconAtlas : IDisposable
    {
        private Texture2D _atlasTexture;
        private readonly float _iconSize;
        private readonly float _atlasWidth;
        private readonly float _atlasHeight;

        public IntPtr TextureId => new IntPtr(_atlasTexture.Id);
        public Vector2 IconSizeVec => new Vector2(_iconSize, _iconSize);

        public IconAtlas(string texturePath, float iconSize = 16f)
        {
            _atlasTexture = Raylib.LoadTexture(texturePath);
            _iconSize = iconSize;
            _atlasWidth = _atlasTexture.Width;
            _atlasHeight = _atlasTexture.Height;
        }

        /// <summary>
        /// Converts a checkerboard string like "b12" to UV coordinates (uv0 = top-left, uv1 = bottom-right).
        /// </summary>
        public (Vector2 uv0, Vector2 uv1) GetUvCoordinates(string coordinate)
        {
            if (string.IsNullOrEmpty(coordinate) || coordinate.Length < 2)
                return (Vector2.Zero, Vector2.One);

            // 'a' = 0, 'b' = 1, etc.
            int row = char.ToLower(coordinate) - 'a';
            
            // "12" -> index 11 (assuming 1-based columns in the string)
            if (!int.TryParse(coordinate.Substring(1), out int col))
                col = 1;
            
            col -= 1; 

            float u0 = (col * _iconSize) / _atlasWidth;
            float v0 = (row * _iconSize) / _atlasHeight;
            float u1 = u0 + (_iconSize / _atlasWidth);
            float v1 = v0 + (_iconSize / _atlasHeight);

            return (new Vector2(u0, v0), new Vector2(u1, v1));
        }

        public void Dispose()
        {
            if (_atlasTexture.Id != 0)
            {
                Raylib.UnloadTexture(_atlasTexture);
                _atlasTexture.Id = 0;
            }
        }
    }
}
```

Next, we build the stateless public API providing the requested functions: inline rendering (moving the X position behind the icon), absolute positioning, clickable buttons that visually indicate hovered and pressed states, and toggle icons.

```
using System.Numerics;
// global using Gui = ImGuiNET.ImGui; is assumed per toolkit configuration

namespace FDP.Toolkit.ImGui.Icons
{
    /// <summary>
    /// Immediate-mode widgets for rendering atlas-based colored icons.
    /// </summary>
    public static class IconWidgets
    {
        /// <summary>
        /// Renders the colored icon at the current layout cursor and automatically advances the X position behind it [1].
        /// </summary>
        public static void InlineIcon(IconAtlas atlas, string coordinate)
        {
            var (uv0, uv1) = atlas.GetUvCoordinates(coordinate);
            
            Gui.Image(atlas.TextureId, atlas.IconSizeVec, uv0, uv1);
            Gui.SameLine(); // Moves the X position behind the rendered icon
        }

        /// <summary>
        /// Renders the colored icon at a specific absolute coordinate without affecting the standard layout flow [1].
        /// </summary>
        public static void AbsoluteIcon(IconAtlas atlas, string coordinate, Vector2 screenPos)
        {
            var (uv0, uv1) = atlas.GetUvCoordinates(coordinate);
            
            var drawList = Gui.GetWindowDrawList();
            drawList.AddImage(atlas.TextureId, screenPos, screenPos + atlas.IconSizeVec, uv0, uv1);
        }

        /// <summary>
        /// Renders a clickable icon button that responds to mouse hover and clicks by visually indicating state [1].
        /// </summary>
        public static bool IconButton(IconAtlas atlas, string id, string coordinate)
        {
            var (uv0, uv1) = atlas.GetUvCoordinates(coordinate);
            
            // ImGui.ImageButton automatically handles HitTesting and visual push/depress states
            return Gui.ImageButton(id, atlas.TextureId, atlas.IconSizeVec, uv0, uv1);
        }

        /// <summary>
        /// Renders a toggle icon that takes its current toggle state as an input parameter [1].
        /// </summary>
        public static bool ToggleIcon(IconAtlas atlas, string id, string coordinate, ref bool isToggled)
        {
            var (uv0, uv1) = atlas.GetUvCoordinates(coordinate);
            
            // Visually alter the background or tint color to reflect the toggled state
            Vector4 bgCol = isToggled ? new Vector4(0.2f, 0.6f, 0.2f, 1.0f) : new Vector4(0, 0, 0, 0);
            Vector4 tintCol = isToggled ? new Vector4(1, 1, 1, 1) : new Vector4(0.6f, 0.6f, 0.6f, 1.0f);
            
            Gui.PushStyleColor(ImGuiNET.ImGuiCol.Button, bgCol);
            
            if (Gui.ImageButton(id, atlas.TextureId, atlas.IconSizeVec, uv0, uv1, bgCol, tintCol))
            {
                isToggled = !isToggled;
                Gui.PopStyleColor();
                return true;
            }
            
            Gui.PopStyleColor();
            return false;
        }
    }
}
```

Subsystems registered to render a section of the status bar can then seamlessly utilize these widget methods to draw their custom icons and easily handle user clicks within their reserved sections.

how to make the icons buttons/toggles to respond to hover (change visual look to indicate the icon is active and can be clicked)? maybe by drawing a rectanle around it? How to visually indicate that an icon button is currently pressed - like adding a small one pixel shift to the right and down so the icon visually moves a bit? How to express toggled state - maybe by slightly changing the background?

To achieve this level of custom visual feedback while adhering to clean architecture principles, we must separate the component's **hit-testing logic** from its **rendering logic**. Relying on ImGui's default `ImageButton` restricts us to its internal styling rules, making pixel-perfect shifts and custom borders cumbersome.

Instead, the best practice in immediate-mode GUI development is to use an `InvisibleButton` to handle the state machine (hovered, active/pressed, clicked) and use the `ImDrawList` to manually render the visuals exactly as you described.

Here is how we architect the `ToggleIcon` and `IconButton` to support your custom states:

**1\. Hit-Testing & State Resolution** We allocate space and register the control's ID using `ImGui.InvisibleButton()`. This function returns `true` if clicked, and allows us to immediately query `ImGui.IsItemHovered()` and `ImGui.IsItemActive()` (which means the mouse is currently held down on it).

**2\. The Rendering Pipeline** Once we have the state, we grab the current window's `ImDrawList` and build the visuals in layers:

-   **Background (Toggled):** If the input state is true, we draw a filled rectangle behind the icon with a subtle highlight color.-   **Image (Pressed Shift):** We calculate the render coordinate. If `IsItemActive()` is true, we add a `(1, 1)` pixel offset to the render position before drawing the texture UVs.-   **Border (Hovered):** If `IsItemHovered()` is true, we draw an outlined rectangle around the original bounds.

Here is the implementation extending our `IconWidgets` class:

```
using System.Numerics;
// global using Gui = ImGuiNET.ImGui; 

namespace FDP.Toolkit.ImGui.Icons
{
    public static class IconWidgets
    {
        /// <summary>
        /// A custom toggle icon that manually handles hover, press, and toggle visualization.
        /// </summary>
        public static bool ToggleIcon(IconAtlas atlas, string id, string coordinate, ref bool isToggled)
        {
            var (uv0, uv1) = atlas.GetUvCoordinates(coordinate);
            Vector2 size = atlas.IconSizeVec;
            
            // Capture the starting screen position before allocating the button
            Vector2 screenPos = Gui.GetCursorScreenPos();

            // 1. Hit Testing: Allocate space and handle input routing without drawing anything
            bool clicked = Gui.InvisibleButton(id, size);
            bool isHovered = Gui.IsItemHovered();
            bool isPressed = Gui.IsItemActive();

            if (clicked) 
            {
                isToggled = !isToggled;
            }

            var drawList = Gui.GetWindowDrawList();

            // 2. Render Background (Toggled State)
            if (isToggled)
            {
                // Subtle gray background for toggled state
                uint bgColor = Gui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
                drawList.AddRectFilled(screenPos, screenPos + size, bgColor);
            }

            // 3. Render Image (Pressed State Shift)
            Vector2 imagePos = screenPos;
            if (isPressed)
            {
                // Shift 1 pixel right and down when pressed
                imagePos.X += 1.0f;
                imagePos.Y += 1.0f;
            }
            
            drawList.AddImage(atlas.TextureId, imagePos, imagePos + size, uv0, uv1);

            // 4. Render Border (Hovered State)
            if (isHovered)
            {
                // Bright outline when hovered
                uint borderColor = Gui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
                drawList.AddRect(screenPos, screenPos + size, borderColor);
            }

            return clicked;
        }

        /// <summary>
        /// A standard clickable icon button using the same custom visual logic.
        /// </summary>
        public static bool IconButton(IconAtlas atlas, string id, string coordinate)
        {
            // We can reuse the exact same logic, passing a dummy toggle state 
            // that is forced to false, achieving pure button behavior.
            bool dummyState = false;
            return ToggleIcon(atlas, id, coordinate, ref dummyState);
        }
    }
}
```

**Why this is the right architecture:** By utilizing `InvisibleButton` combined with `ImDrawList`, we completely fulfill the requirement for a public function that visually indicates hovered, pressed, and depressed states while taking a toggle state as an input parameter. This approach is entirely stateless, produces zero garbage collection allocations per frame, and gives you absolute control over the pixel-perfect rendering of your UI toolkit.

good! i will also need two new types of user elements

1.  toggle icon that changes the icon in toggled state (2-icons alternating when clicking on them - replaces the specific toggled visualization of a single icon)
    
2.  a multi-face pull-down-radio-button icon which on click shows a little context menu with different "faces" (different icons) and once one of them is selected, the menu disappears and the icon turns into the selected one.
    

These additions fit perfectly into the immediate-mode GUI paradigm. From an architectural standpoint, adding these as stateless static methods ensures we maintain a zero-allocation hot path and keep our view logic cleanly separated from the underlying state.

_(Note: The specific implementation of these two new widget types is an architectural extension based on your requirements and is not directly present in the provided source materials. You may need to adapt the method signatures slightly depending on your exact internal layout utilities.)_

Here is how we can implement both of these elegantly within our `IconWidgets` class:

1\. The Alternating Face Toggle Icon

Instead of relying on background colors to indicate state, we simply evaluate the boolean state parameter _before_ we resolve the UV coordinates. We continue to use the `InvisibleButton` and `ImDrawList` approach to maintain our pixel-perfect click-shift and hover borders.

```
/// <summary>
/// A toggle icon that completely swaps its visual face (texture coordinates) based on the active state.
/// </summary>
public static bool AlternatingFaceToggleIcon(IconAtlas atlas, string id, string trueCoordinate, string falseCoordinate, ref bool isToggled)
{
    // Resolve the correct UVs based on the current state
    string activeCoordinate = isToggled ? trueCoordinate : falseCoordinate;
    var (uv0, uv1) = atlas.GetUvCoordinates(activeCoordinate);
    Vector2 size = atlas.IconSizeVec;

    Vector2 screenPos = Gui.GetCursorScreenPos();
    
    // 1. Hit Testing
    bool clicked = Gui.InvisibleButton(id, size);
    bool isHovered = Gui.IsItemHovered();
    bool isPressed = Gui.IsItemActive();

    if (clicked) 
    {
        isToggled = !isToggled;
    }

    var drawList = Gui.GetWindowDrawList();

    // 2. Render Image (with pressed state shift)
    Vector2 imagePos = screenPos;
    if (isPressed)
    {
        imagePos.X += 1.0f;
        imagePos.Y += 1.0f;
    }
    
    drawList.AddImage(atlas.TextureId, imagePos, imagePos + size, uv0, uv1);

    // 3. Render Border (Hovered State)
    if (isHovered)
    {
        uint borderColor = Gui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
        drawList.AddRect(screenPos, screenPos + size, borderColor);
    }

    return clicked;
}
```

2\. The Multi-Face Dropdown Icon

For the dropdown radio button, we need to bind a popup menu to the button's click event. By utilizing ImGui's `BeginPopup` and `PushID` stack, we can render a clean grid of available icons.

Passing the available coordinates as an `IReadOnlyList<string>` allows you to pass a pre-allocated array or list from your subsystems, avoiding any garbage collection overhead during the render loop.

```
/// <summary>
/// A multi-face icon that opens a context-menu grid of alternative icons when clicked.
/// </summary>
public static bool DropdownFaceIcon(IconAtlas atlas, string id, IReadOnlyList<string> availableCoordinates, ref int selectedIndex)
{
    // Safety clamp to ensure we don't read out of bounds
    if (selectedIndex < 0 || selectedIndex >= availableCoordinates.Count)
        selectedIndex = 0;

    string currentCoordinate = availableCoordinates[selectedIndex];
    var (uv0, uv1) = atlas.GetUvCoordinates(currentCoordinate);
    Vector2 size = atlas.IconSizeVec;

    Vector2 screenPos = Gui.GetCursorScreenPos();
    
    // 1. Render the main button using our custom hit-test pipeline
    bool clicked = Gui.InvisibleButton(id, size);
    bool isHovered = Gui.IsItemHovered();
    bool isPressed = Gui.IsItemActive();

    var drawList = Gui.GetWindowDrawList();
    Vector2 imagePos = screenPos;
    if (isPressed) { imagePos.X += 1.0f; imagePos.Y += 1.0f; }

    drawList.AddImage(atlas.TextureId, imagePos, imagePos + size, uv0, uv1);

    if (isHovered)
    {
        uint borderColor = Gui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
        drawList.AddRect(screenPos, screenPos + size, borderColor);
    }

    // 2. Handle the Popup state machine
    string popupId = $"popup_{id}";
    if (clicked)
    {
        Gui.OpenPopup(popupId);
    }

    bool selectionChanged = false;

    // Tighten the padding so the grid looks like a cohesive tool palette
    Gui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
    
    if (Gui.BeginPopup(popupId))
    {
        const int iconsPerRow = 4; // Configurable grid width
        
        for (int i = 0; i < availableCoordinates.Count; i++)
        {
            if (i > 0 && i % iconsPerRow != 0)
                Gui.SameLine();

            var (pUv0, pUv1) = atlas.GetUvCoordinates(availableCoordinates[i]);

            // Push a unique ID for each item in the loop to prevent ImGui ID collisions
            Gui.PushID(i);
            
            // Native ImageButton is fine here since it's inside a standard popup
            if (Gui.ImageButton("##choice", atlas.TextureId, size, pUv0, pUv1))
            {
                selectedIndex = i;
                selectionChanged = true;
                Gui.CloseCurrentPopup();
            }
            
            Gui.PopID();
        }
        Gui.EndPopup();
    }
    
    Gui.PopStyleVar();

    return selectionChanged;
}
```

Architectural Takeaway

By designing these components this way:

-   **No hidden state:** The UI relies strictly on the `ref bool` and `ref int` passed into it, remaining a pure projection of your application's state.-   **Zero string-allocations in the hot path:** We avoid `string.Format` inside the render loop (except for the static `popupId` which could be pre-cached by the caller if extreme micro-optimization is necessary).-   **Encapsulation:** The consumer of the API doesn't need to know anything about `ImDrawList`, UV math, or ImGui popup lifecycles. They just pass the data and get a `true/false` return indicating if a mutation occurred.

good! let's now move to the window manager. First pls evaluate th proposal if it is consistent from the user perspective (regarding stuff like the window visibility control via menus and pinning in relation to currently active subsystem perspective)

Evaluating the proposed window manager from a user experience (UX) and architectural standpoint, the design is quite robust, but there are a few edge cases and consistency factors we must address to ensure a seamless experience.

**The Good: Smart Defaults and Clear Hierarchy** The core proposal of using "perspectives" to group UI elements, while allowing individual windows to be "pinned" across perspectives, is an excellent pattern for complex, multi-subsystem applications.

From a UX perspective, the most brilliant decision in this proposal is the auto-pinning behavior. If a user is currently in Subsystem A's perspective, but goes into the fixed "Windows" main menu and explicitly opens a window belonging to Subsystem B, the system automatically opens it in a pinned state. This perfectly anticipates user intent; it prevents the frustrating scenario where a user clicks "Show Window" and nothing happens because the parent perspective is hidden.

Furthermore, exempting "Debug" windows from the perspective system entirely is a solid architectural choice, ensuring developers and operators never lose their diagnostics when switching views.

**Potential UX Inconsistencies & Friction Points** As a software architect, I see a few areas where the interaction logic needs careful handling to prevent confusing the user:

-   **The "Unpin Vanish" Effect:** According to the rules, an unpinned window is only shown if its corresponding perspective is selected. If a user opens Window B while in Perspective A (which auto-pins it), and then clicks the custom 2-state pin icon to _unpin_ it, the window will instantaneously vanish. While technically correct according to the state machine, this can feel like a glitch to the user. _Recommendation:_ When an unpin action will result in immediate hiding, the UI should ideally provide a brief visual fade or a tooltip explaining "Unpinning will hide this window in the current perspective."-   **Global Menu Clutter:** The proposal states that the Main Menu is _not_ affected by the currently selected perspective. However, it also allows any subsystem to inject items into the main menu using an "a/b/c/leaf" path convention. If every loaded subsystem injects its own operational menus globally, the top menu bar will quickly become an overwhelming, cluttered mess of irrelevant actions. _Recommendation:_ We should enforce a strict separation. The Main Menu should contain global configurations, perspective switchers, and the fixed "Windows" visibility toggles. Subsystem-specific actions should ideally be housed within the client areas of their respective ImGui windows, rather than polluting the global menu.-   **Persistence of the "Pinned" State:** The proposal specifies that window layouts should be auto-saved to `imgui.ini` and that we must explicitly remember the custom "hidden" state. To maintain consistency, we must also serialize the "pinned" state into this configuration. If a user spends time curating a cross-perspective dashboard of pinned windows, they will expect that exact layout and pin configuration to survive an application restart.

By addressing the "unpin vanish" edge case and strictly policing what gets injected into the global menu bar, we can implement this generic window manager cleanly without compromising the user's mental model of the application.

ad #1 - the tooltip will do

ad #2 - agreed. window-specific menus are great idea - let's support it. Windows could show optional local menu bar (not shown if not defined). But still the subsystem should be able to inject stuff to global menu, not affected by perspective switching.

ad #3 - agreed.

This refined approach strikes an excellent balance between modularity and user experience. Supporting optional local window menus natively within the window instances prevents global menu bloat, while still honoring the requirement that subsystems can inject critical actionable or checkable items into the global menu using the slash path convention.

From a clean architecture perspective, we should model this with a robust separation of concerns:

-   **MenuRegistry**: A trie-like data structure that parses the `"a/b/c/leaf"` paths and builds the global menu bar independently of the windowing logic.-   **ManagedWindow**: An abstract base class that encapsulates the ImGui lifecycle, the custom title bar (with pin/close icons and the requested tooltip), and the optional local menu bar.-   **WindowManager**: The central orchestrator that evaluates the perspective state machine, handles auto-pinning, and orchestrates persistence.

Here are the architectural code skeletons demonstrating this design.

1\. The Managed Window Base Class

This class is responsible for rendering its own client area and optional menu bar, as well as handling the custom title bar icons. We inject our previously discussed colored icons here.

```
using ImGuiNET;
using System.Numerics;
// global using Gui = ImGuiNET.ImGui; 

namespace FDP.Toolkit.ImGui.WindowManager
{
    public abstract class ManagedWindow
    {
        public string Id { get; }
        public string Title { get; }
        public string OwningPerspective { get; }
        public bool IsDebugWindow { get; }
        
        public bool IsOpen { get; set; } = false;
        public bool IsPinned { get; set; } = false;
        
        // Subclasses can override this to enable local window menus
        protected virtual bool HasMenuBar => false;

        protected ManagedWindow(string id, string title, string perspective, bool isDebug = false)
        {
            Id = id;
            Title = title;
            OwningPerspective = perspective;
            IsDebugWindow = isDebug;
        }

        public void Render(string currentPerspective, Icons.IconAtlas atlas)
        {
            if (!IsOpen) return;

            // Visibility rule: Debug windows always show. Others show if pinned OR if their perspective is active [2, 5].
            bool perspectiveActive = (OwningPerspective == currentPerspective);
            if (!IsDebugWindow && !IsPinned && !perspectiveActive) 
                return;

            ImGuiWindowFlags flags = ImGuiWindowFlags.None;
            if (HasMenuBar) flags |= ImGuiWindowFlags.MenuBar;

            if (Gui.Begin($"{Title}###{Id}", ref IsOpen, flags))
            {
                DrawCustomTitleBarControls(currentPerspective, perspectiveActive, atlas);

                if (HasMenuBar && Gui.BeginMenuBar())
                {
                    DrawLocalMenuBar();
                    Gui.EndMenuBar();
                }

                DrawClientArea();
            }
            Gui.End();
        }

        private void DrawCustomTitleBarControls(string currentPerspective, bool perspectiveActive, Icons.IconAtlas atlas)
        {
            // Position icons at the top right of the window
            float iconSize = atlas.IconSizeVec.X;
            Gui.SameLine(Gui.GetWindowWidth() - (iconSize * 2) - Gui.GetStyle().ItemSpacing.X - 10f);
            
            // Ad #1: The Pin icon with the unpin-vanish tooltip [2]
            bool pinState = IsPinned;
            if (Icons.IconWidgets.AlternatingFaceToggleIcon(atlas, $"pin_{Id}", "pin_on", "pin_off", ref pinState))
            {
                IsPinned = pinState;
            }
            
            if (!IsPinned && !perspectiveActive && Gui.IsItemHovered())
            {
                Gui.SetTooltip("Unpinning will hide this window in the current perspective.");
            }

            Gui.SameLine();

            // The Close (Hide) icon [2]
            if (Icons.IconWidgets.IconButton(atlas, $"close_{Id}", "cross"))
            {
                IsOpen = false;
                IsPinned = false; // "If the window is pinned and closed... the pinned state is turned off" [3]
            }
        }

        protected virtual void DrawLocalMenuBar() { }
        protected abstract void DrawClientArea();
    }
}
```

2\. The Global Menu Registry

To handle the `"a/b/c/leaf"` slash convention, we need a node-based structure that the subsystems can populate during initialization.

```
using System;
using System.Collections.Generic;

namespace FDP.Toolkit.ImGui.WindowManager
{
    public class MenuItemNode
    {
        public string Name { get; set; }
        public Action OnClick { get; set; }
        public Func<bool> GetCheckedState { get; set; }
        public Action<bool> OnCheckedChanged { get; set; }
        public bool IsSeparator { get; set; }
        
        // Ordering hints could be added here (e.g., float SortOrder or Before/After strings) [1]
        public Dictionary<string, MenuItemNode> Children { get; } = new();
    }

    public class GlobalMenuRegistry
    {
        public MenuItemNode Root { get; } = new MenuItemNode { Name = "Root" };

        public void RegisterItem(string path, Action onClick)
        {
            // Parses "File/Export/Data" and builds the tree nodes
            var parts = path.Split('/');
            var current = Root;
            foreach (var part in parts)
            {
                if (!current.Children.ContainsKey(part))
                    current.Children[part] = new MenuItemNode { Name = part };
                current = current.Children[part];
            }
            current.OnClick = onClick;
        }
        
        // Overloads for Checkable items and Separators would exist here [1].
    }
}
```

3\. The Window Manager Orchestrator

The Window Manager ties it all together. It evaluates the global menu (which is completely decoupled from the current perspective), handles the fixed "Windows" and "Help" menus, and manages the lifecycle and auto-pinning rules.

```
using System.Collections.Generic;
using System.Linq;

namespace FDP.Toolkit.ImGui.WindowManager
{
    public class WindowManager
    {
        private readonly List<ManagedWindow> _windows = new();
        private readonly GlobalMenuRegistry _globalMenu = new();
        private readonly Icons.IconAtlas _iconAtlas;
        
        public string CurrentPerspective { get; private set; } = "Default";

        public WindowManager(Icons.IconAtlas atlas)
        {
            _iconAtlas = atlas;
        }

        public void RegisterWindow(ManagedWindow window) => _windows.Add(window);
        public GlobalMenuRegistry GlobalMenu => _globalMenu;

        public void Render()
        {
            if (Gui.BeginMainMenuBar())
            {
                RenderGlobalMenu(_globalMenu.Root);
                RenderFixedWindowsMenu();
                RenderPerspectiveSwitcher();
                RenderFixedHelpMenu();
                Gui.EndMainMenuBar();
            }

            foreach (var window in _windows)
            {
                window.Render(CurrentPerspective, _iconAtlas);
            }
        }

        private void RenderFixedWindowsMenu()
        {
            // "Main menu contains fixed Windows pulldown showing subsystems as submenus" [5]
            if (Gui.BeginMenu("Windows"))
            {
                var byPerspective = _windows.Where(w => !w.IsDebugWindow).GroupBy(w => w.OwningPerspective);
                foreach (var group in byPerspective)
                {
                    if (Gui.BeginMenu(group.Key))
                    {
                        foreach (var win in group)
                        {
                            bool isOpen = win.IsOpen;
                            if (Gui.MenuItem(win.Title, "", ref isOpen))
                            {
                                win.IsOpen = isOpen;
                                // Auto-pin logic: opened from menu while perspective is NOT active [3]
                                if (win.IsOpen && win.OwningPerspective != CurrentPerspective)
                                {
                                    win.IsPinned = true;
                                }
                            }
                        }
                        Gui.EndMenu();
                    }
                }
                Gui.EndMenu();
            }
        }

        private void RenderFixedHelpMenu()
        {
            // "Help menu contains 'Debug' subitem... These debug windows are NOT affected by any perspective" [5]
            if (Gui.BeginMenu("Help"))
            {
                if (Gui.BeginMenu("Debug"))
                {
                    foreach (var win in _windows.Where(w => w.IsDebugWindow))
                    {
                        bool isOpen = win.IsOpen;
                        if (Gui.MenuItem(win.Title, "", ref isOpen))
                            win.IsOpen = isOpen;
                    }
                    Gui.EndMenu();
                }
                Gui.MenuItem("About"); // Shows version/hash [5]
                Gui.EndMenu();
            }
        }

        private void RenderPerspectiveSwitcher()
        {
            // Rendered as quick radio buttons for each registered perspective [2]
            var perspectives = _windows.Where(w => !w.IsDebugWindow).Select(w => w.OwningPerspective).Distinct();
            foreach (var p in perspectives)
            {
                bool isActive = (CurrentPerspective == p);
                if (Gui.RadioButton(p, isActive))
                {
                    CurrentPerspective = p;
                }
                Gui.SameLine();
            }
        }
        
        // Recursive method to draw the dynamic global menu tree [1]
        private void RenderGlobalMenu(MenuItemNode node) { /* ... ImGui.BeginMenu / MenuItem iteration ... */ }
    }
}
```

Persistence Strategy (Ad #3)

Because ImGui natively saves standard window sizes, positions, and collapse states to `imgui.ini`, relying on it is ideal. To securely save our custom `Hidden` and `Pinned` states, we should register a custom settings handler using `ImGui.AddSettingsHandler()`. This allows us to hook directly into the `imgui.ini` lifecycle (ReadOpenFn, ReadLineFn, WriteAllFn) so that when ImGui saves or loads its layout, it automatically serializes a custom block (e.g., `[FDP_WindowManager][State]`) containing a clean JSON or key-value map of our `IsOpen` and `IsPinned` booleans.

Maybe let's consider a case when subsystem needs to register a global window that is not affected by the perspective switching - it is simply shown until closed (like the debug windows do)

To cleanly support subsystem-provided global windows, we must refactor how we classify window visibility. Currently, the design conflates the window's _purpose_ (Debug) with its _layout behavior_ (ignoring perspectives).

From an architectural standpoint, a "Debug" window provided by the runner is simply a specific instance of a "Global" window. By replacing the `IsDebugWindow` flag with a generalized `WindowScope` concept, any subsystem can register global windows that remain visible until explicitly closed, bypassing the perspective state machine entirely.

Here is how we adjust the `ManagedWindow` base class to enforce this clean separation of concerns:

```
using ImGuiNET;
using System.Numerics;

namespace FDP.Toolkit.ImGui.WindowManager
{
    public enum WindowScope
    {
        /// <summary>Window visibility is tied to the active perspective unless pinned.</summary>
        PerspectiveBound,
        
        /// <summary>Window is always visible when open, ignoring perspectives.</summary>
        Global
    }

    public abstract class ManagedWindow
    {
        public string Id { get; }
        public string Title { get; }
        public string OwningPerspective { get; }
        public WindowScope Scope { get; }
        
        public bool IsOpen { get; set; } = false;
        public bool IsPinned { get; set; } = false;

        protected ManagedWindow(string id, string title, string perspective, WindowScope scope = WindowScope.PerspectiveBound)
        {
            Id = id;
            Title = title;
            OwningPerspective = perspective;
            Scope = scope;
        }

        public void Render(string currentPerspective, Icons.IconAtlas atlas)
        {
            if (!IsOpen) return;

            // The visibility rule is now a pure projection of the scope and state
            bool perspectiveActive = (OwningPerspective == currentPerspective);
            bool isVisible = (Scope == WindowScope.Global) || IsPinned || perspectiveActive;

            if (!isVisible) 
                return;

            if (Gui.Begin($"{Title}###{Id}", ref IsOpen))
            {
                DrawCustomTitleBarControls(currentPerspective, perspectiveActive, atlas);
                DrawClientArea();
            }
            Gui.End();
        }

        private void DrawCustomTitleBarControls(string currentPerspective, bool perspectiveActive, Icons.IconAtlas atlas)
        {
            float iconSize = atlas.IconSizeVec.X;
            Gui.SameLine(Gui.GetWindowWidth() - (iconSize * 2) - Gui.GetStyle().ItemSpacing.X - 10f);
            
            // Only render the Pin toggle for perspective-bound windows.
            // Global windows are inherently "pinned" by definition, so the control is redundant.
            if (Scope == WindowScope.PerspectiveBound)
            {
                bool pinState = IsPinned;
                if (Icons.IconWidgets.AlternatingFaceToggleIcon(atlas, $"pin_{Id}", "pin_on", "pin_off", ref pinState))
                {
                    IsPinned = pinState;
                }
                
                if (!IsPinned && !perspectiveActive && Gui.IsItemHovered())
                {
                    Gui.SetTooltip("Unpinning will hide this window in the current perspective.");
                }
                Gui.SameLine();
            }

            if (Icons.IconWidgets.IconButton(atlas, $"close_{Id}", "cross"))
            {
                IsOpen = false;
                IsPinned = false; // Reset pin state when closed
            }
        }

        protected abstract void DrawClientArea();
    }
}
```

Menu Integration

With this architecture, subsystems have two clean paths for exposing their global windows to the user:

-   **Fixed Windows Menu:** The `WindowManager` can still automatically list these global windows under the standard `Windows -> [Subsystem]` hierarchy. Because their `Scope` is `Global`, the manager will simply skip the auto-pinning logic when they are toggled on.-   **Custom Global Injections:** If the window is a critical operational tool, the subsystem can inject a dedicated toggle directly into the global menu bar utilizing the `"a/b/c/leaf"` slash convention.

This guarantees that the core rendering pipeline treats the Runner's built-in debug windows and a subsystem's custom global dashboards identically, keeping the framework extensible and strict.

the windows should be addressable and controllable via windows manager api - to show or hide or pin, for example. Assume also the imgui docking (hopefully this is all handled by imgui internally)

To make the windows addressable and controllable programmatically, we need to transition the `WindowManager` from holding a simple list of windows to using a fast, key-based registry. From a clean architecture perspective, providing a dedicated API surface for visibility and state mutation ensures that whether a window is toggled via the global menu, the window's own title bar, or an external subsystem event, all state transitions pass through a single source of truth.

Here is how we architect the API extensions.

1\. Dictionary-Based Registry & Programmatic API

We replace the `List<ManagedWindow>` with a `Dictionary<string, ManagedWindow>` keyed by the window's unique `Id`. We then expose public methods to manipulate the state, ensuring we enforce the business rules (like auto-pinning) consistently.

```
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace FDP.Toolkit.ImGui.WindowManager
{
    public class WindowManager
    {
        private readonly Dictionary<string, ManagedWindow> _windows = new();
        private readonly GlobalMenuRegistry _globalMenu = new();
        private readonly Icons.IconAtlas _iconAtlas;
        
        public string CurrentPerspective { get; private set; } = "Default";

        public WindowManager(Icons.IconAtlas atlas)
        {
            _iconAtlas = atlas;
        }

        public void RegisterWindow(ManagedWindow window) 
        {
            _windows[window.Id] = window;
        }

        public bool TryGetWindow(string id, [MaybeNullWhen(false)] out ManagedWindow window)
        {
            return _windows.TryGetValue(id, out window);
        }

        /// <summary>
        /// Programmatically shows a window. 
        /// Enforces the auto-pinning rule if the perspective does not match.
        /// </summary>
        public void ShowWindow(string id)
        {
            if (_windows.TryGetValue(id, out var win))
            {
                win.IsOpen = true;
                // Enforce auto-pinning consistency: if opened while its perspective is inactive, it pins [1].
                if (win.Scope == WindowScope.PerspectiveBound && win.OwningPerspective != CurrentPerspective)
                {
                    win.IsPinned = true;
                }
            }
        }

        /// <summary>
        /// Programmatically hides a window and resets its pin state.
        /// </summary>
        public void HideWindow(string id)
        {
            if (_windows.TryGetValue(id, out var win))
            {
                win.IsOpen = false;
                win.IsPinned = false; // The pinned state is turned off when hidden [1].
            }
        }

        /// <summary>
        /// Programmatically pins or unpins a window.
        /// </summary>
        public void SetWindowPinned(string id, bool isPinned)
        {
            if (_windows.TryGetValue(id, out var win) && win.Scope == WindowScope.PerspectiveBound)
            {
                win.IsPinned = isPinned;
            }
        }
        
        // ... Render() and Menu methods remain identical ...
    }
}
```

By routing all external requests through `ShowWindow`, subsystems can trigger UI panels without needing to know the user's current perspective context, and the system will gracefully auto-pin the window so it remains visible,.

2\. Integration with ImGui Docking

Regarding ImGui docking, your assumption is correct: ImGui handles this entirely internally.

_Note: The following specific details regarding ImGui's internal docking flags are not from the provided sources, and you may want to independently verify them in the ImGui documentation._

Because our `ManagedWindow` implementation constructs the ImGui window declaration using the `"{Title}###{Id}"` format, ImGui relies exclusively on the `###{Id}` portion to compute the window's stable 32-bit ID.

When ImGui's docking branch is enabled (typically by setting `ImGuiConfigFlags.DockingEnable` during framework initialization), ImGui builds a background node graph mapped directly to these stable IDs.

-   **Zero-Code Docking:** Our `WindowManager` does not need to manually calculate dock nodes, splits, or sizes.-   **State Restoration:** ImGui natively serializes the dock node hierarchy into the `imgui.ini` file. As long as our injected subsystems consistently use the same `Id` strings across application restarts, ImGui will automatically remember which windows were docked together, tabbed, or floating, and restore them flawlessly.-   **Visibility Independence:** Docking is completely agnostic to our `IsOpen`, `IsPinned`, or perspective state machine. If we programmatically call `HideWindow("my_panel")`, ImGui simply stops rendering that panel inside its dock node. If it was part of a tab bar, the tab disappears. When we call `ShowWindow("my_panel")`, ImGui instantly restores the tab exactly where the user left it.

This clean separation means our `WindowManager` acts strictly as the business-logic orchestrator for visibility and perspective switching, while delegating 100% of the spatial layout and docking orchestration to the underlying immediate-mode backend.

i also need an api for bringing a window on top of others

To cleanly support bringing a window to the top (focusing) within an immediate-mode GUI paradigm, we must handle the focus request as a transient state. Since ImGui evaluates window Z-order and docking focus during its render pass, we cannot simply reorder our backend dictionary; we must explicitly instruct ImGui to focus the specific window ID during the next frame's render loop.

From an architectural standpoint, we should encapsulate this intent by adding a "focus requested" flag to our `ManagedWindow` base class, and expose a dedicated method in the `WindowManager` API.

Here is how we extend the architecture:

**1\. Extending the Managed Window** We add a private flag and a method to request focus. During the `Render` method, we consume this flag and call `ImGui.SetWindowFocus`. By doing this right before or after `ImGui.Begin`, we ensure ImGui brings the window (or its dock tab) to the foreground.

```
namespace FDP.Toolkit.ImGui.WindowManager
{
    public abstract class ManagedWindow
    {
        // ... existing properties ...
        
        private bool _focusRequested = false;

        // ... existing constructor ...

        /// <summary>
        /// Flags the window to be brought to the front during the next render pass.
        /// </summary>
        public void RequestFocus() => _focusRequested = true;

        public void Render(string currentPerspective, Icons.IconAtlas atlas)
        {
            if (!IsOpen) return;

            bool perspectiveActive = (OwningPerspective == currentPerspective);
            bool isVisible = (Scope == WindowScope.Global) || IsPinned || perspectiveActive;

            if (!isVisible) 
                return;

            // Consume the focus request before rendering the window
            string windowInternalName = $"{Title}###{Id}";
            if (_focusRequested)
            {
                Gui.SetWindowFocus(windowInternalName);
                _focusRequested = false;
            }

            if (Gui.Begin(windowInternalName, ref IsOpen))
            {
                DrawCustomTitleBarControls(currentPerspective, perspectiveActive, atlas);
                DrawClientArea();
            }
            Gui.End();
        }
        
        // ... existing methods ...
    }
}
```

**2\. Extending the Window Manager API** We expose a `FocusWindow` method in the `WindowManager`. Crucially, if a subsystem requests focus on a window, that window _must_ be visible. If the window is currently hidden because its owning perspective is inactive, we should reuse our smart auto-pinning logic to bring it into view before focusing it.

```
namespace FDP.Toolkit.ImGui.WindowManager
{
    public class WindowManager
    {
        // ... existing state and methods ...

        /// <summary>
        /// Programmatically shows a window and brings it to the top of the Z-order.
        /// Enforces auto-pinning if the perspective does not match.
        /// </summary>
        public void FocusWindow(string id)
        {
            if (_windows.TryGetValue(id, out var win))
            {
                // 1. Ensure the window is open
                win.IsOpen = true;
                
                // 2. Enforce auto-pinning consistency if it's out of perspective
                if (win.Scope == WindowScope.PerspectiveBound && win.OwningPerspective != CurrentPerspective)
                {
                    win.IsPinned = true;
                }

                // 3. Queue the ImGui focus command for the next frame
                win.RequestFocus();
            }
        }
    }
}
```

**Why this is best practice:**

-   **Zero-Coupling:** Subsystems calling `WindowManager.FocusWindow("target_panel")` do not need to know if the panel is currently docked, tabbed, or floating. ImGui internally resolves the `###Id` string and brings the correct tab to the foreground.-   **State Machine Integrity:** By ensuring `FocusWindow` routes through the exact same auto-pinning logic as `ShowWindow`, we guarantee the user is never left in a confusing state where the system tries to focus a window that is technically forbidden from rendering by the perspective rules.-   **Thread Safety / Lifecycle:** In immediate-mode GUIs, mutating UI focus from outside the render loop can cause glitches. Storing it as a transient `_focusRequested` boolean ensures the actual ImGui API is only invoked exactly where it is safe—inside the active rendering pipeline.

pls show an conceptual example of all the window related features - how a subsystem can register few windows (one global, other scoped, i.e. perspective-affected), how it can inject stuff in global menu, how it can define local menu for each window. And how to programmatically show/hide/focus some of those windows via window manager api.

To demonstrate a clean, decoupled architecture for the windowing system, we can create a conceptual `RadarSubsystem`. This subsystem will encapsulate its own view logic by defining two distinct windows and then inject them into the central `WindowManager`.

In this design, the subsystem utilizes the `"a/b/c/leaf_menu"` slash convention to inject actionable items directly into the global menu bar. Because the global main menu is never affected by the currently selected perspective, these injected commands will remain accessible at all times, allowing operators to programmatically trigger the Window Manager API to show or focus the subsystem's windows regardless of the current layout.

Here is the implementation:

1\. Defining the Windows

We inherit from the `ManagedWindow` base class to enforce a strict separation between the window's layout properties and its internal rendering logic.

```
using ImGuiNET;

namespace FDP.Subsystems.Radar.UI
{
    /// <summary>
    /// A perspective-scoped window that includes a local menu bar.
    /// It will only render when the "Radar" perspective is active, unless the user pins it.
    /// </summary>
    public class RadarDisplayWindow : Toolkit.ImGui.WindowManager.ManagedWindow
    {
        // Opt-in to the local menu bar rendering pipeline
        protected override bool HasMenuBar => true;

        public RadarDisplayWindow(string perspectiveId) 
            : base(id: "radar_display_main", 
                   title: "Tactical Radar", 
                   perspective: perspectiveId, 
                   scope: WindowScope.PerspectiveBound)
        {
        }

        protected override void DrawLocalMenuBar()
        {
            // Renders a window-specific menu inside the ImGui window client area
            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Reset Zoom")) { /* Handle local view reset */ }
                if (ImGui.MenuItem("Toggle Grid")) { /* Handle grid toggle */ }
                ImGui.EndMenu();
            }
        }

        protected override void DrawClientArea()
        {
            ImGui.Text("Radar sweep visualization goes here.");
            // ... immediate mode rendering for the radar ...
        }
    }

    /// <summary>
    /// A global diagnostic window. 
    /// It completely ignores perspective switching and remains visible until closed [3].
    /// </summary>
    public class RadarDiagnosticsWindow : Toolkit.ImGui.WindowManager.ManagedWindow
    {
        public RadarDiagnosticsWindow(string perspectiveId) 
            : base(id: "radar_diag_main", 
                   title: "Radar Diagnostics", 
                   perspective: perspectiveId, 
                   scope: WindowScope.Global) // Bypasses perspective constraints
        {
        }

        protected override void DrawClientArea()
        {
            ImGui.Text("Antenna Status: OK");
            ImGui.Text("Signal-to-Noise Ratio: 98%");
        }
    }
}
```

2\. Subsystem Initialization & API Integration

When the subsystem bootstraps, it registers its windows and injects its actionable commands into the global menu. By relying on the `WindowManager` API, the subsystem's business logic does not need to manage ImGui focus stacks or auto-pinning rules—the framework handles it.

```
using System;
using FDP.Toolkit.ImGui.WindowManager;
using FDP.Subsystems.Radar.UI;

namespace FDP.Subsystems.Radar
{
    public class RadarSubsystem
    {
        private const string PerspectiveName = "Radar";

        public void Initialize(WindowManager windowManager)
        {
            // 1. Register the subsystem's unique windows [1]
            windowManager.RegisterWindow(new RadarDisplayWindow(PerspectiveName));
            windowManager.RegisterWindow(new RadarDiagnosticsWindow(PerspectiveName));

            // 2. Inject actionable items into the Global Menu using slash convention [1]
            
            windowManager.GlobalMenu.RegisterItem("Tools/Radar/Show Tactical Display", () =>
            {
                // Programmatically showing the window. 
                // If the user is currently in a different perspective (e.g., "Combat"),
                // the WindowManager will automatically set IsPinned = true to guarantee visibility.
                windowManager.ShowWindow("radar_display_main");
            });

            windowManager.GlobalMenu.RegisterItem("Tools/Radar/Focus Diagnostics", () =>
            {
                // Brings the global diagnostic window to the front of the Z-order,
                // automatically un-collapsing or switching tabs if docked.
                windowManager.FocusWindow("radar_diag_main");
            });

            windowManager.GlobalMenu.RegisterItem("Tools/Radar/Hide All", () =>
            {
                // Programmatic tear-down of the subsystem's UI footprint
                windowManager.HideWindow("radar_display_main");
                windowManager.HideWindow("radar_diag_main");
            });
        }
    }
}
```

Architectural Benefits

-   **Zero Coupling to ImGui State:** The `RadarSubsystem` invokes pure intent-based methods (`ShowWindow`, `FocusWindow`). It has no knowledge of ImGui's internal IDs, docking nodes, or rendering loops.-   **Encapsulation:** By overriding `DrawLocalMenuBar()`, the `RadarDisplayWindow` keeps its highly specific actions (like "Reset Zoom") completely isolated from the global framework menu, preventing global UI clutter.-   **Lifecycle Integrity:** If the user executes `"Tools/Radar/Show Tactical Display"` while a different perspective is active, the `WindowManager.ShowWindow` API internally enforces the business rule that auto-pins the window, ensuring the user's mental model is never broken.

how do we tell the window manager to switch perspective? no event bus and ECS in the window manager please! no coupling with extra unnecessary components! Perspectives should be a window manager concept completely decoupled from other parts of the system!

You are absolutely right, and I appreciate the strict architectural boundary check. Leaking the ECS event bus and domain-specific components like `ActivePerspective` into a generic GUI toolkit is a violation of clean architecture. A UI toolkit should be completely agnostic of the underlying simulation engine or state management framework.

Perspectives must be a pure, self-contained concept within the `WindowManager`. If the application's simulation layer needs to know about UI perspective changes (for example, to gate 3D rendering), the application layer should bridge that gap by listening to a standard C# event exposed by the window manager, keeping the dependency unidirectional.

Here is the decoupled architecture for perspective switching natively within the `WindowManager`:

The Decoupled Window Manager

We manage the `CurrentPerspective` as native state inside the `WindowManager` and expose a standard C# event for external subscribers.

```
using System;
using System.Collections.Generic;
using System.Linq;

namespace FDP.Toolkit.ImGui.WindowManager
{
    public class WindowManager
    {
        private readonly Dictionary<string, ManagedWindow> _windows = new();
        private readonly GlobalMenuRegistry _globalMenu = new();
        private readonly Icons.IconAtlas _iconAtlas;
        
        public string CurrentPerspective { get; private set; } = "Default";

        /// <summary>
        /// Fired when the user or the system changes the active perspective.
        /// Payload: (oldPerspective, newPerspective)
        /// </summary>
        public event Action<string, string>? OnPerspectiveChanged;

        public WindowManager(Icons.IconAtlas atlas)
        {
            _iconAtlas = atlas;
        }

        // ... Registration and programmatic API remain the same ...

        /// <summary>
        /// Programmatically switches the UI perspective.
        /// </summary>
        public void SwitchPerspective(string newPerspective)
        {
            if (CurrentPerspective == newPerspective) 
                return;

            string oldPerspective = CurrentPerspective;
            CurrentPerspective = newPerspective;

            // Notify any external subscribers (like the application root) without coupling to them
            OnPerspectiveChanged?.Invoke(oldPerspective, newPerspective);
        }

        public void Render()
        {
            if (Gui.BeginMainMenuBar())
            {
                RenderGlobalMenu(_globalMenu.Root);
                RenderFixedWindowsMenu();
                RenderPerspectiveSwitcher(); // Native switching
                RenderFixedHelpMenu();
                Gui.EndMainMenuBar();
            }

            foreach (var window in _windows.Values)
            {
                window.Render(CurrentPerspective, _iconAtlas);
            }
        }

        private void RenderPerspectiveSwitcher()
        {
            // Discover all distinct perspectives registered by subsystems
            var perspectives = _windows.Values
                .Where(w => w.Scope == WindowScope.PerspectiveBound)
                .Select(w => w.OwningPerspective)
                .Distinct()
                .OrderBy(p => p);

            // "There are also quick 'perspective' switcher buttons... acting as radiobuttons" [1]
            foreach (var p in perspectives)
            {
                bool isActive = (CurrentPerspective == p);
                if (Gui.RadioButton(p, isActive))
                {
                    SwitchPerspective(p);
                }
                Gui.SameLine();
            }
        }
    }
}
```

How the Application Uses This (The Composition Root)

With this design, the `FDP.Toolkit.ImGui` library has absolutely zero references to `Fdp.Kernel`, `FdpEventBus`, or any ECS concepts.

When the application bootstraps, it wires the generic UI toolkit to the domain logic using standard event subscription. If the core simulation needs an ECS event to happen when the UI changes, the application root does the translation:

```
// Inside your application's bootstrap/initialization code:

var windowManager = new WindowManager(iconAtlas);

// Bridge the generic UI toolkit to the specific ECS domain logic
windowManager.OnPerspectiveChanged += (oldPersp, newPersp) =>
{
    // E.g., if switching to the Sim Map, tell the ECS to update the ActivePerspective component
    // or publish a domain event so the map cameras snap properly.
    _ecsWorld.Bus.Publish(new TogglePerspectiveEvent());
};
```

This enforces the golden rule of clean architecture: dependencies point inward. The UI toolkit knows nothing about the ECS, but the composition root uses the UI toolkit's events to drive the ECS state.

actually, the perspective is not related just to windows. The subsystems provide a lot of 2d map stuff, rendering various things like grids and icons and polygons to the apps main window client area independently on the windows. Similar perspective concept applies also here. But these background-map related perspective should be handled in parallel, by some kind of background map manager, just synchronized with the window manager if the app wants to, correct?

You hit the nail on the head. That is exactly how we maintain a clean separation of concerns between immediate-mode UI layouts (ImGui) and world-space 2D rendering (Raylib).

The existing framework actually already implements this decoupled architecture for the background maps. It uses an ECS-based state machine centered around an `ActivePerspective` singleton component, which tracks whether the `IG` or `Sim` presentation tier is currently active.

Here is how this architecture handles the parallel synchronization cleanly:

**1\. Independent Map Renderers (World Space)** Instead of tying map rendering to the window layout, the framework uses dedicated ECS systems like `IgMapRenderSystem` and `SimMapRenderSystem`. These systems are responsible for invoking the 2D `MapCanvas` to draw the grids, tactical graphics, and unit icons. During their `OnUpdate` loop, they simply read the `ActivePerspective` singleton; if the active perspective doesn't match their designated view, they immediately return and skip the `_canvas?.Draw()` call entirely.

**2\. The Perspective Coordinator** To manage the background map specifically, there is a dedicated `PerspectiveCoordinatorSystem`. When a perspective switch occurs, this coordinator handles the spatial logic: it flips the `ActivePerspective` state and automatically snaps the incoming map camera to the outgoing camera's exact position and zoom. This guarantees the operator sees the exact same geographic region after the switch without any jarring visual jumps.

**3\. Event-Driven Synchronization** To keep the Window Manager and the Map Renderers perfectly synchronized without coupling them together, the framework uses an event bus. When a user clicks the perspective toggle button in the UI, the immediate-mode GUI code does not mutate map cameras or ECS state directly; it simply publishes a stateless `TogglePerspectiveEvent`.

The `PerspectiveCoordinatorSystem` consumes this event to update the background map state. Meanwhile, our `WindowManager` can observe this exact same transition (either via a bridged C# event or by reading the newly updated state) and hide/show the appropriate ImGui panels simultaneously.

By separating the 2D map canvas lifecycle from the ImGui window lifecycle, you ensure that complex spatial rendering, camera culling, and geographic interpolations remain completely isolated from standard UI panel logic. Excellent architectural instinct!

























How to register checkable menu item to global menu? How to local menu if concrete window instance?

To register a checkable item in the global menu, we utilize the path-based routing in the `GlobalMenuRegistry`. From a clean architecture perspective, the UI should never own the state; it should merely project it. We achieve this by registering a menu node with two delegates: a `Func<bool>` to read the authoritative state, and an `Action<bool>` to mutate it when the user toggles the item.

Here is how you would extend the registry and register the checkable item:

```
// 1. Extension in GlobalMenuRegistry
public void RegisterCheckableItem(string path, Func<bool> getCheckedState, Action<bool> onCheckedChanged)
{
    var parts = path.Split('/');
    var current = Root;
    foreach (var part in parts)
    {
        if (!current.Children.ContainsKey(part))
            current.Children[part] = new MenuItemNode { Name = part };
        current = current.Children[part];
    }
    
    // Bind the state delegates
    current.GetCheckedState = getCheckedState;
    current.OnCheckedChanged = onCheckedChanged;
}

// 2. Usage in your Subsystem Bootstrap
windowManager.GlobalMenu.RegisterCheckableItem(
    "View/Overlays/Show Grid",
    getCheckedState: () => _mySubsystemState.IsGridVisible,
    onCheckedChanged: (newState) => _mySubsystemState.IsGridVisible = newState
);
```

For defining a local menu within a concrete window instance, we rely on the Template Method pattern defined in our `ManagedWindow` base class. This encapsulates window-specific actions and prevents them from polluting the global menu space.

You simply opt-in by overriding the `HasMenuBar` property to return `true`, and then implement the `DrawLocalMenuBar` method to render your immediate-mode menu items.

```
public class TacticalMapWindow : ManagedWindow
{
    // Opt-in to the local menu bar rendering pipeline
    protected override bool HasMenuBar => true;

    public TacticalMapWindow(string perspectiveId) 
        : base("tac_map", "Tactical Map", perspectiveId, WindowScope.PerspectiveBound)
    {
    }

    protected override void DrawLocalMenuBar()
    {
        // Renders a window-specific menu inside the ImGui window client area
        if (ImGui.BeginMenu("Map Tools"))
        {
            if (ImGui.MenuItem("Center on Player")) 
            { 
                // Dispatch domain command
            }
            ImGui.EndMenu();
        }
    }

    protected override void DrawClientArea()
    {
        // Render window contents...
    }
}
```

By structuring it this way, the `WindowManager` automatically handles the `ImGuiWindowFlags.MenuBar` flag allocation when rendering the window, while the concrete class retains absolute cohesion over its own commands and layout.

Is it better to let the window instance handle the rendering of its local menu cimpletely or should we use similar injection mechanism as for global main menu?

From a clean architecture perspective, it is significantly better to let the window instance handle the rendering of its local menu completely rather than over-engineering a string-based injection mechanism for it.

The provided sources explicitly mandate the `"a/b/c/leaf_menu"` slash convention for the global main menu because the main menu is a shared, cross-cutting integration point where every loaded subsystem must be able to contribute. However, the sources do not specify an injection pattern for local window menus, so applying standard architectural best practices dictates we keep local concerns localized. (Please note that the specific architectural recommendation for local windows is derived from general clean architecture principles, as the sources only detail the global menu behavior).

Here is why relying on the window instance (using a Template Method like `DrawLocalMenuBar()`) is the superior design for immediate-mode GUIs:

**1\. Encapsulation and High Cohesion** A local window menu typically controls the specific view state of that window (e.g., "Reset Zoom", "Toggle Grid", "Show Legend"). These actions are tightly coupled to the window's internal variables. If you use a generic injection registry for local windows, you are forced to expose the window's private state and methods to the outside world just to wire up the callbacks. By letting the window render its own menu, the view logic and the state it mutates remain perfectly encapsulated in the same class.

**2\. Single Responsibility and Bounded Contexts** The global menu is application-scoped, but a specific window (like a Tactical Radar display) is owned entirely by its providing subsystem. It is extremely rare that Subsystem A has any business injecting menu items into Subsystem B's local window. If a subsystem wants to expose global functionality, it should inject it into the global main menu.

**3\. The Immediate-Mode Paradigm** ImGui is designed around executing view logic inline. Creating a complex trie-node registry (like we did for the global menu) is necessary when merging disparate sources, but doing this for a single window introduces unnecessary allocations and state-tracking. Calling `if (ImGui.BeginMenuBar())` directly inside your window's `Render` loop is zero-allocation, strongly typed, and idiomatic to the framework.

Keep the `"a/b/c/leaf"` registry strictly for the global main menu to satisfy the requirement for subsystem integration, and let your concrete window classes own their local menu bars completely. This maintains a strict boundary between global application orchestration and local subsystem view logic.

Ok. What other winfow management or ui related features might be necessary for a good ux?

To build a truly robust user experience (UX) within an immediate-mode GUI, we must focus on surfacing the right data at the right time without overwhelming the operator. From a clean architecture perspective, the framework already lays the groundwork for several advanced UI paradigms that go far beyond basic windows and menus.

Here are the key UI/UX features we should implement, supported by our framework's design:

**1\. Extensible Status Bar** A global status bar is critical for providing at-a-glance system health and quick actions. The framework dictates that subsystems can register their own reserved sections within a status bar. This allows decoupled clients to render their own colored icons, diagnostic text, and respond to user clicks locally without polluting the main menu bar.

**2\. Context-Sensitive Menus** Instead of cluttering global or window-local menus with entity-specific commands, we should utilize extensible right-click context menus. The framework supports registering handlers (like `IEntityContextMenuHandler` and `IDerContextMenuHandler`) that dynamically build menus when a user right-clicks an entity row. This allows multiple decoupled subsystems to inject their own actions (e.g., "Delete", "Center on Entity", or "Edit Route") into the same popup menu using a clean builder pattern.

**3\. Visual Data Mutation Highlighting** In a high-frequency simulation, operators need to know what data is actively changing. The UI toolkit implements a `ComponentReflector` that performs byte-level snapshot comparisons of unmanaged components. When a component's data mutates between frames, its ImGui header is drawn in a highlight color (e.g., yellow) to draw the user's eye immediately to the changing state.

**4\. Linked Selection (Chain-to-Map)** When dealing with complex data like entity lists alongside a 2D/3D world view, users need to cross-reference data. The UI supports a "Chain-to-Map" toggle. When enabled (indicated by a "Linked" button state), clicking an entity in an inspector panel automatically propagates the selection out to the active map view, synchronizing the operator's spatial and tabular contexts.

**5\. Advanced Data Navigation (Filtering, Trees, and Extraction)** When rendering hundreds of entities or events, scrolling is not enough:

-   **Search & Filtering:** Text input filters (like the ORBAT tree search) allow operators to instantly narrow down dense hierarchical lists, automatically ignoring collapsed states to find deep matches. Complex event browsers also use checkbox-driven popup menus to toggle visibility of specific event types.-   **Tree Controls:** For deeply nested component properties, providing "Expand All" and "Collapse All" toolbar buttons allows operators to rapidly navigate hierarchical property grids.-   **Data Extraction:** Placing a simple "Copy JSON" button next to selected entities allows developers or operators to instantly dump the exact, serialized state of an entity to their clipboard for debugging or reporting.

**6\. Global Modals and Progress Indicators** For long-running tasks or critical failures (such as a distributed network transaction failing), the UI must interrupt the user's flow. The framework accommodates progress bars tied to asynchronous operations (like exporting archives to a NAS) and global modal alerts that lock the UI until the user acknowledges an error state, such as a version conflict during a mission commit.

By leaning heavily into these patterns—specifically context-awareness, state synchronization, and clear visual feedback for mutations—we keep the architecture strictly decoupled while delivering a highly responsive, professional-grade UX.

Lets focus on the status bar. I tkink i suggested something in my window manager source. How can we approach the status bar?

To support a global status bar where decoupled clients and subsystems can register their own interactive sections, we need to fully embrace the immediate-mode GUI paradigm.

From a clean architecture perspective, passing heavy UI objects or instantiating complex widget classes is an anti-pattern in ImGui. Instead, we should use a **Registry of Render Delegates**. The framework will orchestrate the physical layout (reserving the rectangular space at the bottom of the screen and drawing separators), but it will yield control to the subsystem's injected delegate to actually draw the contents and handle the click logic.

Here is the architectural approach to achieve this cleanly:

1\. The Status Bar Registry

We create a dedicated orchestrator for the status bar. Subsystems register an `Action` (the render delegate) along with an ordering hint, so we can deterministically lay out the sections from left to right (or right to left) regardless of initialization order.

```
using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace FDP.Toolkit.ImGui.WindowManager
{
    public class StatusBarManager
    {
        // Represents a registered section of the status bar
        private struct StatusBarSection
        {
            public string Id;
            public int SortOrder;
            public Action RenderDelegate;
        }

        private readonly List<StatusBarSection> _sections = new();
        private bool _needsSort = false;

        /// <summary>
        /// Registers a custom section to be rendered in the global status bar.
        /// </summary>
        public void RegisterSection(string id, int sortOrder, Action renderDelegate)
        {
            _sections.Add(new StatusBarSection { Id = id, SortOrder = sortOrder, RenderDelegate = renderDelegate });
            _needsSort = true;
        }

        /// <summary>
        /// Renders the status bar at the bottom of the main viewport.
        /// </summary>
        public void Render()
        {
            if (_needsSort)
            {
                _sections.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
                _needsSort = false;
            }

            var viewport = ImGuiNET.ImGui.GetMainViewport();
            float height = ImGuiNET.ImGui.GetFrameHeight() + ImGuiNET.ImGui.GetStyle().WindowPadding.Y * 2.0f;
            
            // Lock the window to the bottom of the screen
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(viewport.WorkPos.X, viewport.WorkPos.Y + viewport.WorkSize.Y - height));
            ImGuiNET.ImGui.SetNextWindowSize(new Vector2(viewport.WorkSize.X, height));
            
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | 
                                     ImGuiWindowFlags.NoDocking | 
                                     ImGuiWindowFlags.NoSavedSettings | 
                                     ImGuiWindowFlags.NoFocusOnAppearing | 
                                     ImGuiWindowFlags.NoNav;

            if (ImGuiNET.ImGui.Begin("##GlobalStatusBar", flags))
            {
                for (int i = 0; i < _sections.Count; i++)
                {
                    // Yield control to the subsystem to render whatever they wish (icons, text) and handle clicks [1]
                    _sections[i].RenderDelegate.Invoke();

                    // Automatically draw vertical separators between sections
                    if (i < _sections.Count - 1)
                    {
                        ImGuiNET.ImGui.SameLine();
                        ImGuiNET.ImGui.SeparatorEx(ImGuiSeparatorFlags.Vertical);
                        ImGuiNET.ImGui.SameLine();
                    }
                }
            }
            ImGuiNET.ImGui.End();
        }
    }
}
```

2\. Integration with Subsystems

Because the framework simply calls a delegate, subsystems can render anything they want in their reserved section, including the colored icons we designed earlier, and easily respond to user clicks.

Continuing our `RadarSubsystem` example, here is how the subsystem injects a diagnostic section into the status bar during its bootstrap phase:

```
using System;
using FDP.Toolkit.ImGui.WindowManager;
using FDP.Toolkit.ImGui.Icons;
using ImGuiNET;

namespace FDP.Subsystems.Radar
{
    public class RadarSubsystem
    {
        private bool _radarEmitting = true;

        public void Initialize(WindowManager windowManager, StatusBarManager statusBar, IconAtlas atlas)
        {
            // Register Windows...
            // Register Global Menus...

            // Register a Status Bar section with SortOrder 100 (e.g., placing it towards the left)
            statusBar.RegisterSection("radar_status", 100, () =>
            {
                // 1. Render custom text
                ImGui.Text("Radar: ");
                ImGui.SameLine();

                // 2. Use our previously built IconWidgets to render a clickable toggle icon [1]
                // This visually handles hover/pressed states and alters the bool if clicked
                if (IconWidgets.ToggleIcon(atlas, "radar_emit_toggle", "antenna", ref _radarEmitting))
                {
                    // Respond to user click [1]
                    if (_radarEmitting)
                        Console.WriteLine("Radar emissions turned ON.");
                    else
                        Console.WriteLine("Radar emissions turned OFF.");
                }

                ImGui.SameLine();

                // 3. Render dynamic status text based on internal state
                if (_radarEmitting)
                    ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1), "ACTIVE");
                else
                    ImGui.TextColored(new System.Numerics.Vector4(1, 0, 0, 1), "SILENT");
            });
        }
    }
}
```

Architectural Benefits

-   **Zero Allocations in the Hot Path:** The `Action` delegates are allocated exactly once during initialization. The `Render` loop executes without creating any garbage.-   **Total Decoupling:** The `StatusBarManager` knows absolutely nothing about radar antennas, network statuses, or what an icon is. It only knows it has a list of delegates to invoke.-   **High Cohesion:** The subsystem's business logic (e.g., `_radarEmitting` state and the side effects of toggling it) remains encapsulated entirely within the subsystem's own scope.

Good. Now lets discuss the context menus. In a local window context i assume this should be fully controlled by the window in similar manner as the local menu bar. Is there any more global context we need to handle? Tha background map manager needs to handle the menus on his own, inedepently on windowd, correct? Is there any overlap or some connection between thesr two contexts necessary or usefull?

You are absolutely right about the local window context menus. From a clean architecture perspective, if a window contains highly specific local data (like a configuration grid), it should entirely own its context menus using native immediate-mode calls (like `ImGui.BeginPopupContextItem()`). That keeps the view logic highly cohesive and encapsulated.

However, when we look at the broader application—specifically the background map manager versus global UI windows—there is a massive and critical overlap that we must handle: **Domain Entities**.

If a user right-clicks a "Tank" on the 2D map canvas, or right-clicks that exact same "Tank" row in an "Entity Inspector" window, they expect to see the exact same context menu. If we let the map manager and the window manager handle these completely independently, we violate the DRY (Don't Repeat Yourself) principle and end up with fragmented UX.

Here is how the framework's architecture elegantly solves this overlap using a decoupled builder pattern:

**1\. The Shared Context Menu Builder** Instead of hardcoding menu items, the framework defines an `IContextMenuBuilder` interface. This builder exposes methods like `AddItem`, `BeginSubmenu`, and `AddSeparator`.

Alongside this, subsystems implement `IEntityContextMenuHandler` (for standard ECS entities) or `IDerContextMenuHandler` (for dynamic repository entities). When a subsystem boots up, it registers its handlers. For example, a Logistics subsystem might register a handler that adds "Resupply" and "Transfer", while an Admin subsystem registers "Teleport" and "Delete".

**2\. Bridging the Map and the Windows** Because the menu generation is decoupled into these handlers, both the window manager and the map manager can consume them effortlessly:

-   **In the UI Windows:** Panels like the `EntityInspectorPanel` maintain a list of registered handlers. When the operator right-clicks an entity row, the panel simply iterates through the handlers, passing them the target entity and the builder, and the menu is drawn instantly. Multiple handlers simply append their items in registration order, separated by visual dividers.-   **On the Map Canvas:** The map uses raycasting to find the entity under the mouse. When a right-click occurs, the map doesn't need to know anything about logistics or admin commands. It simply invokes the exact same handler pipeline for the picked entity, ensuring 100% feature parity with the UI windows.

**3\. Advanced State Management (The ECS Approach)** For complex map rendering—especially in distributed setups where the UI might be decoupled from the simulation—the framework takes this a step further. Instead of rendering the ImGui popup directly inside the map's input loop, the map input layer flags an ECS managed component called `ContextMenuState` on the targeted entity.

A dedicated `ContextMenuSystem` observes this state and manages the lifecycle. Finally, a screen-space `ContextMenuPanel` reads this ECS component and renders the actual ImGui popup at the correct screen coordinates.

**Architectural Takeaway** Keep your purely local UI context menus inside your concrete window classes. But for anything representing your core domain data (like entities, routes, or tactical graphics), extract the menu generation into injected handlers. This guarantees that whether the operator interacts via a data-grid window or the background map canvas, the behavior remains perfectly synchronized and extensible.

Even if we deal with domain entities sharing context menu content, the local context (map or window) should be able to add context specific menu items

You are absolutely correct. From a clean architecture perspective, view-specific operations—like adjusting a local camera or focusing a specific UI list—must remain tightly encapsulated within that specific view, rather than polluting the shared domain logic.

The framework anticipates this exact requirement through a composable builder pattern using `IEntityContextMenuHandler` (or `IDerContextMenuHandler` for Dynamic Entity Repository entities) and the `IContextMenuBuilder`.

Here is how the architecture handles merging shared domain actions with local, context-specific actions:

**1\. The Multi-Handler Pipeline** UI panels like the `EntityInspectorPanel` maintain a list of registered context menu handlers. When the user right-clicks an entity row, the panel iterates through all registered handlers, passing them the target entity and the shared `IContextMenuBuilder`. Because multiple handlers can be registered, the items are simply appended in registration order and separated by visual dividers.

**2\. Local Lambda Injection** To make injecting local view logic effortless and avoid creating bloated class files for simple UI actions, the toolkit provides adapter classes like `LambdaEntityContextMenuHandler` and `LambdaDerContextMenuHandler`. This allows the host application or specific window to register inline delegate handlers that capture local state.

**3\. Real-World Example in the Framework** We can see this architectural pattern perfectly applied in the Image Generator (IG) application shell (`IgApplication`). The IG hosts the generic `EntityInspectorPanel` but needs to add map-specific actions to it. It registers a local lambda handler that appends actions tied directly to the IG's local map camera and tools:

-   It adds a **"Center on entity"** item that invokes the local `CenterCameraOn(entity)` method.-   It adds a **"Select entity"** item that triggers `SelectEntityOnMap(entity)`.-   If the entity contains an editable polyline overlay, it appends an **"Edit overlay"** action that triggers the local `ActivateAreaEditingTool`.

By designing the context menu system as a pipeline of handlers acting on a shared builder, your domain layer can inject the global business actions (like "Delete" or "Resupply"), while your specific ImGui window or Raylib map canvas can append its own view-centric actions without either system knowing about the other.

When thinking about big app with many subsystems, should we include some kind in-app user action handling concept? Imagine i will be creating a scenario editor app wirh undo redo. Can it benefit from unified action concept? Should it be an independent toolkit for that?

Implementing a unified user action handling concept—often known as the Command Pattern—is absolutely essential for a large-scale scenario editor, particularly when undo/redo functionality is required.

The framework already heavily embraces command and CQRS (Command Query Responsibility Segregation) patterns for its system operations. For instance, UI interactions from the Interactive Operations Station (ExCon) are encapsulated into specific intent messages like `MapCommandRequest`, `CreateEntityRequest`, and `MissionControlRequest`. Additionally, the ECS (Entity Component System) layer uses an `IEntityCommandBuffer` to record structural mutations (like adding or removing components) for deferred, thread-safe execution.

However, the sources do not explicitly contain a dedicated Undo/Redo history stack or a generalized user-action history manager. Therefore, treating this as an independent toolkit is the correct architectural choice. You already have an `FDP.Toolkit.Commands` project, though currently, it is utilized to provide the `DdsCommandClient` for correlating asynchronous network requests and acknowledgements over DDS. You could either expand this toolkit or create a dedicated `FDP.Toolkit.UserActions` library.

_Note: The specific implementation details of an undo/redo stack described below are drawn from standard software architecture best practices rather than the sources, and you may want to independently verify this approach._

From a clean architecture perspective, building this as an independent toolkit provides several massive benefits:

**1\. Encapsulation of Intent and Reversal** By defining a standard interface (e.g., `IUserAction`) with `Execute()` and `Undo()` methods, you encapsulate the exact state needed to reverse an operation. For example, if a user uses the `RouteEditTool` to move a waypoint, the tool wouldn't mutate the ECS or send a network update directly. Instead, it would instantiate a `MoveWaypointAction` containing the old and new coordinates.

**2\. Centralized History Management** An independent toolkit allows you to build a central `ActionHistoryManager` that maintains `Undo` and `Redo` stacks. The Window Manager or global UI toolkit can bind standard hotkeys (Ctrl+Z / Ctrl+Y) or global menu items directly to this manager. When invoked, the manager simply calls `Undo()` on the top action, completely agnostic to whether the action involved moving a vehicle, changing a tactical overlay, or modifying a mission plan.

**3\. Strict Decoupling of Subsystems** If the action framework is an independent toolkit, it sits at the bottom of your dependency graph. Your various subsystems (Radar, Logistics, Map Editor) can reference the action toolkit, implement their own domain-specific `IUserAction` classes, and push them to the central history stack. The core framework never needs to know the domain-specific details of what is being undone, preventing circular dependencies and massive switch statements.

**4\. Batching and Macros** In a scenario editor, users often perform bulk actions (e.g., selecting 50 entities and changing their affiliation to hostile). A unified action concept allows you to implement a `CompositeAction` or `MacroAction` that wraps those 50 individual state changes into a single actionable unit. When the user clicks "Undo," all 50 entities revert their affiliation simultaneously.

By building this as an independent toolkit, you preserve the zero-allocation hot paths of your ECS and rendering layers, while strictly separating the "view" (what the user clicked) from the "intent" (the reversible action that was performed).

Do we need some global service registry pattern? One registry per whole app or also a subsystem-local ones? Will it simplify something or bring benefits? What are the cons? Think in fdp runner perspective where one app contains many subsystems.

From a clean architecture perspective, introducing a generic global or subsystem-local service registry (often known as the Service Locator pattern or a generic Dependency Injection container) is **not necessary** and would actually be an architectural anti-pattern for the FDP Runner ecosystem.

Based on the provided framework sources, the architecture heavily relies on explicit composition, data-oriented singletons, and event-driven decoupling. Here is why a generic service registry is avoided and how the framework achieves the same goals with better architectural hygiene:

1\. The Cons of a Generic Service Registry

In a large application hosting multiple subsystems like `SimHost`, `IG`, `ExCon`, and `Orchestrator`, a global service locator obscures dependency boundaries. If you inject a generic `IServiceLocator` into your modules, you hide their actual requirements. The framework explicitly avoids this to prevent circular dependencies and to make initialization failures obvious at startup.

Instead of a hidden registry, the framework forces explicit **Constructor Injection** at the composition root. For example, the `NodeBootstrapper` and application shells (like `SimHostApp` and `IgApplication`) manually wire dependencies such as the `DdsParticipant`, `EntityRepository`, and `NetworkEntityMap` directly into the modules that need them. This guarantees that a module cannot be instantiated unless all its specific dependencies are satisfied.

2\. Handling Global State via ECS Singletons (Data-Oriented Services)

When you need a service or global data available across many different systems within a simulation world, the framework uses **ECS Singletons** rather than a service registry.

You can register any struct or class as a singleton directly on the `EntityRepository`. This keeps the data tightly coupled to the simulation lifecycle and memory management, while remaining globally accessible to any system that has a reference to the world. Examples in the sources include:

-   `ITkbDatabase` for blueprint resolution.-   `GlobalTime` for synchronous time-state access.-   `PathfindingBatchData` for querying navigation solvers without direct system-to-system coupling.

3\. Decoupling via the Event Bus

When subsystems or disparate systems need to communicate without holding direct references to each other's services, they use the `FdpEventBus`. This provides a completely decoupled pub/sub mechanism.

-   For example, instead of a UI panel calling a "PerspectiveService", it simply publishes a `TogglePerspectiveEvent` to the bus. The `PerspectiveCoordinatorSystem` consumes this and performs the complex camera snapping and state switching independently.-   Similarly, combat intent is decoupled: a brain node emits a `WeaponFireIntent` event, which an egress translator forwards over DDS, completely unaware of the downstream execution service.

4\. Subsystem-Local Domain Registries

While a _generic_ subsystem-local service registry is not recommended, the framework makes heavy use of **Domain-Specific Registries**. This is the clean architecture alternative to a generic DI container, as it enforces strict type safety and domain constraints:

-   `ScenarioRegistry`: Explicitly maps string names to `IScenario` factory functions, avoiding reflection overhead while keeping startup fast and errors obvious.-   `BehaviorRegistry`: Maps integer IDs to AI `BehaviorDefinition` structures.-   `MapLayerRegistry`: A static, strongly-typed registry defining map layer predicates for the IG.

Summary

Bringing in a generic global or local service registry pattern would dilute the strict, explicit dependency graph the FDP runner currently maintains. By sticking to **Constructor Injection at the Composition Root** for long-lived dependencies, **ECS Singletons** for simulation-scoped shared data, and the **Event Bus** for cross-boundary communication, you maintain a highly decoupled, zero-hidden-state architecture that scales beautifully across many subsystems.

When talking the runner and subsystem, what about debug logging? I like nlog static per source module logging. Is it sufficient for large complex system like ours?

You are absolutely right to prefer static, per-source-module logging. In a massive, distributed simulation ecosystem, this pattern is highly effective, but standard out-of-the-box NLog usage will quickly choke a 60Hz ECS loop if not architected correctly.

The framework explicitly relies on this exact pattern but wraps NLog in a highly optimized facade called `FdpLog<T>` to make it viable for our strict performance constraints.

Here is how the architecture scales the "static per-class NLog" pattern to accommodate a high-performance, multi-subsystem simulation:

1\. The Zero-Allocation Hot Path Facade

In an ECS running tens of thousands of entities per frame, formatting strings or allocating `params object[]` arrays for log arguments triggers devastating Garbage Collection (GC) pauses. The framework solves this by enforcing the use of `FdpLog<T>`.

-   **Static Type Resolution:** By using a generic class (`FdpLog<T>`), the underlying NLog logger instance is resolved exactly once per class at JIT-compile time, eliminating dictionary lookups.-   **Boolean Gates:** It exposes fast, statically-cached boolean flags (e.g., `IsDebugEnabled`, `IsTraceEnabled`) which act as a zero-cost gate before any expensive string interpolations occur.-   **Params Array Avoidance:** The facade provides explicit method overloads for up to four arguments (e.g., `Trace(string format, object arg0, object arg1)`), completely avoiding the implicit array allocations caused by `params` keywords.

2\. Thread-Boundary Protection via Async Wrappers

If a subsystem's main simulation thread waits for disk I/O to write a log line, the simulation frame rate will stutter. The framework's `LogSetup` mitigates this by wrapping the NLog `FileTarget` in an `AsyncTargetWrapper`. This delegates the actual disk writes to a background thread with a bounded queue (e.g., 10,000 items in dev, 50,000 in prod) and batching behavior, ensuring the hot path never blocks on I/O.

3\. Distributed Context Injection (MDC/ScopeContext)

In a runner hosting multiple subsystems (SimHost, IG, ExCon) potentially running in the same process or distributed across a network, reading a flat log file becomes impossible without context. Instead of forcing developers to manually prepend node IDs to every log message, the framework utilizes NLog's `ScopeContext` to inject ambient data into the thread execution context. The logger layout is then centrally configured to stamp every line with `[${scopeproperty:NodeId}]` and the executing subsystem name.

4\. Environment-Specific Log Routing

Because the framework is modular, it centralizes NLog configuration into profiles (`ConfigureForDevelopment`, `ConfigureForProduction`, `ConfigureForTesting`).

-   **Production:** The pipeline strictly filters out anything below `Warn`, increases the async buffer queue sizes significantly, and rolls archives at 50MB.-   **Development:** It selectively routes extremely verbose traces (like network serialization and replication internals) only if explicitly requested, while filtering out noisy kernel logs to keep the stream legible.

By combining the developer ergonomics of `FdpLog<MySystem>.Debug(...)` with zero-allocation wrappers and asynchronous I/O, the architecture guarantees that your static logging preference remains extremely fast and highly structured across the entire distributed cluster.

So basically no changes necessary?

Exactly. No architectural changes are necessary on your end.

The framework already natively implements your exact preference for static, per-module logging through the `FdpLog<T>` facade. By simply calling `FdpLog<MySystem>.Info(...)` within your classes, you get the clean developer ergonomics you want, while the framework handles the complex distributed requirements behind the scenes. It automatically gates allocations, offloads disk I/O to an asynchronous background thread, and uses NLog's `ScopeContext` to automatically inject the executing node's ID into every log line.

You can comfortably stick to this pattern knowing the underlying architecture is already built to handle it without choking the simulation loop.

