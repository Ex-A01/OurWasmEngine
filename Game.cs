using Linearstar.Windows.RawInput;
using Linearstar.Windows.RawInput.Native;
using MeltySynth;
using nkast.Aether.Physics2D;
using nkast.Aether.Physics2D.Collision.Shapes;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Joints;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenAL;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

public class MyGame : IDisposable
{
    private IWindow _window;
    private GL _gl;
    private IInputContext _input;
    private Matrix4X4<float> _projection;
    private Shader _shader;

    public static World PhysicsWorld { get; private set; }

    private SpriteBatch _spriteBatch; // NOUVEAU

    private float _totalTime = 0f;
    //private Camera2D _camera;
    //public static Camera2D MainCamera { get; private set; }

    // --- P/INVOKE POUR INTERCEPTER WINDOWS ---
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, WndProcDelegate dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int GWLP_WNDPROC = -4;
    private const uint WM_INPUT = 0x00FF;

    // On doit garder une référence au delegate pour éviter que le Garbage Collector le supprime
    private WndProcDelegate _wndProcDelegate;
    private IntPtr _prevWndProc;
    // -----------------------------------------

    // Nouveau curseur adapté au Raw Input
    public class VirtualCursor
    {
        public RawInputDeviceHandle DeviceHandle;
        public Vector2 Position;
        public Vector4 Color;

        // NOUVEAU : État des boutons
        public bool LeftDown;
        public bool RightDown;
        public bool MiddleDown;

        public VirtualCursor(RawInputDeviceHandle handle, Vector2 startPos, Vector4 color)
        {
            DeviceHandle = handle;
            Position = startPos;
            Color = color;
        }
    }

    public static List<VirtualCursor> Cursors = new List<VirtualCursor>();
    private Vector4[] _cursorColors = { GameConfig.CursorColor, new Vector4(1f, 0f, 0f, 1f), new Vector4(0f, 1f, 0f, 1f), new Vector4(1f, 1f, 0f, 1f) };

    public MyGame()
    {
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>((int)GameConfig.WindowWidth, (int)GameConfig.WindowHeight);
        options.Title = "Goozy (GOC Engine) - MULTI-MOUSE";

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += Dispose;
    }

    public void Run() => _window.Run();

    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        AssetManager.Initialize(_gl);
        AudioManager.Initialize();

        _spriteBatch = new SpriteBatch(_gl);
        _input = _window.CreateInput();
        _gl.ClearColor(GameConfig.BackgroundColor.X, GameConfig.BackgroundColor.Y, GameConfig.BackgroundColor.Z, GameConfig.BackgroundColor.W);
        _projection = Matrix4x4.CreateOrthographicOffCenter(0f, GameConfig.WindowWidth, GameConfig.WindowHeight, 0f, -1.0f, 1.0f).ToGeneric();

        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (k, key, _) =>
            {
                if (key == Key.Escape) _window.Close();
                if (key == Key.F3) GameConfig.DebugMode = !GameConfig.DebugMode;
            };
        }

        if (_input.Mice.Count > 0)
        {
            _input.Mice[0].Cursor.CursorMode = CursorMode.Disabled;
        }

        // --- SETUP RAW INPUT & WINDOW SUBCLASSING ---
        nint hwnd = _window.Native.Win32.Value.Hwnd;
        RawInputDevice.RegisterDevice(HidUsageAndPage.Mouse, RawInputDeviceFlags.InputSink, hwnd);
        _wndProcDelegate = WndProc;
        if (IntPtr.Size == 8) _prevWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, _wndProcDelegate);
        else _prevWndProc = SetWindowLong(hwnd, GWLP_WNDPROC, _wndProcDelegate);
        // ---------------------------------------------

        _shader = new Shader(_gl, "base.vert", "base.frag");

        // --- MAGIE DU DATA-DRIVEN : ON CHARGE LE NIVEAU DEPUIS LE FICHIER ---
        try
        {
            // Le SceneLoader s'occupe de créer la scène, de l'assigner au SceneManager, et de populer les objets !
            SceneLoader.LoadSceneFromJson("paff.json", _input.Keyboards[0], _input.Mice[0]);
            Console.WriteLine($"[SCENE LOADED] {SceneManager.CurrentScene.Name} chargée avec succès !");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERREUR CRITIQUE] Impossible de charger le niveau : {ex.Message}");
        }
    }

    // --- LA MAGIE EST ICI : Interception des messages Windows ---
    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            var data = RawInputData.FromHandle(lParam);
            if (data is RawInputMouseData mouseData)
            {
                var handle = mouseData.Header.DeviceHandle;
                var cursor = Cursors.Find(c => c.DeviceHandle == handle);

                // --- FIX : Recréer le curseur s'il n'existe pas encore ---
                if (cursor == null)
                {
                    Vector4 color = _cursorColors[Cursors.Count % _cursorColors.Length];
                    cursor = new VirtualCursor(handle, new Vector2(GameConfig.WindowWidth / 2f, GameConfig.WindowHeight / 2f), color);
                    Cursors.Add(cursor);
                    Console.WriteLine($"[NOUVELLE SOURIS] Handle: {handle} - Couleur: {color}");
                }
                // ---------------------------------------------------------

                // Mouvement (Relatif)
                cursor.Position.X = Math.Clamp(cursor.Position.X + mouseData.Mouse.LastX, 0, GameConfig.WindowWidth);
                cursor.Position.Y = Math.Clamp(cursor.Position.Y + mouseData.Mouse.LastY, 0, GameConfig.WindowHeight);

                // État des Boutons
                var flags = mouseData.Mouse.Buttons; // Utilise ButtonFlags au lieu de Flags

                if (flags.HasFlag(RawMouseButtonFlags.LeftButtonDown)) cursor.LeftDown = true;
                if (flags.HasFlag(RawMouseButtonFlags.LeftButtonUp)) cursor.LeftDown = false;

                if (flags.HasFlag(RawMouseButtonFlags.RightButtonDown)) cursor.RightDown = true;
                if (flags.HasFlag(RawMouseButtonFlags.RightButtonUp)) cursor.RightDown = false;

                if (flags.HasFlag(RawMouseButtonFlags.MiddleButtonDown)) cursor.MiddleDown = true;
                if (flags.HasFlag(RawMouseButtonFlags.MiddleButtonUp)) cursor.MiddleDown = false;
            }
        }

        return CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
    }
    // -------------------------------------------------------------

    private void OnUpdate(double deltaTime)
    {
        float dt = Math.Min((float)deltaTime, 0.05f);
        _totalTime += dt;

        // La Scène s'occupe de la physique, des updates d'objets, et de la caméra !
        SceneManager.CurrentScene?.Update(dt);
    }

    private void DrawDebugLine(SpriteBatch spriteBatch, Vector3 start, Vector2 end, Vector4 color)
    {
        Vector2 start2D = new Vector2(start.X, start.Y);
        Vector2 direction = end - start2D;
        float length = direction.Length();
        float angle = MathF.Atan2(direction.Y, direction.X);
        Vector2 midPoint = (start2D + end) / 2f;

        float thickness = 2f;

        Texture pixelTex = AssetManager.GetTexture("pixel.png");

        // NOUVEAU : On ajoute l'origine (pour bien centrer la ligne sur le midPoint) et le depth à 1.0f !
        spriteBatch.Draw(pixelTex, midPoint, new Vector2(length, thickness), color, angle, new Vector2(length / 2f, thickness / 2f), null, null, 1.0f);
    }

    private void OnRender(double deltaTime)
    {
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        if (SceneManager.CurrentScene != null)
        {
            // 1. Rendu du monde
            _spriteBatch.Begin(_shader, _projection, SceneManager.CurrentScene.Camera.GetViewMatrix().ToGeneric());
            SceneManager.CurrentScene.Draw(_spriteBatch, _totalTime);
            _spriteBatch.End();
        }

        // 2. Interface Utilisateur (Curseurs)
        var identity = Matrix4X4<float>.Identity;
        _spriteBatch.Begin(_shader, _projection, identity);

        Texture atlasTex = AssetManager.GetTexture("atlas.png");
        float tileSizeInAtlas = 0.125f;
        Vector2 cursorUVOffset = new Vector2(5 * tileSizeInAtlas, 0f);
        Vector2 cursorUVSize = new Vector2(tileSizeInAtlas, tileSizeInAtlas);

        foreach (var cursor in Cursors)
        {
            _spriteBatch.Draw(atlasTex, cursor.Position, new Vector2(24f, 24f), cursor.Color, 0f, Vector2.Zero, cursorUVOffset, cursorUVSize, 1.0f);
        }
        _spriteBatch.End();
    }

    public void Dispose()
    {
        nint hwnd = _window.Native.Win32.Value.Hwnd;
        if (IntPtr.Size == 8)
            SetWindowLongPtr(hwnd, GWLP_WNDPROC, _wndProcDelegate);
        else
            SetWindowLong(hwnd, GWLP_WNDPROC, _wndProcDelegate);

        _spriteBatch.Dispose();
        AssetManager.DisposeAll(); // Propre !
        AudioManager.Dispose();
        _shader.Dispose();
        _gl.Dispose();
        _input.Dispose();
    }
}

