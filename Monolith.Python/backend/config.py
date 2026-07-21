import os
from dataclasses import dataclass

"""Carga configuracion de entorno para backend.

Se soporta python-dotenv, pero el modulo tambien funciona con un cargador
minimalista para no depender de esa libreria en entornos simples.
"""

# Intentamos usar python-dotenv; si no está disponible, proporcionamos
# una implementación mínima para cargar variables desde un archivo .env
try:
    from dotenv import load_dotenv
except Exception:
    def load_dotenv(dotenv_path=None, override=False):
        """Carga variables desde un archivo .env simple (clave=valor).
        Esta implementación mínima ignora comentarios y líneas vacías.
        """
        path = dotenv_path or os.path.join(os.path.dirname(os.path.dirname(__file__)), ".env")
        if not os.path.exists(path):
            return False
        with open(path, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                if "=" not in line:
                    continue
                key, val = line.split("=", 1)
                key = key.strip()
                val = val.strip().strip('"').strip("'")
                if override or key not in os.environ:
                    os.environ[key] = val
        return True


@dataclass(frozen=True)
class AppConfig:
    gemini_api_key: str | None
    model_name: str = "gemini-2.5-flash"
    whisper_model: str = "small"
    whisper_device: str = "cuda"
    whisper_compute_type: str = "float16"

    @property
    def has_gemini_key(self) -> bool:
        return bool(self.gemini_api_key)


def load_config() -> AppConfig:
    # El .env vive en la raiz del proyecto, por eso subimos un nivel desde backend/.
    ruta_env = os.path.join(os.path.dirname(os.path.dirname(__file__)), ".env")
    load_dotenv(dotenv_path=ruta_env)
    return AppConfig(
        gemini_api_key=os.getenv("GEMINI_API_KEY"),
        model_name=os.getenv("GEMINI_MODEL_NAME", "gemini-2.5-flash"),
        whisper_model=os.getenv("WHISPER_MODEL", "small"),
        whisper_device=os.getenv("WHISPER_DEVICE", "cuda"),
        whisper_compute_type=os.getenv("WHISPER_COMPUTE_TYPE", "float16"),
    )


CONFIG = load_config()