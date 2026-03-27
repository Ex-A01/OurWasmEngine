using Silk.NET.OpenAL;
using MeltySynth;

public static class AudioManager
{
    private static ALContext _alc;

    // NOUVEAU : On rend l'API AL publique !
    public static AL AL { get; private set; }

    private static unsafe Device* _device;
    private static unsafe Context* _context;

    public static unsafe void Initialize()
    {
        // On récupère les API
        _alc = ALContext.GetApi();
        AL = AL.GetApi();

        // On ouvre le périphérique par défaut (la carte son)
        _device = _alc.OpenDevice(string.Empty);
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

    // À appeler dans la méthode Dispose() de MyGame
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