//██████████████████████████████████
//████      CONFIG & UTILS      ████
//██████████████████████████████████

public static class GameConfig
{
    public const float WindowWidth = 800f;
    public const float WindowHeight = 600f;
    public static bool DebugMode = true;

    // --- NOUVELLES CONSTANTES PHYSIQUES ---
    public const float PixelsPerMeter = 50f;
    public const float MetersPerPixel = 1f / PixelsPerMeter;

    // Une gravité physique réaliste est de 9.81f. 
    // Pour un jeu, on l'augmente souvent un peu (ex: 20f) pour plus de dynamisme.
    public const float PhysicsGravity = 20f;
    // --------------------------------------

    public const float BaseTileSize = 50f;
    public const float HalfTile = BaseTileSize / 2f;

    public static readonly Vector4 BackgroundColor = new Vector4(0.0f, 0.0f, 0.1f, 1.0f);
    public static readonly Vector4 PlayerColor = new Vector4(1f, 1f, 1f, 1f);
    public static readonly Vector4 CursorColor = new Vector4(0f, 1f, 1f, 1f);
    public static readonly Vector4 DebugColor = new Vector4(1f, 0f, 1f, 1f);
}

public struct AABB
{
    public float X, Y, Width, Height;
    public float Left => X;
    public float Right => X + Width;
    public float Top => Y;
    public float Bottom => Y + Height;

    public AABB(float x, float y, float width, float height)
    {
        X = x; Y = y; Width = width; Height = height;
    }

    public bool Intersects(AABB other)
    {
        return Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
    }
}

public class Camera2D
{
    public Vector2 Position { get; set; } = Vector2.Zero;
    public float Zoom { get; set; } = 1f;
    public float Rotation { get; set; } = 0f;
    public AABB? CurrentBounds { get; set; }

    private float _screenWidth, _screenHeight;

    public Camera2D(float screenWidth, float screenHeight)
    {
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
    }

    public Matrix4x4 GetViewMatrix()
    {
        // On arrondit la position pour s'aligner parfaitement sur la grille des pixels (Pixel-Perfect)
        var translation = Matrix4x4.CreateTranslation(MathF.Round(-Position.X), MathF.Round(-Position.Y), 0f);
        var rotation = Matrix4x4.CreateRotationZ(Rotation);
        var scale = Matrix4x4.CreateScale(Zoom, Zoom, 1f);
        var centerScreen = Matrix4x4.CreateTranslation(_screenWidth / 2f, _screenHeight / 2f, 0f);
        return translation * rotation * scale * centerScreen;
    }

    public void Follow(Vector2 targetPosition, float lerpSpeed, float dt)
    {
        Vector2 desiredPos = Vector2.Lerp(Position, targetPosition, lerpSpeed * dt);

        if (CurrentBounds.HasValue)
        {
            var bounds = CurrentBounds.Value;
            float halfWidth = (_screenWidth / 2f) / Zoom;
            float halfHeight = (_screenHeight / 2f) / Zoom;

            float minX = bounds.Left + halfWidth;
            float maxX = Math.Max(minX, bounds.Right - halfWidth);
            float minY = bounds.Top + halfHeight;
            float maxY = Math.Max(minY, bounds.Bottom - halfHeight);

            desiredPos.X = Math.Clamp(desiredPos.X, minX, maxX);
            desiredPos.Y = Math.Clamp(desiredPos.Y, minY, maxY);
        }

        Position = desiredPos;
    }

    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        float worldX = (screenPos.X - _screenWidth / 2f) / Zoom + Position.X;
        float worldY = (screenPos.Y - _screenHeight / 2f) / Zoom + Position.Y;
        return new Vector2(worldX, worldY);
    }
}

public enum ColliderShape
{
    Rectangle,
    SlopeRight, // Pente qui monte vers la droite : /|
    SlopeLeft,
    PlayerBox
}

//██████████████████████████████████
//████       C.O.G  CORE        ████
//██████████████████████████████████

public class Transform : Component
{
    public Vector3 Position;
    public Vector2 Size;
}

public abstract class Component
{
    public GameObject GameObject { get; internal set; }
    public Transform Transform => GameObject.Transform;

    public virtual void Awake() { }
    public virtual void Update(float deltaTime) { }
    // NOUVEAU : On passe uniquement le SpriteBatch
    public virtual void Draw(SpriteBatch spriteBatch, float gameTime) { }
    public virtual void OnOverlap(GameObject other) { }

    public virtual void ReceiveMessage(string message)
    {
        // à vérif
        if (message.ToLower() == "destroy")
        {
            GameObject.IsDestroyed = true;
        }
    }
}

public class GameObject
{
    public string Name { get; set; }
    public bool IsDestroyed { get; set; } = false;
    public Transform Transform { get; private set; }
    public string Uid { get; set; } = Guid.NewGuid().ToString();

    private List<Component> _components = new List<Component>();

    public GameObject(string name)
    {
        Name = name;
        Transform = AddComponent<Transform>();
    }

