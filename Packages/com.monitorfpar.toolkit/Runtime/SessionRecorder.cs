using System;
using System.Collections.Generic;
using UnityEngine;
#if MONITORFP_XR_INTERACTION
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#endif

[System.Serializable]
public struct PositionSample
{
    public float x;
    public float y;
    public float z;
    public long timestampMs;
}

[System.Serializable]
public struct SessionEvent
{
    public long timestampMs;
    public string eventType;  // "interaction", "movement_start", "movement_stop", etc.
    public string description;
    public string objectName;
}

[System.Serializable]
public struct SessionStats
{
    public long sessionStartMs;
    public long elapsedSeconds;
    public float distanceTraveled;
    public float averageSpeed;
    public float currentSpeed;
    public float maxDistance;
    public PositionSample[] positionHistory;
    public SessionEvent[] events;
    public int sampleCount;
}

[System.Serializable]
public struct PerformanceMetrics
{
    public int captureFramerate;
    public float averageLatencyMs;
    public long totalFramesCaptured;
    public long uptime;
}

public class SessionRecorder : MonoBehaviour
{
    private static SessionRecorder instance;

    [Header("Historial")]
    [SerializeField] private int maxPositionSamples = 20000;
    [SerializeField] private int maxEvents = 1000;

    [Header("Eventos de interaccion")]
    [SerializeField] private bool logXRSelectEvents = true;
    [SerializeField] private bool logXRHoverEvents;
    [SerializeField] private float xrInteractableScanInterval = 2f;

    private List<PositionSample> positionHistory = new List<PositionSample>();
    private List<SessionEvent> events = new List<SessionEvent>();
    
    private long sessionStartMs;
    private Vector3 lastPosition = Vector3.zero;
    private float distanceTraveled = 0f;
    private float currentSpeed = 0f;
    private float maxDistance = 0f;
    private int frameCount = 0;
    private int lastFrameCount = 0;
    private float frameTimer = 0f;
    private int currentFramerate = 0;
    private long totalCaptureLatency = 0;
    private long framesCaptured = 0;
    private float nextInteractableScanTime;

#if MONITORFP_XR_INTERACTION
    private readonly List<XRBaseInteractable> trackedInteractables = new List<XRBaseInteractable>();
#endif
    private bool xrWarningLogged;

