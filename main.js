import { dotnet } from './_framework/dotnet.js';

async function startApp() {
    try {
        const { runMain, getAssemblyExports, getConfig, instance } = await dotnet
            .withDiagnosticTracing(false)
            .create();

        // On force le canvas pour Emscripten / EGL
        const canvasElement = document.getElementById('canvas');
        instance.Module["canvas"] = canvasElement;

        // --- LA MAGIE EST ICI : On récupère les fonctions C# [JSExport] ---
        const exports = await getAssemblyExports(getConfig().mainAssemblyName);
        const interop = exports.Interop; // Si tu as mis un namespace, ce serait exports.TonNamespace.Interop

        // 1. Redimensionnement de la fenêtre
        window.addEventListener('resize', () => {
            interop.OnCanvasResize(window.innerWidth, window.innerHeight, window.devicePixelRatio);
        });

        // 2. Mouvements et clics de la souris
        canvasElement.addEventListener('mousemove', (e) => {
            interop.OnMouseMove(e.clientX, e.clientY);
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
        interop.OnCanvasResize(window.innerWidth, window.innerHeight, window.devicePixelRatio);

        // On lance le jeu
        const loading = document.getElementById('loading');
        if (loading) loading.style.display = 'none';
        await runMain();

    } catch (err) {
        console.error("Erreur critique :", err);
    }
}

startApp();