    public T AddComponent<T>() where T : Component, new()
    {
        T component = new T { GameObject = this };
        _components.Add(component);
        component.Awake();
        return component;
    }

    public T GetComponent<T>() where T : Component
    {
        foreach (var component in _components)
            if (component is T match) return match;
        return null;
    }

    public bool HasComponent<T>() where T : Component => GetComponent<T>() != null;

    public void Update(float deltaTime)
    {
        foreach (var comp in _components) comp.Update(deltaTime);
    }

    public void Draw(SpriteBatch spriteBatch, float gameTime)
    {
        foreach (var comp in _components) comp.Draw(spriteBatch, gameTime);
    }

    public void OnOverlap(GameObject other)
    {
        foreach (var comp in _components) comp.OnOverlap(other);
    }

    // to interract between objects
    public void SendMessage(string message)
    {
        foreach (var comp in _components)
        {
            comp.ReceiveMessage(message);
        }
        // à vérif
        if (message.ToLower() == "destroy")
        {
            IsDestroyed = true;
        }
    }
}

//██████████████████████████████████
//████    COMPOSANTS LOGIQUES   ████
//██████████████████████████████████

public class COG_Sprite : Component
{
    public bool IsVisible { get; set; } = true;
    public Vector4 Color { get; set; } = new Vector4(1, 1, 1, 1);
    public Material Mat { get; set; } = new SimpleMaterial();
    public string TexturePath { get; set; } = "atlas.png";
    public float LayerDepth { get; set; } = 0.5f;

    public override void Draw(SpriteBatch spriteBatch, float gameTime)
    {
        // Si c'est invisible et qu'on n'est pas en debug, on s'arrête.
        if (!IsVisible && !GameConfig.DebugMode) return;

        Vector4 drawColor = (!IsVisible && GameConfig.DebugMode) ? GameConfig.DebugColor : Color;
        drawColor = drawColor * Mat.Color;

        float rotationZ = 0f;
        var collider = GameObject.GetComponent<COG_Collider>();
        if (collider != null && collider.PhysicsBody != null)
        {
            rotationZ = collider.PhysicsBody.Rotation;
        }

        Texture tex = AssetManager.GetTexture(TexturePath);

        // --- PROBLÈME 1 CORRIGÉ : DESSIN DES ZONES DE DEBUG ---
        // On dessine 4 lignes fines (un rectangle creux) pour les objets invisibles !
        if (!IsVisible && GameConfig.DebugMode)
        {
            Texture pixelTex = AssetManager.GetTexture("pixel.png");
            float t = 2f; // Épaisseur des bords
            float w = Transform.Size.X;
            float h = Transform.Size.Y;
            Vector2 pos = new Vector2(Transform.Position.X, Transform.Position.Y);

            // L'astuce est là : on force l'origine en haut à gauche (0, 0)
            Vector2 zeroOrigin = Vector2.Zero;

            // On ajoute zeroOrigin à la fin des arguments pour écraser le comportement par défaut
            spriteBatch.Draw(pixelTex, pos, new Vector2(w, t), drawColor, 0f, zeroOrigin); // Haut
            spriteBatch.Draw(pixelTex, new Vector2(pos.X, pos.Y + h - t), new Vector2(w, t), drawColor, 0f, zeroOrigin); // Bas
            spriteBatch.Draw(pixelTex, pos, new Vector2(t, h), drawColor, 0f, zeroOrigin); // Gauche
            spriteBatch.Draw(pixelTex, new Vector2(pos.X + w - t, pos.Y), new Vector2(t, h), drawColor, 0f, zeroOrigin); // Droite
            return;
        }

        // --- PROBLÈME 2 CORRIGÉ : LES MURS ÉTIRÉS (TILING) ---
        // Au lieu d'étirer, on utilise la puissance du batcher pour dessiner N tuiles !
        if (Mat is StretchMaterial stretch)
        {
            float tileSize = stretch.TextureBaseSize;
            int tilesX = (int)Math.Ceiling(Transform.Size.X / tileSize);
            int tilesY = (int)Math.Ceiling(Transform.Size.Y / tileSize);

            Vector2 tileOrigin = new Vector2(tileSize / 2f, tileSize / 2f);

            for (int x = 0; x < tilesX; x++)
            {
                for (int y = 0; y < tilesY; y++)
                {
                    // Position physique de cette petite tuile
                    Vector2 tilePos = new Vector2(
                        Transform.Position.X + (x * tileSize) + tileOrigin.X,
                        Transform.Position.Y + (y * tileSize) + tileOrigin.Y
                    );

                    // On coupe proprement si le mur n'est pas un multiple exact de 50
                    float drawWidth = (x == tilesX - 1 && Transform.Size.X % tileSize != 0) ? Transform.Size.X % tileSize : tileSize;
                    float drawHeight = (y == tilesY - 1 && Transform.Size.Y % tileSize != 0) ? Transform.Size.Y % tileSize : tileSize;

                    Vector2 uvSize = new Vector2(
                        Mat.AtlasSize.X * (drawWidth / tileSize),
                        Mat.AtlasSize.Y * (drawHeight / tileSize)
                    );

                    spriteBatch.Draw(tex, tilePos, new Vector2(drawWidth, drawHeight), drawColor, rotationZ, tileOrigin, Mat.AtlasOffset, uvSize, LayerDepth);
                }
            }
            return;
        }

        // --- CAS 3 : SPRITE NORMAL (Le Joueur, les Pièces...) ---
        Vector2 position = new Vector2(Transform.Position.X + Transform.Size.X / 2f, Transform.Position.Y + Transform.Size.Y / 2f);
        Vector2 origin = new Vector2(Transform.Size.X / 2f, Transform.Size.Y / 2f);
        spriteBatch.Draw(tex, position, Transform.Size, drawColor, rotationZ, origin, Mat.AtlasOffset, Mat.AtlasSize, LayerDepth);
    }
}

public class COG_Collider : Component
{
    private bool _isSolid = true;
    public bool IsSolid
    {
        get => _isSolid;
        set
        {
            _isSolid = value;
            if (PhysicsBody != null)
            {
                foreach (var fixture in PhysicsBody.FixtureList)
                    fixture.IsSensor = !_isSolid || _isTrigger;
            }
        }
    }

    private bool _isTrigger = false;
    public bool IsTrigger
    {
        get => _isTrigger;
        set
        {
            _isTrigger = value;
            if (PhysicsBody != null)
            {
                foreach (var fixture in PhysicsBody.FixtureList)
                    fixture.IsSensor = _isTrigger || !_isSolid;
            }
        }
    }

    private float _friction = 0.2f;
    public float Friction
    {
        get => _friction;
        set
        {
            _friction = value;
            if (PhysicsBody != null)
            {
                foreach (var fixture in PhysicsBody.FixtureList) fixture.Friction = value;
            }
        }
    }

    private float _restitution = 0f;
    public float Restitution
    {
        get => _restitution;
        set
        {
            _restitution = value;
            if (PhysicsBody != null)
            {
                foreach (var fixture in PhysicsBody.FixtureList) fixture.Restitution = value;
            }
        }
    }

