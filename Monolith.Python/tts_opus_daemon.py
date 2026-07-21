from __future__ import annotations

import asyncio
import os
import struct
import subprocess
import sys

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import StreamingResponse

app = FastAPI(title="TTS Opus Daemon")

API_KEY = os.environ.get("TTS_DAEMON_API_KEY", "")
EDGE_TTS_CMD = os.environ.get("EDGE_TTS_CMD", "python -m edge_tts")
FFMPEG_CMD = os.environ.get("FFMPEG_CMD", "ffmpeg")
TTS_BITRATE = os.environ.get("TTS_BITRATE", "48k")


def check_auth(request: Request) -> None:
    if API_KEY:
        key = request.headers.get("X-Api-Key", "")
        if key != API_KEY:
            raise HTTPException(status_code=403, detail="Invalid API key")


async def generate_opus_stream(text: str, voice: str) -> bytes:
    tts_args = shlex_split(EDGE_TTS_CMD) + [
        "--voice", voice,
        "--text", text,
        "--write-media", "-",
    ]
    ffmpeg_args = [
        FFMPEG_CMD,
        "-i", "pipe:0",
        "-c:a", "libopus",
        "-b:a", TTS_BITRATE,
        "-vbr", "on",
        "-frame_duration", "20",
        "-f", "opus",
        "pipe:1",
    ]

    process_tts = await asyncio.create_subprocess_exec(
        *tts_args,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    process_ff = await asyncio.create_subprocess_exec(
        *ffmpeg_args,
        stdin=process_tts.stdout,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    process_tts.stdout.close()

    CHUNK = 4096
    try:
        while True:
            packet = await process_ff.stdout.read(CHUNK)
            if not packet:
                break
            yield struct.pack(">I", len(packet)) + packet
    finally:
        try:
            process_ff.kill()
        except Exception:
            pass
        try:
            process_tts.kill()
        except Exception:
            pass
        await process_ff.wait()
        await process_tts.wait()


@app.post("/stream")
async def stream(request: Request):
    check_auth(request)
    body = await request.json()
    text = body.get("text", "").strip()
    if not text:
        raise HTTPException(status_code=400, detail="text required")
    if len(text) > 5000:
        raise HTTPException(status_code=400, detail="text too long (max 5000)")
    voice = body.get("voice", "es-CO-GonzaloNeural")

    return StreamingResponse(
        generate_opus_stream(text, voice),
        media_type="application/octet-stream",
    )


@app.get("/health")
async def health():
    return {"status": "ok"}


def shlex_split(cmd: str) -> list[str]:
    import shlex as _shlex
    return _shlex.split(cmd)


if __name__ == "__main__":
    import uvicorn
    port = int(os.environ.get("TTS_DAEMON_PORT", "5000"))
    host = os.environ.get("TTS_DAEMON_HOST", "127.0.0.1")
    uvicorn.run(app, host=host, port=port, log_level="info")
