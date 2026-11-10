using UnityEngine;
using UnityEngine.EventSystems;

public class RTSCameraController : MonoBehaviour
{
    public static RTSCameraController instance;

    // If we want to select an item to follow, inside the item script add:
    // public void OnMouseDown(){
    //   CameraController.instance.followTransform = transform;
    // }

    [Header("General")]
    [SerializeField] Transform cameraTransform;
    public Transform followTransform;
    Vector3 newPosition;
    Vector3 dragStartPosition;
    Vector3 dragCurrentPosition;

    [Header("Optional Functionality")]
    [SerializeField] bool moveWithKeyboad;
    [SerializeField] bool moveWithEdgeScrolling;
    [SerializeField] bool moveWithMouseDrag;

    [Header("Keyboard Movement")]
    [SerializeField] float fastSpeed = 0.05f;
    [SerializeField] float normalSpeed = 0.01f;
    [SerializeField] float movementSensitivity = 1f; //Hardcoded Sensitivity
    float movementSpeed;

    [Header("Edge Scrolling Movement")]
    [SerializeField] float edgeSize = 50f;
    bool isCursorSet = false;
    public Texture2D cursorArrowUp;
    public Texture2D cursorArrowDown;
    public Texture2D cursorArrowLeft;
    public Texture2D cursorArrowRight;

    CursorArrow currentCursor = CursorArrow.DEFAULT;

    [Header("Bounds")]
    [Tooltip("Keep the camera over the map: the planets on the galactic map, the battlefield in battle.")]
    [SerializeField] bool clampToMap = true;
    [Tooltip("How far past the outermost planets the galactic map camera may go.")]
    [SerializeField] float mapMargin = 80f;
    bool hasBounds;
    Rect bounds;

    [Header("Zoom")]
    [SerializeField] float zoomSpeed = 4f;
    [SerializeField] float minZoom = 10f;
    [SerializeField] float maxZoom = 50f;
    float targetZoom;

    enum CursorArrow
    {
        UP,
        DOWN,
        LEFT,
        RIGHT,
        DEFAULT
    }

    private Camera cam;

    private void Start()
    {
        instance = this;
        cam = Camera.main;

        newPosition = transform.position;

        movementSpeed = normalSpeed;

        targetZoom = cameraTransform.localPosition.y;
        FindBounds();
    }

    // Galactic map: the planets' spread plus a margin. Battle: the battlefield plane.
    private void FindBounds()
    {
        hasBounds = false;
        if (!clampToMap) return;

        if (Planet.All.Count > 0)
        {
            Vector3 first = Planet.All[0].transform.position;
            float minX = first.x, maxX = first.x, minZ = first.z, maxZ = first.z;
            foreach (Planet planet in Planet.All)
            {
                Vector3 position = planet.transform.position;
                minX = Mathf.Min(minX, position.x);
                maxX = Mathf.Max(maxX, position.x);
                minZ = Mathf.Min(minZ, position.z);
                maxZ = Mathf.Max(maxZ, position.z);
            }
            bounds = Rect.MinMaxRect(minX - mapMargin, minZ - mapMargin, maxX + mapMargin, maxZ + mapMargin);
            hasBounds = true;
            return;
        }

        if (BattleTerrain.Instance != null)
        {
            bounds = BattleTerrain.Instance.Map;
            hasBounds = true;
            return;
        }

        int groundLayer = LayerMask.NameToLayer("Ground");
        foreach (Renderer ground in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (ground.gameObject.layer != groundLayer) continue;
            bounds = new Rect(ground.bounds.min.x, ground.bounds.min.z, ground.bounds.size.x, ground.bounds.size.z);
            hasBounds = true;
            return;
        }
    }

    // Clamps what you're looking at, not the rig: the camera sits back from the rig and looks ahead at an
    // angle, so the point under the middle of the screen is what has to stay over the map.
    private Vector3 Clamp(Vector3 position)
    {
        if (!hasBounds) return position;

        Vector3 lookOffset = LookOffset();
        Vector3 focus = position + lookOffset;
        focus.x = Mathf.Clamp(focus.x, bounds.xMin, bounds.xMax);
        focus.z = Mathf.Clamp(focus.z, bounds.yMin, bounds.yMax);
        return focus - lookOffset;
    }

    // Flat offset from the rig to the map point under the centre of the screen.
    private Vector3 LookOffset()
    {
        Transform view = cam != null ? cam.transform : cameraTransform;
        if (view == null) return Vector3.zero;

        Plane map = new Plane(Vector3.up, Vector3.zero);
        Ray ray = new Ray(view.position, view.forward);
        if (!map.Raycast(ray, out float distance)) return Vector3.zero;

        Vector3 offset = ray.GetPoint(distance) - transform.position;
        offset.y = 0f;
        return offset;
    }

    private void Update()
    {
        if (GameSpeed.MenuPaused) return; // Pause menu is open.

        //Allow Camera to follow Target
        if (followTransform != null)
        {
            transform.position = Clamp(followTransform.position);
        }
        //Let us control Camera
        else
        {
            HandleOrbit();
            HandleCameraMovement();
            HandleZoom();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            followTransform = null;
        }
    }

