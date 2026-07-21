# Monolith.CSharp — Resumen del proyecto

Refactorización completa del sistema monolítico Python a C# .NET 8, manteniendo la misma funcionalidad de STT + TTS + LLM.

## Stack

| Componente | Librería |
|------------|----------|
| Runtime | .NET 8, C# 12 |
| STT | Google Cloud Speech API (+ WebRTC VAD nativo en C#) |
| TTS | Sistema por prioridad: SystemSpeech → ONNX (char-level) → ONNX (Piper/fonético) → Native C++ |
| LLM | Google Gemini API |
| VAD | WebRTC VAD (pure C#, sin Python) |
| Audio | NAudio (captura y playback) |
| Transcoding | FFmpeg (WAV → FLAC) |
| WebSocket | Servicio de streaming bidireccional |
| Native | C++/CMake + P/Invoke para TTS nativo |

## TTS Pipeline (prioridad)

1. **TtsSystemSpeechService** — Windows SAPI (System.Speech), fallback por defecto.
2. **TtsOnnxService** — Modelos ONNX (Coqui VITS / Piper) en subdirectorios de `./Models/`.
   - **Scanner automático**: busca `model.onnx` + `config.json` en subcarpetas.
   - **Detección automática**: si `config.json` tiene `phoneme_id_map` → modelo fonético (Piper). Si tiene `characters` → char-level (Coqui VITS).
   - **Char-level**: tokenizador dinámico desde `config.json`, soporta `add_blank`.
   - **Fonético**: llama `espeak-ng.exe --ipa` para obtener IPA, mapea a IDs del modelo.
   - Selección por `TtsOnnxSelectedVoice` en `appsettings.json`.
3. **TtsNativeService** — Módulo C++ nativo (senoidal prototipo), preparado para vocoder ONNX + Opus.

## espeak-ng (fonetizador)

Integrado para modelos Piper que requieren phoneme input.

- Binarios: `runtimes/win-x64/native/espeak-ng.exe` + `libespeak-ng.dll` + `espeak-ng-data/`
- Copia automática al output en build (target `CopyEspeakNg` en `.csproj`)
- `TtsPhonemizerService` — wrapper vía subprocess con parseo de IPA Unicode
- Soporta múltiples voces espeak (`es`, `es-mx`, `en`, etc.) desde `config.json`

## Modelos descargados

| Modelo | Tipo | Tamaño | Sample Rate | Descripción |
|--------|------|--------|-------------|-------------|
| `proxectonos-celtia` | Char-level (VITS) | 131 MB | 16 kHz | Gallego con español |
| `es_MX-claude-high` | Fonético (Piper) | 63 MB | 22 kHz | Español mexicano, voz `es-419` |

## Módulo nativo C++

- `native/tts_native.cpp` — API C ABI con threading + callbacks
- Prototipo senoidal (reemplazable por HiFi-GAN ONNX)
- Build: `cmake -S native -B native/build && cmake --build native/build`
- P/Invoke: `Monolith.Voice/Native/NativeMethods.cs`
- Managed wrapper: `Monolith.Voice/Native/TtsNativeService.cs`

## VAD

- Parámetros ajustables en `appsettings.json`: `VadFrameMs`, `VadPreRollMs`, `VadPostRollMs`, `VadMode`
- Métricas en runtime cada 5 utterances
- Fallback a grabado fijo si `UseVad: false`

## Tests

- 51 tests, todos pasan
- `dotnet test Monolith.Core.Tests/Monolith.Core.Tests.csproj`

## Build

```bash
dotnet build Monolith.Console/Monolith.Console.csproj
dotnet run --project Monolith.Console/Monolith.Console.csproj
```

Variables de entorno: `GEMINI_API_KEY`, `GOOGLE_APPLICATION_CREDENTIALS`.

## Pendiente / Roadmap

- [ ] Voice cloning: XTTS-v2 ONNX, OpenVoice v2 ONNX, CosyVoice 3 ONNX (investigado)
- [ ] Vocoder ONNX en módulo nativo (reemplazar senoidal por HiFi-GAN)
- [ ] Codificación Opus nativa en C++
- [ ] Más voces Piper latinas (es_AR-daniela-high, es_MX-lilith2, etc.)
- [ ] Voces de personajes (GLaDOS, BT-7274, Picard) vía espeak-ng
