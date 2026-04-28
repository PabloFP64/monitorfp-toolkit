using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class RecordingManager : MonoBehaviour
{
    private static RecordingManager instance;

    [Header("Android Integration")]
    [Tooltip("Enable calling native Android recording (requires plugin)")]
    public bool enableAndroidRecording = false;
    public bool androidLowQuality = true;

    [Header("Browser Recording")]
    public bool enableBrowserRecording = true;

    private long recordingStartMs;
    private bool isRecording;
    private readonly List<(long ms, string label)> markers = new List<(long ms, string label)>();

    public static RecordingManager Instance => instance;

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

    public bool IsRecording() => isRecording;

    public void StartRecording()
    {
        if (isRecording) return;
        isRecording = true;
        recordingStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        markers.Clear();

        if (enableAndroidRecording && Application.platform == RuntimePlatform.Android)
        {
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    AndroidJavaClass plugin = new AndroidJavaClass("com.monitorfp.recorder.RecorderBridge");
                    if (plugin != null)
                    {
                        plugin.CallStatic("startRecording", activity, androidLowQuality);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MONITOR] Android recording start failed: " + ex.Message);
            }
        }
    }

    public void StopRecording()
    {
        if (!isRecording) return;
        isRecording = false;

        if (enableAndroidRecording && Application.platform == RuntimePlatform.Android)
        {
            try
            {
                AndroidJavaClass plugin = new AndroidJavaClass("com.monitorfp.recorder.RecorderBridge");
                if (plugin != null)
                {
                    plugin.CallStatic("stopRecording");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MONITOR] Android recording stop failed: " + ex.Message);
            }
        }
    }

    public void AddMarker(string label)
    {
        if (!isRecording) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        markers.Add((now - recordingStartMs, label));
    }

    public (long startMs, List<(long ms, string label)> markers) GetRecordingData()
    {
        return (recordingStartMs, new List<(long ms, string label)>(markers));
    }
}
