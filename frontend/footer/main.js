import { typeText } from './typewriter.js';
import { animMouth } from './mouth_animation.js';
import { startIdleBob } from './idle_bob.js';
import { connectWS } from './websocket_client.js';

document.addEventListener('DOMContentLoaded', () => {
    // IDLE BOB
    startIdleBob();

    // BOOT
    // Mensaje inicial antes de recibir eventos por WebSocket.
    setTimeout(() => {
        typeText('"Zael V1 online. Listo para el stream, carnal. Arrancamos?"', '#00ff88', 'Zael V1');
        animMouth('zael', 3400);
    }, 400);

    connectWS();
});