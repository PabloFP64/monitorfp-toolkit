# MonitorFP Toolkit Viewer (sin Unity Render Streaming)

Guía completa para configurar y usar el sistema de monitorización en tiempo real desde Unity hacia web y app de escritorio.

## Qué hace este sistema

El componente `MjpegTelemetryServer` embebido en Unity publica:

- Vídeo en vivo del usuario (MJPEG)
- Telemetría de posición/rotación
- Estadísticas de sesión
- Métricas de rendimiento del servidor
- Dashboard web con gráfica 3D y mapa 2D de recorrido en planta

No depende de Unity Render Streaming.

## 1) Configuración en Unity

1. En tu escena, identifica la cámara del usuario (HMD/cámara principal).
2. Crea tres GameObjects vacíos:
   - `MonitorServer` con `MjpegTelemetryServer`
   - `SessionRecorder` con `SessionRecorder`
   - `UserTracker` con `UserTracker`
3. En `MjpegTelemetryServer`, asigna `Source Camera`.
4. Añade `UserTracker` en escena para actualizar snapshots.
5. Para configurar el mapa sin hacerlo a mano, usa el menú de Unity `MonitorFP > Crear Camara Top Down para Mapa`.
6. La cámara top-down creada se deja en `Target Display 2` para no pisar la cámara principal.

### Parámetros principales (MjpegTelemetryServer)

- `Port`: puerto HTTP (por defecto `8080`)
- `Frame Width` / `Frame Height`: resolución del stream
- `FPS`: frames capturados por segundo
- `JPG Quality`: calidad JPEG (1-100)

### Parámetros de mapa 2D (nuevo)

Sección `Mapa 2D (planta XZ)`:

- `Map Texture`: imagen del plano/mapa que se mostrará en la web
- `Map World Min XZ`: esquina mínima del mundo en Unity (X,Z)
- `Map World Max XZ`: esquina máxima del mundo en Unity (X,Z)
- `Map Flip X`: invierte horizontalmente el mapeo
- `Map Flip Y`: invierte verticalmente el mapeo
- `Map Rotation Degrees`: rota el mapa y la trayectoria en la web en pasos de 90 grados (`0`, `90`, `180`, `270`)

Sección `Mapa dinámico (opcional)`:

- `Map Capture Camera`: cámara cenital para generar mapa automáticamente
- `Auto Generate Map From Camera`: si está activo, genera mapa desde `Map Capture Camera` cuando no hay mapa usable
- `Auto Generate Grid If No Map`: si está activo, usa una rejilla procedural como fallback
- `Generated Map Width` / `Generated Map Height`: resolución del mapa generado

Notas:

- Prioridad de carga del mapa: `Map Texture` -> `Map Capture Camera` -> rejilla procedural.
- Si cambias `Map Texture` o parámetros de generación, reinicia Play para regenerar el mapa en memoria.
- Si Unity no puede codificar la textura, activa `Read/Write` o usa `Map Capture Camera`.

## 2) Endpoints HTTP disponibles

Servidor escuchando en `0.0.0.0:<port>`:

- `/` dashboard web completo
- `/frame.jpg` frame JPEG actual
- `/stream.mjpg` stream MJPEG continuo
- `/state.json` posición/rotación actual
- `/stats.json` estadísticas e historial de sesión
- `/metrics.json` métricas de rendimiento
- `/map-config.json` configuración del mapa 2D para la web
- `/map.png` imagen del mapa en PNG

### Diagnóstico de mapa (`/map-config.json`)

Campos útiles para debug:

- `hasMap`: indica si el servidor tiene un mapa listo para servir
- `hasAssignedMapTexture`: indica si hay `Map Texture` asignada en el Inspector
- `mapSource`: origen actual del mapa (`texture`, `camera`, `grid`, `none`)
- `mapMessage`: mensaje descriptivo del último intento de carga/generación

## 3) Uso por web (recomendado)

En el mismo equipo de Unity:

```text
http://localhost:8080
```

Desde otro equipo de la LAN:

```text
http://<IP_DEL_PC_UNITY>:8080
```


## 4) Qué muestra el dashboard web

1. Vista en vivo (frame de cámara)
2. Estadísticas de sesión:
   - duración
   - distancia recorrida
   - velocidad actual
   - velocidad media
3. Trayectoria 3D (Plotly) en ejes Unity XYZ
   - `Y` se muestra como altura
4. Mapa 2D de recorrido (planta XZ)
   - trayectoria superpuesta al plano
   - punto rojo en la última posición
5. Eventos de sesión (últimos eventos)
   - ahora pueden mostrar el nombre del objeto interactuado en el campo `[obj: ...]`
6. Rendimiento:
   - FPS servidor
   - latencia media de captura (ms)
   - frames capturados
   - uptime

## 5) Calibración del mapa 2D (importante)

El mapa 2D usa coordenadas de Unity `XZ` y las normaliza entre `Map World Min XZ` y `Map World Max XZ`.

Proceso recomendado:

1. Mide o estima los límites reales del área caminable en Unity (X y Z).
2. Pon esos valores en `Min XZ` y `Max XZ`.
3. Prueba moviéndote a esquinas conocidas y verifica si coinciden en el mapa.
4. Si sale espejado, activa `Map Flip X` y/o `Map Flip Y`.

## 6) Registro de eventos personalizados

Para añadir eventos desde tus scripts:

```csharp
using UnityEngine;

public class MyInteractionScript : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            SessionRecorder recorder = SessionRecorder.GetInstance();
            if (recorder != null)
            {
                recorder.RecordEvent("interaction", "Usuario pulsó E");
            }
        }
    }
}
```

Tipos sugeridos:

- `interaction`
- `movement_start`
- `movement_stop`
- `gaze_target`
- `marker`

## 7) Visualización por app de escritorio (opcional)

Ejecutable publicado:

`ViewerApp/MonitorFPViewer/bin/Release/net8.0-windows/win-x64/publish/MonitorFPViewer.exe`

Uso:

1. Introduce host/IP del equipo Unity
2. Puerto (por defecto `8080`)
3. Conectar

## 8) Recompilar ViewerApp

Desde `ViewerApp/MonitorFPViewer`:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

## 9) Red y acceso remoto

- En LAN, abre firewall para el puerto configurado.
- Fuera de la LAN, necesitas VPN/túnel/port forwarding.

## 10) Solución rápida de problemas

- La web abre pero no actualiza:
  - revisar URL, firewall y puerto
- `latencia` o `frames` en `-`:
  - confirmar que `SessionRecorder` está en escena
- Eventos de sesión sin nombre de objeto:
   - confirmar que `SessionRecorder` está activo y que la escena contiene interactables XR con `selectEntered`/`hoverEntered`
- Posición/rotación no cambia en `/state.json`:
   - confirmar que `UserTracker` está activo y colgado de la cámara del usuario
- Mapa no aparece:
   - revisar `http://localhost:8080/map-config.json`
   - comprobar `mapSource` y `mapMessage`
   - si `mapSource=grid`, el mapa real no se pudo cargar; revisar `Map Texture`/`Read-Write` o configurar `Map Capture Camera`
   - reiniciar Play tras cambios de mapa
- Recorrido desalineado en mapa:
  - recalibrar `Min XZ` / `Max XZ`
  - probar `Flip X` / `Flip Y`
   - probar `Map Rotation Degrees` si la orientación no coincide con la esperada

