using System;
using System.Runtime.InteropServices.JavaScript;

public static partial class AudioManager
{
    public static void Initialize()
    {
        Console.WriteLine("[AUDIO] Moteur Audio HTML5 initialisé !");
    }

    public static void Dispose()
    {
        // Plus rien à nettoyer côté C#, le Garbage Collector JS gérera les balises <audio>
    }

    // --- JS INTEROP : Appelle les fonctions de window.GameAudio ---

    [JSImport("globalThis.GameAudio.play")]
    public static partial void Play(string id, string path, bool loop, float volume);

    [JSImport("globalThis.GameAudio.pause")]
    public static partial void Pause(string id);

    [JSImport("globalThis.GameAudio.stop")]
    public static partial void Stop(string id);

    [JSImport("globalThis.GameAudio.setVolume")]
    public static partial void SetVolume(string id, float volume);
}