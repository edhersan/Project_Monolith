from __future__ import annotations

"""Entrada y salida de audio para Monolith.

Aqui se concentran la grabacion por microfono, el reconocimiento de voz y
la sintesis TTS para no mezclar esas dependencias con el nucleo de la app.
"""

import io
import os
import time

import numpy as np
import edge_tts
import sounddevice as sd
import soundfile as sf
import speech_recognition as sr
import subprocess


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
        subprocess.run(["ffplay", "-nodisp", "-autoexit", "-loglevel", "quiet", output_file], check=True)
    except Exception as error:
        print(f"[Error al generar o reproducir voz]: {error}")


def escuchar_microfono_estable() -> str | None:
    """Captura audio con deteccion de voz: espera a que hables, graba hasta 3s de silencio."""
    sample_rate = 16000
    silence_limit = 3.0
    energy_threshold = 500
    chunk_duration = 0.3

    chunk_samples = int(chunk_duration * sample_rate)
    chunks: list[np.ndarray] = []
    speaking = False
    silence_start: float | None = None
    max_duration = 30.0
    start_time: float | None = None

    print("\n[Escuchando] Esperando que hables...")

    with sd.InputStream(samplerate=sample_rate, channels=1, dtype="int16") as stream:
        while True:
            chunk, _ = stream.read(chunk_samples)
            energy = int(np.abs(chunk).mean())

            if energy > energy_threshold:
                if not speaking:
                    print("[Escuchando] Te detecte hablando...")
                    speaking = True
                    start_time = time.monotonic()
                chunks.append(chunk.copy())
                silence_start = None
            elif speaking:
                chunks.append(chunk.copy())
                if silence_start is None:
                    silence_start = time.monotonic()
                elif time.monotonic() - silence_start >= silence_limit:
                    print("[Escuchando] Silencio detectado, procesando...")
                    break
                if time.monotonic() - start_time >= max_duration:
                    print("[Escuchando] Tiempo maximo alcanzado, procesando...")
                    break

    if not chunks:
        return None

    recording = np.concatenate(chunks)

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