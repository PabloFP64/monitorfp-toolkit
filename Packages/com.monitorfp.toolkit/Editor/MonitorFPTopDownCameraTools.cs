using UnityEditor;
using UnityEngine;

public static class MonitorFPTopDownCameraTools
{
    private const string MenuPath = "MonitorFP/Crear Camara Top Down para Mapa";
    private const float DefaultHeight = 8f;
    private const float DefaultOrthoSize = 6f;

    [MenuItem(MenuPath)]
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

        MjpegTelemetryServer server = FindServerInScene();
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

    private static MjpegTelemetryServer FindServerInScene()
    {
        MjpegTelemetryServer[] servers = Object.FindObjectsByType<MjpegTelemetryServer>(FindObjectsSortMode.None);
        return servers != null && servers.Length > 0 ? servers[0] : null;
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