    private BodyType _bodyType = BodyType.Static;
    public BodyType BodyType
    {
        get => _bodyType;
        set
        {
            _bodyType = value;
            if (PhysicsBody != null) PhysicsBody.BodyType = value;
        }
    }

    // --- NOUVEAU : La propriété Shape déclenche une reconstruction ---
    private ColliderShape _shape = ColliderShape.Rectangle;
    public ColliderShape Shape
    {
        get => _shape;
        set
        {
            _shape = value;
            // Si le corps physique existe déjà, on le reconstruit pour appliquer la nouvelle forme
            if (PhysicsBody != null) RebuildPhysicsBody();
        }
    }

    public Body PhysicsBody { get; private set; }

    public override void Awake()
    {
        RebuildPhysicsBody();
    }

    // --- NOUVEAU : Méthode séparée pour construire/reconstruire le Collider ---
    public void RebuildPhysicsBody()
    {
        // 1. Nettoyage de l'ancien body s'il existe
        if (PhysicsBody != null && SceneManager.CurrentScene?.PhysicsWorld != null)
        {
            SceneManager.CurrentScene.PhysicsWorld.Remove(PhysicsBody);
        }

        // 2. Calculs de base
        Vector2 centerPx = new Vector2(Transform.Position.X + Transform.Size.X / 2f, Transform.Position.Y + Transform.Size.Y / 2f);
        var centerMeters = (centerPx * GameConfig.MetersPerPixel).ToAether();

        float widthMeters = Transform.Size.X * GameConfig.MetersPerPixel;
        float heightMeters = Transform.Size.Y * GameConfig.MetersPerPixel;

        // 3. Création de la forme
        if (_shape == ColliderShape.Rectangle)
        {
            PhysicsBody = SceneManager.CurrentScene.PhysicsWorld.CreateRectangle(
                widthMeters, heightMeters, 1f, centerMeters, 0f, _bodyType);
        }
        else
        {
            var vertices = new nkast.Aether.Physics2D.Common.Vertices();
            float hw = widthMeters / 2f;
            float hh = heightMeters / 2f;

            if (_shape == ColliderShape.SlopeRight)
            {
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(-hw, hh));  // Bas-Gauche
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(hw, hh));   // Bas-Droite
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(hw, -hh));  // Haut-Droite
            }
            else if (_shape == ColliderShape.SlopeLeft)
            {
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(-hw, -hh)); // Haut-Gauche
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(-hw, hh));  // Bas-Gauche
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(hw, hh));   // Bas-Droite
            }
            else if (_shape == ColliderShape.PlayerBox)
            {
                // On "rabote" les coins inférieurs pour éviter qu'ils n'accrochent les bordures des tuiles.
                // On enlève environ 10% de la taille de l'objet dans les coins.
                float chamfer = Math.Min(hw, hh) * 0.2f;

                // Ordre Anti-Horaire (très important pour Aether)
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(-hw, -hh)); // Haut Gauche
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(-hw, hh - chamfer)); // Bas Gauche (début biseau)
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(-hw + chamfer, hh)); // Bas Gauche (fin biseau)
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(hw - chamfer, hh));  // Bas Droite (début biseau)
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(hw, hh - chamfer));  // Bas Droite (fin biseau)
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(hw, -hh)); // Haut Droite
            }

            PhysicsBody = SceneManager.CurrentScene.PhysicsWorld.CreatePolygon(
                vertices, 1f, centerMeters, 0f, _bodyType);
        }

        // 4. Application des propriétés fixes
        PhysicsBody.FixedRotation = true;
        PhysicsBody.Tag = GameObject;

        foreach (var fixture in PhysicsBody.FixtureList)
        {
            fixture.Friction = _friction;
            fixture.Restitution = _restitution;
            if (_isTrigger || !_isSolid) fixture.IsSensor = true;
        }

        PhysicsBody.OnCollision += OnPhysicsCollision;
    }

    public override void Update(float deltaTime)
    {
        if (_bodyType != BodyType.Static)
        {
            Vector2 centerPx = PhysicsBody.Position.ToNumeric() * GameConfig.PixelsPerMeter;
            Transform.Position.X = centerPx.X - Transform.Size.X / 2f;
            Transform.Position.Y = centerPx.Y - Transform.Size.Y / 2f;
        }
    }

    private bool OnPhysicsCollision(Fixture sender, Fixture other, nkast.Aether.Physics2D.Dynamics.Contacts.Contact contact)
    {
        if (IsTrigger && other.Body.Tag is GameObject otherGo)
        {
            GameObject.OnOverlap(otherGo);
        }
        return true;
    }

    public AABB GetAABB()
    {
        return new AABB(Transform.Position.X, Transform.Position.Y, Transform.Size.X, Transform.Size.Y);
    }

    public override void ReceiveMessage(string message)
    {
        switch (message.ToLower()) 
        {
            case "destroy":
                this.Destroy();
                break;
        }
    }

    public void Destroy()
    {
        if (PhysicsBody != null && SceneManager.CurrentScene?.PhysicsWorld != null)
        {
            SceneManager.CurrentScene.PhysicsWorld.Remove(PhysicsBody);
            PhysicsBody = null;
        }
    }
}

public enum PlayerState
{
    Normal,
    Climbing, // Placeholder pour les deux souris
    Ragdoll   // Chute libre / Roulade
}

public class COG_PlayerController : Component
{
    public float Speed { get; set; } = 6f;
    public float JumpImpulse { get; set; } = 15f;
    public IKeyboard Keyboard { get; set; }
    public IMouse Mouse { get; set; }

    // --- MACHINE A ETATS ---
    public PlayerState State { get; private set; } = PlayerState.Normal;
    private float _fallTimer = 0f;
    public float FallRagdollThreshold { get; set; } = 0.8f; // Au bout de 0.8s de chute, on passe en Ragdoll

    private nkast.Aether.Physics2D.Dynamics.Body _body;
    private bool _wasJumpPressed = false;

    private float _currentGroundFriction = 0.2f;

    public override void Update(float deltaTime)
    {
        if (_body == null)
        {
            var collider = GameObject.GetComponent<COG_Collider>();
            if (collider != null) _body = collider.PhysicsBody;
            if (_body == null) return;
        }

        bool isGrounded = false;
        var contactEdge = _body.ContactList;
        while (contactEdge != null)
        {
            var contact = contactEdge.Contact;
            if (contact.IsTouching && contact.Enabled && !contact.FixtureA.IsSensor && !contact.FixtureB.IsSensor)
            {
                contact.GetWorldManifold(out var normal, out _);
                var pushDirection = contact.FixtureA.Body == _body ? -normal : normal;

                if (pushDirection.Y < -0.85f && Math.Abs(_body.LinearVelocity.Y) < 1f)
                {
                    isGrounded = true;

                    // --- NOUVEAU : On lit la friction du sol qu'on touche ! ---
                    var otherFixture = contact.FixtureA.Body == _body ? contact.FixtureB : contact.FixtureA;
                    _currentGroundFriction = otherFixture.Friction;
                    // -----------------------------------------------------------
                    break;
                }
            }
            contactEdge = contactEdge.Next;
        }

        bool isJumpAction = Keyboard.IsKeyPressed(Key.Space) || Keyboard.IsKeyPressed(Key.Up) || Mouse.IsButtonPressed(MouseButton.Right);

        switch (State)
        {
            case PlayerState.Normal:
                HandleNormalMode(deltaTime, isGrounded, isJumpAction);
                break;
            case PlayerState.Climbing:
                HandleClimbingMode(deltaTime, isGrounded);
                break;
            case PlayerState.Ragdoll:
                HandleRagdollMode(deltaTime, isGrounded, isJumpAction);
                break;
        }

        _wasJumpPressed = isJumpAction;
    }

