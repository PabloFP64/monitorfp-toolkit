using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class MjpegTelemetryServer : MonoBehaviour
{
    [Header("Servidor")]
    [SerializeField] private int port = 8080;

    [Header("Captura")]
    [SerializeField] private Camera sourceCamera;
    [SerializeField] private int frameWidth = 960;
    [SerializeField] private int frameHeight = 540;
    [SerializeField] private int fps = 30;
    [Range(1, 100)]
    [SerializeField] private int jpgQuality = 65;

    [Header("Mapa 2D (planta XZ)")]
    [SerializeField] private Texture2D mapTexture;
    [SerializeField] private Vector2 mapWorldMinXZ = new Vector2(-5f, -5f);
    [SerializeField] private Vector2 mapWorldMaxXZ = new Vector2(5f, 5f);
    [SerializeField] private bool mapFlipX;
    [SerializeField] private bool mapFlipY;
    [Range(0, 270)]
    [SerializeField] private int mapRotationDegrees = 90;

    [Header("Mapa dinamico (opcional)")]
    [SerializeField] private Camera mapCaptureCamera;
    [SerializeField] private bool autoGenerateMapFromCamera = true;
    [SerializeField] private bool autoGenerateGridIfNoMap = true;
    [SerializeField] private int generatedMapWidth = 1024;
    [SerializeField] private int generatedMapHeight = 1024;

    private TcpListener listener;
    private Thread listenerThread;
    private bool serverRunning;

    private RenderTexture captureTexture;
    private Texture2D readbackTexture;

    private readonly object frameLock = new object();
    private readonly object telemetryFallbackLock = new object();
    private byte[] latestFrameJpg = Array.Empty<byte>();
    private TelemetrySnapshot telemetryFallbackSnapshot;
    private byte[] mapPngBytes = Array.Empty<byte>();
    private string currentMapSource = "none";
    private string currentMapMessage = "No map loaded";
    private bool hasAssignedMapTexture;

    [Serializable]
    private struct MapConfigPayload
    {
        public bool hasMap;
        public float worldMinX;
        public float worldMinZ;
        public float worldMaxX;
        public float worldMaxZ;
        public bool flipX;
        public bool flipY;
        public int mapRotationDegrees;
        public bool hasAssignedMapTexture;
        public string mapSource;
        public string mapMessage;
    }

    private int GetNormalizedMapRotationDegrees()
    {
        int normalized = ((mapRotationDegrees % 360) + 360) % 360;
        int snapped = Mathf.RoundToInt(normalized / 90f) * 90;
        return snapped == 360 ? 0 : snapped;
    }

    private void Start()
    {
        if (sourceCamera == null)
        {
            Camera main = Camera.main;
            sourceCamera = main;
        }

        if (sourceCamera == null)
        {
            Debug.LogError("[MONITOR] MjpegTelemetryServer: no hay camara asignada.");
            enabled = false;
            return;
        }

        captureTexture = new RenderTexture(frameWidth, frameHeight, 24, RenderTextureFormat.ARGB32);
        readbackTexture = new Texture2D(frameWidth, frameHeight, TextureFormat.RGB24, false);
        hasAssignedMapTexture = mapTexture != null;
        PrepareMapImage();

        StartServer();
        StartCoroutine(CaptureLoop());
    }

    private void PrepareMapImage()
    {
        currentMapSource = "none";
        currentMapMessage = "No map loaded";

        if (mapTexture != null)
        {
            TryEncodeMapTexture();
            if (mapPngBytes != null && mapPngBytes.Length > 0)
            {
                return;
            }
        }

        if (autoGenerateMapFromCamera && mapCaptureCamera != null)
        {
            if (TryGenerateMapFromCamera())
            {
                return;
            }
        }

        if (autoGenerateGridIfNoMap)
        {
            if (TryGenerateProceduralGridMap())
            {
                return;
            }
        }

        mapPngBytes = Array.Empty<byte>();
        currentMapSource = "none";
        currentMapMessage = "No texture/camera available";
        Debug.Log("[MONITOR] Mapa 2D no disponible. El panel de mapa se mostrara sin fondo.");
    }

    private void TryEncodeMapTexture()
    {
        try
        {
            mapPngBytes = mapTexture.EncodeToPNG();
            if (mapPngBytes == null || mapPngBytes.Length == 0)
            {
                mapPngBytes = TryEncodeTextureViaBlit(mapTexture);
            }

            if (mapPngBytes == null || mapPngBytes.Length == 0)
            {
                mapPngBytes = Array.Empty<byte>();
                currentMapSource = "none";
                currentMapMessage = "Assigned mapTexture could not be encoded";
                Debug.LogWarning("[MONITOR] No se pudo codificar mapTexture a PNG. Activa Read/Write o usa formato compatible.");
                return;
            }

            currentMapSource = "texture";
            currentMapMessage = "Loaded from assigned mapTexture";
            Debug.Log($"[MONITOR] Mapa 2D cargado desde Texture ({mapTexture.width}x{mapTexture.height}).");
        }
        catch (Exception ex)
        {
            mapPngBytes = TryEncodeTextureViaBlit(mapTexture);
            if (mapPngBytes == null || mapPngBytes.Length == 0)
            {
                mapPngBytes = Array.Empty<byte>();
                currentMapSource = "none";
                currentMapMessage = "Assigned mapTexture failed and GPU fallback failed";
                Debug.LogWarning($"[MONITOR] Error codificando mapTexture: {ex.Message}");
                return;
            }

            currentMapSource = "texture";
            currentMapMessage = "Loaded from assigned mapTexture via GPU fallback";
            Debug.Log($"[MONITOR] Mapa 2D cargado desde Texture via blit GPU ({mapTexture.width}x{mapTexture.height}).");
        }
    }

    private static byte[] TryEncodeTextureViaBlit(Texture source)
    {
        if (source == null)
        {
            return Array.Empty<byte>();
        }

        RenderTexture rt = null;
        Texture2D readback = null;

        try
        {
            int width = Mathf.Max(2, source.width);
            int height = Mathf.Max(2, source.height);

            rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            readback = new Texture2D(width, height, TextureFormat.RGB24, false);
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readback.Apply(false);

            RenderTexture.active = previous;
            return readback.EncodeToPNG() ?? Array.Empty<byte>();
        }
        catch
        {
            return Array.Empty<byte>();
        }
        finally
        {
            if (readback != null)
            {
                Destroy(readback);
            }

            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }
        }
    }

    private bool TryGenerateMapFromCamera()
    {
        RenderTexture rt = null;
        Texture2D generated = null;

        try
        {
            int width = Mathf.Clamp(generatedMapWidth, 256, 4096);
            int height = Mathf.Clamp(generatedMapHeight, 256, 4096);

            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            generated = new Texture2D(width, height, TextureFormat.RGB24, false);

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = mapCaptureCamera.targetTexture;

            mapCaptureCamera.targetTexture = rt;
            mapCaptureCamera.Render();
            RenderTexture.active = rt;

            generated.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            generated.Apply(false);

            mapCaptureCamera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;

            mapPngBytes = generated.EncodeToPNG();
            if (mapPngBytes == null || mapPngBytes.Length == 0)
            {
                mapPngBytes = Array.Empty<byte>();
                currentMapSource = "none";
                currentMapMessage = "Camera map generation failed";
                Debug.LogWarning("[MONITOR] No se pudo generar mapa desde mapCaptureCamera.");
                return false;
            }

            currentMapSource = "camera";
            currentMapMessage = "Generated from mapCaptureCamera";
            Debug.Log($"[MONITOR] Mapa 2D generado dinamicamente desde camara ({width}x{height}).");
            return true;
        }
        catch (Exception ex)
        {
            mapPngBytes = Array.Empty<byte>();
            currentMapSource = "none";
            currentMapMessage = "Camera map generation threw exception";
            Debug.LogWarning($"[MONITOR] Error generando mapa desde camara: {ex.Message}");
            return false;
        }
        finally
        {
            if (generated != null)
            {
                Destroy(generated);
            }

            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }
        }
    }

    private bool TryGenerateProceduralGridMap()
    {
        Texture2D generated = null;

        try
        {
            int width = Mathf.Clamp(generatedMapWidth, 256, 4096);
            int height = Mathf.Clamp(generatedMapHeight, 256, 4096);
            generated = new Texture2D(width, height, TextureFormat.RGB24, false);

            Color bg = new Color(0.93f, 0.95f, 0.98f, 1f);
            Color grid = new Color(0.78f, 0.82f, 0.88f, 1f);
            Color axis = new Color(0.20f, 0.34f, 0.58f, 1f);

            int step = Mathf.Max(16, width / 20);
            int centerX = width / 2;
            int centerY = height / 2;

            for (int y = 0; y < height; y++)
            {
                bool isGridY = y % step == 0;
                bool isAxisY = Mathf.Abs(y - centerY) <= 1;

                for (int x = 0; x < width; x++)
                {
                    bool isGridX = x % step == 0;
                    bool isAxisX = Mathf.Abs(x - centerX) <= 1;

                    Color pixel = bg;
                    if (isGridX || isGridY)
                    {
                        pixel = grid;
                    }
                    if (isAxisX || isAxisY)
                    {
                        pixel = axis;
                    }

                    generated.SetPixel(x, y, pixel);
                }
            }

            generated.Apply(false);
            mapPngBytes = generated.EncodeToPNG();
            if (mapPngBytes == null || mapPngBytes.Length == 0)
            {
                mapPngBytes = Array.Empty<byte>();
                currentMapSource = "none";
                currentMapMessage = "Procedural grid generation failed";
                Debug.LogWarning("[MONITOR] No se pudo generar mapa procedural.");
                return false;
            }

            currentMapSource = "grid";
            currentMapMessage = "Using procedural fallback grid";
            Debug.Log("[MONITOR] Mapa 2D procedural generado automaticamente.");
            return true;
        }
        catch (Exception ex)
        {
            mapPngBytes = Array.Empty<byte>();
            currentMapSource = "none";
            currentMapMessage = "Procedural grid generation threw exception";
            Debug.LogWarning($"[MONITOR] Error generando mapa procedural: {ex.Message}");
            return false;
        }
        finally
        {
            if (generated != null)
            {
                Destroy(generated);
            }
        }
    }

    private void OnDestroy()
    {
        StopServer();

        if (captureTexture != null)
        {
            captureTexture.Release();
            Destroy(captureTexture);
        }

        if (readbackTexture != null)
        {
            Destroy(readbackTexture);
        }
    }

    private IEnumerator CaptureLoop()
    {
        float wait = 1f / Mathf.Max(1, fps);
        WaitForSeconds waiter = new WaitForSeconds(wait);

        while (serverRunning)
        {
            yield return new WaitForEndOfFrame();
            CaptureFrame();
            yield return waiter;
        }
    }

    private void CaptureFrame()
    {
        float startMs = Time.realtimeSinceStartup * 1000f;

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = sourceCamera.targetTexture;

        sourceCamera.targetTexture = captureTexture;
        sourceCamera.Render();

        RenderTexture.active = captureTexture;
        readbackTexture.ReadPixels(new Rect(0, 0, frameWidth, frameHeight), 0, 0);
        readbackTexture.Apply(false);

        sourceCamera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;

        byte[] jpg = readbackTexture.EncodeToJPG(Mathf.Clamp(jpgQuality, 1, 100));

        lock (frameLock)
        {
            latestFrameJpg = jpg;
        }

        if (sourceCamera != null)
        {
            Vector3 pos = sourceCamera.transform.position;
            Vector3 rot = sourceCamera.transform.eulerAngles;
            TelemetrySnapshot fallback = new TelemetrySnapshot
            {
                x = pos.x,
                y = pos.y,
                z = pos.z,
                rotX = rot.x,
                rotY = rot.y,
                rotZ = rot.z,
                unixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            lock (telemetryFallbackLock)
            {
                telemetryFallbackSnapshot = fallback;
            }
        }

        long captureLatencyMs = (long)Mathf.Max(0f, (Time.realtimeSinceStartup * 1000f) - startMs);
        SessionRecorder.OnFrameCaptured(captureLatencyMs);
    }

    private void StartServer()
    {
        try
        {
            listener = new TcpListener(System.Net.IPAddress.Any, port);
            listener.Start();
            serverRunning = true;

            listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "MonitorFP-MJPEG-Listener"
            };
            listenerThread.Start();

            Debug.Log($"[MONITOR] Servidor activo en 0.0.0.0:{port}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MONITOR] Error al iniciar servidor: {ex.Message}");
            serverRunning = false;
        }
    }

    private void StopServer()
    {
        serverRunning = false;

        try
        {
            listener?.Stop();
        }
        catch
        {
        }

        try
        {
            if (listenerThread != null && listenerThread.IsAlive)
            {
                listenerThread.Join(300);
            }
        }
        catch
        {
        }
    }

    private void ListenLoop()
    {
        while (serverRunning)
        {
            TcpClient client = null;

            try
            {
                client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
            catch (SocketException)
            {
                if (!serverRunning)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MONITOR] Error en listener: {ex.Message}");
                client?.Close();
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        {
            client.ReceiveTimeout = 3000;
            client.SendTimeout = 3000;

            using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    string path = ReadRequestPath(stream);
                    if (string.IsNullOrEmpty(path))
                    {
                        return;
                    }

                    if (path.StartsWith("/stream.mjpg", StringComparison.OrdinalIgnoreCase))
                    {
                        SendMjpegStream(stream);
                        return;
                    }

                    if (path.StartsWith("/frame.jpg", StringComparison.OrdinalIgnoreCase))
                    {
                        SendSingleFrame(stream);
                        return;
                    }

                    if (path.StartsWith("/state.json", StringComparison.OrdinalIgnoreCase))
                    {
                        SendState(stream);
                        return;
                    }

                    if (path.StartsWith("/stats.json", StringComparison.OrdinalIgnoreCase))
                    {
                        SendStats(stream);
                        return;
                    }

                    if (path.StartsWith("/metrics.json", StringComparison.OrdinalIgnoreCase))
                    {
                        SendMetrics(stream);
                        return;
                    }

                    if (path.StartsWith("/map-config.json", StringComparison.OrdinalIgnoreCase))
                    {
                        SendMapConfig(stream);
                        return;
                    }

                    if (path.StartsWith("/map.png", StringComparison.OrdinalIgnoreCase))
                    {
                        SendMapImage(stream);
                        return;
                    }

                    if (path.StartsWith("/observation/start", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleObservationTrackingCommand(stream, true);
                        return;
                    }
                        if (path.StartsWith("/record/start", StringComparison.OrdinalIgnoreCase))
                        {
                            HandleRecordingCommand(stream, true);
                            return;
                        }

                        if (path.StartsWith("/record/stop", StringComparison.OrdinalIgnoreCase))
                        {
                            HandleRecordingCommand(stream, false);
                            return;
                        }

                        if (path.StartsWith("/record/marker", StringComparison.OrdinalIgnoreCase))
                        {
                            HandleRecordingMarker(stream, path);
                            return;
                        }
                    if (path.StartsWith("/observation/stop", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleObservationTrackingCommand(stream, false);
                        return;
                    }

                    if (path.StartsWith("/observation/state", StringComparison.OrdinalIgnoreCase))
                    {
                        SendObservationState(stream);
                        return;
                    }

                    if (path.StartsWith("/events.json", StringComparison.OrdinalIgnoreCase))
                    {
                        SendEventsConfig(stream);
                        return;
                    }

                    if (path == "/" || path.StartsWith("/index.html", StringComparison.OrdinalIgnoreCase))
                    {
                        SendHtml(stream);
                        return;
                    }

                    SendInfo(stream);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MONITOR] Error en request handler: {ex.Message}");
                    try
                    {
                        SendServiceUnavailable(stream, "Internal server error");
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    private string ReadRequestPath(NetworkStream stream)
    {
        using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 8192, true))
        {
            string requestLine = reader.ReadLine();
            if (string.IsNullOrEmpty(requestLine))
            {
                return string.Empty;
            }

            string[] parts = requestLine.Split(' ');
            if (parts.Length < 2)
            {
                return string.Empty;
            }

            string line;
            do
            {
                line = reader.ReadLine();
            } while (!string.IsNullOrEmpty(line));

            return parts[1];
        }
    }

    private void SendHtml(NetworkStream stream)
    {
        string html = @"<!DOCTYPE html>
<html lang=""es"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>MonitorFP Viewer</title>
    <script src=""https://cdn.plot.ly/plotly-latest.min.js""></script>
    <style>
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background-color: #f5f7fa;
            color: #333;
        }
        .container {
            max-width: 1600px;
            margin: 0 auto;
            padding: 12px;
        }
        .header {
            background: white;
            padding: 16px;
            border-radius: 8px;
            margin-bottom: 12px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }
        .header h1 {
            font-size: 28px;
            margin-bottom: 12px;
        }
        .status-bar {
            display: grid;
            grid-template-columns: auto 1fr;
            gap: 12px;
            align-items: center;
        }
        .status {
            padding: 8px 16px;
            border-radius: 4px;
            font-size: 14px;
            font-weight: 500;
            white-space: nowrap;
        }
        .status.connected {
            background: #d4edda;
            color: #238c3a;
        }
        .status.disconnected {
            background: #f8d7da;
            color: #be3344;
        }
        .grid-main {
            display: grid;
            grid-template-columns: 2fr 1fr;
            gap: 12px;
            margin-bottom: 12px;
        }
        .panel {
            background: white;
            padding: 16px;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }
        .panel h2 {
            font-size: 18px;
            margin-bottom: 12px;
            border-bottom: 2px solid #2d82b6;
            padding-bottom: 8px;
        }
        .video-container {
            text-align: center;
        }
        #videoFrame {
            max-width: 100%;
            max-height: 500px;
            background: #000;
            border-radius: 4px;
            margin-bottom: 12px;
        }
        .stats-grid {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 12px;
        }
        .stat-item {
            background: #f9f9f9;
            padding: 12px;
            border-left: 4px solid #2d82b6;
            border-radius: 4px;
        }
        .stat-label {
            font-size: 12px;
            color: #666;
            font-weight: 600;
            text-transform: uppercase;
        }
        .stat-value {
            font-size: 20px;
            color: #2d82b6;
            font-weight: bold;
            margin-top: 4px;
            font-family: monospace;
        }
        .chart-container {
            position: relative;
            height: 300px;
            margin-bottom: 12px;
        }
        .events-log {
            background: #f9f9f9;
            max-height: 200px;
            overflow-y: auto;
            border-radius: 4px;
            border: 1px solid #e0e0e0;
        }
        .observation-controls {
            display: flex;
            gap: 8px;
            margin-bottom: 10px;
            align-items: center;
            flex-wrap: wrap;
        }
        .obs-button {
            border: none;
            border-radius: 6px;
            padding: 8px 12px;
            font-size: 12px;
            font-weight: 600;
            cursor: pointer;
        }
        .obs-button.start {
            background: #d4edda;
            color: #176e2d;
        }
        .obs-button.stop {
            background: #f8d7da;
            color: #8b1f2e;
        }
        .obs-status {
            font-size: 12px;
            color: #666;
            font-family: monospace;
        }
        .interesting-table {
            width: 100%;
            border-collapse: collapse;
            font-size: 12px;
            margin-top: 8px;
        }
        .interesting-table th,
        .interesting-table td {
            border-bottom: 1px solid #e5e5e5;
            text-align: left;
            padding: 6px 8px;
        }
        .interesting-table th {
            color: #23425e;
            font-weight: 700;
            background: #f4f8fc;
            position: sticky;
            top: 0;
        }
        .event-item {
            padding: 8px 12px;
            border-bottom: 1px solid #e0e0e0;
            font-size: 12px;
            font-family: monospace;
        }
        .event-item:last-child {
            border-bottom: none;
        }
        .event-time {
            color: #2d82b6;
            font-weight: bold;
        }
        .event-type {
            color: #666;
            margin: 0 4px;
        }
        .event-desc {
            color: #333;
        }
        .empty-log {
            padding: 20px;
            text-align: center;
            color: #999;
        }
        .map-wrapper {
            position: relative;
            width: 100%;
            min-height: 260px;
            background: #f2f4f8;
            border: 1px solid #d5dbe3;
            border-radius: 6px;
            overflow: hidden;
        }
        #mapCanvas {
            width: 100%;
            height: auto;
            display: block;
            background: #eef1f6;
        }
        .map-status {
            margin-top: 8px;
            font-size: 12px;
            color: #666;
            font-family: monospace;
        }
        @media (max-width: 1200px) {
            .grid-main {
                grid-template-columns: 1fr;
            }
        }
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>MonitorFP - Monitor de Usuarios</h1>
            <div class=""status-bar"">
                <div id=""status"" class=""status disconnected"">● Desconectado</div>
                <div id=""connectionInfo"" style=""font-size: 12px; color: #666;""></div>
            </div>
        </div>

        <div class=""grid-main"">
            <div class=""panel video-container"">
                <h2>Vista en vivo</h2>
                <img id=""videoFrame"" src=""/frame.jpg"" alt=""Video stream"" width=""100%"">
                <div id=""posXyz"" style=""font-size: 12px; color: #666;"">Posición: -</div>
                <div id=""rotXyz"" style=""font-size: 12px; color: #666;"">Rotación: -</div>
            </div>

            <div class=""panel"">
                <h2>Sesión</h2>
                <div class=""stats-grid"">
                    <div class=""stat-item"">
                        <div class=""stat-label"">Duración</div>
                        <div class=""stat-value"" id=""duration"">00:00</div>
                    </div>
                    <div class=""stat-item"">
                        <div class=""stat-label"">Distancia (m)</div>
                        <div class=""stat-value"" id=""distance"">0.0</div>
                    </div>
                    <div class=""stat-item"">
                        <div class=""stat-label"">Vel. Actual (m/s)</div>
                        <div class=""stat-value"" id=""speed"">0.0</div>
                    </div>
                    <div class=""stat-item"">
                        <div class=""stat-label"">Vel. Media (m/s)</div>
                        <div class=""stat-value"" id=""avgSpeed"">0.0</div>
                    </div>
                </div>
            </div>
        </div>

        <div class=""panel"">
            <h2>Gráfica de trayectoria 3D (XYZ)</h2>
            <div class=""chart-container"" id=""trajChart"" style=""height: 400px;""></div>
        </div>

        <div class=""panel"">
            <h2>Mapa de recorrido (planta XZ)</h2>
            <div class=""map-wrapper"">
                <canvas id=""mapCanvas"" width=""1024"" height=""1024""></canvas>
            </div>
            <div id=""mapStatus"" class=""map-status"">Cargando mapa...</div>
        </div>

        <div class=""grid-main"">
            <div class=""panel"">
                <h2>Eventos de sesión</h2>
                <div class=""observation-controls"">
                    <button class=""obs-button start"" onclick=""startObservationTracking()"">Iniciar seguimiento observación</button>
                    <button class=""obs-button stop"" onclick=""stopObservationTracking()"">Parar seguimiento observación</button>
                    <span id=""obsStatus"" class=""obs-status"">Tracking: inactivo</span>
                </div>
                <div class=""observation-controls"" style=""margin-top:8px;"">
                    <button class=""obs-button start"" onclick=""startRecording()"">Iniciar grabación</button>
                    <button class=""obs-button stop"" onclick=""stopRecording()"">Parar grabación</button>
                    <label style=""font-size:12px; margin-left:8px;""><input id=""browserRecordToggle"" type=""checkbox"" checked> Grabar en navegador (.webm)</label>
                    <label style=""font-size:12px; margin-left:8px;""><input id=""nativeRecordToggle"" type=""checkbox""> Intentar grabación nativa Android</label>
                </div>
                <div id=""eventButtons"" style=""margin-top:8px; display:flex; gap:6px; flex-wrap:wrap;""></div>
                <canvas id=""captureCanvas"" width=""960"" height=""540"" style=""display:none;""></canvas>
                <div class=""events-log"" id=""eventsList"">
                    <div class=""empty-log"">Esperando eventos...</div>
                </div>

                <h2 style=""margin-top: 14px;"">Interesting Objects</h2>
                <div class=""events-log"" style=""max-height: 260px;"">
                    <table class=""interesting-table"">
                        <thead>
                            <tr>
                                <th>Name</th>
                                <th>Primera vez visto (s)</th>
                                <th>Tiempo observado (s)</th>
                                <th>Veces centro</th>
                                <th>% tiempo centro</th>
                            </tr>
                        </thead>
                        <tbody id=""interestingObjectsBody"">
                            <tr><td colspan=""5"" style=""color:#999;"">Sin objetos interesantes detectados.</td></tr>
                        </tbody>
                    </table>
                </div>
            </div>

            <div class=""panel"">
                <h2>Rendimiento</h2>
                <div class=""stats-grid"">
                    <div class=""stat-item"">
                        <div class=""stat-label"">FPS Servidor</div>
                        <div class=""stat-value"" id=""serverFps"">-</div>
                    </div>
                    <div class=""stat-item"">
                        <div class=""stat-label"">Latencia (ms)</div>
                        <div class=""stat-value"" id=""latency"">-</div>
                    </div>
                    <div class=""stat-item"">
                        <div class=""stat-label"">Frames capturados</div>
                        <div class=""stat-value"" id=""framesCapt"">-</div>
                    </div>
                    <div class=""stat-item"">
                        <div class=""stat-label"">Uptime (s)</div>
                        <div class=""stat-value"" id=""uptime"">-</div>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <script>
        const baseUrl = window.location.origin;
        let isConnected = false;
        let trajChart = null;
        let lastStats = null;
        let eventTimestamps = new Map();
        let mapCanvas = null;
        let mapCtx = null;
        let mapImage = new Image();
        let mapImageReady = false;
        let mapConfig = {
            hasMap: false,
            worldMinX: -5,
            worldMinZ: -5,
            worldMaxX: 5,
            worldMaxZ: 5,
            mapRotationDegrees: 90,
            flipX: false,
            flipY: false
        };

        function getMapRotationDegrees() {
            const raw = Number(mapConfig.mapRotationDegrees);
            if (!Number.isFinite(raw)) return 0;
            const snapped = Math.round(raw / 90) * 90;
            return ((snapped % 360) + 360) % 360;
        }

        function isQuarterTurnRotation() {
            const rot = getMapRotationDegrees();
            return rot === 90 || rot === 270;
        }

        function createChart() {
            const trace = {
                x: [],
                y: [],
                z: [],
                mode: 'lines',
                type: 'scatter3d',
                line: {
                    color: '#2d82b6',
                    width: 3
                },
                name: 'Trayectoria XYZ (Y = altura)'
            };
            const layout = {
                autosize: true,
                margin: { l: 0, r: 0, b: 0, t: 0 },
                scene: {
                    xaxis: { title: 'X' },
                    yaxis: { title: 'Y (altura)' },
                    zaxis: { title: 'Z (plano)' },
                    camera: {
                        up: { x: 0, y: 1, z: 0 },
                        eye: { x: 1.6, y: 1.2, z: 1.6 }
                    }
                }
            };
            Plotly.newPlot('trajChart', [trace], layout, { responsive: true });
        }

        async function loadMapConfig() {
            try {
                const resp = await fetch(baseUrl + '/map-config.json?t=' + Date.now());
                if (!resp.ok) throw new Error('Failed');
                const cfg = await resp.json();
                mapConfig = cfg;

                const status = document.getElementById('mapStatus');
                const boundsText = 'Bounds X[' + cfg.worldMinX.toFixed(2) + ', ' + cfg.worldMaxX.toFixed(2) + '] Z[' + cfg.worldMinZ.toFixed(2) + ', ' + cfg.worldMaxZ.toFixed(2) + ']';
                const sourceText = 'source=' + (cfg.mapSource || 'unknown');
                const rotText = ' rot=' + getMapRotationDegrees() + 'deg';
                const msgText = cfg.mapMessage ? (' msg=' + cfg.mapMessage) : '';

                if (cfg.hasMap) {
                    mapImageReady = false;
                    mapImage.onload = () => {
                        mapImageReady = true;
                        resizeMapCanvas();
                        status.textContent = 'Mapa cargado (' + sourceText + '). ' + boundsText + rotText + msgText;
                        drawMapPath(lastStats && lastStats.positionHistory ? lastStats.positionHistory : []);
                    };
                    mapImage.onerror = () => {
                        mapImageReady = false;
                        resizeMapCanvas();
                        status.textContent = 'No se pudo cargar /map.png. Se usa fondo neutro. ' + boundsText + ' ' + sourceText + rotText + msgText;
                    };
                    mapImage.src = baseUrl + '/map.png?t=' + Date.now();
                } else {
                    mapImageReady = false;
                    resizeMapCanvas();
                    status.textContent = 'Sin mapa disponible. ' + boundsText + ' ' + sourceText + rotText + msgText;
                }
            } catch (e) {
                document.getElementById('mapStatus').textContent = 'Error cargando configuración de mapa';
            }
        }

        function worldToMap(x, z) {
            const draw = getMapDrawRect();

            const sizeX = Math.max(0.0001, mapConfig.worldMaxX - mapConfig.worldMinX);
            const sizeZ = Math.max(0.0001, mapConfig.worldMaxZ - mapConfig.worldMinZ);

            let nx = (x - mapConfig.worldMinX) / sizeX;
            let nz = (z - mapConfig.worldMinZ) / sizeZ;

            nx = Math.max(0, Math.min(1, nx));
            nz = Math.max(0, Math.min(1, nz));

            let px;
            let py;
            const rot = getMapRotationDegrees();

            if (rot === 90) {
                px = draw.x + ((1 - nz) * draw.width);
                py = draw.y + ((1 - nx) * draw.height);
            } else if (rot === 180) {
                px = draw.x + ((1 - nx) * draw.width);
                py = draw.y + (nz * draw.height);
            } else if (rot === 270) {
                px = draw.x + (nz * draw.width);
                py = draw.y + (nx * draw.height);
            } else {
                px = draw.x + (nx * draw.width);
                py = draw.y + ((1 - nz) * draw.height);
            }

            if (mapConfig.flipX) px = draw.x + draw.width - (px - draw.x);
            if (mapConfig.flipY) py = draw.y + draw.height - (py - draw.y);

            return { x: px, y: py };
        }

        function getMapDrawRect() {
            const w = mapCanvas.width;
            const h = mapCanvas.height;

            if (!mapImageReady || mapImage.width === 0 || mapImage.height === 0) {
                return { x: 0, y: 0, width: w, height: h };
            }

            const canvasAspect = w / Math.max(1, h);
            const imageAspectRaw = mapImage.width / Math.max(1, mapImage.height);
            const imageAspect = isQuarterTurnRotation() ? (1 / imageAspectRaw) : imageAspectRaw;

            if (imageAspect > canvasAspect) {
                const drawWidth = w;
                const drawHeight = w / imageAspect;
                return {
                    x: 0,
                    y: (h - drawHeight) * 0.5,
                    width: drawWidth,
                    height: drawHeight
                };
            }

            const drawHeight2 = h;
            const drawWidth2 = h * imageAspect;
            return {
                x: (w - drawWidth2) * 0.5,
                y: 0,
                width: drawWidth2,
                height: drawHeight2
            };
        }

        function getTargetMapAspect() {
            if (mapImageReady && mapImage.width > 0 && mapImage.height > 0) {
                const rawAspect = mapImage.width / Math.max(1, mapImage.height);
                return isQuarterTurnRotation() ? (1 / rawAspect) : rawAspect;
            }

            const sizeX = Math.max(0.0001, mapConfig.worldMaxX - mapConfig.worldMinX);
            const sizeZ = Math.max(0.0001, mapConfig.worldMaxZ - mapConfig.worldMinZ);
            return isQuarterTurnRotation() ? (sizeZ / sizeX) : (sizeX / sizeZ);
        }

        function resizeMapCanvas() {
            if (!mapCanvas) return;

            const wrapper = mapCanvas.parentElement;
            if (!wrapper) return;

            const width = Math.max(320, Math.floor(wrapper.clientWidth || 1024));
            const aspect = Math.max(0.35, Math.min(3.0, getTargetMapAspect()));
            const targetHeight = Math.round(width / aspect);
            const clampedHeight = Math.max(260, Math.min(640, targetHeight));

            if (mapCanvas.width !== width || mapCanvas.height !== clampedHeight) {
                mapCanvas.width = width;
                mapCanvas.height = clampedHeight;
                mapCanvas.style.height = clampedHeight + 'px';
            }
        }

        function drawMapPath(positionHistory) {
            if (!mapCtx) return;

            const w = mapCanvas.width;
            const h = mapCanvas.height;
            mapCtx.clearRect(0, 0, w, h);
            mapCtx.fillStyle = '#eef1f6';
            mapCtx.fillRect(0, 0, w, h);

            if (mapImageReady) {
                const draw = getMapDrawRect();
                const rot = getMapRotationDegrees();
                if (rot === 0) {
                    mapCtx.drawImage(mapImage, draw.x, draw.y, draw.width, draw.height);
                } else {
                    const cx = draw.x + (draw.width * 0.5);
                    const cy = draw.y + (draw.height * 0.5);
                    const quarterTurn = (rot === 90 || rot === 270);
                    const drawW = quarterTurn ? draw.height : draw.width;
                    const drawH = quarterTurn ? draw.width : draw.height;
                    mapCtx.save();
                    mapCtx.translate(cx, cy);
                    mapCtx.rotate(-rot * Math.PI / 180);
                    mapCtx.drawImage(mapImage, -drawW * 0.5, -drawH * 0.5, drawW, drawH);
                    mapCtx.restore();
                }
            }

            if (!positionHistory || positionHistory.length === 0) {
                return;
            }

            mapCtx.lineWidth = 2;
            mapCtx.strokeStyle = '#2d82b6';
            mapCtx.beginPath();

            for (let i = 0; i < positionHistory.length; i++) {
                const p = worldToMap(positionHistory[i].x, positionHistory[i].z);
                if (i === 0) {
                    mapCtx.moveTo(p.x, p.y);
                } else {
                    mapCtx.lineTo(p.x, p.y);
                }
            }

            mapCtx.stroke();

            const last = positionHistory[positionHistory.length - 1];
            const lp = worldToMap(last.x, last.z);
            mapCtx.fillStyle = '#d32f2f';
            mapCtx.beginPath();
            mapCtx.arc(lp.x, lp.y, 5, 0, Math.PI * 2);
            mapCtx.fill();
        }

        async function updateFrame() {
            try {
                const ts = Date.now();
                document.getElementById('videoFrame').src = baseUrl + '/frame.jpg?t=' + ts;
                setConnected(true);
            } catch (e) {
                setConnected(false);
            }
        }

        async function updateStats() {
            try {
                const resp = await fetch(baseUrl + '/stats.json?t=' + Date.now());
                if (!resp.ok) throw new Error('Failed');
                const stats = await resp.json();
                lastStats = stats;

                document.getElementById('duration').textContent = formatSeconds(stats.elapsedSeconds);
                document.getElementById('distance').textContent = stats.distanceTraveled.toFixed(2);
                document.getElementById('speed').textContent = stats.currentSpeed.toFixed(2);
                document.getElementById('avgSpeed').textContent = stats.averageSpeed.toFixed(2);

                if (stats.positionHistory && stats.positionHistory.length > 0) {
                    const xs = stats.positionHistory.map(s => s.x);
                    const ys = stats.positionHistory.map(s => s.y);
                    const zs = stats.positionHistory.map(s => s.z);
                    Plotly.restyle('trajChart', { x: [xs], y: [ys], z: [zs] });
                }

                drawMapPath(stats.positionHistory || []);

                if (stats.events && stats.events.length > 0) {
                    updateEventsList(stats.events);
                }

                updateInterestingObjectsTable(stats.interestingObjects || []);
                updateObservationControls(stats.observationTrackingActive, stats.observationTrackingStartMs);

                setConnected(true);
            } catch (e) {
                setConnected(false);
            }
        }

        async function updateMetrics() {
            try {
                const resp = await fetch(baseUrl + '/metrics.json?t=' + Date.now());
                if (!resp.ok) throw new Error('Failed');
                const metrics = await resp.json();
                
                document.getElementById('serverFps').textContent = metrics.captureFramerate ?? '-';
                document.getElementById('latency').textContent = Number.isFinite(metrics.averageLatencyMs) ? metrics.averageLatencyMs.toFixed(1) : '-';
                document.getElementById('framesCapt').textContent = metrics.totalFramesCaptured ?? '-';
                document.getElementById('uptime').textContent = metrics.uptime ?? '-';
                
                setConnected(true);
            } catch (e) {
                setConnected(false);
            }
        }

        function updateEventsList(events) {
            const list = document.getElementById('eventsList');
            if (!events || events.length === 0) {
                list.innerHTML = '<div class=""empty-log"">Sin eventos</div>';
                return;
            }
            
            const recent = events.slice(-10);
            list.innerHTML = recent.map(e => {
                const dt = new Date(e.timestampMs);
                const time = dt.toLocaleTimeString();
                const objectText = e.objectName && e.objectName.length > 0 ? (' [obj: ' + e.objectName + ']') : '';
                return '<div class=""event-item""><span class=""event-time"">' + time + '</span><span class=""event-type"">' + e.eventType + '</span><span class=""event-desc"">' + e.description + objectText + '</span></div>';
            }).join('');
        }

        function updateInterestingObjectsTable(items) {
            const body = document.getElementById('interestingObjectsBody');
            if (!items || items.length === 0) {
                body.innerHTML = '<tr><td colspan=""5"" style=""color:#999;"">Sin objetos interesantes detectados.</td></tr>';
                return;
            }

            body.innerHTML = items.map(item => {
                const firstSeen = item.firstSeenAtSeconds >= 0 ? item.firstSeenAtSeconds.toFixed(2) : '-';
                const observed = Number.isFinite(item.totalObservedSeconds) ? item.totalObservedSeconds.toFixed(2) : '0.00';
                const centerCount = Number.isFinite(item.centerObservationCount) ? item.centerObservationCount : 0;
                const centerPercent = Number.isFinite(item.centerTimePercent) ? item.centerTimePercent.toFixed(1) + '%' : '0.0%';
                return '<tr>' +
                    '<td>' + escapeHtml(item.name || 'unknown') + '</td>' +
                    '<td>' + firstSeen + '</td>' +
                    '<td>' + observed + '</td>' +
                    '<td>' + centerCount + '</td>' +
                    '<td>' + centerPercent + '</td>' +
                    '</tr>';
            }).join('');
        }

        function updateObservationControls(isActive, startMs) {
            const status = document.getElementById('obsStatus');
            if (!status) return;

            if (isActive) {
                const since = startMs ? (' | startMs=' + startMs) : '';
                status.textContent = 'Tracking: ACTIVO' + since;
            } else {
                status.textContent = 'Tracking: inactivo';
            }
        }

        async function startObservationTracking() {
            try {
                await fetch(baseUrl + '/observation/start?t=' + Date.now());
            } catch (e) {
                console.warn('No se pudo iniciar tracking de observación', e);
            }
        }

        async function stopObservationTracking() {
            try {
                await fetch(baseUrl + '/observation/stop?t=' + Date.now());
            } catch (e) {
                console.warn('No se pudo parar tracking de observación', e);
            }
        }

        let browserRecording = false;
        let mediaRecorder = null;
        let recordedChunks = [];
        let browserMarkers = [];
        let browserRecordStart = 0;
        const captureCanvasEl = document.getElementById('captureCanvas');
        const captureCtx = captureCanvasEl ? captureCanvasEl.getContext('2d') : null;

        async function startRecording() {
            try {
                await fetch(baseUrl + '/record/start?t=' + Date.now());
            } catch (e) {
                console.warn('No se pudo iniciar grabación (server)', e);
            }

            if (document.getElementById('browserRecordToggle') && document.getElementById('browserRecordToggle').checked) {
                startBrowserRecording();
            }
        }

        function startBrowserRecording() {
            if (browserRecording) return;
            const img = document.getElementById('videoFrame');
            if (!captureCanvasEl || !captureCtx || !img) {
                console.warn('No canvas/img available for browser recording');
                return;
            }

            browserRecording = true;
            recordedChunks = [];
            browserMarkers = [];
            browserRecordStart = Date.now();

            captureCanvasEl.width = img.naturalWidth || 960;
            captureCanvasEl.height = img.naturalHeight || 540;

            const stream = captureCanvasEl.captureStream(30);
            try {
                mediaRecorder = new MediaRecorder(stream, { mimeType: 'video/webm;codecs=vp8' });
            } catch (e) {
                try { mediaRecorder = new MediaRecorder(stream); } catch (e2) { console.warn('MediaRecorder unavailable', e2); browserRecording = false; return; }
            }

            mediaRecorder.ondataavailable = (ev) => { if (ev.data && ev.data.size > 0) recordedChunks.push(ev.data); };
            mediaRecorder.start();

            const drawFn = () => {
                if (!browserRecording) return;
                try { captureCtx.drawImage(img, 0, 0, captureCanvasEl.width, captureCanvasEl.height); } catch (e) { }
                requestAnimationFrame(drawFn);
            };
            drawFn();
        }

        async function stopRecording() {
            if (browserRecording) await stopBrowserRecording();

            try {
                const resp = await fetch(baseUrl + '/record/stop?t=' + Date.now());
                if (resp.ok) {
                    const json = await resp.json();
                    const srt = generateSrtFromServer(json);
                    downloadBlob(new Blob([srt], { type: 'text/plain' }), 'markers_server.srt');
                }
            } catch (e) {
                console.warn('No se pudo parar grabación (server)', e);
            }
        }

        function stopBrowserRecording() {
            return new Promise((resolve) => {
                if (!browserRecording) { resolve(); return; }
                browserRecording = false;
                if (!mediaRecorder) { resolve(); return; }
                mediaRecorder.onstop = () => {
                    const blob = new Blob(recordedChunks, { type: 'video/webm' });
                    downloadBlob(blob, 'recording_browser.webm');
                    const srt = generateSrtFromBrowser();
                    downloadBlob(new Blob([srt], { type: 'text/plain' }), 'markers_browser.srt');
                    resolve();
                };
                mediaRecorder.stop();
            });
        }

        function onEventButtonClick(label) {
            try { fetch(baseUrl + '/record/marker?label=' + encodeURIComponent(label)); } catch (e) { }
            if (browserRecording) {
                browserMarkers.push({ ms: Date.now() - browserRecordStart, label: label });
            }
        }

        function fetchEventsConfig() {
            fetch(baseUrl + '/events.json?t=' + Date.now()).then(r => r.json()).then(j => {
                const container = document.getElementById('eventButtons');
                if (!container) return;
                if (!j || !j.labels) { container.innerHTML = ''; return; }
                container.innerHTML = j.labels.map(l => '<button class=""obs-button"" style=""font-size:12px;"" onclick=""onEventButtonClick(\'' + escapeHtml(l) + '\')"">' + escapeHtml(l) + '</button>').join('');
            }).catch(() => { });
        }

        function generateSrtFromBrowser() {
            let out = '';
            for (let i = 0; i < browserMarkers.length; i++) {
                const idx = i + 1;
                const startMs = browserMarkers[i].ms;
                const endMs = startMs + 1000;
                out += idx + '\n' + formatSrtTime(startMs) + ' --> ' + formatSrtTime(endMs) + '\n' + browserMarkers[i].label + '\n\n';
            }
            return out;
        }

        function generateSrtFromServer(json) {
            if (!json || !json.markers) return '';
            let out = '';
            for (let i = 0; i < json.markers.length; i++) {
                const idx = i + 1;
                const startMs = json.markers[i].ms;
                const endMs = startMs + 1000;
                out += idx + '\n' + formatSrtTime(startMs) + ' --> ' + formatSrtTime(endMs) + '\n' + json.markers[i].label + '\n\n';
            }
            return out;
        }

        function formatSrtTime(ms) {
            const d = new Date(ms);
            const hh = String(d.getUTCHours()).padStart(2, '0');
            const mm = String(d.getUTCMinutes()).padStart(2, '0');
            const ss = String(d.getUTCSeconds()).padStart(2, '0');
            const ms3 = String(Math.floor((ms % 1000))).padStart(3, '0');
            return hh + ':' + mm + ':' + ss + ',' + ms3;
        }

        function downloadBlob(blob, filename) {
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url; a.download = filename; document.body.appendChild(a); a.click(); setTimeout(() => { URL.revokeObjectURL(url); a.remove(); }, 1000);
        }

        function escapeHtml(text) {
            return String(text)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/""/g, '&quot;')
                .replace(/'/g, '&#39;');
        }

        function setConnected(connected) {
            if (connected === isConnected) return;
            isConnected = connected;
            const statusEl = document.getElementById('status');
            if (connected) {
                statusEl.textContent = '● Conectado';
                statusEl.className = 'status connected';
                document.getElementById('connectionInfo').textContent = 'Última actualización: ahora';
            } else {
                statusEl.textContent = '● Desconectado';
                statusEl.className = 'status disconnected';
                document.getElementById('connectionInfo').textContent = 'Sin conexión al servidor';
            }
        }

        function formatSeconds(seconds) {
            const hrs = Math.floor(seconds / 3600);
            const mins = Math.floor((seconds % 3600) / 60);
            const secs = seconds % 60;
            const padZero = (n) => String(n).padStart(2, '0');
            return padZero(hrs) + ':' + padZero(mins) + ':' + padZero(secs);
        }

        mapCanvas = document.getElementById('mapCanvas');
        mapCtx = mapCanvas.getContext('2d');
        resizeMapCanvas();
        createChart();
        loadMapConfig();
        updateFrame();
        updateStats();
        updateMetrics();
        fetchEventsConfig();

        window.addEventListener('resize', () => {
            resizeMapCanvas();
            drawMapPath(lastStats && lastStats.positionHistory ? lastStats.positionHistory : []);
        });

        setInterval(updateFrame, 33);
        setInterval(updateStats, 250);
        setInterval(updateMetrics, 500);
    </script>
</body>
</html>";

        byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\n" +
            $"Content-Length: {htmlBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        WriteAscii(stream, headers);
        stream.Write(htmlBytes, 0, htmlBytes.Length);
    }

    private void SendInfo(NetworkStream stream)
    {
        const string body = "MonitorFP Toolkit server running. Endpoints: /, /frame.jpg, /stream.mjpg, /state.json, /stats.json, /metrics.json, /map-config.json, /map.png, /observation/start, /observation/stop, /observation/state, /record/start, /record/stop, /record/marker, /events.json";
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        WriteAscii(stream, headers);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
    }

    private void SendSingleFrame(NetworkStream stream)
    {
        byte[] frame = GetLatestFrame();
        if (frame.Length == 0)
        {
            SendServiceUnavailable(stream, "No frame available yet");
            return;
        }

        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: image/jpeg\r\n" +
            "Cache-Control: no-store\r\n" +
            $"Content-Length: {frame.Length}\r\n" +
            "Connection: close\r\n\r\n";

        WriteAscii(stream, headers);
        stream.Write(frame, 0, frame.Length);
    }

    private void SendEventsConfig(NetworkStream stream)
    {
        string[] labels = ExperimentalEventConfig.GetCachedLabelsSnapshot();

        var payload = new { labels = labels };
        string json = JsonUtility.ToJson(payload);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\n" +
            $"Content-Length: {jsonBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        WriteAscii(stream, headers);
        stream.Write(jsonBytes, 0, jsonBytes.Length);
    }

    private void SendState(NetworkStream stream)
    {
        TelemetrySnapshot snapshot;
        if (!UserTracker.TryGetLatestSnapshot(out snapshot))
        {
            lock (telemetryFallbackLock)
            {
                snapshot = telemetryFallbackSnapshot;
            }
        }

        string json = JsonUtility.ToJson(snapshot);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\n" +
            $"Content-Length: {jsonBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        WriteAscii(stream, headers);
        stream.Write(jsonBytes, 0, jsonBytes.Length);
    }

    private void SendStats(NetworkStream stream)
    {
        SessionRecorder recorder = SessionRecorder.GetInstance();
        SessionStats stats = recorder != null ? recorder.GetSessionStats() : new SessionStats();

        string json = JsonUtility.ToJson(stats);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\n" +
            $"Content-Length: {jsonBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        WriteAscii(stream, headers);
        stream.Write(jsonBytes, 0, jsonBytes.Length);
    }

    private void SendMetrics(NetworkStream stream)
    {
        SessionRecorder recorder = SessionRecorder.GetInstance();
        PerformanceMetrics metrics = recorder != null ? recorder.GetPerformanceMetrics() : new PerformanceMetrics();

        string json = JsonUtility.ToJson(metrics);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\n" +
            $"Content-Length: {jsonBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        WriteAscii(stream, headers);
        stream.Write(jsonBytes, 0, jsonBytes.Length);
    }

    private void SendMapConfig(NetworkStream stream)
    {
        MapConfigPayload cfg = new MapConfigPayload
        {
            hasMap = mapPngBytes != null && mapPngBytes.Length > 0,
            worldMinX = mapWorldMinXZ.x,
            worldMinZ = mapWorldMinXZ.y,
            worldMaxX = mapWorldMaxXZ.x,
            worldMaxZ = mapWorldMaxXZ.y,
            flipX = mapFlipX,
            flipY = mapFlipY,
            mapRotationDegrees = GetNormalizedMapRotationDegrees(),
            hasAssignedMapTexture = hasAssignedMapTexture,
            mapSource = currentMapSource,
            mapMessage = currentMapMessage
        };

        string json = JsonUtility.ToJson(cfg);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\n" +
            $"Content-Length: {jsonBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        WriteAscii(stream, headers);
        stream.Write(jsonBytes, 0, jsonBytes.Length);
    }

    private void SendMapImage(NetworkStream stream)
    {
        if (mapPngBytes == null || mapPngBytes.Length == 0)
        {
            string headers =
                "HTTP/1.1 404 Not Found\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n";
            WriteAscii(stream, headers);
            return;
        }

        string okHeaders =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: image/png\r\n" +
            "Cache-Control: no-store\r\n" +
            $"Content-Length: {mapPngBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        WriteAscii(stream, okHeaders);
        stream.Write(mapPngBytes, 0, mapPngBytes.Length);
    }

    private void SendMjpegStream(NetworkStream stream)
    {
        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Connection: close\r\n" +
            "Cache-Control: no-store\r\n" +
            "Pragma: no-cache\r\n" +
            "Content-Type: multipart/x-mixed-replace; boundary=frame\r\n\r\n";

        WriteAscii(stream, headers);

        int delayMs = Mathf.RoundToInt(1000f / Mathf.Max(1, fps));

        while (serverRunning && stream.CanWrite)
        {
            byte[] frame = GetLatestFrame();
            if (frame.Length == 0)
            {
                Thread.Sleep(delayMs);
                continue;
            }

            string partHeader =
                "--frame\r\n" +
                "Content-Type: image/jpeg\r\n" +
                $"Content-Length: {frame.Length}\r\n\r\n";

            try
            {
                WriteAscii(stream, partHeader);
                stream.Write(frame, 0, frame.Length);
                WriteAscii(stream, "\r\n");
                stream.Flush();
                Thread.Sleep(delayMs);
            }
            catch
            {
                return;
            }
        }
    }

    private void SendServiceUnavailable(NetworkStream stream, string message)
    {
        byte[] body = Encoding.UTF8.GetBytes(message);
        string headers =
            "HTTP/1.1 503 Service Unavailable\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";

        WriteAscii(stream, headers);
        stream.Write(body, 0, body.Length);
    }

    [Serializable]
    private struct ObservationStatePayload
    {
        public bool active;
        public long trackingStartMs;
        public int interestingObjectCount;
    }

    private void HandleObservationTrackingCommand(NetworkStream stream, bool start)
    {
        ObservationTracker tracker = ObservationTracker.GetInstance();
        if (tracker == null)
        {
            SendServiceUnavailable(stream, "ObservationTracker not found in scene");
            return;
        }

        if (start)
        {
            tracker.StartObservationTracking();
        }
        else
        {
            tracker.StopObservationTracking();
        }

        SendObservationState(stream);
    }

    private void SendObservationState(NetworkStream stream)
    {
        ObservationTracker tracker = ObservationTracker.GetInstance();
        if (tracker == null)
        {
            SendServiceUnavailable(stream, "ObservationTracker not found in scene");
            return;
        }

        tracker.GetObservationSnapshot(out InterestingObjectObservationStats[] stats, out bool active, out long startMs);
        ObservationStatePayload payload = new ObservationStatePayload
        {
            active = active,
            trackingStartMs = startMs,
            interestingObjectCount = stats != null ? stats.Length : 0
        };

        string json = JsonUtility.ToJson(payload);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\n" +
            $"Content-Length: {jsonBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        WriteAscii(stream, headers);
        stream.Write(jsonBytes, 0, jsonBytes.Length);
    }

    private void HandleRecordingCommand(NetworkStream stream, bool start)
    {
        RecordingManager manager = RecordingManager.Instance;
        if (manager == null)
        {
            SendServiceUnavailable(stream, "RecordingManager not available");
            return;
        }

        if (start)
        {
            manager.StartRecording();
            byte[] payload = Encoding.ASCII.GetBytes("OK");
            WriteAscii(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: " + payload.Length + "\r\n\r\n");
            stream.Write(payload, 0, payload.Length);
            return;
        }

        manager.StopRecording();
        var data = manager.GetRecordingData();
        var markers = data.markers;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append('{');
        sb.Append("\"startMs\":").Append(data.startMs).Append(',');
        sb.Append("\"markers\":[");
        bool first = true;
        foreach (var m in markers)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('{');
            sb.Append("\"ms\":").Append(m.ms).Append(',');
            sb.Append("\"label\":\"").Append(EscapeJson(m.label)).Append("\"");
            sb.Append('}');
        }
        sb.Append(']');
        sb.Append('}');

        string json = sb.ToString();
        byte[] body = Encoding.UTF8.GetBytes(json);
        WriteAscii(stream, "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + body.Length + "\r\n\r\n");
        stream.Write(body, 0, body.Length);
    }

    private void HandleRecordingMarker(NetworkStream stream, string path)
    {
        string label = "marker";
        try
        {
            int q = path.IndexOf('?');
            if (q >= 0)
            {
                string qs = path.Substring(q + 1);
                var parts = qs.Split('&');
                foreach (var p in parts)
                {
                    var kv = p.Split('=');
                    if (kv.Length == 2 && kv[0] == "label")
                    {
                        label = Uri.UnescapeDataString(kv[1]);
                    }
                }
            }
        }
        catch { }

        RecordingManager manager = RecordingManager.Instance;
        if (manager == null)
        {
            SendServiceUnavailable(stream, "RecordingManager not available");
            return;
        }

        manager.AddMarker(label);
        byte[] payload = Encoding.ASCII.GetBytes("OK");
        WriteAscii(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: " + payload.Length + "\r\n\r\n");
        stream.Write(payload, 0, payload.Length);
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private byte[] GetLatestFrame()
    {
        lock (frameLock)
        {
            return latestFrameJpg;
        }
    }

    private static void WriteAscii(Stream stream, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}