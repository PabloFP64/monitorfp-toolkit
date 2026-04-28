using UnityEngine;

[System.Serializable]
public struct TelemetrySnapshot
{
    public float x;
    public float y;
    public float z;
    public float rotX;
    public float rotY;
    public float rotZ;
    public long unixTimeMs;
}

public class UserTracker : MonoBehaviour
{
    [Header("Configuración")]
    public float interval = 1.0f; 
    [SerializeField] private bool logPositionToConsole = false;
    
    private float timer = 0f;
    private Transform userCamera;
    private static readonly object SnapshotLock = new object();
    private static TelemetrySnapshot latestSnapshot;

    void Start()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindFirstObjectByType<Camera>();
        }
        userCamera = mainCamera != null ? mainCamera.transform : null;
        
        if (userCamera == null)
        {
            Debug.LogError("No se encuentra Main Camera");
        }
    }

    void Update()
    {
        UpdateLatestSnapshot();
        timer += Time.deltaTime;

        if (timer >= interval)
        {
            LogUserData();
            timer = 0f;
        }
    }

    void UpdateLatestSnapshot()
    {
        if (userCamera == null)
        {
            return;
        }

        Vector3 pos = userCamera.position;
        Vector3 rot = userCamera.eulerAngles;
        TelemetrySnapshot snapshot = new TelemetrySnapshot
        {
            x = pos.x,
            y = pos.y,
            z = pos.z,
            rotX = rot.x,
            rotY = rot.y,
            rotZ = rot.z,
            unixTimeMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        lock (SnapshotLock)
        {
            latestSnapshot = snapshot;
        }

        SessionRecorder recorder = SessionRecorder.GetInstance();
        if (recorder != null)
        {
            recorder.RecordPositionSample(pos);
        }
    }

    public static bool TryGetLatestSnapshot(out TelemetrySnapshot snapshot)
    {
        lock (SnapshotLock)
        {
            snapshot = latestSnapshot;
        }

        return snapshot.unixTimeMs > 0;
    }

    void LogUserData()
    {
        if (TryGetLatestSnapshot(out TelemetrySnapshot snapshot))
        {
            if (logPositionToConsole)
            {
                Debug.Log($"[MONITOR] Pos: ({snapshot.x:F3}, {snapshot.y:F3}, {snapshot.z:F3}) | Rot: ({snapshot.rotX:F2}, {snapshot.rotY:F2}, {snapshot.rotZ:F2})");
            }
            
        }
    }
}