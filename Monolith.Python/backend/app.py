from __future__ import annotations

"""Nucleo de aplicacion de Monolith.

Este modulo mantiene la logica importable y testeable: recibe texto final,
delegia la generacion al proveedor LLM y expone una interfaz pequena para
la capa de entrada.
"""

import asyncio
from dataclasses import dataclass
from typing import Callable, Protocol
from typing import Optional


class LLMProvider(Protocol):
    def generate(self, prompt: str) -> str:
        ...


@dataclass(frozen=True)
class TranscriptEvent:
    text: str
    is_final: bool = False


class MonolithApp:
    def __init__(self, llm_provider: LLMProvider):
        self.llm_provider = llm_provider
        self._response_callback: Callable[[str], None] | None = None
        self.ws_server: Optional[any] = None

    def set_response_callback(self, callback: Callable[[str], None]) -> None:
        # Permite enganchar efectos secundarios sin acoplarlos al nucleo.
        self._response_callback = callback

    def set_ws_server(self, ws_server: any) -> None:
        self.ws_server = ws_server

    async def handle_transcript(self, event: TranscriptEvent) -> str | None:
        # Solo procesamos eventos ya cerrados para evitar respuestas parciales.
        if not event.is_final:
            return None

        prompt = (event.text or "").strip()
        if not prompt:
            return None

        print(f"[APP] Procesando input: {prompt}")

        response = self.llm_provider.generate(prompt)

        if self._response_callback is not None:
            self._response_callback(response)

        if self.ws_server is not None and response:
            print(f"[APP] Enviando WebSocket: {response}")
            asyncio.create_task(self.ws_server.broadcast({
                "speaker": "zael",
                "text": response
            }))

        return response


def create_app(llm_provider: LLMProvider | None = None) -> MonolithApp:
    # Si no se inyecta un proveedor, se construye el real con la configuracion local.
    if llm_provider is None:
        from backend.config import CONFIG
        from backend.llm import GeminiLLMProvider

        if not CONFIG.has_gemini_key:
            raise RuntimeError(
                "GEMINI_API_KEY no esta configurada en el entorno o en .env."
            )

        llm_provider = GeminiLLMProvider(model_name=CONFIG.model_name)

    return MonolithApp(llm_provider=llm_provider)


__all__ = ["MonolithApp", "TranscriptEvent", "create_app"]