using UnityEngine;
#if MONITORFP_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ARInteractionEventTracker : MonoBehaviour
{
    [Header("Deteccion")]
    [SerializeField] private bool enableTouch = true;
    [SerializeField] private bool enableMouseInEditor = true;
    [SerializeField] private float maxRayDistance = 100f;
    [SerializeField] private LayerMask interactionMask = ~0;

    private Camera targetCamera;
    private GameObject hoveredObject;

    private void Start()
    {
        targetCamera = Camera.main;
        if (targetCamera == null)
        {
            targetCamera = FindFirstObjectByType<Camera>();
        }
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            return;
        }

        Vector2 pointerPosition;
        bool hasPointer = TryGetPointerPosition(out pointerPosition);
        UpdateHoverState(hasPointer, pointerPosition);

        if (!hasPointer)
        {
            return;
        }

        if (IsPointerPressedThisFrame())
        {
            RegisterPointerEvent("interaction_press", "Interaccion iniciada", pointerPosition);
        }

        if (IsPointerReleasedThisFrame())
        {
            RegisterPointerEvent("interaction_release", "Interaccion finalizada", pointerPosition);
        }
    }

    private bool TryGetPointerPosition(out Vector2 pointerPosition)
    {
        pointerPosition = Vector2.zero;

#if MONITORFP_INPUT_SYSTEM
        if (enableTouch && Touchscreen.current != null)
        {
            pointerPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            return true;
        }

        if (enableMouseInEditor && Mouse.current != null)
        {
            pointerPosition = Mouse.current.position.ReadValue();
            return true;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER

        if (enableTouch && Input.touchCount > 0)
        {
            pointerPosition = Input.GetTouch(0).position;
            return true;
        }

        if (enableMouseInEditor)
        {
            pointerPosition = Input.mousePosition;
            return true;
        }
#endif

        return false;
    }

    private bool IsPointerPressedThisFrame()
    {
#if MONITORFP_INPUT_SYSTEM
        bool touchPressed = enableTouch && Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
        bool mousePressed = enableMouseInEditor && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        return touchPressed || mousePressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (enableTouch && Input.touchCount > 0)
        {
            return Input.GetTouch(0).phase == TouchPhase.Began;
        }

        return enableMouseInEditor && Input.GetMouseButtonDown(0);
#else
        return false;
#endif
    }

    private bool IsPointerReleasedThisFrame()
    {
#if MONITORFP_INPUT_SYSTEM
        bool touchReleased = enableTouch && Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame;
        bool mouseReleased = enableMouseInEditor && Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
        return touchReleased || mouseReleased;
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (enableTouch && Input.touchCount > 0)
        {
            TouchPhase phase = Input.GetTouch(0).phase;
            return phase == TouchPhase.Ended || phase == TouchPhase.Canceled;
        }

        return enableMouseInEditor && Input.GetMouseButtonUp(0);
#else
        return false;
#endif
    }

    private void UpdateHoverState(bool hasPointer, Vector2 pointerPosition)
    {
        GameObject currentHover = null;
        if (hasPointer)
        {
            currentHover = RaycastObject(pointerPosition);
        }

        if (hoveredObject == currentHover)
        {
            return;
        }

        SessionRecorder recorder = SessionRecorder.GetInstance();
        if (recorder == null)
        {
            hoveredObject = currentHover;
            return;
        }

        if (hoveredObject != null)
        {
            recorder.RecordEvent("interaction_hover_exit", "Hover finalizado", hoveredObject.name);
        }

        if (currentHover != null)
        {
            recorder.RecordEvent("interaction_hover_enter", "Hover iniciado", currentHover.name);
        }

        hoveredObject = currentHover;
    }

    private void RegisterPointerEvent(string eventType, string description, Vector2 pointerPosition)
    {
        SessionRecorder recorder = SessionRecorder.GetInstance();
        if (recorder == null)
        {
            return;
        }

        GameObject target = RaycastObject(pointerPosition);
        string objectName = target != null ? target.name : "none";
        recorder.RecordEvent(eventType, description, objectName);
    }

    private GameObject RaycastObject(Vector2 screenPosition)
    {
        Ray ray = targetCamera.ScreenPointToRay(screenPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, interactionMask, QueryTriggerInteraction.Collide))
        {
            return hit.collider != null ? hit.collider.gameObject : null;
        }

        return null;
    }
}