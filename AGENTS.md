<!-- headroom:rtk-instructions -->
# RTK (Rust Token Killer) - Token-Optimized Commands

When running shell commands, **always prefix with `rtk`**. This reduces context
usage by 60-90% with zero behavior change. If rtk has no filter for a command,
it passes through unchanged — so it is always safe to use.

## Key Commands
```bash
# Git (59-80% savings)
rtk git status          rtk git diff            rtk git log

# Files & Search (60-75% savings)
rtk ls <path>           rtk read <file>         rtk grep <pattern>
rtk find <pattern>      rtk diff <file>

# Test (90-99% savings) — shows failures only
rtk pytest tests/       rtk cargo test          rtk test <cmd>

# Build & Lint (80-90% savings) — shows errors only
rtk tsc                 rtk lint                rtk cargo build
rtk prettier --check    rtk mypy                rtk ruff check

# Analysis (70-90% savings)
rtk err <cmd>           rtk log <file>          rtk json <file>
rtk summary <cmd>       rtk deps                rtk env

# GitHub (26-87% savings)
rtk gh pr view <n>      rtk gh run list         rtk gh issue list

# Infrastructure (85% savings)
rtk docker ps           rtk kubectl get         rtk docker logs <c>

# Package managers (70-90% savings)
rtk pip list            rtk pnpm install        rtk npm run <script>
```

## Rules
- In command chains, prefix each segment: `rtk git add . && rtk git commit -m "msg"`
- For debugging, use raw command without rtk prefix
- `rtk proxy <cmd>` runs command without filtering but tracks usage
<!-- /headroom:rtk-instructions -->

# Project Monolith

Proyecto dividido en dos implementaciones separadas.

## Estructura

```
Monolith.Python/        - Versión original en Python
Monolith.CSharp/        - Versión refactorizada en C# .NET 8
```

---

## Monolith.Python (original)

```bash
cd Monolith.Python
pip install -r requirements.txt
python main.py
```

## Monolith.CSharp (.NET 8)

```bash
cd Monolith.CSharp
dotnet build Monolith.Console/Monolith.Console.csproj
dotnet run --project Monolith.Console/Monolith.Console.csproj
```

Editar `Monolith.Console/appsettings.json` con tus keys.

O usar variables de entorno: `GEMINI_API_KEY`.

---

## VAD (Voice Activity Detection) - Tuning Tips

El sistema VAD captura voz en tiempo real usando WebRTC VAD + Google Speech API.

### Parámetros en `appsettings.json`

| Parámetro | Default | Descripción |
|-----------|---------|-------------|
| `UseVad` | `true` | Activa/desactiva VAD. Si es `false`, vuelve al grabado fijo de `RecordingDurationSeconds` segundos |
| `VadFrameMs` | `20` | Tamaño de frame para VAD. Valores: `10`, `20`, o `30` ms |
| `VadPreRollMs` | `500` | Audio previo incluido antes de detectar voz (ms) |
| `VadPostRollMs` | `400` | Silencio sostenido para considerar que la frase terminó (ms) |
| `VadMode` | `0` | Agresividad VAD: `0` (más permisivo) a `3` (más estricto) |

### Cómo tunear

**Cortes prematuros** (la frase se corta antes de terminar):
- Aumentar `VadPostRollMs` (ej: 600-800ms)
- Bajar `VadMode` a `0` o `1`

**Mucho silencio antes de detectar** (se pierde el inicio de la frase):
- Aumentar `VadPreRollMs` (ej: 700-1000ms)
- Bajar `VadMode`

 **Falsos positivos** (detecta voz cuando no hay):
- Subir `VadMode` a `2` o `3`
- Aumentar `VadFrameMs` a `30`
- Revisar ruido de fondo (ventiladores, teclado)

**Latencia alta de STT**:
- Reducir `VadPostRollMs` (mínimo 100ms)
- Verificar respuesta de Google Speech API

### Métricas en runtime

Cada 5 utterances se imprimen métricas VAD:
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

---

## Opus Streaming TTS

Architectura de streaming de baja latencia: daemon Python + cliente C# con Opus.

### Daemon (Python)