    private void HandleNormalMode(float deltaTime, bool isGrounded, bool isJumpAction)
    {

        // --- NOUVEAU SYSTÈME DE DÉPLACEMENT AVEC INERTIE ---
        float targetVelX = 0f;
        if (Keyboard.IsKeyPressed(Key.Left)) targetVelX -= Speed;
        if (Keyboard.IsKeyPressed(Key.Right)) targetVelX += Speed;

        // On détermine la vitesse de réaction en fonction de la surface
        float lerpSpeed;
        if (!isGrounded)
            lerpSpeed = 5f; // Dans les airs (Un peu d'inertie pour ajuster le saut)
        else if (_currentGroundFriction < 0.1f)
            lerpSpeed = 1.5f; // SUR LA GLACE ! (Très faible friction, on glisse longtemps)
        else
            lerpSpeed = 25f; // SUR SOL NORMAL (Friction forte, on s'arrête et on démarre sec)

        // On "glisse" de notre vélocité actuelle vers la vélocité cible
        float newVelX = _body.LinearVelocity.X + (targetVelX - _body.LinearVelocity.X) * (lerpSpeed * deltaTime);
        _body.LinearVelocity = new nkast.Aether.Physics2D.Common.Vector2(newVelX, _body.LinearVelocity.Y);
        // ---------------------------------------------------

        if (isJumpAction && !_wasJumpPressed && isGrounded && _body.LinearVelocity.Y >= -0.1f)
        {
            _body.LinearVelocity = new nkast.Aether.Physics2D.Common.Vector2(_body.LinearVelocity.X, 0f);
            _body.ApplyLinearImpulse(new nkast.Aether.Physics2D.Common.Vector2(0, -JumpImpulse));
        }

        if (!isGrounded && _body.LinearVelocity.Y > 2f)
        {
            _fallTimer += deltaTime;
            if (_fallTimer > FallRagdollThreshold)
            {
                SwitchState(PlayerState.Ragdoll);
                Random rnd = new Random();
                float randomSpin = (float)(rnd.NextDouble()) * 4f; // Entre -0.5 et 0.5
                _body.ApplyAngularImpulse(randomSpin);
            }
        }
        else
        {
            _fallTimer = 0f;
        }

        if (Keyboard.IsKeyPressed(Key.C))
        {
            SwitchState(PlayerState.Climbing);
            // BOOM ! On donne une énorme impulsion pour te décoller du sol avant d'accrocher la corde
            _body.ApplyLinearImpulse(new nkast.Aether.Physics2D.Common.Vector2(0, -10f));
        }
    }

    private void HandleClimbingMode(float deltaTime, bool isGrounded)
    {
        // En mode grimpe, la gravité s'applique NORMALEMENT. 
        // Ce sont les forces calculées par COG_RopeClimber qui nous soulèveront !
        _body.IgnoreGravity = false;

        // Sortir du mode (La touche N détache tout via RopeClimber, puis on passe en Ragdoll)
        if (Keyboard.IsKeyPressed(Key.N))
        {
            SwitchState(PlayerState.Ragdoll);
            Random rnd = new Random();
            float randomSpin = (float)(rnd.NextDouble()) * 0.5f; // Entre -0.5 et 0.5
            _body.ApplyAngularImpulse(randomSpin);
        }
    }

    private void HandleRagdollMode(float deltaTime, bool isGrounded, bool isJumpAction)
    {
        // LA MAGIE EST ICI : On libère la rotation !
        _body.FixedRotation = false;

        // On ne peut plus se diriger, on subit juste la physique.
        // Mécanique de récupération : Si on est au sol et qu'on a presque fini de rouler, on peut sauter pour se relever.
        if (isGrounded && Math.Abs(_body.LinearVelocity.X) < 1.5f && Math.Abs(_body.LinearVelocity.Y) < 1.5f)
        {
            if (isJumpAction && !_wasJumpPressed)
            {
                SwitchState(PlayerState.Normal);
                // Petit saut bonus pour marquer la récupération
                _body.ApplyLinearImpulse(new nkast.Aether.Physics2D.Common.Vector2(0, -5f));
            }
        }
    }

    private void SwitchState(PlayerState newState)
    {
        State = newState;
        _fallTimer = 0f;

        var collider = GameObject.GetComponent<COG_Collider>();
        if (collider == null || _body == null) return;

        switch (newState)
        {
            case PlayerState.Normal:
                collider.Friction = 0f;
                collider.Restitution = 0f;
                _body.AngularDamping = 0f;
                _body.LinearDamping = 0f;
                _body.FixedRotation = true; // SEUL LE MODE NORMAL EST DROIT
                _body.Rotation = 0f;
                break;

            case PlayerState.Climbing:
                collider.Friction = 0f;
                collider.Restitution = 0f;
                _body.AngularDamping = 1f; // On freine un peu le balancement du pendule
                _body.LinearDamping = 0.5f; // Friction de l'air pendant qu'on se balance
                _body.FixedRotation = false; // ON PEUT SE BALANCER ET TOURNER !
                break;

            case PlayerState.Ragdoll:
                collider.Friction = 0.3f;
                collider.Restitution = 0.3f;
                _body.AngularDamping = 1.0f;
                _body.LinearDamping = 0.4f;
                _body.FixedRotation = false; // LA CHUTE TOURNE AUSSI
                break;
        }

        Console.WriteLine($"[ÉTAT DU JOUEUR] -> {newState} (Friction: {collider.Friction})");
    }
}

public class COG_CameraZone : Component
{
    public Camera2D Camera { get; set; }

    public override void OnOverlap(GameObject other)
    {
        if (other.Name == "Player")
        {
            Camera.CurrentBounds = new AABB(Transform.Position.X, Transform.Position.Y, Transform.Size.X, Transform.Size.Y);
        }
    }
}

// Nouveaux composants de comportement
public class COG_CoinBehavior : Component
{
    public override void OnOverlap(GameObject other)
    {
        if (other.Name == "Player" && !GameObject.IsDestroyed)
        {
            Console.WriteLine("+1 Pièce !");
            GameObject.IsDestroyed = true;
        }
    }
}

public class COG_TriggerBehavior : Component
{
    public string TargetUid { get; set; } = "";
    public string MessageToSend { get; set; } = "";
    public bool TriggerOnce { get; set; } = true;

    private bool _hasTriggered = false;

