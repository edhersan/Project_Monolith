from __future__ import annotations

"""Entrada y salida de audio para Monolith.

Aqui se concentran la grabacion por microfono, el reconocimiento de voz y
la sintesis TTS para no mezclar esas dependencias con el nucleo de la app.
"""

import io
import os

import edge_tts
import sounddevice as sd
import soundfile as sf
import speech_recognition as sr
from playsound import playsound


async def hablar_monolith(texto: str) -> None:
    """Genera la voz con Edge-TTS y la reproduce."""
    voz = "es-CO-GonzaloNeural"
    # Guardamos en la raiz del proyecto para reutilizar el mismo archivo temporal.
    output_file = os.path.join(os.path.dirname(os.path.dirname(__file__)), "monolith_voice.mp3")

    try:
        if os.path.exists(output_file):
            try:
                os.remove(output_file)
            except OSError:
                pass

        communicate = edge_tts.Communicate(texto, voz)
        await communicate.save(output_file)

        print("[Hablando...]")
        playsound(output_file)
    except Exception as error:
        print(f"[Error al generar o reproducir voz]: {error}")


def escuchar_microfono_estable() -> str | None:
    """Captura audio usando sounddevice y lo procesa."""
    sample_rate = 16000
    duration = 5

    # Se fija una duracion corta y predecible para simplificar la captura.
    print("\n[Escuchando] Habla ahora (Grabando 5 segundos)...")
    recording = sd.rec(int(duration * sample_rate), samplerate=sample_rate, channels=1, dtype="int16")
    sd.wait()

    print("[Procesando voz...]")
    buffer_audio = io.BytesIO()
    sf.write(buffer_audio, recording, sample_rate, format="WAV", subtype="PCM_16")
    buffer_audio.seek(0)

    recognizer = sr.Recognizer()
    with sr.AudioFile(buffer_audio) as source:
        audio_data = recognizer.record(source)
        try:
            text = recognizer.recognize_google(audio_data, language="es-CO")
            print(f"Tu dijiste: {text}")
            return text
        except sr.UnknownValueError:
            print("[?] No entendi bien lo que dijiste, intenta de nuevo.")
            return None
        except sr.RequestError as error:
            print(f"[Error STT]: {error}")
            return None