```bash
cd Monolith.Python
pip install fastapi uvicorn
uvicorn tts_opus_daemon:app --host 127.0.0.1 --port 5000
```

Variables de entorno: `TTS_DAEMON_API_KEY`, `TTS_DAEMON_PORT`, `TTS_DAEMON_HOST`, `TTS_BITRATE` (default `48k`).

### Cliente (C# .NET 8)

Se activa con `"UseTtsDaemon": true` en `appsettings.json`.

| Parámetro | Default | Descripción |
|-----------|---------|-------------|
| `UseTtsDaemon` | `false` | Usar daemon Opus en vez de edge-tts directo |
| `TtsDaemonUrl` | `http://127.0.0.1:5000` | URL del daemon |
| `TtsDaemonVoice` | `es-CO-GonzaloNeural` | Voz TTS |
| `TtsDaemonApiKey` | `""` | API key opcional para `X-Api-Key` |
| `TtsDaemonBufferMs` | `300` | Buffer prefill antes de playback |

### Protocolo de streaming

Cada paquete: **4 bytes big‑endian** = longitud N → **N bytes** = paquete Opus raw.

### Cómo probar

```bash
# Terminal 1: daemon
cd Monolith.Python
uvicorn tts_opus_daemon:app --host 127.0.0.1 --port 5000

# Terminal 2: test con curl
curl -X POST http://127.0.0.1:5000/stream \
  -H "Content-Type: application/json" \
  -d '{"text":"Hola mundo","voice":"es-CO-GonzaloNeural"}' \
  -o /dev/null -w "bytes=%{size_download} time=%{time_total}s\n"
```

---

## Native TTS (C/C++ P/Invoke)

Módulo TTS nativo con API C ABI, threading y callbacks. Prototipo senoidal listo para reemplazar con vocoder ONNX.

### Archivos

| Archivo | Descripción |
|---------|-------------|
| `native/tts_native.h` | API C ABI header |
| `native/tts_native.cpp` | Prototipo senoidal + threading + callbacks |
| `native/CMakeLists.txt` | Build CMake multiplataforma |
| `Monolith.Voice/Native/NativeMethods.cs` | P/Invoke signatures |
| `Monolith.Voice/Native/TtsNativeService.cs` | Managed wrapper (Channel + NAudio) |

### API C ABI

```c
TtsHandle tts_create(const TtsConfig* cfg, OnOpusPacket packet_cb, OnLog log_cb, void* user);
int tts_speak_async(TtsHandle h, const char* text, const char* style, int utterance_id);
int tts_stop(TtsHandle h, int utterance_id);
char* tts_get_metrics(TtsHandle h);
void tts_free_string(char* s);
void tts_destroy(TtsHandle h);
```

- `packet_cb` se invoca con cada frame PCM (20ms, 48kHz, 16-bit).
- `packet_cb(nullptr, 0, user)` señala fin de síntesis.
- `tts_speak_async` es no bloqueante; el síntesis corre en su propio hilo.

### Configuración en `appsettings.json`

| Parámetro | Default | Descripción |
|-----------|---------|-------------|
| `UseTtsNative` | `false` | Usar módulo nativo en vez de edge-tts |
| `TtsNativeModelPath` | `""` | Ruta al modelo (opcional en prototipo) |
| `TtsNativeSampleRate` | `48000` | Sample rate de síntesis |
| `TtsNativeChannels` | `1` | Canales (mono) |
| `TtsNativeOpusBitrate` | `48000` | Bitrate Opus (reservado) |
| `TtsNativeMaxConcurrency` | `1` | Síntesis concurrentes máximas |

### Cómo compilar la librería nativa

```bash
cd Monolith.CSharp
cmake -S native -B native/build
cmake --build native/build
```

La DLL se copia a `Monolith.Console/bin/Debug/net8.0/runtimes/<platform>/native/`.

### Test rápido

```powershell
dotnet test Monolith.Core.Tests/Monolith.Core.Tests.csproj
```

### Roadmap

1. **Prototipo** (actual) — senoidal + threading + P/Invoke wrapper
2. **Vocoder ONNX** — integrar HiFi‑GAN para voz realista
3. **Opus nativo** — codificar a Opus en C++ en vez de enviar PCM
```
