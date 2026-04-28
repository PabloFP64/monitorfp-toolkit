using UnityEngine;
using System;
using System.Collections.Generic;

public class ARInteractionEventTracker : MonoBehaviour
{
    private struct TrackedTransformState
    {
        public Vector3 lastPosition;
        public Quaternion lastRotation;
        public float lastMovementTime;
        public bool isMoving;
    }

    [Header("Deteccion")]
    [SerializeField] private bool enableTouch = true;
    [SerializeField] private bool enableMouseInEditor = true;
    [SerializeField] private float maxRayDistance = 100f;
    [SerializeField] private LayerMask interactionMask = ~0;

    [Header("Eventos de movimiento")]
    [SerializeField] private bool detectObjectMovement = true;
    [SerializeField] private bool logInteractionsToConsole = false;
    [SerializeField] private float movementRefreshInterval = 1.5f;
    [SerializeField] private float movementCheckInterval = 0.15f;
    [SerializeField] private float movementStopDelay = 0.35f;
    [SerializeField] private float movementPositionThreshold = 0.005f;
    [SerializeField] private float movementRotationThresholdDeg = 1f;
    [SerializeField] private int maxTrackedObjects = 256;

    private Camera targetCamera;
    private GameObject hoveredObject;
    private bool inputBackendWarningLogged;
    private float nextRefreshTime;
    private float nextMovementCheckTime;
    private readonly List<Transform> trackedTransforms = new List<Transform>();
    private readonly Dictionary<Transform, TrackedTransformState> trackedStates = new Dictionary<Transform, TrackedTransformState>();

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

        if (hasPointer)
        {
            if (IsPointerPressedThisFrame())
            {
                RegisterPointerEvent("interaction_press", "Interaccion iniciada", pointerPosition);
            }

            if (IsPointerReleasedThisFrame())
            {
                RegisterPointerEvent("interaction_release", "Interaccion finalizada", pointerPosition);
            }
        }

        if (!detectObjectMovement)
        {
            return;
        }

