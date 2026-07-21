# VAD (Voice Activity Detection) - Monolith.CSharp

El sistema VAD captura voz en tiempo real usando WebRTC VAD nativo (via
[WebRtcVadSharp](https://www.nuget.org/packages/WebRtcVadSharp/)) + Google
Speech API, con fallback por RMS energy.

## Arquitectura

```text
NaudioVadCapture        Captura NAudio WaveInEvent, raw PCM 16kHz 16-bit mono
       |
       v
CircularAudioBuffer     Buffer circular ring (~30s) con pre-roll
       |
       v
WebRtcVadDetector       VAD real (WebRtcVadSharp) + fallback RMS energy
       |
       v
VadCaptureService       State machine: Idle → Speech → Flushing
       |
       ├─► OnUtteranceReady(byte[] pcm16k16bit)   ← evento público
       └─► GoogleSpeechSTT.RecognizeAsync()        ← transcripción
```

## Parámetros en `appsettings.json`

| Parámetro | Default | Descripción |
|-----------|---------|-------------|
| `UseVad` | `true` | Activa/desactiva VAD. Si `false`, vuelve a grabado fijo de `RecordingDurationSeconds` segundos |
| `VadFrameMs` | `20` | Tamaño de frame VAD: `10`, `20`, o `30` ms |
| `VadPreRollMs` | `500` | Audio previo incluido antes de detectar voz (ms) |
| `VadPostRollMs` | `400` | Silencio sostenido para considerar frase terminada (ms) |
| `VadMode` | `0` | `0` (mínima agresividad, más permisivo) a `3` (máxima agresividad) |
| `VadRmsFallbackThreshold` | `0.0008` | Umbral RMS si el VAD nativo no está disponible |

## Tuning

**Cortes prematuros** (frase se corta antes de terminar):
- Aumentar `VadPostRollMs` (ej: 800-1200ms)
- Bajar `VadMode` a `0`

**Mucho silencio antes de detectar** (se pierde inicio de frase):
- Aumentar `VadPreRollMs` (ej: 700-1000ms)
- Bajar `VadMode`

**Falsos positivos** (detecta voz cuando no hay):
- Subir `VadMode` a `2` o `3`
- Aumentar `VadFrameMs` a `30`

**Latencia alta de STT**:
- Reducir `VadPostRollMs` (mínimo 100ms)
- Verificar respuesta de Google Speech API

## Integración con Google Speech

`VadCaptureService` implementa `ISTTService` y se usa igual que el
`GoogleSpeechSTT` directo. El main loop puede suscribirse al evento
`OnUtteranceReady` para recibir el raw PCM de la utterance:

```csharp
var vadService = new VadCaptureService(stt, vad, config);
vadService.OnUtteranceReady += pcmData => {
    File.WriteAllBytes("utterance.raw", pcmData);
};
```

## Métricas en runtime

Cada 5 utterances se imprimen métricas VAD automáticamente:

```
VAD Metrics:
  Utterances: 12 (false: 1)
  Avg length: 2340ms
  Avg STT latency: 850ms
  Speech ratio: 32.5%
  Frames: 15000 (speech=4875, silence=10125)
```

### Tests

```bash
cd Monolith.CSharp
dotnet test Monolith.Core.Tests/Monolith.Core.Tests.csproj
```
