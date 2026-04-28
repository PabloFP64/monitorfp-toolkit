using System;
using System.Collections.Generic;
using UnityEngine;

public class ObservationTracker : MonoBehaviour
{
    private sealed class ObservationState
    {
        public InterestingGameObject marker;
        public bool currentlyObserved;
        public bool currentlyCenter;
        public float observationSegmentStart;
        public float firstSeenAtSeconds = -1f;
        public float totalObservedSeconds;
        public float centerObservedSeconds;
        public float peripheralObservedSeconds;
        public int centerObservationCount;
        public readonly List<float> segments = new List<float>();
    }

    private static ObservationTracker instance;
    private static readonly List<InterestingGameObject> pendingRegistrations = new List<InterestingGameObject>();

    [SerializeField] private ObservationTrackingSettings settings;
    [SerializeField] private bool logObservationEventsToConsole = false;

    private readonly Dictionary<InterestingGameObject, ObservationState> tracked = new Dictionary<InterestingGameObject, ObservationState>();
    private readonly object snapshotLock = new object();
    private readonly List<InterestingObjectObservationStats> snapshotBuffer = new List<InterestingObjectObservationStats>();

    private Camera targetCamera;
    private bool trackingActive;
    private long trackingStartMs;
    private float nextRediscoveryTime;

    public static ObservationTracker GetInstance()
    {
        return instance;
    }

    public static void RegisterInterestingObject(InterestingGameObject marker)
    {
        if (marker == null)
        {
            return;
        }

        if (instance == null)
        {
            if (!pendingRegistrations.Contains(marker))
            {
                pendingRegistrations.Add(marker);
            }
            return;
        }

        instance.RegisterInternal(marker);
    }

    public static void UnregisterInterestingObject(InterestingGameObject marker)
    {
        if (marker == null)
        {
            return;
        }

        if (instance == null)
        {
            if (pendingRegistrations.Contains(marker))
            {
                pendingRegistrations.Remove(marker);
            }
            return;
        }

        instance.UnregisterInternal(marker);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        settings = settings != null ? settings : GetComponent<ObservationTrackingSettings>();
        ResolveCamera();

        if (pendingRegistrations.Count > 0)
        {
            foreach (InterestingGameObject pending in pendingRegistrations.ToArray())
            {
                if (pending != null)
                {
                    RegisterInternal(pending);
                }
            }

            pendingRegistrations.Clear();
        }
    }

    private void Start()
    {
        RediscoverInterestingObjects();

        if (settings != null && settings.StartMode == ObservationTrackingStartMode.OnSceneStart)
        {
            StartObservationTracking();
        }
        else
        {
            trackingActive = false;
            trackingStartMs = 0;
            RebuildSnapshot();
        }
    }

    private void Update()
    {
        if (settings == null)
        {
            settings = GetComponent<ObservationTrackingSettings>();
        }

        ResolveCamera();

        float now = Time.unscaledTime;
        float rediscoveryInterval = settings != null ? settings.RediscoveryInterval : 1f;
        if (now >= nextRediscoveryTime)
        {
            RediscoverInterestingObjects();
            nextRediscoveryTime = now + rediscoveryInterval;
        }

        if (!trackingActive || targetCamera == null)
        {
            return;
        }

        float elapsedSinceStart = Mathf.Max(0f, (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - trackingStartMs) / 1000f);
        float dt = Mathf.Max(0f, Time.unscaledDeltaTime);

        foreach (KeyValuePair<InterestingGameObject, ObservationState> kv in tracked)
        {
            InterestingGameObject marker = kv.Key;
            ObservationState state = kv.Value;
            if (marker == null || marker.ObservationTarget == null)
            {
                continue;
            }

            bool visible;
            bool center;
            EvaluateVisibility(marker, out visible, out center);

            if (visible)
            {
                if (!state.currentlyObserved)
                {
                    state.currentlyObserved = true;
                    state.observationSegmentStart = elapsedSinceStart;
                    if (state.firstSeenAtSeconds < 0f)
                    {
                        state.firstSeenAtSeconds = elapsedSinceStart;
                    }

                    if (logObservationEventsToConsole)
                    {
                        Debug.Log($"[MONITOR][OBS] START {marker.DisplayName} | t={elapsedSinceStart:F2}s | center={center}");
                    }
                }

                state.totalObservedSeconds += dt;

                if (center)
                {
                    state.centerObservedSeconds += dt;
                    if (!state.currentlyCenter)
                    {
                        state.centerObservationCount++;
                    }
                }
                else
                {
                    state.peripheralObservedSeconds += dt;
                }

                state.currentlyCenter = center;
            }
            else if (state.currentlyObserved)
            {
                float segment = Mathf.Max(0f, elapsedSinceStart - state.observationSegmentStart);
                state.segments.Add(segment);
                if (logObservationEventsToConsole)
                {
                    Debug.Log($"[MONITOR][OBS] STOP  {marker.DisplayName} | segment={segment:F2}s");
                }
                state.currentlyObserved = false;
                state.currentlyCenter = false;
            }
        }

        RebuildSnapshot();
    }

