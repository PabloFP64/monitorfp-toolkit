using System;
using System.Collections.Generic;
using UnityEngine;

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
}
