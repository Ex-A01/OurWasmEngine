using MeltySynth;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenAL;
using System.Runtime.InteropServices;

public static class AudioManager
{
    private static ALContext _alc;

    // L'API AL publique !
    public static AL AL { get; private set; }

    private static unsafe Device* _device;
    private static unsafe Context* _context;

    // --- NOUVEAU : Contexte spécial pour le WebAssembly ---
    // Ce contexte empêche Silk.NET de chercher une DLL et utilise directement Emscripten
    private class WebALContext : INativeContext
    {
        [DllImport("openal32", EntryPoint = "alGetProcAddress")]
        private static extern IntPtr AlGetProcAddress(string procName);

        [DllImport("openal32", EntryPoint = "alcGetProcAddress")]
        private static extern IntPtr AlcGetProcAddress(IntPtr device, string procName);

        public IntPtr GetProcAddress(string proc, int? slot = null)
        {
            // Si la fonction commence par "alc", on appelle le loader spécifique ALC
            if (proc.StartsWith("alc", StringComparison.OrdinalIgnoreCase))
            {
                return AlcGetProcAddress(IntPtr.Zero, proc);
            }
            // Sinon, c'est une fonction AL classique
            return AlGetProcAddress(proc);
        }

        public bool TryGetProcAddress(string proc, out IntPtr addr, int? slot = null)
        {
            addr = GetProcAddress(proc, slot);
            return addr != IntPtr.Zero;
        }

        public void Dispose() { }
    }

    public static unsafe void Initialize()
    {
        // On crée notre contexte WebALContext qui fait le pont avec Emscripten
        var webContext = new WebALContext();

        // CORRECTION : On utilise le constructeur classique au lieu de GetApi() !
        _alc = new ALContext(webContext);
        AL = new AL(webContext);

        // On ouvre le périphérique par défaut (la carte son du navigateur)
        _device = _alc.OpenDevice(null);
        if (_device == null)
        {
            Console.WriteLine("[AUDIO] Erreur critique : Impossible de trouver une carte son.");
            return;
        }

        // On crée et active le contexte audio
        _context = _alc.CreateContext(_device, null);
        _alc.MakeContextCurrent(_context);

        Console.WriteLine("[AUDIO] OpenAL initialisé avec succès !");
    }

    public static unsafe void Dispose()
    {
        if (_context != null)
        {
            _alc.DestroyContext(_context);
            _context = null;
        }
        if (_device != null)
        {
            _alc.CloseDevice(_device);
            _device = null;
        }

        AL?.Dispose();
        _alc?.Dispose();
    }

}

public class MidiPlayer
{
    private Synthesizer _synthesizer;
    private MidiFileSequencer _sequencer;

    public MidiPlayer(string sf2Path, string midiPath)
    {
        // 1. Charger la banque de sons (SoundFont)
        var synthesizerSettings = new SynthesizerSettings(44100);
        _synthesizer = new Synthesizer(sf2Path, synthesizerSettings);

        // 2. Préparer le séquenceur
        _sequencer = new MidiFileSequencer(_synthesizer);
        var midiFile = new MidiFile(midiPath);

        // 3. Lancer la lecture en boucle
        _sequencer.Play(midiFile, true);
    }

    // Cette fonction sera appelée pour récupérer l'audio généré
    public void RenderAudio(float[] leftBuffer, float[] rightBuffer)
    {
        // Le séquenceur génère le son à la volée en lisant la partition !
        _sequencer.Render(leftBuffer, rightBuffer);
    }
}