    private readonly object historyLock = new object();

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        sessionStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        RecordEvent("session_start", "Sesión iniciada");
        RefreshXRInteractableSubscriptions();
    }

    private void Update()
    {
        frameCount++;
        frameTimer += Time.deltaTime;

        if (frameTimer >= 1f)
        {
            currentFramerate = frameCount - lastFrameCount;
            lastFrameCount = frameCount;
            frameTimer -= 1f;
        }

        if (Time.unscaledTime >= nextInteractableScanTime)
        {
            RefreshXRInteractableSubscriptions();
            nextInteractableScanTime = Time.unscaledTime + Mathf.Max(0.5f, xrInteractableScanInterval);
        }
    }

    public void RecordPositionSample(Vector3 position)
    {
        // Calcular velocidad y distancia
        float distance = Vector3.Distance(lastPosition, position);
        float deltaTime = Time.deltaTime;
        currentSpeed = deltaTime > 0 ? distance / deltaTime : 0f;
        distanceTraveled += distance;
        maxDistance = Mathf.Max(maxDistance, Vector3.Distance(Vector3.zero, position));

        PositionSample sample = new PositionSample
        {
            x = position.x,
            y = position.y,
            z = position.z,
            timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        lock (historyLock)
        {
            positionHistory.Add(sample);
            int maxSamples = Mathf.Max(1000, maxPositionSamples);
            // Mantener solo los últimos samples configurados para controlar memoria.
            while (positionHistory.Count > maxSamples)
            {
                positionHistory.RemoveAt(0);
            }
        }

        lastPosition = position;
    }

    public void RecordEvent(string eventType, string description)
    {
        RecordEvent(eventType, description, string.Empty);
    }

    public void RecordEvent(string eventType, string description, string objectName)
    {
        SessionEvent evt = new SessionEvent
        {
            timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            eventType = eventType,
            description = description,
            objectName = objectName
        };

        lock (historyLock)
        {
            events.Add(evt);
            int maxEvt = Mathf.Max(100, maxEvents);
            // Mantener últimos eventos configurados.
            while (events.Count > maxEvt)
            {
                events.RemoveAt(0);
            }
        }
    }

    public SessionStats GetSessionStats()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long elapsedMs = now - sessionStartMs;
        float avgSpeed = elapsedMs > 0 ? distanceTraveled / (elapsedMs / 1000f) : 0f;

        lock (historyLock)
        {
            return new SessionStats
            {
                sessionStartMs = sessionStartMs,
                elapsedSeconds = elapsedMs / 1000,
                distanceTraveled = distanceTraveled,
                averageSpeed = avgSpeed,
                currentSpeed = currentSpeed,
                maxDistance = maxDistance,
                positionHistory = positionHistory.ToArray(),
                events = events.ToArray(),
                sampleCount = positionHistory.Count
            };
        }
    }

    public PerformanceMetrics GetPerformanceMetrics()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long uptime = now - sessionStartMs;

        return new PerformanceMetrics
        {
            captureFramerate = currentFramerate,
            averageLatencyMs = framesCaptured > 0 ? totalCaptureLatency / (float)framesCaptured : 0f,
            totalFramesCaptured = framesCaptured,
            uptime = uptime / 1000
        };
    }

    public static void OnFrameCaptured(long latencyMs)
    {
        if (instance != null)
        {
            instance.framesCaptured++;
            instance.totalCaptureLatency += latencyMs;
        }
    }

    public static SessionRecorder GetInstance()
    {
        return instance;
    }

    private void OnDestroy()
    {
#if MONITORFP_XR_INTERACTION
        UnsubscribeXRInteractables();
#endif
    }

    private void RefreshXRInteractableSubscriptions()
    {
#if MONITORFP_XR_INTERACTION
        if (!logXRSelectEvents && !logXRHoverEvents)
        {
            UnsubscribeXRInteractables();
            return;
        }

        XRBaseInteractable[] interactables = UnityEngine.Object.FindObjectsByType<XRBaseInteractable>(FindObjectsSortMode.None);
        if (interactables == null || interactables.Length == 0)
        {
            UnsubscribeXRInteractables();
            return;
        }

        HashSet<XRBaseInteractable> current = new HashSet<XRBaseInteractable>(interactables);

        for (int i = trackedInteractables.Count - 1; i >= 0; i--)
        {
            XRBaseInteractable tracked = trackedInteractables[i];
            if (tracked == null || !current.Contains(tracked))
            {
                if (tracked != null)
                {
                    UnsubscribeInteractable(tracked);
                }

                trackedInteractables.RemoveAt(i);
            }
        }

        foreach (XRBaseInteractable interactable in interactables)
        {
            if (interactable == null || trackedInteractables.Contains(interactable))
            {
                continue;
            }

            SubscribeInteractable(interactable);
            trackedInteractables.Add(interactable);
        }
#else
        if ((logXRSelectEvents || logXRHoverEvents) && !xrWarningLogged)
        {
            Debug.Log("[MONITOR][AR] XR interaction events desactivados: instala 'com.unity.xr.interaction.toolkit' para habilitar select/hover.");
            xrWarningLogged = true;
        }
#endif
    }

#if MONITORFP_XR_INTERACTION
    private void SubscribeInteractable(XRBaseInteractable interactable)
    {
        if (logXRSelectEvents)
        {
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
        }

        if (logXRHoverEvents)
        {
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
        }
    }

    private void UnsubscribeInteractable(XRBaseInteractable interactable)
    {
        interactable.selectEntered.RemoveListener(OnSelectEntered);
        interactable.selectExited.RemoveListener(OnSelectExited);
        interactable.hoverEntered.RemoveListener(OnHoverEntered);
        interactable.hoverExited.RemoveListener(OnHoverExited);
    }

    private void UnsubscribeXRInteractables()
    {
        for (int i = trackedInteractables.Count - 1; i >= 0; i--)
        {
            XRBaseInteractable interactable = trackedInteractables[i];
            if (interactable != null)
            {
                UnsubscribeInteractable(interactable);
            }
        }

        trackedInteractables.Clear();
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        string objectName = GetInteractableName(args != null ? args.interactableObject : null);
        RecordEvent("interaction_select_enter", "Objeto seleccionado", objectName);
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        string objectName = GetInteractableName(args != null ? args.interactableObject : null);
        RecordEvent("interaction_select_exit", "Objeto liberado", objectName);
    }

    private void OnHoverEntered(HoverEnterEventArgs args)
    {
        string objectName = GetInteractableName(args != null ? args.interactableObject : null);
        RecordEvent("interaction_hover_enter", "Hover iniciado", objectName);
    }

    private void OnHoverExited(HoverExitEventArgs args)
    {
        string objectName = GetInteractableName(args != null ? args.interactableObject : null);
        RecordEvent("interaction_hover_exit", "Hover finalizado", objectName);
    }

    private static string GetInteractableName(IXRInteractable interactable)
    {
        if (interactable == null || interactable.transform == null)
        {
            return "unknown";
        }

        return interactable.transform.name;
    }
#endif
}
