import { typeText } from './typewriter.js';
import { animMouth } from './mouth_animation.js';

// WEBSOCKET
// Conecta con backend local y actualiza UI al recibir mensajes.
let ws = null;
export function connectWS() {
    try {
        ws = new WebSocket('ws://localhost:8765');
        ws.onopen = () => {
            document.getElementById('ws-dot').classList.add('on');
            document.getElementById('ws-label').textContent = 'Zael V1 conectado';
        };
        ws.onmessage = (e) => {
            try {
                const d = JSON.parse(e.data);
                // speaker: "edhersan" o "zael"
                const who = d.speaker === 'edhersan' ? 'edhersan' : 'zael';
                const col = who === 'edhersan' ? '#aa88ff' : '#00ff88';
                const name = who === 'edhersan' ? 'edhersan' : 'Zael V1';
                const text = d.text || '';
                typeText(text, col, name);
                animMouth(who, Math.max(text.length * 55 + 400, 1500));
            } catch (err) { console.error('WS parse:', err); }
        };
        ws.onclose = () => {
            document.getElementById('ws-dot').classList.remove('on');
            document.getElementById('ws-label').textContent = 'WebSocket: reconectando...';
            setTimeout(connectWS, 3000);
        };
        ws.onerror = () => {
            document.getElementById('ws-label').textContent = 'WebSocket: sin servidor en :8765';
        };
    } catch (e) {
        document.getElementById('ws-label').textContent = 'WebSocket: error';
    }
}