    public override void OnOverlap(GameObject other)
    {
        if (other.Name == "Player" && !string.IsNullOrEmpty(TargetUid))
        {
            if (TriggerOnce && _hasTriggered) return;

            _hasTriggered = true;
            Console.WriteLine($"[TRIGGER] Le joueur a touché le trigger. Envoi de '{MessageToSend}' à l'objet '{TargetUid}'.");

            var target = SceneManager.CurrentScene.FindGameObjectByUid(TargetUid);
            if (target != null)
            {
                target.SendMessage(MessageToSend);
            }
            else
            {
                Console.WriteLine($"[ATTENTION] Cible UID '{TargetUid}' introuvable dans la scène !");
            }
        }
    }
}

public class COG_RopeClimber : Component
{
    public float MaxTotalRopeLength { get; set; } = 15f;
    public float WinchSpeed { get; set; } = 8f;

    // La force du ressort de la corde (plus c'est grand, plus la corde tire fort)
    public float RopeStiffness { get; set; } = 150f;

    public class Rope(byte Id)
    {
        public RawInputDeviceHandle MouseHandle;
        public Vector2 AnchorPointPx;
        public float DesiredLength;
        public bool IsAttached = false;
        public byte _id = Id;

        // NOUVEAU : Mémorise si on cliquait à la frame précédente
        public bool WasPullInput = false;
    }

    public Rope LeftArm = new Rope(0);
    public Rope RightArm = new Rope(1);

    private nkast.Aether.Physics2D.Dynamics.Body _playerBody;
    private COG_PlayerController _controller;

    public Vector2 PlayerCenterPx => _playerBody.Position.ToNumeric() * GameConfig.PixelsPerMeter;

    public override void Awake()
    {
        var col = GameObject.GetComponent<COG_Collider>();
        if (col != null) _playerBody = col.PhysicsBody;
        _controller = GameObject.GetComponent<COG_PlayerController>();
    }

    public override void Update(float deltaTime)
    {
        if (_playerBody == null || _controller == null) return;

        if (_controller.State != PlayerState.Climbing)
        {
            DetachRope(LeftArm);
            DetachRope(RightArm);
            return;
        }

        // Assignation automatique des souris aux bras
        if (MyGame.Cursors.Count > 0) LeftArm.MouseHandle = MyGame.Cursors[0].DeviceHandle;
        if (MyGame.Cursors.Count > 1) RightArm.MouseHandle = MyGame.Cursors[1].DeviceHandle;

        // Si une seule souris, elle contrôle les deux bras alternativement selon où on clique
        if (MyGame.Cursors.Count == 1) RightArm.MouseHandle = MyGame.Cursors[0].DeviceHandle;

        HandleArm(LeftArm, deltaTime);
        HandleArm(RightArm, deltaTime);
    }

    private void HandleArm(Rope arm, float deltaTime)
    {
        if (arm.MouseHandle == default) return;

        var cursor = MyGame.Cursors.Find(c => c.DeviceHandle == arm.MouseHandle);
        if (cursor == null) return;

        Vector2 targetWorld = SceneManager.CurrentScene.Camera.ScreenToWorld(cursor.Position);

        // --- LOGIQUE D'INVERSION ---
        bool pullInput;
        bool releaseInput;

        if (arm._id == 1) // Ton inversion selon l'ID du bras
        {
            pullInput = cursor.RightDown;
            releaseInput = cursor.LeftDown;
        }
        else
        {
            pullInput = cursor.LeftDown;
            releaseInput = cursor.RightDown;
        }

        // --- NOUVEAU : DÉTECTION DU CLIC (JustPressed) ---
        // Vrai uniquement à la frame exacte où le bouton s'enfonce
        bool pullJustPressed = pullInput && !arm.WasPullInput;
        arm.WasPullInput = pullInput;

        // Si on vient juste de cliquer...
        if (pullJustPressed)
        {
            // 1. On détache l'ancienne corde (si elle existait)
            if (arm.IsAttached) DetachRope(arm);

            // 2. On tente immédiatement d'en accrocher une nouvelle !
            TryAttachRope(arm, targetWorld);
        }

        // Le clic molette reste dispo si on veut "juste" se lâcher sans relancer de corde
        if (cursor.MiddleDown) DetachRope(arm);

        // --- APPLICATION DES FORCES ET DU TREUIL ---
        if (arm.IsAttached)
        {
            if (pullInput) Winch(arm, 1f, deltaTime);    // Maintenir enfoncé pour tirer
            if (releaseInput) Winch(arm, -1f, deltaTime); // Maintenir l'autre bouton pour relâcher

            ApplyRopeForce(arm);
        }
    }

    private void TryAttachRope(Rope arm, Vector2 targetWorld)
    {
        var targetMeters = (targetWorld * GameConfig.MetersPerPixel).ToAether();
        var playerCenterMeters = _playerBody.Position;

        nkast.Aether.Physics2D.Dynamics.Fixture hitFixture = null;
        nkast.Aether.Physics2D.Common.Vector2 hitPoint = targetMeters;

        SceneManager.CurrentScene.PhysicsWorld.RayCast((fixture, point, normal, fraction) =>
        {
            if (fixture.IsSensor || fixture.Body == _playerBody) return -1;
            hitFixture = fixture;
            hitPoint = point;
            return fraction;
        }, playerCenterMeters, targetMeters);

        if (hitFixture != null)
        {
            float distanceMeters = (hitPoint - playerCenterMeters).Length();
            float otherArmLength = (arm == LeftArm ? RightArm.DesiredLength : LeftArm.DesiredLength);
            if (!RightArm.IsAttached && !LeftArm.IsAttached) otherArmLength = 0;

            float maxAvailable = MaxTotalRopeLength - otherArmLength;

            if (distanceMeters <= maxAvailable)
            {
                arm.AnchorPointPx = hitPoint.ToNumeric() * GameConfig.PixelsPerMeter;
                arm.DesiredLength = distanceMeters;
                arm.IsAttached = true;
                Console.WriteLine($"[CORDE ATTACHÉE] Longueur: {distanceMeters}m");
            }
        }
    }

    private void DetachRope(Rope arm)
    {
        if (arm.IsAttached)
        {
            arm.IsAttached = false;
            arm.DesiredLength = 0f;
            Console.WriteLine("[CORDE DÉTACHÉE]");
        }
    }

    private void Winch(Rope arm, float direction, float deltaTime)
    {
        arm.DesiredLength -= direction * WinchSpeed * deltaTime;
        if (arm.DesiredLength < 0.5f) arm.DesiredLength = 0.5f;
    }

    private void ApplyRopeForce(Rope arm)
    {
        // Loi de Hooke : Force = Rigidité * (DistanceActuelle - DistanceVoulue)
        var anchorMeters = (arm.AnchorPointPx * GameConfig.MetersPerPixel).ToAether();
        var playerMeters = _playerBody.Position;

        var direction = anchorMeters - playerMeters;
        float currentDistance = direction.Length();

        // On ne tire que si la corde est tendue (si le joueur est plus loin que la longueur désirée)
        if (currentDistance > arm.DesiredLength && currentDistance > 0.01f)
        {
            direction.Normalize(); // Obtenir le vecteur directionnel de base (longueur de 1)

            // Calcul de la tension de la corde
            float stretch = currentDistance - arm.DesiredLength;
            float forceMagnitude = RopeStiffness * stretch;

            // On applique la force au centre du joueur
            var force = direction * forceMagnitude;
            _playerBody.ApplyForce(force);
        }
    }

