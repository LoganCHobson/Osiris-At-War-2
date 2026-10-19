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
    [SerializeField] float mapMargin = 40f;
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

    private Vector3 Clamp(Vector3 position)
    {
        if (!hasBounds) return position;
        position.x = Mathf.Clamp(position.x, bounds.xMin, bounds.xMax);
        position.z = Mathf.Clamp(position.z, bounds.yMin, bounds.yMax);
        return position;
    }

    private void Update()
    {
        //Allow Camera to follow Target
        if (followTransform != null)
        {
            transform.position = Clamp(followTransform.position);
        }
        //Let us control Camera
        else
        {
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

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            {
                newPosition += (transform.forward * movementSpeed);
            }
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            {
                newPosition += (transform.forward * -movementSpeed);
            }
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            {
                newPosition += (transform.right * movementSpeed);
            }
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
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
        if (Input.GetMouseButtonDown(2) && EventSystem.current.IsPointerOverGameObject() == false)
        {
            Plane plane = new Plane(Vector3.up, Vector3.zero);
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            float entry;

            if (plane.Raycast(ray, out entry))
            {
                dragStartPosition = ray.GetPoint(entry);
            }
        }
        if (Input.GetMouseButton(2) && EventSystem.current.IsPointerOverGameObject() == false)
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

    void HandleZoom()
    {
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        targetZoom -= scrollInput * zoomSpeed;
        targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);

        Vector3 zoomVector = new Vector3(cameraTransform.localPosition.x, targetZoom, cameraTransform.localPosition.z);
        cameraTransform.localPosition = Vector3.Lerp(cameraTransform.localPosition, zoomVector, Time.unscaledDeltaTime * zoomSpeed);
    }
}