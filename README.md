# 🎙️ Project Monolith: Modular Voice Co-Pilot Engine

Monolith es una arquitectura híbrida de alto rendimiento para copilotos de voz en tiempo real. **El proyecto nació originalmente como un prototipo en Python**, pero a medida que evolucionó, se hizo evidente que para alcanzar la funcionalidad, confiabilidad y eficiencia de milisegundo requeridas por un sistema de voz en tiempo real, el entorno interpretado se quedaba corto.

**Por ello, se está migrando la totalidad del núcleo orquestador a .NET 8 (C#)**. El objetivo es consolidar Monolith como un **programa 100% compilado y no interpretado**, eliminando las penalizaciones de rendimiento del intérprete en el hilo principal y aprovechando la velocidad de ejecución nativa, la seguridad de tipos y la gestión de memoria de C#. Python queda relegado exclusivamente a daemons de streaming aislados (como el codec Opus), mientras que el "cerebro" y la lógica de negocio del sistema son enteramente compilados.

---

## 🏗️ Arquitectura General del Sistema

El proyecto adopta un enfoque políglota estratégico, donde el núcleo compilado en C# actúa como el director de orquesta de alto rendimiento, delegando tareas específicas a binarios nativos o microservicios aislados:

```text
  audio de entrada
        │
        ▼
 ┌──────────────────┐      ┌────────────────────┐      ┌───────────────┐
 │ WebRTC VAD Engine │ ──▶ │  Google Speech STT   │ ──▶ │ Gemini LLM Core │
 └──────────────────┘      └────────────────────┘      └───────┬───────┘
                                                                  │ respuesta
                                                                  ▼
                                  ┌───────────────────────────────────────┐
                                  │        Estrategia de TTS / síntesis     │
                                  ├─────────────────────┬───────────────────┤
                                  │ Opus Streaming Daemon │  Native Engine   │
                                  │  (Python / FastAPI)   │ (C/C++, P/Invoke)│
                                  └─────────────────────┴───────────────────┘
                                                  │
                                                  ▼
                                          audio de salida
```

Todo el flujo de control, estado y concurrencia vive dentro del núcleo compilado en .NET 8. Entra audio por el VAD, se transcribe con Google Speech, Gemini genera la respuesta, y esa respuesta sale por uno de los dos motores de síntesis: el daemon Opus en Python o el motor nativo en C/C++.

---

## 🚀 Tecnologías y Características Clave

- **Núcleo Orquestador en .NET 8 (C#):** Aplicación 100% compilada que gestiona el ciclo de vida de la conversación, el estado de la memoria y la concurrencia asíncrona sin las sobrecargas de un intérprete.
- **VAD interno en tiempo real:** Integración con WebRTC VAD para detección precisa del habla, ajuste fino de pre/post-roll y prevención de falsos positivos en entornos con ruido.
- **Pipeline de streaming Opus:** Daemon desacoplado en Python (FastAPI/Uvicorn) que transmite audio codificado en Opus mediante paquetes binarios de bajo consumo de red.
- **Módulo nativo TTS (C/C++ / P/Invoke):** Motor nativo compilado con CMake, arquitectura asíncrona no bloqueante basada en callbacks C ABI, preparado para inferencia local vía vocoders ONNX.
- **Inyección de dependencias y resiliencia:** Arquitectura desacoplada en C# basada en interfaces, lista para intercambiar en caliente motores de voz y modelos LLM sin recompilar el núcleo.

---

## 🛠️ Estructura del repositorio

```text
Monolith.CSharp/              # Núcleo de producción 100% compilado en .NET 8
├── Monolith.Console/         # CLI, orquestación principal y punto de entrada
├── Monolith.Voice/           # Wrappers nativos, P/Invoke y audio pipelines
├── Monolith.Core.Tests/      # Suite de pruebas unitarias
└── native/                   # Código fuente C/C++ (CMake, P/Invoke)

Monolith.Python/              # Prototipo inicial y daemon de streaming Opus (Aislado)
```

---

## ⚙️ Configuración y puesta en marcha

**Requisitos previos:** .NET 8 SDK · Python 3.10+ *(solo para el daemon Opus)* · CMake (para el módulo nativo C++)

### 1. Núcleo en .NET 8 (Monolith.CSharp)

```bash
cd Monolith.CSharp
dotnet build Monolith.Console/Monolith.Console.csproj
dotnet run --project Monolith.Console/Monolith.Console.csproj
```

**Configuración:** Establece la variable de entorno `GEMINI_API_KEY` o edita `Monolith.Console/appsettings.json`.

### 2. Módulo de TTS en streaming (Opus Daemon - Python)

*Nota: Este paso es opcional si se utiliza el motor nativo C/C++.*

```bash
cd Monolith.Python
pip install fastapi uvicorn
uvicorn tts_opus_daemon:app --host 127.0.0.1 --port 5000
```

Activa `"UseTtsDaemon": true` en el `appsettings.json` de la CLI.

### 3. Compilación del módulo nativo C/C++ (P/Invoke)

```bash
cd Monolith.CSharp
cmake -S native -B native/build
cmake --build native/build
```

La librería compilada (DLL/SO) se ubica automáticamente en `runtimes/<platform>/native/` junto al ejecutable.

---

## 🎛️ Sintonización fina de VAD

El motor VAD expone métricas en tiempo real y parámetros ajustables para optimizar la latencia según el micrófono o entorno:

| Parámetro | Valor por defecto | Descripción |
| :--- | :--- | :--- |
| `UseVad` | `true` | Habilita la captura dinámica sin corte fijo de tiempo |
| `VadFrameMs` | `20` | Ventana del frame de audio (10, 20 o 30 ms) |
| `VadPreRollMs` | `500` | Buffer previo a la detección de voz, para no perder inicios |
| `VadPostRollMs` | `400` | Silencio requerido para determinar el fin de la frase |
| `VadMode` | `0` | Agresividad del algoritmo (0 = permisivo, 3 = estricto) |

**Diagnóstico rápido:**

- **Cortes prematuros:** Aumenta `VadPostRollMs` (600–800 ms) o reduce `VadMode`.
- **Falsos positivos por ruido:** Sube `VadMode` a 2 o 3 y ajusta `VadFrameMs` a 30.

---

## 🧪 Pruebas unitarias

```bash
cd Monolith.CSharp
dotnet test Monolith.Core.Tests/Monolith.Core.Tests.csproj
```

---

## 🗺️ Roadmap técnico

- [x] Prototipado e integración inicial de LLM en Python
- [x] **Decisión arquitectónica:** Migración del motor principal a .NET 8 para garantizar un núcleo 100% compilado
- [x] Implementación de soporte VAD e hilos asíncronos en C#
- [x] Módulo nativo C++ P/Invoke con soporte de callbacks y streaming Opus
- [ ] Integración de vocoder ONNX (HiFi-GAN) en la DLL nativa para síntesis neuronal local sin dependencia de la nube

---

## 📝 Nota sobre la evolución del proyecto

La decisión de migrar de Python a C# no fue trivial. Python ofreció rapidez de desarrollo inicial y un ecosistema maduro para prototipado de IA. Sin embargo, para un copiloto de voz en tiempo real donde cada milisegundo cuenta, donde la confiabilidad del sistema es crítica y donde se necesita control fino sobre la memoria y la concurrencia, un lenguaje compilado como C# era la elección correcta.

Hoy, Monolith es un sistema híbrido donde el núcleo orquestador es 100% compilado en C#, mientras que Python se mantiene como herramienta especializada para componentes específicos que benefician de su ecosistema (como el daemon de streaming Opus). Esta arquitectura políglota nos da lo mejor de ambos mundos: la velocidad y confiabilidad de C# en el hilo principal, y la flexibilidad de Python donde realmente aporta valor.
