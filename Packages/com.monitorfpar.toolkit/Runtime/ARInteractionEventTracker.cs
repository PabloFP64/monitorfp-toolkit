using UnityEngine;

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

        if (Input.GetMouseButtonDown(0))
        {
            RegisterPointerEvent("interaction_press", "Interaccion iniciada", pointerPosition);
        }

        if (Input.GetMouseButtonUp(0))
        {
            RegisterPointerEvent("interaction_release", "Interaccion finalizada", pointerPosition);
        }
    }

    private bool TryGetPointerPosition(out Vector2 pointerPosition)
    {
        pointerPosition = Vector2.zero;

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

        return false;
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