        if (Time.unscaledTime >= nextRefreshTime)
        {
            RefreshTrackedObjects();
            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.25f, movementRefreshInterval);
        }

        if (Time.unscaledTime >= nextMovementCheckTime)
        {
            EvaluateTrackedObjectMovement();
            nextMovementCheckTime = Time.unscaledTime + Mathf.Max(0.05f, movementCheckInterval);
        }
    }

    private void RefreshTrackedObjects()
    {
        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);

        trackedTransforms.Clear();
        if (colliders == null || colliders.Length == 0)
        {
            trackedStates.Clear();
            return;
        }

        int mask = interactionMask.value;
        HashSet<Transform> unique = new HashSet<Transform>();

        for (int i = 0; i < colliders.Length && trackedTransforms.Count < Mathf.Max(1, maxTrackedObjects); i++)
        {
            Collider col = colliders[i];
            if (col == null)
            {
                continue;
            }

            if (((1 << col.gameObject.layer) & mask) == 0)
            {
                continue;
            }

            Transform t = col.attachedRigidbody != null ? col.attachedRigidbody.transform : col.transform;
            if (t == null || !unique.Add(t))
            {
                continue;
            }

            trackedTransforms.Add(t);
            if (!trackedStates.ContainsKey(t))
            {
                trackedStates[t] = new TrackedTransformState
                {
                    lastPosition = t.position,
                    lastRotation = t.rotation,
                    lastMovementTime = Time.unscaledTime,
                    isMoving = false
                };
            }
        }

        if (trackedStates.Count > trackedTransforms.Count)
        {
            HashSet<Transform> current = new HashSet<Transform>(trackedTransforms);
            List<Transform> toRemove = new List<Transform>();
            foreach (KeyValuePair<Transform, TrackedTransformState> kv in trackedStates)
            {
                if (kv.Key == null || !current.Contains(kv.Key))
                {
                    toRemove.Add(kv.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                trackedStates.Remove(toRemove[i]);
            }
        }
    }

    private void EvaluateTrackedObjectMovement()
    {
        SessionRecorder recorder = SessionRecorder.GetInstance();
        if (recorder == null)
        {
            return;
        }

        float now = Time.unscaledTime;

        for (int i = 0; i < trackedTransforms.Count; i++)
        {
            Transform t = trackedTransforms[i];
            if (t == null)
            {
                continue;
            }

            TrackedTransformState state;
            if (!trackedStates.TryGetValue(t, out state))
            {
                state = new TrackedTransformState
                {
                    lastPosition = t.position,
                    lastRotation = t.rotation,
                    lastMovementTime = now,
                    isMoving = false
                };
            }

            bool movedPosition = Vector3.Distance(state.lastPosition, t.position) >= movementPositionThreshold;
            bool movedRotation = Quaternion.Angle(state.lastRotation, t.rotation) >= movementRotationThresholdDeg;
            bool moved = movedPosition || movedRotation;

            if (moved)
            {
                if (!state.isMoving)
                {
                    RecordInteractionEvent("interaction_move_start", "Movimiento de objeto iniciado", t.name);
                    state.isMoving = true;
                }

                state.lastMovementTime = now;
                state.lastPosition = t.position;
                state.lastRotation = t.rotation;
            }
            else if (state.isMoving && (now - state.lastMovementTime) >= movementStopDelay)
            {
                RecordInteractionEvent("interaction_move_stop", "Movimiento de objeto finalizado", t.name);
                state.isMoving = false;
            }

            trackedStates[t] = state;
        }
    }

    private bool TryGetPointerPosition(out Vector2 pointerPosition)
    {
        pointerPosition = Vector2.zero;
        try
        {
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
        catch (InvalidOperationException)
        {
            LogInputBackendWarningOnce();
            return false;
        }
    }

    private bool IsPointerPressedThisFrame()
    {
        try
        {
            if (enableTouch && Input.touchCount > 0)
            {
                return Input.GetTouch(0).phase == TouchPhase.Began;
            }

            return enableMouseInEditor && Input.GetMouseButtonDown(0);
        }
        catch (InvalidOperationException)
        {
            LogInputBackendWarningOnce();
            return false;
        }
    }

    private bool IsPointerReleasedThisFrame()
    {
        try
        {
            if (enableTouch && Input.touchCount > 0)
            {
                TouchPhase phase = Input.GetTouch(0).phase;
                return phase == TouchPhase.Ended || phase == TouchPhase.Canceled;
            }

            return enableMouseInEditor && Input.GetMouseButtonUp(0);
        }
        catch (InvalidOperationException)
        {
            LogInputBackendWarningOnce();
            return false;
        }
    }

    private void LogInputBackendWarningOnce()
    {
        if (inputBackendWarningLogged)
        {
            return;
        }

        Debug.LogWarning("[MONITOR][AR] ARInteractionEventTracker no puede leer UnityEngine.Input con la configuracion actual de Input Handling. Activa 'Both' o usa Input Manager para eventos de puntero.");
        inputBackendWarningLogged = true;
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
            RecordInteractionEvent("interaction_hover_exit", "Hover finalizado", hoveredObject.name);
        }

        if (currentHover != null)
        {
            RecordInteractionEvent("interaction_hover_enter", "Hover iniciado", currentHover.name);
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
        RecordInteractionEvent(eventType, description, objectName);
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

    private void RecordInteractionEvent(string eventType, string description, string objectName)
    {
        SessionRecorder recorder = SessionRecorder.GetInstance();
        if (recorder == null)
        {
            return;
        }

        recorder.RecordEvent(eventType, description, objectName);

        if (logInteractionsToConsole)
        {
            string obj = string.IsNullOrEmpty(objectName) ? "none" : objectName;
            Debug.Log($"[MONITOR][AR][EVENT] {eventType} | {description} | object={obj}");
        }
    }
}