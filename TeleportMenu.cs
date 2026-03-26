using UnityEngine;
using ModTool.Interface;

/// <summary>
/// Displays an on-screen teleportation menu toggled by a configurable key.
/// Supports two display modes selectable via Inspector checkbox:
///   - Button List  : scrollable list of named teleport buttons (default)
///   - Map View     : top-down map image with clickable zone markers
/// Drag GameObjects into the Inspector to define teleport destinations.
/// The window is draggable and resizable in both modes.
/// </summary>
public class TeleportMenu : ModBehaviour
{
    // =========================================================================
    //  Inspector Fields
    // =========================================================================

    [Header("Menu Toggle")]
    [Tooltip("Keyboard key that shows / hides the teleportation menu.")]
    public KeyCode toggleKey = KeyCode.F1;

    // -------------------------------------------------------------------------
    [Header("Teleportation Zones")]
    [Tooltip("Drag one GameObject here per zone. Its name is used as the label.")]
    public GameObject[] teleportZones = new GameObject[0];

    // -------------------------------------------------------------------------
    [Header("Teleport Options")]
    [Tooltip("Keep Player_Human's current velocity after teleporting.")]
    public bool preserveVelocity = false;

    // -------------------------------------------------------------------------
    [Header("Map View")]
    [Tooltip("When checked the menu displays a top-down map instead of a button list.")]
    public bool useMapView = false;

    [Tooltip("Top-down map texture (JPG or PNG imported as Texture2D).")]
    public Texture2D mapTexture;

    [Tooltip("World-space X/Z coordinate of the map image's bottom-left corner.\n" +
             "Y (height) is ignored – only X and Z matter for a top-down projection.")]
    public Vector2 mapWorldMin = new Vector2(-500f, -500f);

    [Tooltip("World-space X/Z coordinate of the map image's top-right corner.\n" +
             "Y (height) is ignored – only X and Z matter for a top-down projection.")]
    public Vector2 mapWorldMax = new Vector2(500f, 500f);

    [Tooltip("(Optional) Custom icon drawn at each zone's map position.\n" +
             "Leave empty to use a coloured circle instead.")]
    public Texture2D zoneMarkerIcon;

    [Tooltip("Size in pixels of each zone marker on the map.")]
    public float markerSize = 24f;

    [Tooltip("Show Player_Human's live position on the map.\n" +
             "Position is only sampled while the map is visible.")]
    public bool showPlayerOnMap = false;

    [Tooltip("(Optional) Custom icon for the player marker.\n" +
             "Leave empty to use a coloured circle instead.")]
    public Texture2D playerMarkerIcon;

    [Tooltip("Size in pixels of the player marker on the map.")]
    public float playerMarkerSize = 28f;

    // =========================================================================
    //  Layout Constants
    // =========================================================================

    private const float BUTTON_HEIGHT   = 48f;
    private const float BUTTON_SPACING  = 6f;
    private const float PADDING         = 12f;
    private const float TITLE_H         = 46f;
    private const float DIVIDER_H       = 1f;
    private const float INFO_H          = 36f;
    private const float CLOSE_H         = 64f;
    private const float RESIZE_GRIP     = 18f;
    private const float MIN_WIDTH       = 300f;
    private const float MIN_HEIGHT      = 220f;
    private const float MAP_MIN_WIDTH   = 360f;
    private const float MAP_MIN_HEIGHT  = 360f;
    private const float MAX_WIDTH_FRAC  = 0.90f;
    private const float MAX_HEIGHT_FRAC = 0.92f;
    private const int   WINDOW_ID       = 9901;

    // Marker colours used when no icon texture is supplied
    private static readonly Color ZONE_CIRCLE_COLOR   = new Color(0.20f, 0.80f, 0.90f, 0.92f);
    private static readonly Color ZONE_CIRCLE_OUTLINE = new Color(0.00f, 0.20f, 0.30f, 1.00f);
    private static readonly Color PLAYER_CIRCLE_COLOR = new Color(1.00f, 0.85f, 0.10f, 0.95f);
    private static readonly Color PLAYER_OUTLINE_COLOR= new Color(0.50f, 0.30f, 0.00f, 1.00f);

