from __future__ import annotations

import struct
from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from fastapi.testclient import TestClient

from tts_opus_daemon import app, API_KEY

client = TestClient(app)


def test_health():
    resp = client.get("/health")
    assert resp.status_code == 200
    assert resp.json() == {"status": "ok"}


def test_stream_empty_text():
    resp = client.post("/stream", json={"text": ""})
    assert resp.status_code == 400
    assert "text required" in resp.json()["detail"]


def test_stream_no_text():
    resp = client.post("/stream", json={})
    assert resp.status_code == 400


def test_stream_long_text():
    resp = client.post("/stream", json={"text": "x" * 5001})
    assert resp.status_code == 400
    assert "too long" in resp.json()["detail"]


def test_stream_auth_required():
    try:
        old_key = API_KEY
        import tts_opus_daemon as mod
        mod.API_KEY = "secret123"

        resp = client.post("/stream", json={"text": "hola"})
        assert resp.status_code == 403
        assert "Invalid" in resp.json()["detail"]

        resp = client.post("/stream", json={"text": "hola"}, headers={"X-Api-Key": "wrong"})
        assert resp.status_code == 403

        resp = client.post("/stream", json={"text": "hola"}, headers={"X-Api-Key": "secret123"})
        assert resp.status_code != 403
    finally:
        import tts_opus_daemon as mod
        mod.API_KEY = old_key


@pytest.mark.asyncio
async def test_generate_opus_stream_framing():
    from tts_opus_daemon import generate_opus_stream

    mock_process = MagicMock()
    mock_process.stdout = AsyncMock()

    fake_opus_data = b"\x00\x01\x02\x03" * 256  # ~1KB of fake opus data
    mock_process.stdout.read.return_value = fake_opus_data
    mock_process.wait = AsyncMock()

    async def mock_create_subprocess_exec(*args, **kwargs):
        return mock_process

    with patch("tts_opus_daemon.asyncio.create_subprocess_exec", mock_create_subprocess_exec):
        chunks = []
        async for chunk in generate_opus_stream("test", "test-voice"):
            chunks.append(chunk)

    assert len(chunks) > 0
    for chunk in chunks:
        assert len(chunk) >= 4
        (packet_len,) = struct.unpack(">I", chunk[:4])
        assert packet_len == len(chunk) - 4
        assert chunk[4:] == fake_opus_data[:packet_len]
