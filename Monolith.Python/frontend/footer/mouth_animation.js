// ANIMACION DE BOCA
let timers = { edhersan: null, zael: null };

// Cambia la forma/color de la boca segun personaje y estado (abierta/cerrada).
export function setMouth(who, open) {
    if (who === 'edhersan') {
        const m = document.getElementById('mouth-edhersan');
        m.style.height = open ? '8px' : '4px';
        m.style.top = open ? '40px' : '42px';
        m.style.background = open ? '#4a1008' : '#0a0604';
    } else {
        const m = document.getElementById('mouth-zael');
        m.style.height = open ? '7px' : '3px';
        m.style.top = open ? '42px' : '43px';
        m.style.background = open ? '#002810' : '#224433';
    }
}

// Anima boca y activa los dots de voz durante el tiempo indicado.
export function animMouth(who, dur) {
    const dotsEl = document.getElementById('dots-' + who);
    dotsEl.classList.add('on');
    if (timers[who]) clearInterval(timers[who]);
    let t = 0;
    timers[who] = setInterval(() => {
        t += 80;
        setMouth(who, Math.sin(t / 100) > 0);
        if (t >= dur) { clearInterval(timers[who]); setMouth(who, false); dotsEl.classList.remove('on'); }
    }, 80);
}