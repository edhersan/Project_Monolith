// TYPEWRITER
let typeT = null;

// Escribe el texto letra por letra y actualiza quien habla.
export function typeText(text, col, name) {
    document.getElementById('dlg-speaker').textContent = name;
    document.getElementById('dlg-speaker').style.color = col;
    const el = document.getElementById('dlg-content');
    el.textContent = '';
    let i = 0;
    if (typeT) clearInterval(typeT);
    typeT = setInterval(() => {
        if (i < text.length) el.textContent += text[i++]; else clearInterval(typeT);
    }, 22);
}