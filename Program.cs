using Silk.NET.OpenGLES;
using System;
using System.Runtime.InteropServices;
using WebGL.Sample;

public static class Program
{
    private static MyGame _game;
    private static double _lastTime = 0;

    [UnmanagedCallersOnly]
    public static int Frame(double time, nint userData)
    {
        // Calcul du temps écoulé (deltaTime)
        float dt = _lastTime == 0 ? 0.016f : (float)((time - _lastTime) / 1000.0);
        _lastTime = time;

        // On met à jour la logique, puis on dessine !
        _game.Update(dt);
        _game.Render();

        return 1; // 1 = dire au navigateur de continuer la boucle
    }

    public static void Main(string[] args)
    {
        Console.WriteLine("Démarrage du moteur via EGL...");

        var display = EGL.GetDisplay(IntPtr.Zero);
        if (!EGL.Initialize(display, out _, out _)) throw new Exception("EGL Init failed");

        int[] attributeList = new int[]
        {
            EGL.EGL_RED_SIZE, 8, EGL.EGL_GREEN_SIZE, 8, EGL.EGL_BLUE_SIZE, 8,
            EGL.EGL_DEPTH_SIZE, 24, EGL.EGL_SURFACE_TYPE, EGL.EGL_WINDOW_BIT,
            EGL.EGL_RENDERABLE_TYPE, EGL.EGL_OPENGL_ES3_BIT, EGL.EGL_NONE
        };

        IntPtr config = IntPtr.Zero, numConfig = IntPtr.Zero;
        EGL.ChooseConfig(display, attributeList, ref config, (IntPtr)1, ref numConfig);
        EGL.BindApi(EGL.EGL_OPENGL_ES_API);

        int[] ctxAttribs = new int[] { EGL.EGL_CONTEXT_CLIENT_VERSION, 3, EGL.EGL_NONE };
        var context = EGL.CreateContext(display, config, IntPtr.Zero, ctxAttribs);
        var surface = EGL.CreateWindowSurface(display, config, IntPtr.Zero, IntPtr.Zero);

        EGL.MakeCurrent(display, surface, surface, context);

        TrampolineFuncs.ApplyWorkaroundFixingInvocations();

        // Obtenir l'API OpenGL ES
        var gl = GL.GetApi(EGL.GetProcAddress);

        // Instanciation du jeu
        _game = new MyGame(gl);
        _game.Load();

        unsafe
        {
            // Lance la boucle infinie dans le navigateur
            Emscripten.RequestAnimationFrameLoop((delegate* unmanaged<double, nint, int>)&Frame, nint.Zero);
        }
    }
}