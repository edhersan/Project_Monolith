from __future__ import annotations

"""Proveedor Gemini para la respuesta conversacional de Monolith."""

from google import genai
from google.genai import types

from backend.prompts import SYSTEM_PROMPT


class GeminiLLMProvider:
    def __init__(
        self,
        model_name: str = "gemini-2.5-flash",
        system_instruction: str = SYSTEM_PROMPT,
        temperature: float = 0.7,
    ):
        # Se crea una sola sesion de chat para conservar el contexto conversacional.
        self._client = genai.Client()
        self._chat = self._client.chats.create(
            model=model_name,
            config=types.GenerateContentConfig(
                system_instruction=system_instruction,
                temperature=temperature,
            ),
        )

    def generate(self, prompt: str) -> str:
        # Encapsula la llamada remota y normaliza una respuesta vacia si hace falta.
        response = self._chat.send_message(prompt)
        return response.text or ""