    void HandleCameraMovement()
    {
        //Mouse Drag
        if (moveWithMouseDrag)
        {
            HandleMouseDragInput();
        }

        //Keyboard Control
        if (moveWithKeyboad)
        {
            if (Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.LeftShift))
            {
                movementSpeed = fastSpeed * GameSettings.CameraSpeed;
            }
            else
            {
                movementSpeed = normalSpeed * GameSettings.CameraSpeed;
            }

            if (Keybinds.Held(KeyAction.CameraForward) || Input.GetKey(KeyCode.UpArrow))
            {
                newPosition += (transform.forward * movementSpeed);
            }
            if (Keybinds.Held(KeyAction.CameraBack) || Input.GetKey(KeyCode.DownArrow))
            {
                newPosition += (transform.forward * -movementSpeed);
            }
            if (Keybinds.Held(KeyAction.CameraRight) || Input.GetKey(KeyCode.RightArrow))
            {
                newPosition += (transform.right * movementSpeed);
            }
            if (Keybinds.Held(KeyAction.CameraLeft) || Input.GetKey(KeyCode.LeftArrow))
            {
                newPosition += (transform.right * -movementSpeed);
            }
        }

        // Edge Scrolling - only while the game has focus and the mouse is really in the window, so an
        // AFK cursor parked on the edge (or on another monitor) doesn't drift the camera forever.
        Vector3 mouse = Input.mousePosition;
        bool mouseInWindow = Application.isFocused && mouse.x >= 0f && mouse.y >= 0f && mouse.x <= Screen.width && mouse.y <= Screen.height;
        if (moveWithEdgeScrolling && GameSettings.EdgeScrolling && mouseInWindow && !GameSpeed.MenuPaused)
        {

            // Move Right
            if (Input.mousePosition.x > Screen.width - edgeSize)
            {
                newPosition += (transform.right * movementSpeed);
                ChangeCursor(CursorArrow.RIGHT);
                isCursorSet = true;
            }

            // Move Left
            else if (Input.mousePosition.x < edgeSize)
            {
                newPosition += (transform.right * -movementSpeed);
                ChangeCursor(CursorArrow.LEFT);
                isCursorSet = true;
            }

            // Move Up
            else if (Input.mousePosition.y > Screen.height - edgeSize)
            {
                newPosition += (transform.forward * movementSpeed);
                ChangeCursor(CursorArrow.UP);
                isCursorSet = true;
            }

            // Move Down
            else if (Input.mousePosition.y < edgeSize)
            {
                newPosition += (transform.forward * -movementSpeed);
                ChangeCursor(CursorArrow.DOWN);
                isCursorSet = true;
            }
            else
            {
                if (isCursorSet)
                {
                    ChangeCursor(CursorArrow.DEFAULT);
                    isCursorSet = false;
                }
            }
        }

        else if (isCursorSet)
        {
            ChangeCursor(CursorArrow.DEFAULT);
            isCursorSet = false;
        }

        newPosition = Clamp(newPosition);
        transform.position = Vector3.Lerp(transform.position, newPosition, Time.unscaledDeltaTime * movementSensitivity);

