using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class MonitorFPEditorTools
{
    private const string MenuPathSetup = "MonitorFP/1 Crear Setup Minimo en Escena";
    private const string MenuPathTopDown = "MonitorFP/2 Crear Camara Top Down para Mapa";
    private const float DefaultHeight = 8f;
    private const float DefaultOrthoSize = 6f;

    [MenuItem(MenuPathTopDown)]
    public static void CreateTopDownMapCamera()
    {
        Transform target = FindBestPlayerReference();
        Vector3 targetPosition = target != null ? target.position : Vector3.zero;

        GameObject cameraGO = new GameObject("MapCaptureCamera_TopDown");
        Undo.RegisterCreatedObjectUndo(cameraGO, "Create top-down map camera");

        Camera cam = cameraGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = DefaultOrthoSize;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 200f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.targetDisplay = 1;

        cameraGO.transform.position = targetPosition + Vector3.up * DefaultHeight;
        cameraGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        Component server = FindServerInScene();
        if (server != null)
        {
            Undo.RecordObject(server, "Assign map capture camera");
            SerializedObject so = new SerializedObject(server);

            SerializedProperty mapCameraProp = so.FindProperty("mapCaptureCamera");
            if (mapCameraProp != null)
            {
                mapCameraProp.objectReferenceValue = cam;
            }

            SerializedProperty autoMapProp = so.FindProperty("autoGenerateMapFromCamera");
            if (autoMapProp != null)
            {
                autoMapProp.boolValue = true;
            }

            SerializedProperty minProp = so.FindProperty("mapWorldMinXZ");
            SerializedProperty maxProp = so.FindProperty("mapWorldMaxXZ");
            if (minProp != null && maxProp != null)
            {
                Vector2 minXZ = new Vector2(targetPosition.x - DefaultOrthoSize, targetPosition.z - DefaultOrthoSize);
                Vector2 maxXZ = new Vector2(targetPosition.x + DefaultOrthoSize, targetPosition.z + DefaultOrthoSize);
                minProp.vector2Value = minXZ;
                maxProp.vector2Value = maxXZ;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(server);
        }

        Selection.activeGameObject = cameraGO;
        EditorGUIUtility.PingObject(cameraGO);

        if (server != null)
        {
            Debug.Log("[MONITOR] Camara top-down creada y asignada a MjpegTelemetryServer.");
        }
        else
        {
            Debug.LogWarning("[MONITOR] Camara top-down creada, pero no se encontro MjpegTelemetryServer en la escena para asignarla automaticamente.");
        }
    }

    [MenuItem(MenuPathSetup)]
    public static void CreateMinimalSceneSetup()
    {
        Component server = EnsureRuntimeComponentOnNamedObject("MonitorServer", "MjpegTelemetryServer");
        EnsureRuntimeComponentOnNamedObject("SessionRecorder", "SessionRecorder");
        EnsureRuntimeComponentOnNamedObject("UserTracker", "UserTracker");
        EnsureRuntimeComponentOnNamedObject("InteractionTracker", "ARInteractionEventTracker");
        Component obsSettings = EnsureRuntimeComponentOnNamedObject("ObservationTracking", "ObservationTrackingSettings");
        EnsureRuntimeComponentOnNamedObject("ObservationTracking", "ObservationTracker");

        GameObject sessionRecorderGO = GameObject.Find("SessionRecorder");
        if (sessionRecorderGO != null)
        {
            Undo.RecordObject(sessionRecorderGO, "Ensure SessionRecorder is active");
            sessionRecorderGO.SetActive(true);
            EditorUtility.SetDirty(sessionRecorderGO);
        }

        if (server != null)
        {
            Camera source = Camera.main;
            if (source == null)
            {
                source = UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Exclude);
            }

            if (source != null)
            {
                Undo.RecordObject(server, "Assign source camera");
                SerializedObject so = new SerializedObject(server);
                SerializedProperty sourceCameraProp = so.FindProperty("sourceCamera");
                if (sourceCameraProp != null)
                {
                    sourceCameraProp.objectReferenceValue = source;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(server);
                }

                // Also assign ObservationTrackingSettings.observationCamera if present
                if (obsSettings != null)
                {
                    Undo.RecordObject(obsSettings, "Assign observation camera");
                    SerializedObject soObs = new SerializedObject(obsSettings);
                    SerializedProperty obsCameraProp = soObs.FindProperty("observationCamera");
                    if (obsCameraProp != null)
                    {
                        obsCameraProp.objectReferenceValue = source;
                        soObs.ApplyModifiedProperties();
                        EditorUtility.SetDirty(obsSettings);
                    }
                }
            }
            else
            {
                Debug.LogWarning("[MONITOR] Setup minimo creado, pero no se encontro ninguna Camera para asignar en Source Camera.");
            }
        }

        Debug.Log("[MONITOR] Setup minimo creado: MonitorServer + SessionRecorder + UserTracker + InteractionTracker + ObservationTracking.");
    }

    private static Component EnsureRuntimeComponentOnNamedObject(string gameObjectName, string componentTypeName)
    {
        Type runtimeType = FindTypeByName(componentTypeName);
        if (runtimeType == null || !typeof(Component).IsAssignableFrom(runtimeType))
        {
            Debug.LogError($"[MONITOR] No se encontro el tipo runtime '{componentTypeName}'.");
            return null;
        }

        GameObject go = GameObject.Find(gameObjectName);
        if (go == null)
        {
            go = new GameObject(gameObjectName);
            Undo.RegisterCreatedObjectUndo(go, $"Create {gameObjectName}");
        }

        Component existing = go.GetComponent(runtimeType);
        if (existing != null)
        {
            return existing;
        }

        Component created = Undo.AddComponent(go, runtimeType);
        EditorUtility.SetDirty(go);
        return created;
    }

    private static Component FindServerInScene()
    {
        Type serverType = FindTypeByName("MjpegTelemetryServer");
        if (serverType == null || !typeof(Component).IsAssignableFrom(serverType))
        {
            return null;
        }

        UnityEngine.Object[] servers = UnityEngine.Object.FindObjectsByType(serverType, FindObjectsSortMode.None);
        return servers != null && servers.Length > 0 ? servers[0] as Component : null;
    }

    private static Type FindTypeByName(string typeName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Assembly assembly = assemblies[i];
            Type exactType = assembly.GetType(typeName, false);
            if (exactType != null)
            {
                return exactType;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            if (types == null)
            {
                continue;
            }

            for (int j = 0; j < types.Length; j++)
            {
                Type t = types[j];
                if (t != null && t.Name == typeName)
                {
                    return t;
                }
            }
        }

        return null;
    }

    private static Transform FindBestPlayerReference()
    {
        GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
        if (taggedPlayer != null)
        {
            return taggedPlayer.transform;
        }

        Camera main = Camera.main;
        if (main != null)
        {
            return main.transform;
        }

        GameObject xrOrigin = GameObject.Find("XR Origin");
        if (xrOrigin != null)
        {
            return xrOrigin.transform;
        }

        GameObject cameraOffset = GameObject.Find("Camera Offset");
        if (cameraOffset != null)
        {
            return cameraOffset.transform;
        }

        return null;
    }
}