    // =========================================================================
    //  Private State
    // =========================================================================

    private bool    _menuVisible = false;
    private Vector2 _scrollPos   = Vector2.zero;

    // Cached player references
    private GameObject  _playerObject;
    private Rigidbody   _playerRigidbody;
    private Rigidbody2D _playerRigidbody2D;

    // Live player world position (X/Z only; updated each frame while map is open)
    private Vector2 _playerMapUV = new Vector2(-1f, -1f); // invalid sentinel

    // Window
    private Rect _windowRect;

    // Resize drag
    private bool    _resizing        = false;
    private Vector2 _resizeDragStart = Vector2.zero;
    private Vector2 _sizeAtDragStart = Vector2.zero;

    // Tooltip state (map view)
    private string _hoveredZoneName  = "";
    private Rect   _tooltipRect;

    // Fallback circle textures (created once, lazily)
    private Texture2D _circleZone;
    private Texture2D _circlePlayer;
    private Texture2D _circleOutline;

    // =========================================================================
    //  Styles  (lazily created inside OnGUI)
    // =========================================================================

    private GUIStyle _windowStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _infoStyle;
    private GUIStyle _resizeStyle;
    private GUIStyle _tooltipStyle;
    private GUIStyle _mapWarningStyle;
    private bool     _stylesInitialised = false;

    // =========================================================================
    //  Unity Messages
    // =========================================================================

    private void OnValidate()
    {
        if (!useMapView)
            showPlayerOnMap = false;
    }

