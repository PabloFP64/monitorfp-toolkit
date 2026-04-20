# MonitorFP Toolkit - Guia de Uso

Guia practica para montar monitorizacion en tiempo real en Unity sin Unity Render Streaming.

Con este toolkit puedes exponer por HTTP:

- video en vivo (MJPEG)
- telemetria de posicion/rotacion
- estadisticas y eventos de sesion
- mapa 2D de recorrido (planta XZ)
- dashboard web listo para usar

## 1) Requisitos

- Unity 6 (probado en 6000.3.x)
- Paquete `com.unity.xr.interaction.toolkit` instalado
- Una escena con camara del usuario (`Main Camera` o camara equivalente)
- Red local abierta si quieres verlo desde otro dispositivo

## 2) Instalacion (elige una opcion)

Instalarlo desde Git en Package Manager:

1. Unity -> Window -> Package Manager
2. `+` -> Add package from git URL
3. URL

## 3) Setup minimo en escena

1. Crea tres GameObjects vacios:
   - `MonitorServer` con `MjpegTelemetryServer`
   - `SessionRecorder` con `SessionRecorder`
   - `UserTracker` con `UserTracker`
2. En `MjpegTelemetryServer`, asigna `Source Camera`.
3. Deja `SessionRecorder` activo en escena.
4. Ejecuta Play y abre:

```text
http://localhost:8080
```

Si ves dashboard y video, ya funciona.

## 4) Configurar mapa 2D rapido

### Opcion automatica (recomendada)

Usa el menu de Unity:

`MonitorFP > Crear Camara Top Down para Mapa`

Esto crea una camara ortográfica cenital y:

- la asigna a `mapCaptureCamera`
- activa `Auto Generate Map From Camera`
- ajusta `mapWorldMinXZ` / `mapWorldMaxXZ`
- deja la camara en `Target Display 2` para no pisar la principal

### Opcion manual

En `MjpegTelemetryServer`:

- `Map Texture` (si tienes plano prehecho)
- `Map World Min XZ` / `Map World Max XZ`
- `Map Flip X` / `Map Flip Y` si sale espejado
- `Map Rotation Degrees` para orientar el mapa (`0`, `90`, `180`, `270`)

Prioridad de origen del mapa:

`Map Texture` -> `Map Capture Camera` -> rejilla procedural

## 5) Parametros importantes

### Captura / stream

- `Port`: puerto HTTP (`8080` por defecto)
- `Frame Width` / `Frame Height`: resolucion
- `FPS`: frames por segundo
- `JPG Quality`: calidad JPEG (1-100)

### Mapa

- `Map Rotation Degrees`: rota fondo + trayectoria en pasos de 90
- `Generated Map Width` / `Generated Map Height`: resolucion del mapa generado

## 6) Eventos de sesion

`SessionRecorder` registra:

- inicio de sesion
- historial de posicion
- eventos personalizados via `RecordEvent(...)`
- eventos XR de interaccion (select enter/exit)

En la web, los eventos pueden incluir nombre de objeto en formato `[obj: Nombre]`.

Si quieres hover XR tambien:

- activa `logXRHoverEvents` en `SessionRecorder`

Ejemplo de evento personalizado:

```csharp
SessionRecorder recorder = SessionRecorder.GetInstance();
if (recorder != null)
{
    recorder.RecordEvent("interaction", "Usuario pulso E", "PanelBotonAzul");
}
```

## 7) Endpoints HTTP

- `/` dashboard web
- `/frame.jpg` ultimo frame
- `/stream.mjpg` stream MJPEG
- `/state.json` snapshot actual
- `/stats.json` estadisticas + historial
- `/metrics.json` metricas de rendimiento
- `/map-config.json` configuracion y diagnostico de mapa
- `/map.png` imagen del mapa

Campos utiles en `/map-config.json`:

- `hasMap`
- `hasAssignedMapTexture`
- `mapSource` (`texture`, `camera`, `grid`, `none`)
- `mapMessage`

## 8) Uso en red

- En el mismo PC: `http://localhost:8080`
- En LAN: `http://<IP_PC_UNITY>:8080`
- Abre firewall para el puerto configurado



## 9) Checklist de validacion rapida

1. `http://localhost:8080` abre
2. `/frame.jpg` devuelve imagen
3. `/state.json` cambia al moverte
4. `/stats.json` crece en `positionHistory`
5. `/map-config.json` muestra `hasMap=true` o fallback `grid`

## 10) Troubleshooting

- Web abre pero no actualiza:
  - revisar puerto, firewall y URL
- `latencia` o `frames` en `-`:
  - verificar que `SessionRecorder` existe en escena
- No cambia `/state.json`:
  - verificar `UserTracker` activo y camara correcta
- Mapa no aparece:
  - revisar `/map-config.json` (`mapSource`, `mapMessage`)
  - si `mapSource=grid`, revisar `Map Texture` o `Map Capture Camera`
  - reiniciar Play tras cambios de mapa
- Recorrido desalineado:
  - recalibrar `Map World Min XZ` / `Map World Max XZ`
  - ajustar `Map Flip X` / `Map Flip Y`
  - ajustar `Map Rotation Degrees`
- Eventos sin nombre de objeto:
  - verificar interactables XR en escena
  - confirmar `SessionRecorder` activo


