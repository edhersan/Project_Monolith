from __future__ import annotations

"""Servidor WebSocket para overlays (por ejemplo, footer en OBS)."""

import asyncio
import json
from typing import Any

import websockets


class FooterWSServer:
    """Mantiene un servidor WS simple y la lista de clientes conectados."""

    def __init__(self, host: str = "localhost", port: int = 8765):
        self.host = host
        self.port = port
        self._server: Any | None = None
        self._clients: set[Any] = set()

    @property
    def connected_clients(self) -> int:
        return len(self._clients)

    async def _handler(self, websocket: Any):
        self._clients.add(websocket)
        try:
            await websocket.wait_closed()
        finally:
            self._clients.discard(websocket)

    async def start(self) -> None:
        if self._server is not None:
            return

        self._server = await websockets.serve(
            self._handler,
            self.host,
            self.port,
            ping_interval=20,
            ping_timeout=20,
        )

    async def stop(self) -> None:
        clients = list(self._clients)
        self._clients.clear()

        if clients:
            await asyncio.gather(
                *(client.close() for client in clients),
                return_exceptions=True,
            )

        if self._server is None:
            return

        self._server.close()
        await self._server.wait_closed()
        self._server = None

    async def broadcast(self, payload: dict[str, Any]) -> None:
        if not self._clients:
            print(f"[WS] No hay clientes conectados para enviar mensaje: {payload}")
            return

        message = json.dumps(payload, ensure_ascii=False)
        clients = list(self._clients)

        print(f"[WS] Enviando mensaje a {len(clients)} clientes: {message[:100]}...")

        results = await asyncio.gather(
            *(client.send(message) for client in clients),
            return_exceptions=True,
        )

        for client, result in zip(clients, results):
            if isinstance(result, Exception):
                self._clients.discard(client)
