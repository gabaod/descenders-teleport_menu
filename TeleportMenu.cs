using UnityEngine;
using ModTool.Interface;

/// <summary>
/// Displays an on-screen teleportation menu that can be toggled with a configurable key.
/// Drag GameObjects into the Inspector to define teleport destinations.
/// The window auto-sizes to fit all buttons on open and can be freely resized by
/// dragging the handle at the bottom-right corner.
/// Attach this script to any GameObject in the scene.
/// </summary>
public class TeleportMenu : ModBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector Fields
    // -------------------------------------------------------------------------

    [Header("Menu Toggle")]
    [Tooltip("Keyboard key that shows / hides the teleportation menu.")]
    public KeyCode toggleKey = KeyCode.F1;

    [Header("Teleportation Zones")]
    [Tooltip("Drag one GameObject here for each teleportation zone. " +
             "The GameObject's name is used as the button label.")]
    public GameObject[] teleportZones = new GameObject[0];

    [Header("Teleport Options")]
    [Tooltip("When enabled, Player_Human keeps their current velocity after teleporting.")]
    public bool preserveVelocity = false;

    // -------------------------------------------------------------------------
    //  Layout Constants
    // -------------------------------------------------------------------------

    private const float BUTTON_HEIGHT   = 48f;
    private const float BUTTON_SPACING  = 6f;
    private const float PADDING         = 12f;
    private const float TITLE_H         = 46f;  // title label + gap below it
    private const float DIVIDER_H       = 1f;
    private const float INFO_H          = 36f;  // info strip (approximate)
    private const float CLOSE_H         = 64f;  // divider + spaces + close btn + bottom pad
    private const float RESIZE_GRIP     = 18f;  // width & height of the resize handle
    private const float MIN_WIDTH       = 280f;
    private const float MIN_HEIGHT      = 180f;
    private const float MAX_WIDTH_FRAC  = 0.85f;
    private const float MAX_HEIGHT_FRAC = 0.90f;
    private const int   WINDOW_ID       = 9901;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------

    private bool    _menuVisible    = false;
    private Vector2 _scrollPos      = Vector2.zero;

    // Cached player references
    private GameObject  _playerObject;
    private Rigidbody   _playerRigidbody;
    private Rigidbody2D _playerRigidbody2D;

    // Window rect
    private Rect _windowRect;

    // Resize-drag state
    private bool    _resizing         = false;
    private Vector2 _resizeDragStart  = Vector2.zero; // screen-space mouse pos when drag began
    private Vector2 _sizeAtDragStart  = Vector2.zero; // window size when drag began

    // -------------------------------------------------------------------------
    //  Styles  (lazily created inside OnGUI)
    // -------------------------------------------------------------------------

    private GUIStyle _windowStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _infoStyle;
    private GUIStyle _resizeStyle;
    private bool     _stylesInitialised = false;

    // -------------------------------------------------------------------------
    //  Unity Messages
    // -------------------------------------------------------------------------

    private void Start()
    {
        // Place off-screen until first opened so auto-size runs first.
        _windowRect = new Rect(0f, 0f, MIN_WIDTH, MIN_HEIGHT);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            _menuVisible = !_menuVisible;

            if (_menuVisible)
            {
                CachePlayerReferences();
                AutoSizeWindow(); // fit to button count every time the menu opens
            }
        }

        // Handle resize dragging in Update so delta tracking works even when
        // the cursor moves outside the grip area between frames.
        if (_resizing)
        {
            if (Input.GetMouseButton(0))
            {
                // Unity's Input.mousePosition is bottom-left origin; GUI is top-left.
                Vector2 mouse = new Vector2(
                    Input.mousePosition.x,
                    Screen.height - Input.mousePosition.y);

                Vector2 delta = mouse - _resizeDragStart;

                _windowRect.width = Mathf.Clamp(
                    _sizeAtDragStart.x + delta.x,
                    MIN_WIDTH,
                    Screen.width * MAX_WIDTH_FRAC);

                _windowRect.height = Mathf.Clamp(
                    _sizeAtDragStart.y + delta.y,
                    MIN_HEIGHT,
                    Screen.height * MAX_HEIGHT_FRAC);
            }
            else
            {
                _resizing = false; // mouse button released
            }
        }
    }

    private void OnGUI()
    {
        if (!_menuVisible) return;

        InitialiseStyles();

        // --- Dim overlay behind the window ---
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Keep window on screen after dragging or screen-size changes
        _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Screen.width  - _windowRect.width);
        _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Screen.height - _windowRect.height);

        _windowRect = GUI.Window(WINDOW_ID, _windowRect, DrawWindowContents,
                                 GUIContent.none, _windowStyle);
    }

    // -------------------------------------------------------------------------
    //  Window Contents
    // -------------------------------------------------------------------------

    private void DrawWindowContents(int windowId)
    {
        // ---- Title bar ----
        GUILayout.Space(PADDING);
        GUILayout.Label("  \u2708  Teleport Menu", _titleStyle);
        GUILayout.Space(4f);

        DrawHorizontalLine(new Color(1f, 1f, 1f, 0.25f));

        // ---- Info strip ----
        string playerStatus = (_playerObject != null)
            ? "<color=#90ee90>\u2713 Player_Human found</color>"
            : "<color=#ff7f7f>\u2717 Player_Human not found</color>";

        GUILayout.Label(string.Format(
            "Toggle: <b>{0}</b>   |   Preserve velocity: <b>{1}</b>   |   {2}",
            toggleKey,
            preserveVelocity ? "Yes" : "No",
            playerStatus), _infoStyle);

        DrawHorizontalLine(new Color(1f, 1f, 1f, 0.15f));
        GUILayout.Space(6f);

        // ---- Scroll area with zone buttons ----
        if (teleportZones == null || teleportZones.Length == 0)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "<color=#ffcc44>No teleport zones defined.\n" +
                "Add GameObjects in the Inspector.</color>", _infoStyle);
            GUILayout.FlexibleSpace();
        }
        else
        {
            _scrollPos = GUILayout.BeginScrollView(
                _scrollPos, false, false, GUIStyle.none, GUIStyle.none);

            for (int i = 0; i < teleportZones.Length; i++)
            {
                GameObject zone = teleportZones[i];
                if (zone == null) continue;

                string label = string.Format("{0}  \u279C  {1}",
                    (i + 1).ToString("00"), zone.name);

                if (GUILayout.Button(label, _buttonStyle,
                    GUILayout.Height(BUTTON_HEIGHT)))
                {
                    TeleportPlayer(zone);
                }

                GUILayout.Space(BUTTON_SPACING);
            }

            GUILayout.EndScrollView();
        }

        // ---- Close button ----
        GUILayout.Space(8f);
        DrawHorizontalLine(new Color(1f, 1f, 1f, 0.15f));
        GUILayout.Space(8f);

        if (GUILayout.Button(string.Format("Close  ( {0} )", toggleKey),
            _buttonStyle, GUILayout.Height(40f)))
        {
            _menuVisible = false;
        }

        // Space for the resize grip
        GUILayout.Space(RESIZE_GRIP + 4f);

        // ---- Resize grip (drawn last so it sits on top) ----
        DrawResizeGrip();

        // Only the title-bar strip triggers a window drag
        GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, TITLE_H + PADDING));
    }

    // -------------------------------------------------------------------------
    //  Resize Grip
    // -------------------------------------------------------------------------

    private void DrawResizeGrip()
    {
        Rect gripRect = new Rect(
            _windowRect.width  - RESIZE_GRIP - 4f,
            _windowRect.height - RESIZE_GRIP - 4f,
            RESIZE_GRIP,
            RESIZE_GRIP);

        // Draw the visible handle (◢ = bottom-right filled-triangle Unicode)
        GUI.Box(gripRect, "\u25E2", _resizeStyle);

        Event e = Event.current;
        if (!_resizing &&
            e.type == EventType.MouseDown &&
            e.button == 0 &&
            gripRect.Contains(e.mousePosition))
        {
            _resizing = true;

            // Record drag origin in screen space for Update() delta calculation
            _resizeDragStart = new Vector2(
                _windowRect.x + e.mousePosition.x,
                _windowRect.y + e.mousePosition.y);

            _sizeAtDragStart = new Vector2(_windowRect.width, _windowRect.height);

            e.Use(); // prevent DragWindow from also consuming this event
        }
    }

    // -------------------------------------------------------------------------
    //  Auto-size on Open
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets the window height so all buttons are visible without scrolling,
    /// capped at MAX_HEIGHT_FRAC of the screen. Centres the window.
    /// </summary>
    private void AutoSizeWindow()
    {
        int zoneCount = 0;
        if (teleportZones != null)
            foreach (GameObject z in teleportZones)
                if (z != null) zoneCount++;

        // Fixed chrome: title + info strip + close area + resize grip
        float chromeH = PADDING + TITLE_H + DIVIDER_H + INFO_H + DIVIDER_H
                      + 6f + CLOSE_H + RESIZE_GRIP + 8f;

        float buttonsH = zoneCount * (BUTTON_HEIGHT + BUTTON_SPACING);
        float idealH   = chromeH + buttonsH;
        float maxH     = Screen.height * MAX_HEIGHT_FRAC;
        float finalH   = Mathf.Clamp(idealH, MIN_HEIGHT, maxH);

        // Preserve any width the user has manually set; otherwise use the default.
        float finalW   = Mathf.Max(_windowRect.width, 340f);
        finalW         = Mathf.Min(finalW, Screen.width * MAX_WIDTH_FRAC);

        _windowRect.width  = finalW;
        _windowRect.height = finalH;

        // Centre on screen
        _windowRect.x = (Screen.width  - finalW) * 0.5f;
        _windowRect.y = (Screen.height - finalH) * 0.5f;

        _scrollPos = Vector2.zero;
    }

    // -------------------------------------------------------------------------
    //  Teleportation Logic
    // -------------------------------------------------------------------------

    private void TeleportPlayer(GameObject zone)
    {
        if (_playerObject == null)
        {
            Debug.LogWarning("[TeleportMenu] Cannot teleport – Player_Human not found.");
            return;
        }

        Vector3 savedVelocity   = Vector3.zero;
        Vector2 savedVelocity2D = Vector2.zero;
        bool hasRb   = _playerRigidbody   != null;
        bool hasRb2D = _playerRigidbody2D != null;

        if (preserveVelocity)
        {
            if (hasRb)   savedVelocity   = _playerRigidbody.velocity;
            if (hasRb2D) savedVelocity2D = _playerRigidbody2D.velocity;
        }

        _playerObject.transform.position = zone.transform.position;
        _playerObject.transform.rotation = zone.transform.rotation; 

        if (hasRb)
        {
            _playerRigidbody.velocity        = preserveVelocity ? savedVelocity   : Vector3.zero;
            _playerRigidbody.angularVelocity = Vector3.zero;
        }

        if (hasRb2D)
        {
            _playerRigidbody2D.velocity        = preserveVelocity ? savedVelocity2D : Vector2.zero;
            _playerRigidbody2D.angularVelocity = 0f;
        }

        Debug.LogFormat("[TeleportMenu] Teleported Player_Human to '{0}' at {1}",
            zone.name, zone.transform.position);
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private void CachePlayerReferences()
    {
        _playerObject      = GameObject.Find("Player_Human");
        _playerRigidbody   = null;
        _playerRigidbody2D = null;

        if (_playerObject != null)
        {
            _playerRigidbody   = _playerObject.GetComponent<Rigidbody>();
            _playerRigidbody2D = _playerObject.GetComponent<Rigidbody2D>();
        }
        else
        {
            Debug.LogWarning("[TeleportMenu] 'Player_Human' not found in scene.");
        }
    }

    private static Texture2D MakeSolidTexture(Color col)
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, col);
        tex.Apply();
        return tex;
    }

    private static void DrawHorizontalLine(Color col)
    {
        Rect r    = GUILayoutUtility.GetRect(1f, 1f);
        r.x       = PADDING;
        r.width  -= PADDING * 2f;
        GUI.color = col;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void InitialiseStyles()
    {
        if (_stylesInitialised) return;
        _stylesInitialised = true;

        // Window background
        _windowStyle = new GUIStyle(GUI.skin.box);
        _windowStyle.normal.background = MakeSolidTexture(new Color(0.08f, 0.08f, 0.12f, 0.97f));
        _windowStyle.border  = new RectOffset(6, 6, 6, 6);
        _windowStyle.padding = new RectOffset((int)PADDING, (int)PADDING, 0, (int)PADDING);

        // Title
        _titleStyle                  = new GUIStyle(GUI.skin.label);
        _titleStyle.fontSize         = 22;
        _titleStyle.fontStyle        = FontStyle.Bold;
        _titleStyle.normal.textColor = Color.white;
        _titleStyle.alignment        = TextAnchor.MiddleLeft;
        _titleStyle.richText         = true;

        // Info strip
        _infoStyle                  = new GUIStyle(GUI.skin.label);
        _infoStyle.fontSize         = 11;
        _infoStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        _infoStyle.wordWrap         = true;
        _infoStyle.richText         = true;
        _infoStyle.alignment        = TextAnchor.MiddleLeft;
        _infoStyle.padding          = new RectOffset(4, 4, 4, 4);

        // Zone / close buttons
        _buttonStyle                   = new GUIStyle(GUI.skin.button);
        _buttonStyle.fontSize          = 15;
        _buttonStyle.fontStyle         = FontStyle.Bold;
        _buttonStyle.alignment         = TextAnchor.MiddleLeft;
        _buttonStyle.richText          = true;
        _buttonStyle.padding           = new RectOffset(16, 16, 0, 0);
        _buttonStyle.normal.background = MakeSolidTexture(new Color(0.10f, 0.28f, 0.30f, 1f));
        _buttonStyle.normal.textColor  = new Color(0.85f, 1.00f, 0.95f);
        _buttonStyle.hover.background  = MakeSolidTexture(new Color(0.15f, 0.45f, 0.48f, 1f));
        _buttonStyle.hover.textColor   = Color.white;
        _buttonStyle.active.background = MakeSolidTexture(new Color(0.05f, 0.18f, 0.20f, 1f));
        _buttonStyle.active.textColor  = new Color(0.70f, 1.00f, 0.90f);

        // Resize grip
        _resizeStyle                  = new GUIStyle(GUI.skin.box);
        _resizeStyle.fontSize         = 14;
        _resizeStyle.fontStyle        = FontStyle.Bold;
        _resizeStyle.alignment        = TextAnchor.MiddleCenter;
        _resizeStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
        _resizeStyle.normal.background= MakeSolidTexture(new Color(0.18f, 0.18f, 0.26f, 1f));
        _resizeStyle.hover.background = MakeSolidTexture(new Color(0.28f, 0.55f, 0.58f, 1f));
        _resizeStyle.hover.textColor  = Color.white;
        _resizeStyle.border           = new RectOffset(2, 2, 2, 2);
        _resizeStyle.padding          = new RectOffset(0, 0, 0, 0);
    }
}