    public override void Draw(SpriteBatch spriteBatch, float gameTime)
    {
        if (_playerBody == null) return;
        Vector3 physicalCenter = new Vector3(PlayerCenterPx.X, PlayerCenterPx.Y, 0);
        Texture pixelTex = AssetManager.GetTexture("pixel.png");
        float thickness = 2f;

        if (LeftArm.IsAttached) DrawRope(spriteBatch, pixelTex, physicalCenter, LeftArm.AnchorPointPx, new Vector4(1f, 0.3f, 0.3f, 1f), thickness);
        if (RightArm.IsAttached) DrawRope(spriteBatch, pixelTex, physicalCenter, RightArm.AnchorPointPx, new Vector4(0.3f, 0.3f, 1f, 1f), thickness);
    }

    private void DrawRope(SpriteBatch spriteBatch, Texture tex, Vector3 start, Vector2 end, Vector4 color, float thickness)
    {
        Vector2 start2D = new Vector2(start.X, start.Y);
        Vector2 direction = end - start2D;
        float length = direction.Length();
        float angle = MathF.Atan2(direction.Y, direction.X);
        Vector2 midPoint = (start2D + end) / 2f;

        // Depth à 1.0f pour qu'elles soient dessinées tout devant !
        spriteBatch.Draw(tex, midPoint, new Vector2(length, thickness), color, angle, new Vector2(length / 2f, thickness / 2f), null, null, 1.0f);
    }
}

public class COG_AudioSource : Component
{
    public string SoundFontPath { get; set; } = "assets/banks/DS_Square.sf2";
    public string MidiPath { get; set; } = "assets/tracks/Never-Gonna-Give-You-Up-3.mid";
    public float Volume { get; set; } = 0.5f;
    public bool Loop { get; set; } = true;
    public bool PlayOnAwake { get; set; } = true;

    private uint _sourceId;
    private uint[] _buffers;
    private const int NUM_BUFFERS = 3;
    private const int BUFFER_SIZE = 4096;
    private const int SAMPLE_RATE = 44100;

    private Synthesizer _synth;
    private MidiFileSequencer _sequencer;

    private float[] _leftBuffer;
    private float[] _rightBuffer;
    private short[] _interleavedBuffer;

    private bool _isInitialized = false;

    // NOUVEAU : On garde en mémoire si on a volontairement lancé ou coupé le son
    private bool _isPlaying = false;

    public override void Awake()
    {
        // On attend la première frame pour charger les bons chemins depuis le JSON
    }