    private void Start()
    {
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
                AutoSizeWindow();
            }
        }

        // Sample player position every frame only while the map is actually shown
        if (_menuVisible && useMapView && showPlayerOnMap && _playerObject != null)
        {
            Vector3 wp = _playerObject.transform.position;
            _playerMapUV = WorldXZToUV(wp.x, wp.z);
        }

        // Resize drag tracking
        if (_resizing)
        {
            if (Input.GetMouseButton(0))
            {
                Vector2 mouse = new Vector2(
                    Input.mousePosition.x,
                    Screen.height - Input.mousePosition.y);
                Vector2 delta = mouse - _resizeDragStart;

                float minW = useMapView ? MAP_MIN_WIDTH  : MIN_WIDTH;
                float minH = useMapView ? MAP_MIN_HEIGHT : MIN_HEIGHT;

                _windowRect.width  = Mathf.Clamp(_sizeAtDragStart.x + delta.x,
                                                  minW, Screen.width  * MAX_WIDTH_FRAC);
                _windowRect.height = Mathf.Clamp(_sizeAtDragStart.y + delta.y,
                                                  minH, Screen.height * MAX_HEIGHT_FRAC);
            }
            else
            {
                _resizing = false;
            }
        }
    }

    private void OnGUI()
    {
        if (!_menuVisible) return;

        InitialiseStyles();

        // Dim overlay
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Screen.width  - _windowRect.width);
        _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Screen.height - _windowRect.height);

        _windowRect = GUI.Window(WINDOW_ID, _windowRect, DrawWindowContents,
                                 GUIContent.none, _windowStyle);
    }

    // =========================================================================
    //  Window Contents – dispatcher
    // =========================================================================

    private void DrawWindowContents(int windowId)
    {
        DrawTitleBar();
        DrawInfoStrip();

        if (useMapView)
            DrawMapView();
        else
            DrawButtonList();

        DrawCloseButton();
        GUILayout.Space(RESIZE_GRIP + 4f);
        DrawResizeGrip();

        // Draw tooltip inside the window so it is never clipped or mis-positioned
        if (useMapView && _hoveredZoneName.Length > 0)
            DrawTooltip();

        GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, TITLE_H + PADDING));
    }

    // =========================================================================
    //  Shared Chrome
    // =========================================================================

    private void DrawTitleBar()
    {
        GUILayout.Space(PADDING);
        string modeTag = useMapView ? "  \uD83D\uDDFA" : "  \u2708";   // 🗺 or ✈
        GUILayout.Label(modeTag + "  Teleport Menu", _titleStyle);
        GUILayout.Space(4f);
        DrawHorizontalLine(new Color(1f, 1f, 1f, 0.25f));
    }

    private void DrawInfoStrip()
    {
        string playerStatus = (_playerObject != null)
            ? "<color=#90ee90>\u2713 Player_Human found</color>"
            : "<color=#ff7f7f>\u2717 Player_Human not found</color>";

        string mode = useMapView ? "Map" : "List";

        GUILayout.Label(string.Format(
            "Toggle: <b>{0}</b>   |   Mode: <b>{1}</b>   |   Velocity: <b>{2}</b>   |   {3}",
            toggleKey, mode,
            preserveVelocity ? "Kept" : "Reset",
            playerStatus), _infoStyle);

        DrawHorizontalLine(new Color(1f, 1f, 1f, 0.15f));
        GUILayout.Space(6f);
    }

    private void DrawCloseButton()
    {
        GUILayout.Space(8f);
        DrawHorizontalLine(new Color(1f, 1f, 1f, 0.15f));
        GUILayout.Space(8f);

        if (GUILayout.Button(string.Format("Close  ( {0} )", toggleKey),
            _buttonStyle, GUILayout.Height(40f)))
        {
            _menuVisible = false;
        }
    }

    // =========================================================================
    //  Button-List Mode
    // =========================================================================

    private void DrawButtonList()
    {
        if (teleportZones == null || teleportZones.Length == 0)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "<color=#ffcc44>No teleport zones defined.\n" +
                "Add GameObjects in the Inspector.</color>", _infoStyle);
            GUILayout.FlexibleSpace();
            return;
        }

        _scrollPos = GUILayout.BeginScrollView(
            _scrollPos, false, false, GUIStyle.none, GUIStyle.none);

        for (int i = 0; i < teleportZones.Length; i++)
        {
            GameObject zone = teleportZones[i];
            if (zone == null) continue;

            string label = string.Format("{0}  \u279C  {1}",
                (i + 1).ToString("00"), zone.name);

            if (GUILayout.Button(label, _buttonStyle, GUILayout.Height(BUTTON_HEIGHT)))
                TeleportPlayer(zone);

            GUILayout.Space(BUTTON_SPACING);
        }

        GUILayout.EndScrollView();
    }

    // =========================================================================
    //  Map View Mode
    // =========================================================================

    private void DrawMapView()
    {
        // Reserve remaining vertical space for the map area, minus chrome below
        float chromeBelow = CLOSE_H + RESIZE_GRIP + 12f;
        float mapAreaH    = _windowRect.height
                          - (PADDING + TITLE_H + DIVIDER_H + INFO_H + DIVIDER_H + 6f)
                          - chromeBelow;
        mapAreaH = Mathf.Max(mapAreaH, 60f);

        // The map rect in local window space
        Rect mapRect = GUILayoutUtility.GetRect(
            _windowRect.width - PADDING * 2f,
            mapAreaH);

        // --- Draw map background or placeholder ---
        if (mapTexture != null)
        {
            GUI.DrawTexture(mapRect, mapTexture, ScaleMode.StretchToFill);
        }
        else
        {
            GUI.color = new Color(0.05f, 0.10f, 0.12f, 1f);
            GUI.DrawTexture(mapRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(mapRect,
                "<color=#ffcc44>Assign a Map Texture in the Inspector.</color>",
                _mapWarningStyle);
        }

        // Draw a subtle border around the map area
        DrawRectOutline(mapRect, new Color(1f, 1f, 1f, 0.18f));

        // --- World-bounds validity check ---
        bool boundsValid = (mapWorldMax.x - mapWorldMin.x > 0.01f) &&
                           (mapWorldMax.y - mapWorldMin.y > 0.01f);

        if (!boundsValid)
        {
            GUI.Label(mapRect,
                "<color=#ff7f7f>Map World Bounds are invalid.\n" +
                "Check mapWorldMin / mapWorldMax in the Inspector.</color>",
                _mapWarningStyle);
        }
        else
        {
            // --- Zone markers ---
            _hoveredZoneName = "";
            if (teleportZones != null)
            {
                for (int i = 0; i < teleportZones.Length; i++)
                {
                    GameObject zone = teleportZones[i];
                    if (zone == null) continue;

                    Vector3 wp  = zone.transform.position;
                    Vector2 uv  = WorldXZToUV(wp.x, wp.z);
                    Vector2 px  = UVToMapPixel(uv, mapRect);
                    Rect markerRect = CentredRect(px, markerSize);

                    // Detect hover
                    bool hovered = markerRect.Contains(Event.current.mousePosition);
                    if (hovered)
                    {
                        _hoveredZoneName = string.Format("{0}  \u279C  {1}",
                            (i + 1).ToString("00"), zone.name);
                        // Convert window-local mouse to screen space for the tooltip
                        _tooltipRect = new Rect(
                            Event.current.mousePosition.x + 10f,
                            Event.current.mousePosition.y - 28f,
                            0f, 0f);  // sized in DrawTooltip
                    }

                    DrawMarker(markerRect, zoneMarkerIcon,
                               ZONE_CIRCLE_COLOR, ZONE_CIRCLE_OUTLINE,
                               hovered ? 1.35f : 1.0f);

                    // Click to teleport
                    if (hovered &&
                        Event.current.type == EventType.MouseDown &&
                        Event.current.button == 0)
                    {
                        TeleportPlayer(zone);
                        Event.current.Use();
                    }
                }
            }

            // --- Player marker ---
            if (showPlayerOnMap && _playerObject != null &&
                _playerMapUV.x >= 0f && _playerMapUV.x <= 1f &&
                _playerMapUV.y >= 0f && _playerMapUV.y <= 1f)
            {
                Vector2 px = UVToMapPixel(_playerMapUV, mapRect);
                Rect playerRect = CentredRect(px, playerMarkerSize);
                DrawMarker(playerRect, playerMarkerIcon,
                           PLAYER_CIRCLE_COLOR, PLAYER_OUTLINE_COLOR, 1.0f);

                // Small "P" label below the player marker if no custom icon
                if (playerMarkerIcon == null)
                {
                    Rect labelRect = new Rect(
                        playerRect.x - 4f,
                        playerRect.yMax + 1f,
                        playerMarkerSize + 8f, 14f);
                    GUI.color = PLAYER_CIRCLE_COLOR;
                    GUI.Label(labelRect, "<b>YOU</b>", _infoStyle);
                    GUI.color = Color.white;
                }
            }
        }

        GUILayout.Space(4f);
    }

    // =========================================================================
    //  Tooltip  (drawn in OnGUI, outside the Window, so it is never clipped)
    // =========================================================================

    private void DrawTooltip()
    {
        if (_tooltipStyle == null || _hoveredZoneName.Length == 0) return;

        // Measure the text so the background fits perfectly
        GUIContent content  = new GUIContent(_hoveredZoneName);
        Vector2     textSize = _tooltipStyle.CalcSize(content);

        float padH  = 12f;
        float padV  = 6f;
        float width  = textSize.x + padH * 2f;
        float height = textSize.y + padV * 2f;

        // Position: just to the right of and slightly above the cursor
        float tx = Mathf.Clamp(_tooltipRect.x,          4f, _windowRect.width  - width  - 4f);
        float ty = Mathf.Clamp(_tooltipRect.y - height,  4f, _windowRect.height - height - 4f);

        Rect bgRect   = new Rect(tx, ty, width, height);
        Rect textRect = new Rect(tx + padH, ty + padV, textSize.x, textSize.y);

        // Drop shadow
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(bgRect.x + 3f, bgRect.y + 3f, bgRect.width, bgRect.height),
                        Texture2D.whiteTexture);

        // Dark background
        GUI.color = new Color(0.05f, 0.08f, 0.12f, 0.96f);
        GUI.DrawTexture(bgRect, Texture2D.whiteTexture);

        // Thin border
        GUI.color = new Color(0.30f, 0.75f, 0.80f, 0.85f);
        DrawRectOutline(bgRect, new Color(0.30f, 0.75f, 0.80f, 0.85f));

        // Text
        GUI.color = Color.white;
        GUI.Label(textRect, content, _tooltipStyle);
    }

    // =========================================================================
    //  Marker Drawing
    // =========================================================================

    /// <summary>
    /// Draws a marker at <paramref name="rect"/> using either a supplied icon texture
    /// or a coloured circle fallback. <paramref name="scale"/> enlarges on hover.
    /// </summary>
    private void DrawMarker(Rect rect, Texture2D icon,
                            Color circleColor, Color outlineColor, float scale)
    {
        if (scale != 1f)
        {
            float expand = (rect.width * scale - rect.width) * 0.5f;
            rect = new Rect(rect.x - expand, rect.y - expand,
                            rect.width * scale, rect.height * scale);
        }

        if (icon != null)
        {
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
        }
        else
        {
            // Outline (slightly larger)
            Rect outRect = new Rect(rect.x - 2f, rect.y - 2f,
                                    rect.width + 4f, rect.height + 4f);
            GUI.color = outlineColor;
            GUI.DrawTexture(outRect, GetCircleTexture(false), ScaleMode.ScaleToFit);

            GUI.color = circleColor;
            GUI.DrawTexture(rect, GetCircleTexture(true), ScaleMode.ScaleToFit);

            GUI.color = Color.white;
        }
    }

    // =========================================================================
    //  Coordinate Mapping
    // =========================================================================

    /// <summary>
    /// Maps world X/Z to a 0-1 UV where (0,0) is bottom-left and (1,1) is top-right.
    /// Height (Y) is intentionally ignored so hilly terrain works correctly.
    /// </summary>
    private Vector2 WorldXZToUV(float worldX, float worldZ)
    {
        float u = Mathf.InverseLerp(mapWorldMin.x, mapWorldMax.x, worldX);
        float v = Mathf.InverseLerp(mapWorldMin.y, mapWorldMax.y, worldZ);
        return new Vector2(u, v);
    }

    /// <summary>
    /// Converts a 0-1 UV to a pixel position inside <paramref name="mapRect"/>
    /// (window-local coordinates).  V=0 → bottom of map, V=1 → top of map.
    /// </summary>
    private static Vector2 UVToMapPixel(Vector2 uv, Rect mapRect)
    {
        float px = mapRect.x + uv.x * mapRect.width;
        // Flip V because GUI Y increases downward
        float py = mapRect.y + (1f - uv.y) * mapRect.height;
        return new Vector2(px, py);
    }

    // =========================================================================
    //  Resize Grip
    // =========================================================================

    private void DrawResizeGrip()
    {
        Rect gripRect = new Rect(
            _windowRect.width  - RESIZE_GRIP - 4f,
            _windowRect.height - RESIZE_GRIP - 4f,
            RESIZE_GRIP, RESIZE_GRIP);

        GUI.Box(gripRect, "\u25E2", _resizeStyle);

        Event e = Event.current;
        if (!_resizing &&
            e.type == EventType.MouseDown &&
            e.button == 0 &&
            gripRect.Contains(e.mousePosition))
        {
            _resizing = true;
            _resizeDragStart = new Vector2(
                _windowRect.x + e.mousePosition.x,
                _windowRect.y + e.mousePosition.y);
            _sizeAtDragStart = new Vector2(_windowRect.width, _windowRect.height);
            e.Use();
        }
    }

    // =========================================================================
    //  Auto-size on Open
    // =========================================================================

    private void AutoSizeWindow()
    {
        float finalW, finalH;

        if (useMapView)
        {
            // Map view: default to a square-ish window, respecting screen limits
            finalW = Mathf.Clamp(520f, MAP_MIN_WIDTH,  Screen.width  * MAX_WIDTH_FRAC);
            finalH = Mathf.Clamp(560f, MAP_MIN_HEIGHT, Screen.height * MAX_HEIGHT_FRAC);
        }
        else
        {
            int zoneCount = 0;
            if (teleportZones != null)
                foreach (GameObject z in teleportZones)
                    if (z != null) zoneCount++;

            float chromeH = PADDING + TITLE_H + DIVIDER_H + INFO_H + DIVIDER_H
                          + 6f + CLOSE_H + RESIZE_GRIP + 8f;
            float buttonsH = zoneCount * (BUTTON_HEIGHT + BUTTON_SPACING);

            finalW = Mathf.Clamp(Mathf.Max(_windowRect.width, 340f),
                                  MIN_WIDTH,  Screen.width  * MAX_WIDTH_FRAC);
            finalH = Mathf.Clamp(chromeH + buttonsH,
                                  MIN_HEIGHT, Screen.height * MAX_HEIGHT_FRAC);
        }

        _windowRect.width  = finalW;
        _windowRect.height = finalH;
        _windowRect.x = (Screen.width  - finalW) * 0.5f;
        _windowRect.y = (Screen.height - finalH) * 0.5f;
        _scrollPos = Vector2.zero;
    }

    // =========================================================================
    //  Teleportation Logic
    // =========================================================================

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

        Debug.LogFormat("[TeleportMenu] Teleported Player_Human to '{0}' at {1}, facing {2}",
            zone.name, zone.transform.position, zone.transform.forward);
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

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

        _playerMapUV = new Vector2(-1f, -1f); // reset until next sample
    }

    private static Rect CentredRect(Vector2 centre, float size)
    {
        float half = size * 0.5f;
        return new Rect(centre.x - half, centre.y - half, size, size);
    }

    private static void DrawRectOutline(Rect r, Color col)
    {
        GUI.color = col;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f),           Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f),   Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height),          Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height),  Texture2D.whiteTexture);
        GUI.color = Color.white;
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

    /// <summary>
    /// Creates and caches a procedural soft-circle texture for fallback markers.
    /// <paramref name="filled"/> true = solid disc, false = thin ring.
    /// </summary>
    private Texture2D GetCircleTexture(bool filled)
    {
        const int SIZE = 64;

        if (filled && _circleZone  != null) return _circleZone;
        if (!filled && _circleOutline != null) return _circleOutline;

        Texture2D tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        float centre  = SIZE * 0.5f;
        float outerR  = centre - 1f;
        float innerR  = filled ? 0f : outerR - 5f;

        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y),
                                              new Vector2(centre, centre));
                float alpha = 0f;
                if (filled)
                {
                    alpha = 1f - Mathf.Clamp01((dist - outerR + 1.5f) / 1.5f);
                }
                else
                {
                    float dOuter = outerR - dist;
                    float dInner = dist - innerR;
                    float ring   = Mathf.Min(dOuter, dInner);
                    alpha = Mathf.Clamp01(ring / 1.5f);
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        tex.Apply();

        if (filled)  _circleZone    = tex;
        else         _circleOutline = tex;
        return tex;
    }

    // =========================================================================
    //  Style Initialisation
    // =========================================================================

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
        _resizeStyle                   = new GUIStyle(GUI.skin.box);
        _resizeStyle.fontSize          = 14;
        _resizeStyle.fontStyle         = FontStyle.Bold;
        _resizeStyle.alignment         = TextAnchor.MiddleCenter;
        _resizeStyle.normal.textColor  = new Color(0.65f, 0.65f, 0.65f);
        _resizeStyle.normal.background = MakeSolidTexture(new Color(0.18f, 0.18f, 0.26f, 1f));
        _resizeStyle.hover.background  = MakeSolidTexture(new Color(0.28f, 0.55f, 0.58f, 1f));
        _resizeStyle.hover.textColor   = Color.white;
        _resizeStyle.border            = new RectOffset(2, 2, 2, 2);
        _resizeStyle.padding           = new RectOffset(0, 0, 0, 0);

        // Tooltip
        _tooltipStyle                  = new GUIStyle(GUI.skin.label);
        _tooltipStyle.fontSize         = 13;
        _tooltipStyle.fontStyle        = FontStyle.Bold;
        _tooltipStyle.normal.textColor = Color.white;
        _tooltipStyle.alignment        = TextAnchor.MiddleCenter;
        _tooltipStyle.richText         = true;
        _tooltipStyle.padding          = new RectOffset(8, 8, 4, 4);

        // Map warning / placeholder label
        _mapWarningStyle                  = new GUIStyle(GUI.skin.label);
        _mapWarningStyle.fontSize         = 13;
        _mapWarningStyle.wordWrap         = true;
        _mapWarningStyle.richText         = true;
        _mapWarningStyle.alignment        = TextAnchor.MiddleCenter;
        _mapWarningStyle.normal.textColor = Color.white;
    }
}