    private void ResolveCamera()
    {
        if (settings != null && settings.ObservationCamera != null)
        {
            targetCamera = settings.ObservationCamera;
            return;
        }

        if (targetCamera == null)
        {
            Camera found = Camera.main;
            if (found == null)
            {
                found = FindFirstObjectByType<Camera>();
            }

            if (found != null)
            {
                targetCamera = found;
                    if (settings != null && settings.ObservationCamera == null)
                    {
                        settings.SetObservationCamera(found);
                    }
            }
        }
    }

    private void EvaluateVisibility(InterestingGameObject marker, out bool visible, out bool center)
    {
        visible = false;
        center = false;

        Vector3 worldPoint = marker.ObservationTarget.position;
        Vector3 viewport = targetCamera.WorldToViewportPoint(worldPoint);

        if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
        {
            return;
        }

        if (settings != null && settings.RequireLineOfSight)
        {
            Vector3 origin = targetCamera.transform.position;
            Vector3 dir = worldPoint - origin;
            float distance = Mathf.Min(settings.MaxObservationDistance, dir.magnitude);
            if (distance <= 0.001f)
            {
                return;
            }

            if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Collide))
            {
                Transform hitTransform = hit.transform;
                Transform markerTransform = marker.transform;
                if (hitTransform != markerTransform && !hitTransform.IsChildOf(markerTransform) && !markerTransform.IsChildOf(hitTransform))
                {
                    return;
                }
            }
        }

        visible = true;
        float radius = settings != null ? settings.CenterCircleRadius : 0.12f;
        float dx = viewport.x - 0.5f;
        float dy = viewport.y - 0.5f;
        center = (dx * dx + dy * dy) <= (radius * radius);
    }

    private void RegisterInternal(InterestingGameObject marker)
    {
        if (marker == null || tracked.ContainsKey(marker))
        {
            return;
        }

        tracked[marker] = new ObservationState { marker = marker };
        if (logObservationEventsToConsole)
        {
            Debug.Log($"[MONITOR][OBS] Registrado objeto interesante: {marker.DisplayName}");
        }
        RebuildSnapshot();
    }

    private void UnregisterInternal(InterestingGameObject marker)
    {
        if (marker == null)
        {
            return;
        }

        tracked.Remove(marker);
        RebuildSnapshot();
    }

    private void RediscoverInterestingObjects()
    {
        InterestingGameObject[] found = FindObjectsByType<InterestingGameObject>(FindObjectsSortMode.None);
        HashSet<InterestingGameObject> alive = new HashSet<InterestingGameObject>();

        if (found != null)
        {
            for (int i = 0; i < found.Length; i++)
            {
                InterestingGameObject marker = found[i];
                if (marker == null)
                {
                    continue;
                }

                alive.Add(marker);
                if (!tracked.ContainsKey(marker))
                {
                    tracked[marker] = new ObservationState { marker = marker };
                }
            }
        }

        if (tracked.Count > alive.Count)
        {
            List<InterestingGameObject> toRemove = new List<InterestingGameObject>();
            foreach (InterestingGameObject marker in tracked.Keys)
            {
                if (marker == null || !alive.Contains(marker))
                {
                    toRemove.Add(marker);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                tracked.Remove(toRemove[i]);
            }
        }
    }

    private void RebuildSnapshot()
    {
        lock (snapshotLock)
        {
            snapshotBuffer.Clear();
            float elapsed = trackingStartMs > 0 ? Mathf.Max(0f, (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - trackingStartMs) / 1000f) : 0f;

            if (logObservationEventsToConsole)
            {
                Debug.Log($"[MONITOR][OBS] RebuildSnapshot called. Tracked count={tracked.Count}");
            }

            foreach (KeyValuePair<InterestingGameObject, ObservationState> kv in tracked)
            {
                InterestingGameObject marker = kv.Key;
                ObservationState state = kv.Value;
                if (marker == null || state == null)
                {
                    continue;
                }

                if (logObservationEventsToConsole)
                {
                    Debug.Log($"[MONITOR][OBS] Snapshot include: {marker.DisplayName} firstSeen={state.firstSeenAtSeconds} total={state.totalObservedSeconds}");
                }

                float[] segments = BuildSegments(state, elapsed);
                float centerPercent = state.totalObservedSeconds > 0.0001f
                    ? (state.centerObservedSeconds / state.totalObservedSeconds) * 100f
                    : 0f;

                snapshotBuffer.Add(new InterestingObjectObservationStats
                {
                    name = marker.DisplayName,
                    firstSeenAtSeconds = state.firstSeenAtSeconds,
                    totalObservedSeconds = state.totalObservedSeconds,
                    observationSegmentsSeconds = segments,
                    centerObservationCount = state.centerObservationCount,
                    centerObservedSeconds = state.centerObservedSeconds,
                    peripheralObservedSeconds = state.peripheralObservedSeconds,
                    centerTimePercent = centerPercent,
                    currentlyObserved = state.currentlyObserved
                });
            }

            snapshotBuffer.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }
    }

    private static float[] BuildSegments(ObservationState state, float elapsed)
    {
        int baseCount = state.segments.Count;
        bool includeActive = state.currentlyObserved;
        float[] output = new float[baseCount + (includeActive ? 1 : 0)];

        for (int i = 0; i < baseCount; i++)
        {
            output[i] = state.segments[i];
        }

        if (includeActive)
        {
            output[baseCount] = Mathf.Max(0f, elapsed - state.observationSegmentStart);
        }

        return output;
    }

    public void StartObservationTracking()
    {
        trackingActive = true;
        trackingStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (logObservationEventsToConsole)
        {
            Debug.Log($"[MONITOR][OBS] Tracking iniciado. Objetos registrados: {tracked.Count}");
        }

        foreach (ObservationState state in tracked.Values)
        {
            state.currentlyObserved = false;
            state.currentlyCenter = false;
            state.observationSegmentStart = 0f;
            state.firstSeenAtSeconds = -1f;
            state.totalObservedSeconds = 0f;
            state.centerObservedSeconds = 0f;
            state.peripheralObservedSeconds = 0f;
            state.centerObservationCount = 0;
            state.segments.Clear();
        }

        RebuildSnapshot();
    }

    public void StopObservationTracking()
    {
        if (!trackingActive)
        {
            return;
        }

        float elapsed = Mathf.Max(0f, (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - trackingStartMs) / 1000f);
        foreach (ObservationState state in tracked.Values)
        {
            if (state.currentlyObserved)
            {
                float segment = Mathf.Max(0f, elapsed - state.observationSegmentStart);
                state.segments.Add(segment);
                state.currentlyObserved = false;
            }

            state.currentlyCenter = false;
        }

        trackingActive = false;
        if (logObservationEventsToConsole)
        {
            Debug.Log("[MONITOR][OBS] Tracking detenido.");
        }
        RebuildSnapshot();
    }

    public bool IsTrackingActive()
    {
        return trackingActive;
    }

    public void GetObservationSnapshot(out InterestingObjectObservationStats[] stats, out bool isTrackingActive, out long startMs)
    {
        lock (snapshotLock)
        {
            stats = snapshotBuffer.ToArray();
        }

        isTrackingActive = trackingActive;
        startMs = trackingStartMs;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}
