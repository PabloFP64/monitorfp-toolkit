# MonitorFP Toolkit - Guia de Uso

Con este toolkit puedes exponer por HTTP:

### VR y AR:
- video en vivo (MJPEG)
- telemetria de posicion/rotacion
- estadisticas y eventos de sesion
- mapa 2D de recorrido (planta XZ)
- dashboard web listo para usar


## 1) Requisitos

- Unity 6 (probado en 6000.3.x)
- Una escena con camara del usuario (`Main Camera` o camara equivalente)
- Red local abierta si quieres verlo desde otro dispositivo

Notas:

- El paquete detecta automaticamente si `com.unity.xr.interaction.toolkit` esta disponible y habilita los eventos XR.
- Sin XR, sigue funcionando como telemetria, streaming, eventos de movimiento y observacion basicos.

## 2) Instalacion (elige una opcion)

Instalacion desde Git en Package Manager:

1. Unity -> Window -> Package Manager
2. `+` -> Add package from git URL
3. Instala este unico package:

- `https://github.com/PabloFP64/monitorfp-toolkit.git?path=/Packages/com.monitorfp.toolkit`

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

Setup automatico de escena:

`MonitorFP > Crear Setup Minimo en Escena`

Este comando crea automaticamente:

- `MonitorServer` con `MjpegTelemetryServer`
- `SessionRecorder` con `SessionRecorder`
- `UserTracker` con `UserTracker`
- asigna `Source Camera` en `MjpegTelemetryServer` cuando encuentra una camara
- deja `SessionRecorder` activo en escena

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
- eventos XR de interaccion (select enter/exit) si el paquete XR esta instalado

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

## 7) Seguimiento de InterestingGameObject

Para medir que objetos observa el usuario:

1. Anade el componente `InterestingGameObject` a cada objeto relevante.
2. Crea un GO `ObservationTracking` con:
  - `ObservationTrackingSettings`
  - `ObservationTracker`
3. En `ObservationTrackingSettings` configura:
  - `centerRegionFraction`: tamano de la region central de vision
  - `useLineOfSight`: valida linea de vision
  - `startMode`: auto al iniciar escena o manual desde web

Tambien puedes crear esto automaticamente desde:

`MonitorFP > Crear Setup Minimo en Escena`

En el dashboard web:

- Boton `Iniciar seguimiento observacion`
- Boton `Parar seguimiento observacion`
- Tabla `Interesting Objects` con:
  - primera vez visto (segundos desde inicio tracking)
  - tiempo total observado
  - veces detectado en zona central
  - porcentaje de tiempo en zona central

## 8) Endpoints HTTP

- `/` dashboard web
- `/frame.jpg` ultimo frame
- `/stream.mjpg` stream MJPEG
- `/state.json` snapshot actual
- `/stats.json` estadisticas + historial
- `/metrics.json` metricas de rendimiento
- `/map-config.json` configuracion y diagnostico de mapa
- `/map.png` imagen del mapa
- `/observation/start` inicia tracking manual
- `/observation/stop` para tracking manual
- `/observation/state` estado actual del tracking

Campos utiles en `/map-config.json`:

- `hasMap`
- `hasAssignedMapTexture`
- `mapSource` (`texture`, `camera`, `grid`, `none`)
- `mapMessage`

## 9) Uso en red

- En el mismo PC: `http://localhost:8080`
- En LAN: `http://<IP_PC_UNITY>:8080`
- Abre firewall para el puerto configurado



## 10) Checklist de validacion rapida

1. `http://localhost:8080` abre
2. `/frame.jpg` devuelve imagen
3. `/state.json` cambia al moverte
4. `/stats.json` crece en `positionHistory`
5. `/map-config.json` muestra `hasMap=true` o fallback `grid`

## 11) Troubleshooting

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
  - verificar interactables XR en escena si estas en VR
  - confirmar `SessionRecorder` activo