    private unsafe void InitializeAudio()
    {
        _leftBuffer = new float[BUFFER_SIZE];
        _rightBuffer = new float[BUFFER_SIZE];
        _interleavedBuffer = new short[BUFFER_SIZE * 2];

        _sourceId = AudioManager.AL.GenSource();
        AudioManager.AL.SetSourceProperty(_sourceId, SourceFloat.Gain, Volume);

        try
        {
            var settings = new SynthesizerSettings(SAMPLE_RATE);
            _synth = new Synthesizer(SoundFontPath, settings);
            _sequencer = new MidiFileSequencer(_synth);
            var midiFile = new MidiFile(MidiPath);

            _sequencer.Play(midiFile, Loop);

            _buffers = new uint[NUM_BUFFERS];

            fixed (uint* ptr = _buffers)
            {
                AudioManager.AL.GenBuffers(NUM_BUFFERS, ptr);
            }

            for (int i = 0; i < NUM_BUFFERS; i++)
            {
                uint bufferId = _buffers[i];
                FillBuffer(bufferId);

                AudioManager.AL.SourceQueueBuffers(_sourceId, 1, &bufferId);
            }

            // MODIFIÉ : On met à jour notre variable interne
            _isPlaying = PlayOnAwake;
            if (_isPlaying)
            {
                AudioManager.AL.SourcePlay(_sourceId);
                Console.WriteLine($"[AUDIO] BGM lancée : {MidiPath} avec {SoundFontPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIO] Erreur de chargement : {ex.Message}");
        }
    }

    public override unsafe void Update(float deltaTime)
    {
        if (!_isInitialized)
        {
            InitializeAudio();
            _isInitialized = true;
        }

        if (_sequencer == null) return;

        AudioManager.AL.SetSourceProperty(_sourceId, SourceVector3.Position, Transform.Position.X, Transform.Position.Y, 0f);

        AudioManager.AL.GetSourceProperty(_sourceId, GetSourceInteger.BuffersProcessed, out int processed);

        while (processed > 0)
        {
            uint bufferId = 0;
            AudioManager.AL.SourceUnqueueBuffers(_sourceId, 1, &bufferId);
            FillBuffer(bufferId);
            AudioManager.AL.SourceQueueBuffers(_sourceId, 1, &bufferId);
            processed--;
        }

        // CORRECTION MAJEURE ICI : On ne force la lecture QUE si _isPlaying est vrai
        if (_isPlaying)
        {
            AudioManager.AL.GetSourceProperty(_sourceId, GetSourceInteger.SourceState, out int state);
            if ((SourceState)state != SourceState.Playing)
            {
                AudioManager.AL.SourcePlay(_sourceId);
            }
        }
    }

    public override void ReceiveMessage(string message)
    {
        if (!_isInitialized)
        {
            InitializeAudio();
            _isInitialized = true;
        }

        switch (message.ToLower())
        {
            case "play":
                if (! _isPlaying)
                {
                    _isPlaying = true; // On l'autorise à jouer
                    AudioManager.AL.SourcePlay(_sourceId);
                    Console.WriteLine($"[AUDIO] Play reçu : Lecture déclenchée ! ({MidiPath})");
                }
                break;
            case "pause":
                _isPlaying = false; // On lui interdit de forcer la reprise dans l'Update
                AudioManager.AL.SourcePause(_sourceId);
                Console.WriteLine($"[AUDIO] Pause reçue : Lecture suspendue !");
                break;
            case "stop":
                _isPlaying = false;
                AudioManager.AL.SourceStop(_sourceId);
                Console.WriteLine($"[AUDIO] Stop reçu : Lecture arrêtée !");
                break;
        }
    }

    private unsafe void FillBuffer(uint bufferId)
    {
        _sequencer.Render(_leftBuffer, _rightBuffer);

        for (int i = 0; i < BUFFER_SIZE; i++)
        {
            _interleavedBuffer[i * 2] = (short)Math.Clamp(_leftBuffer[i] * 32767f, -32768f, 32767f);
            _interleavedBuffer[i * 2 + 1] = (short)Math.Clamp(_rightBuffer[i] * 32767f, -32768f, 32767f);
        }

        fixed (short* ptr = _interleavedBuffer)
        {
            int sizeInBytes = BUFFER_SIZE * 2 * sizeof(short);
            AudioManager.AL.BufferData(bufferId, BufferFormat.Stereo16, ptr, sizeInBytes, SAMPLE_RATE);
        }
    }

    public unsafe void Destroy()
    {
        AudioManager.AL.SourceStop(_sourceId);
        AudioManager.AL.DeleteSource(_sourceId);

        if (_buffers != null)
        {
            fixed (uint* ptr = _buffers)
            {
                AudioManager.AL.DeleteBuffers(NUM_BUFFERS, ptr);
            }
        }
    }
}

//██████████████████████████████████
//████     MATERIAL SECTION     ████
//██████████████████████████████████

public abstract class Material
{
    public Vector4 Color { get; set; } = new Vector4(1, 1, 1, 1);
    public Vector2 AtlasOffset { get; set; } = new Vector2(0f, 0f);
    public Vector2 AtlasSize { get; set; } = new Vector2(0.125f, 0.125f);
}

public class SimpleMaterial : Material { }

public class StretchMaterial : Material
{
    public float TextureBaseSize { get; set; } = 50f;
    // Plus besoin de GetUVSize ici, la boucle s'en charge !
}

//██████████████████████████████████
//████   PREFABS (Factoires)    ████
//██████████████████████████████████

// Ces classes agissent comme des "Blueprints" pour construire des GameObjects pré-configurés.

public class PlayerPrefab : GameObject
{
    public PlayerPrefab(Vector3 position, IKeyboard kb, IMouse mouse) : base("Player")
    {
        Transform.Position = position;
        Transform.Size = new Vector2(GameConfig.BaseTileSize, GameConfig.BaseTileSize);

        var controller = AddComponent<COG_PlayerController>();
        controller.Keyboard = kb;
        controller.Mouse = mouse;

        var col = AddComponent<COG_Collider>();
        col.IsSolid = true;
        col.BodyType = BodyType.Dynamic; // <--- NOUVEAU !
        col.Friction = 0.0f;

        var sprite = AddComponent<COG_Sprite>();
        sprite.Color = GameConfig.PlayerColor;
        sprite.Mat = new SimpleMaterial
        {
            AtlasSize = new Vector2(0.125f, 0.125f),
            AtlasOffset = new Vector2(4 * 0.125f, 0f) // Tuile du joueur
        };

        var climber = AddComponent<COG_RopeClimber>();
    }
}

public class WallPrefab : GameObject
{
    public WallPrefab(float x, float y, float w, float h, Vector2 tileOffset) : base("Wall")
    {
        Transform.Position = new Vector3(x, y, 0);
        Transform.Size = new Vector2(w, h);

        var col = AddComponent<COG_Collider>();
        col.IsSolid = true;
        col.Friction = 0.1f;
        //col.Restitution = 0.25f;

        var sprite = AddComponent<COG_Sprite>();
        float tileSizeInAtlas = 64f / 512f;
        sprite.Mat = new StretchMaterial
        {
            TextureBaseSize = GameConfig.BaseTileSize,
            AtlasSize = new Vector2(tileSizeInAtlas, tileSizeInAtlas),
            AtlasOffset = new Vector2(tileOffset.X * tileSizeInAtlas, tileOffset.Y * tileSizeInAtlas)
        };
    }
}

public class SlopePrefab : GameObject
{
    public SlopePrefab(float x, float y, float w, float h, ColliderShape slopeType, Vector2 tileOffset) : base("Slope")
    {
        Transform.Position = new Vector3(x, y, 0);
        Transform.Size = new Vector2(w, h);

        var col = AddComponent<COG_Collider>();
        col.IsSolid = true;
        col.Friction = 0.5f; // On met un peu de friction pour ne pas glisser indéfiniment
        col.Shape = slopeType;

        var sprite = AddComponent<COG_Sprite>();
        float tileSizeInAtlas = 64f / 512f;
        // On utilise un SimpleMaterial pour éviter l'étirement des tuiles sur les diagonales complexes
        sprite.Mat = new SimpleMaterial
        {
            AtlasSize = new Vector2(tileSizeInAtlas, tileSizeInAtlas),
            AtlasOffset = new Vector2(tileOffset.X * tileSizeInAtlas, tileOffset.Y * tileSizeInAtlas)
        };
    }
}

public class IcePrefab : GameObject
{
    public IcePrefab(float x, float y, float w, float h, Vector2 tileOffset) : base("Ice")
    {
        Transform.Position = new Vector3(x, y, 0);
        Transform.Size = new Vector2(w, h);

        var col = AddComponent<COG_Collider>();
        col.IsSolid = true;
        col.Friction = 0f; // <--- C'EST DE LA GLACE !

        var sprite = AddComponent<COG_Sprite>();
        float tileSizeInAtlas = 64f / 512f;
        sprite.Mat = new StretchMaterial
        {
            TextureBaseSize = GameConfig.BaseTileSize,
            AtlasSize = new Vector2(tileSizeInAtlas, tileSizeInAtlas),
            AtlasOffset = new Vector2(tileOffset.X * tileSizeInAtlas, tileOffset.Y * tileSizeInAtlas)
        };
        // On lui donne une teinte bleutée pour le différencier des murs normaux
        sprite.Color = new Vector4(0.6f, 0.9f, 1f, 1f);
    }
}

public class CamZonePrefab : GameObject
{
    public CamZonePrefab(float x, float y, float w, float h, Camera2D cam) : base("CamZone")
    {
        Transform.Position = new Vector3(x, y, 0);
        Transform.Size = new Vector2(w, h);

        var col = AddComponent<COG_Collider>();
        col.IsSolid = false;
        col.IsTrigger = true;

        var zone = AddComponent<COG_CameraZone>();
        zone.Camera = cam;

        var sprite = AddComponent<COG_Sprite>();
        sprite.IsVisible = false;
        sprite.Color = new Vector4(1f, 1f, 0f, 1f);
    }
}

public class CoinPrefab : GameObject
{
    public CoinPrefab(float x, float y, float w, float h) : base("Coin")
    {
        Transform.Position = new Vector3(x, y, 0);
        Transform.Size = new Vector2(w, h);

        var col = AddComponent<COG_Collider>();
        col.IsSolid = false;
        col.IsTrigger = true;

        var sprite = AddComponent<COG_Sprite>();
        sprite.Color = new Vector4(1f, 0.8f, 0f, 1f);

        AddComponent<COG_CoinBehavior>();
    }
}

public class TriggerZonePrefab : GameObject
{
    public TriggerZonePrefab(float x, float y, float w, float h) : base("TriggerZone")
    {
        Transform.Position = new Vector3(x, y, 0);
        Transform.Size = new Vector2(w, h);

        var col = AddComponent<COG_Collider>();
        col.IsSolid = false;
        col.IsTrigger = true;

        var sprite = AddComponent<COG_Sprite>();
        sprite.IsVisible = false;
        sprite.Color = new Vector4(1f, 0f, 0f, 1f);

        AddComponent<COG_TriggerBehavior>();
    }
}