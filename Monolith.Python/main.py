from __future__ import annotations

import asyncio
import io
import os
import sys
import threading
import webbrowser
from http.server import HTTPServer, SimpleHTTPRequestHandler

from google.genai.errors import APIError

from backend.app import TranscriptEvent, create_app
from backend.voice import hablar_monolith, escuchar_microfono_estable
from backend.ws_server import FooterWSServer


def configure_console() -> None:
    # Asegura salida UTF-8 consistente en Windows para que no se rompan acentos ni símbolos.
    os.environ.setdefault("PYTHONIOENCODING", "utf-8")

    if hasattr(sys.stdout, "buffer"):
        sys.stdout = io.TextIOWrapper(
            sys.stdout.buffer,
            encoding="utf-8",
            errors="backslashreplace",
            line_buffering=True,
        )
    if hasattr(sys.stderr, "buffer"):
        sys.stderr = io.TextIOWrapper(
            sys.stderr.buffer,
            encoding="utf-8",
            errors="backslashreplace",
            line_buffering=True,
        )


def safe_print(*args, sep=" ", end="\n"):
    # Reemplaza print para escribir de forma segura en la consola actual.
    texto = sep.join(str(arg) for arg in args) + end
    stream = getattr(sys.stdout, "buffer", None)
    if stream is not None:
        stream.write(texto.encode("utf-8", errors="backslashreplace"))
        stream.flush()
        return
    sys.stdout.write(texto)
    sys.stdout.flush()


print = safe_print


async def main_loop() -> None:
    # Ciclo principal: escucha, interpreta y responde hasta que el usuario salga.
    print("Iniciando Monolith con voz y playsound...")
    ws_server = FooterWSServer(host="localhost", port=8765)

    try:
        await ws_server.start()
        print("[WS] Servidor WebSocket activo en ws://localhost:8765")
        print(f"[WS] Clientes conectados: {ws_server.connected_clients}")
    except Exception as error:
        print(f"[WS] No se pudo iniciar el servidor WebSocket: {error}")

    try:
        # Crea la app con su proveedor LLM real; si falta configuracion, aborta limpio.
        app = create_app()
    except Exception as error:
        print(f"[ERROR INICIALIZACION]: {error}")
        await ws_server.stop()
        return

    app.set_ws_server(ws_server)
    print(f"[WS] ws_server inyectado en app. Clientes conectados: {ws_server.connected_clients}")

    footer_dir = os.path.join(os.path.dirname(__file__), "frontend", "footer")

    class FooterHandler(SimpleHTTPRequestHandler):
        def __init__(self, *args, **kwargs):
            super().__init__(*args, directory=footer_dir, **kwargs)

        def log_message(self, format, *args):
            pass

    http_server = HTTPServer(("localhost", 8080), FooterHandler)
    threading.Thread(target=http_server.serve_forever, daemon=True).start()
    print("[HTTP] Servidor frontend en http://localhost:8080/footer.html")

    webbrowser.open("http://localhost:8080/footer.html")
    print("[Navegador] Footer abierto en el navegador")

    try:
        while True:
            try:
                # Captura una frase completa desde el micrófono en un hilo para no bloquear el event loop.
                user_input = await asyncio.to_thread(escuchar_microfono_estable)

                if not user_input or not user_input.strip():
                    continue

                # Permite salir con una orden explícita del usuario.
                if "salir" in user_input.lower():
                    print("Monolith fuera.")
                    break

                # Envía el texto de edhersan al frontend para animar su avatar.
                if ws_server.connected_clients:
                    await ws_server.broadcast({
                        "speaker": "edhersan",
                        "text": user_input
                    })

                # Convierte el texto detectado en un evento final para el núcleo de la app.
                response = await app.handle_transcript(
                    TranscriptEvent(text=user_input, is_final=True)
                )
                if response is None:
                    continue

                # Imprime la respuesta y la convierte a voz.
                print(f"Monolith (Texto): {response}")
                await hablar_monolith(response)

            except APIError as error:
                fallback = "Ese server de IA volvió a fallar, repite lo que dijiste."
                if error.code == 429:
                    fallback = "El server está saturado, dame un respiro y repite."
                print(f"\n[API ERROR]: Codigo {error.code} - {error.message}")
                if ws_server.connected_clients:
                    await ws_server.broadcast({"speaker": "zael", "text": fallback})
                await hablar_monolith(fallback)
            except KeyboardInterrupt:
                print("\nMonolith fuera.")
                break
            except Exception as error:
                fallback = "Ese server de IA volvió a fallar, repite lo que dijiste."
                print(f"\n[ERROR INESPERADO]: {error}")
                if ws_server.connected_clients:
                    await ws_server.broadcast({"speaker": "zael", "text": fallback})
                await hablar_monolith(fallback)
    finally:
        http_server.shutdown()
        await ws_server.stop()


def main() -> None:
    # Punto de entrada de consola: normaliza la salida y arranca el loop asíncrono.
    configure_console()
    try:
        asyncio.run(main_loop())
    except KeyboardInterrupt:
        print("\nMonolith fuera.")


if __name__ == "__main__":
    main()