        // Keep the cursor in the window for edge scrolling on multi-monitor setups (optional in settings).
        Cursor.lockState = GameSettings.ConfineCursor && Application.isFocused ? CursorLockMode.Confined : CursorLockMode.None;
    }

    private void ChangeCursor(CursorArrow newCursor)
    {
        //Only change cursor if its not the same cursor
        if (currentCursor != newCursor)
        {
            switch (newCursor)
            {
                case CursorArrow.UP:
                    Cursor.SetCursor(cursorArrowUp, Vector2.zero, CursorMode.Auto);
                    break;
                case CursorArrow.DOWN:
                    Cursor.SetCursor(cursorArrowDown, new Vector2(cursorArrowDown.width, cursorArrowDown.height), CursorMode.Auto); //So the Cursor will stay inside view
                    break;
                case CursorArrow.LEFT:
                    Cursor.SetCursor(cursorArrowLeft, Vector2.zero, CursorMode.Auto);
                    break;
                case CursorArrow.RIGHT:
                    Cursor.SetCursor(cursorArrowRight, new Vector2(cursorArrowRight.width, cursorArrowRight.height), CursorMode.Auto); //So the Cursor will stay inside view
                    break;
                case CursorArrow.DEFAULT:
                    Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                    break;
            }

            currentCursor = newCursor;
        }
    }



    private void HandleMouseDragInput()
    {
        if (OrbitHeld) return; // Alt + middle mouse orbits instead.

        if (Input.GetMouseButtonDown(2) && !UIKit.PointerOverScreenUI())
        {
            Plane plane = new Plane(Vector3.up, Vector3.zero);
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            float entry;

            if (plane.Raycast(ray, out entry))
            {
                dragStartPosition = ray.GetPoint(entry);
            }
        }
        if (Input.GetMouseButton(2) && !UIKit.PointerOverScreenUI())
        {
            Plane plane = new Plane(Vector3.up, Vector3.zero);
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            float entry;

            if (plane.Raycast(ray, out entry))
            {
                dragCurrentPosition = ray.GetPoint(entry);

                newPosition = transform.position + dragStartPosition - dragCurrentPosition;
            }
        }
    }

    // ---------------- Orbit (battles only - space is 3D there) ----------------
    //   Q / E                 swing the view around the point you're looking at
    //   Alt + middle-drag     orbit and tilt freely

    [Header("Orbit (battle)")]
    [SerializeField] float orbitKeySpeed = 90f;      // Degrees per second.
    [SerializeField] float orbitMouseSpeed = 0.25f;  // Degrees per pixel.
    [SerializeField] float minTilt = 15f;            // Degrees below the horizon the camera looks (galactic map).
    [SerializeField] float maxTilt = 85f;
    // Battles are 3D: the camera can swing right under the battle plane and look up at ships below it.
    private const float BattleMinTilt = -80f;
    private const float BattleMaxTilt = 85f;

    private static bool OrbitHeld => (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetMouseButton(2);
    private bool CanOrbit => BattleTerrain.Instance != null && Planet.All.Count == 0;
    private Vector3 lastOrbitMouse;

    private void HandleOrbit()
    {
        if (!CanOrbit || cam == null) return;

        float yaw = 0f;
        float pitch = 0f;
        if (Keybinds.Held(KeyAction.RotateLeft)) yaw += orbitKeySpeed * Time.unscaledDeltaTime;
        if (Keybinds.Held(KeyAction.RotateRight)) yaw -= orbitKeySpeed * Time.unscaledDeltaTime;

        if (OrbitHeld)
        {
            if (Input.GetMouseButtonDown(2)) lastOrbitMouse = Input.mousePosition;
            Vector3 delta = Input.mousePosition - lastOrbitMouse;
            lastOrbitMouse = Input.mousePosition;
            yaw += delta.x * orbitMouseSpeed;
            pitch -= delta.y * orbitMouseSpeed;
        }

        if (Mathf.Approximately(yaw, 0f) && Mathf.Approximately(pitch, 0f)) return;

        Vector3 focus = transform.position + LookOffset();

        if (!Mathf.Approximately(yaw, 0f))
        {
            transform.RotateAround(focus, Vector3.up, yaw);
            newPosition = transform.position;
        }

        if (!Mathf.Approximately(pitch, 0f))
        {
            // Signed: positive looking down at the plane, negative looking up at it from below.
            float tilt = Mathf.Asin(Mathf.Clamp(-cameraTransform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float clamped = Mathf.Clamp(tilt + pitch, BattleMinTilt, BattleMaxTilt);
            float applied = clamped - tilt;
            if (!Mathf.Approximately(applied, 0f))
            {
                cameraTransform.RotateAround(focus, transform.right, applied);
                targetZoom = cameraTransform.localPosition.y;
                orbitDistance = Vector3.Distance(cameraTransform.position, focus);
            }
        }
    }

    // Battle zoom: closer to or further from the point under the screen centre, along the view - so it works
    // the same from above the plane or below it.
    private float orbitDistance = -1f;

    private void BattleZoom(float scrollInput)
    {
        Vector3 focus = FocusPoint();
        float current = Vector3.Distance(cameraTransform.position, focus);
        if (orbitDistance < 0f) orbitDistance = current;
        orbitDistance = Mathf.Clamp(orbitDistance - scrollInput * zoomSpeed * 1.4f, minZoom, maxZoom * 1.6f);
        float next = Mathf.Lerp(current, orbitDistance, Time.unscaledDeltaTime * zoomSpeed);
        cameraTransform.position = focus - cameraTransform.forward * next;
        targetZoom = cameraTransform.localPosition.y;
    }

    // The point the camera is looking at: where its view meets the battle plane, or (looking along the
    // plane) a point in front of it.
    private Vector3 FocusPoint()
    {
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float distance) && distance < maxZoom * 4f) return ray.GetPoint(distance);
        return cameraTransform.position + cameraTransform.forward * Mathf.Max(minZoom, orbitDistance > 0f ? orbitDistance : maxZoom * 0.6f);
    }

    void HandleZoom()
    {
        float scrollInput = PlacementManager.Instance != null && PlacementManager.Instance.IsPreviewing ? 0f : Input.GetAxis("Mouse ScrollWheel");
        if (CanOrbit)
        {
            BattleZoom(scrollInput);
            return;
        }
        targetZoom -= scrollInput * zoomSpeed;
        targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);

        Vector3 zoomVector = new Vector3(cameraTransform.localPosition.x, targetZoom, cameraTransform.localPosition.z);
        cameraTransform.localPosition = Vector3.Lerp(cameraTransform.localPosition, zoomVector, Time.unscaledDeltaTime * zoomSpeed);
    }
}