// On importe le module .NET généré lors de la compilation
import { dotnet } from './_framework/dotnet.js';

async function startApp() {
    try {
        // Configuration et initialisation du runtime Wasm
        const { getAssemblyExports, getConfig } = await dotnet
            .withDiagnosticTracing(false)
            .create();

        // On cache le texte de chargement une fois que c'est prêt
        document.getElementById('loading').style.display = 'none';

        // Lancement de ton Program.cs (MyGame.Run())
        await dotnet.run();
    } catch (err) {
        console.error("Erreur critique lors du chargement de l'application .NET :", err);
        document.getElementById('loading').innerText = "Erreur de chargement. Regardez la console (F12).";
    }
}

startApp();