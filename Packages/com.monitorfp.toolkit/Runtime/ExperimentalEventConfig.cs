using System;
using System.Collections.Generic;
using UnityEngine;

using System;
using System.Collections.Generic;

public class ExperimentalEventConfig : MonoBehaviour
{
    private static readonly object cacheLock = new object();
    private static string[] cachedLabels = Array.Empty<string>();

    [Tooltip("List of event labels that will appear as buttons in the web dashboard")]
    public List<string> eventLabels = new List<string>() { "Start Trial", "Important Event", "End Trial" };

    public static string[] GetCachedLabelsSnapshot()
    {
        lock (cacheLock)
        {
            return (string[])cachedLabels.Clone();
        }
    }

    private void Awake()
    {
        RefreshCache();
    }

    private void OnEnable()
    {
        RefreshCache();
    }

    private void OnValidate()
    {
        RefreshCache();
    }

    private void RefreshCache()
    {
        string[] labels = eventLabels != null ? eventLabels.ToArray() : Array.Empty<string>();
        lock (cacheLock)
        {
            cachedLabels = labels;
        }
    }

    public static string[] GetCachedLabelsSnapshot()
    {
        lock (cacheLock)
        {
            return (string[])cachedLabels.Clone();
        }
    }

    private void Awake()
    {
        RefreshCache();
    }

    private void OnEnable()
    {
        RefreshCache();
    }

    private void OnValidate()
    {
        RefreshCache();
    }

    private void RefreshCache()
    {
        string[] labels = eventLabels != null ? eventLabels.ToArray() : Array.Empty<string>();
        lock (cacheLock)
        {
            cachedLabels = labels;
        }
    }
}
