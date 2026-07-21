// IDLE BOB
// Movimiento idle continuo para dar vida a ambos avatares.
let idleT = 0;
export function startIdleBob() {
    (function idleBob() {
        idleT += 0.04;
        document.getElementById('head-edhersan').style.transform = `translateY(${Math.sin(idleT) * 2}px)`;
        document.getElementById('head-zael').style.transform = `translateY(${Math.sin(idleT + Math.PI) * 2}px)`;
        requestAnimationFrame(idleBob);
    })();
}