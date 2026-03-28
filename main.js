import { dotnet } from './_framework/dotnet.js';

async function startApp() {
    try {
        const { runMain, getAssemblyExports, getConfig} = await dotnet
            .withDiagnosticTracing(false)
            .create();

        // On force le canvas pour Emscripten / EGL
        const canvasElement = document.getElementById('canvas');
        dotnet.instance.Module["canvas"] = canvasElement;

        // --- LA MAGIE EST ICI : On récupère les fonctions C# [JSExport] ---
        const exports = await getAssemblyExports(getConfig().mainAssemblyName);
        const interop = exports.Interop; // Si tu as mis un namespace, ce serait exports.TonNamespace.Interop

        function resizeCanvas() {
            // On prend la taille définie par ton CSS (800x600) multipliée par le zoom de l'écran
            const ratio = window.devicePixelRatio || 1.0;
            const displayWidth = canvasElement.clientWidth * ratio;
            const displayHeight = canvasElement.clientHeight * ratio;

            // On met à jour les pixels internes
            if (canvasElement.width !== displayWidth || canvasElement.height !== displayHeight) {
                canvasElement.width = displayWidth;
                canvasElement.height = displayHeight;
            }

            // On envoie cette bonne taille au C#
            interop.OnCanvasResize(displayWidth, displayHeight, ratio);
        }

        // 1. Redimensionnement de la fenêtre
        window.addEventListener('resize', resizeCanvas);

        // 2. Mouvements et clics de la souris
        canvasElement.addEventListener('mousemove', (e) => {
            // offsetX/Y donne la position relative au canvas (et non à l'écran)
            // On multiplie par le ratio pour correspondre aux vrais pixels internes du jeu
            const ratio = window.devicePixelRatio || 1.0;
            interop.OnMouseMove(e.offsetX * ratio, e.offsetY * ratio);
        });

        canvasElement.addEventListener('mousedown', (e) => {
            interop.OnMouseDown(e.shiftKey, e.ctrlKey, e.altKey, e.button);
        });
        canvasElement.addEventListener('mouseup', (e) => {
            interop.OnMouseUp(e.shiftKey, e.ctrlKey, e.altKey, e.button);
        });

        // 3. Clavier
        window.addEventListener('keydown', (e) => {
            interop.OnKeyDown(e.code);
        });
        window.addEventListener('keyup', (e) => {
            interop.OnKeyUp(e.code);
        });

        // Désactiver le clic droit du navigateur
        canvasElement.addEventListener('contextmenu', e => e.preventDefault());

        // On appelle le resize une première fois pour initialiser la bonne taille
        resizeCanvas();

        // On lance le jeu
        const loading = document.getElementById('loading');
        if (loading) loading.style.display = 'none';
        await runMain();

    } catch (err) {
        console.error("Erreur critique :", err);
    }
}
/*
// --- SYSTÈME DE COMPTEUR FPS INDÉPENDANT ---
const fpsElement = document.getElementById('fpsCounter');
let lastFpsTime = performance.now();
let frames = 0;

function measureFPS(currentTime) {
    frames++;
    // Si une seconde (1000 ms) s'est écoulée
    if (currentTime - lastFpsTime >= 1000) {
        if (fpsElement) fpsElement.innerText = `FPS: ${frames}`;
        frames = 0; // On remet le compteur à zéro
        lastFpsTime = currentTime;
    }
    // On reboucle à l'infini à la vitesse de rafraîchissement de l'écran
    requestAnimationFrame(measureFPS);
}
// On lance la boucle
requestAnimationFrame(measureFPS);*/

window.GameAudio = {
    sounds: {}, // Dictionnaire pour stocker les balises audio en cours

    play: function (id, path, loop, volume) {
        if (!this.sounds[id]) {
            this.sounds[id] = new Audio(path);
        }
        const audio = this.sounds[id];
        audio.loop = loop;
        audio.volume = volume;

        // Les navigateurs bloquent parfois l'audio si le joueur n'a pas encore cliqué
        audio.play().catch(e => console.warn("[AUDIO] Lecture bloquée par le navigateur (interaction requise) :", e));
    },

    pause: function (id) {
        if (this.sounds[id]) this.sounds[id].pause();
    },

    stop: function (id) {
        if (this.sounds[id]) {
            this.sounds[id].pause();
            this.sounds[id].currentTime = 0; // Remet à zéro
        }
    },

    setVolume: function (id, volume) {
        if (this.sounds[id]) this.sounds[id].volume = volume;
    }
};

startApp();