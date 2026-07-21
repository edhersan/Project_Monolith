import sys
import os

# asegurar que el directorio raíz del proyecto esté en sys.path
sys.path.append(os.path.dirname(os.path.dirname(__file__)))

from backend.app import create_app, TranscriptEvent

class DummyLLM:
    def generate(self, prompt: str) -> str:
        return "Respuesta de prueba: " + prompt.splitlines()[-1][:120]


def on_resp(r):
    print("RESPUESTA:", r)

app = create_app(llm_provider=DummyLLM())
app.set_response_callback(on_resp)
app.handle_transcript(TranscriptEvent(text="hola monolith presentate", is_final=True))
