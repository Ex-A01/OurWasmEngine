using System.Runtime.InteropServices.JavaScript;
using System.Numerics;

// Assure-toi qu'il n'y a PAS de namespace ici (ou modifie main.js en conséquence)
public static partial class Interop
{
    // On garde une référence à ton jeu pour le redimensionnement
    public static MyGame GameInstance { get; set; }

    [JSExport]
    public static void OnCanvasResize(float width, float height, float devicePixelRatio)
    {
        GameConfig.WindowWidth = width;
        GameConfig.WindowHeight = height;
        GameInstance?.Resize((int)width, (int)height);
    }

    [JSExport]
    public static void OnMouseMove(float x, float y)
    {
        if (MyGame.Cursors.Count > 0)
        {
            MyGame.Cursors[0].Position = new Vector2(x, y);
        }
    }

    [JSExport]
    public static void OnMouseDown(bool shift, bool ctrl, bool alt, int button)
    {
        if (MyGame.Cursors.Count > 0)
        {
            if (button == 0) MyGame.Cursors[0].LeftDown = true;
            if (button == 1) MyGame.Cursors[0].MiddleDown = true;
            if (button == 2) MyGame.Cursors[0].RightDown = true;
        }
    }

    [JSExport]
    public static void OnMouseUp(bool shift, bool ctrl, bool alt, int button)
    {
        if (MyGame.Cursors.Count > 0)
        {
            if (button == 0) MyGame.Cursors[0].LeftDown = false;
            if (button == 1) MyGame.Cursors[0].MiddleDown = false;
            if (button == 2) MyGame.Cursors[0].RightDown = false;
        }
    }

    // --- ÉTAT DU CLAVIER ---
    public static bool LeftPressed;
    public static bool RightPressed;
    public static bool UpPressed;
    public static bool SpacePressed;
    public static bool CPressed;
    public static bool NPressed;

    [JSExport]
    public static void OnKeyDown(string code)
    {
        if (code == "ArrowLeft") LeftPressed = true;
        if (code == "ArrowRight") RightPressed = true;
        if (code == "ArrowUp") UpPressed = true;
        if (code == "Space") SpacePressed = true;
        if (code == "KeyC") CPressed = true;
        if (code == "KeyN") NPressed = true;
    }

    [JSExport]
    public static void OnKeyUp(string code)
    {
        if (code == "ArrowLeft") LeftPressed = false;
        if (code == "ArrowRight") RightPressed = false;
        if (code == "ArrowUp") UpPressed = false;
        if (code == "Space") SpacePressed = false;
        if (code == "KeyC") CPressed = false;
        if (code == "KeyN") NPressed = false;
    }

    [JSExport]
    public static void LoadLevelFromWeb(string jsonContent)
    {
        Console.WriteLine("[Interop] Demande de chargement d'un niveau depuis le Web reçue !");

        try
        {
            // Note: Si tes inputs Silk.NET sont null sur le web, passe null ici aussi
            SceneLoader.LoadSceneFromString(jsonContent, null, null);
            Console.WriteLine($"[Interop] Succès ! Scène {SceneManager.CurrentScene.Name} chargée.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Interop] Erreur lors du chargement du niveau distant : {ex.Message}");
        }
    }
}