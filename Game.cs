using MeltySynth;
using nkast.Aether.Physics2D.Dynamics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenAL;
using Silk.NET.OpenGLES;
using System;
using System.Collections.Generic;
using System.Numerics;

public class MyGame : IDisposable
{
    private GL _gl;
    private Matrix4X4<float> _projection;
    private Shader _shader;

    public static World PhysicsWorld { get; private set; }
    private SpriteBatch _spriteBatch;
    private float _totalTime = 0f;

    // Curseur virtuel conservé, mais nous le mettrons à jour via JS Interop plus tard
    public class VirtualCursor
    {
        public int DeviceId;
        public Vector2 Position;
        public Vector4 Color;
        public bool LeftDown;
        public bool RightDown;
        public bool MiddleDown;

        public VirtualCursor(int id, Vector2 startPos, Vector4 color)
        {
            DeviceId = id;
            Position = startPos;
            Color = color;
        }
    }

    public static List<VirtualCursor> Cursors = new List<VirtualCursor>();
    private Vector4[] _cursorColors = { GameConfig.CursorColor, new Vector4(1f, 0f, 0f, 1f) };

    // 1. On injecte GL directement, plus besoin de créer de fenêtre !
    public MyGame(GL gl)
    {
        _gl = gl;
    }

    // 2. Initialisation explicite (remplace _window.Load)
    public void Load()
    {
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        AssetManager.Initialize(_gl);
        AudioManager.Initialize();

        _spriteBatch = new SpriteBatch(_gl);

        _gl.ClearColor(GameConfig.BackgroundColor.X, GameConfig.BackgroundColor.Y, GameConfig.BackgroundColor.Z, GameConfig.BackgroundColor.W);

        // La projection initiale
        Interop.GameInstance = this;

        /*Console.WriteLine("--- DÉBOGAGE DU SYSTÈME DE FICHIERS VIRTUEL ---");
        Console.WriteLine($"Répertoire de base : {AppContext.BaseDirectory}");
        try
        {
            // On fouille depuis la racine du disque dur virtuel
            string[] allFiles = System.IO.Directory.GetFiles("/", "*.*", System.IO.SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                Console.WriteLine(file);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erreur de lecture du VFS : " + ex.Message);
        }
        Console.WriteLine("-----------------------------------------------");*/

        _shader = new Shader(_gl, "assets/base.vert", "assets/base.frag");

        // Ajout d'un curseur par défaut (pour l'instant la souris du navigateur)
        Cursors.Add(new VirtualCursor(0, new Vector2(GameConfig.WindowWidth / 2f, GameConfig.WindowHeight / 2f), _cursorColors[0]));

        try
        {
            // ATTENTION : Les inputs Silk sont cassés sur le Web. 
            // On passe "null" temporairement, on refera l'input juste après.
            SceneLoader.LoadSceneFromJson("assets/levels/Paff.json", null, null);
            Console.WriteLine($"[SCENE LOADED] {SceneManager.CurrentScene.Name} chargée avec succès !");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERREUR CRITIQUE] Impossible de charger le niveau : {ex.Message}");
        }
    }

    // 3. Gestion du redimensionnement du canvas HTML
    public void Resize(int width, int height)
    {
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _projection = Matrix4x4.CreateOrthographicOffCenter(0f, width, height, 0f, -1.0f, 1.0f).ToGeneric();
    }

    // 4. Update logique (remplace _window.Update)
    public void Update(float deltaTime)
    {
        float dt = Math.Min(deltaTime, 0.05f);
        _totalTime += dt;

        SceneManager.CurrentScene?.Update(dt);
    }

    // 5. Update graphique (remplace _window.Render)
    public void Render()
    {
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        // --- DESSIN DE LA SCÈNE --- (Rien n'est cassé ici, ça marche tout seul !)
        if (SceneManager.CurrentScene != null)
        {
            _spriteBatch.Begin(_shader, _projection, SceneManager.CurrentScene.Camera.GetViewMatrix().ToGeneric());
            SceneManager.CurrentScene.Draw(_spriteBatch, _totalTime);
            _spriteBatch.End();
        }

        // --- DESSIN DE L'UI / CURSEURS ---
        var identity = Matrix4X4<float>.Identity;
        _spriteBatch.Begin(_shader, _projection, identity);

        Texture atlasTex = AssetManager.GetTexture("assets/atlas.png");
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
        _spriteBatch?.Dispose();
        AssetManager.DisposeAll();
        AudioManager.Dispose();
        _shader?.Dispose();
        // On ne dispose plus GL ni Input ici, c'est le Program.cs qui gère le contexte global
    }
}

//██████████████████████████████████
//████      CONFIG & UTILS      ████
//██████████████████████████████████

public static class GameConfig
{
    public static float WindowWidth = 800f;
    public static float WindowHeight = 600f;
    public static bool DebugMode = true;

    public const float PixelsPerMeter = 50f;
    public const float MetersPerPixel = 1f / PixelsPerMeter;
    public const float PhysicsGravity = 20f;

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
    SlopeRight,
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
    public virtual void Draw(SpriteBatch spriteBatch, float gameTime) { }
    public virtual void OnOverlap(GameObject other) { }

    public virtual void ReceiveMessage(string message)
    {
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

    public void SendMessage(string message)
    {
        foreach (var comp in _components)
        {
            comp.ReceiveMessage(message);
        }
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
    public string TexturePath { get; set; } = "assets/atlas.png";
    public float LayerDepth { get; set; } = 0.5f;

    public override void Draw(SpriteBatch spriteBatch, float gameTime)
    {
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

        if (!IsVisible && GameConfig.DebugMode)
        {
            Texture pixelTex = AssetManager.GetTexture("assets/pixel.png");
            float t = 2f;
            float w = Transform.Size.X;
            float h = Transform.Size.Y;
            Vector2 pos = new Vector2(Transform.Position.X, Transform.Position.Y);
            Vector2 zeroOrigin = Vector2.Zero;

            spriteBatch.Draw(pixelTex, pos, new Vector2(w, t), drawColor, 0f, zeroOrigin);
            spriteBatch.Draw(pixelTex, new Vector2(pos.X, pos.Y + h - t), new Vector2(w, t), drawColor, 0f, zeroOrigin);
            spriteBatch.Draw(pixelTex, pos, new Vector2(t, h), drawColor, 0f, zeroOrigin);
            spriteBatch.Draw(pixelTex, new Vector2(pos.X + w - t, pos.Y), new Vector2(t, h), drawColor, 0f, zeroOrigin);
            return;
        }

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
                    Vector2 tilePos = new Vector2(
                        Transform.Position.X + (x * tileSize) + tileOrigin.X,
                        Transform.Position.Y + (y * tileSize) + tileOrigin.Y
                    );

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

    private ColliderShape _shape = ColliderShape.Rectangle;
    public ColliderShape Shape
    {
        get => _shape;
        set
        {
            _shape = value;
            if (PhysicsBody != null) RebuildPhysicsBody();
        }
    }

    public Body PhysicsBody { get; private set; }

    public override void Awake()
    {
        RebuildPhysicsBody();
    }

    public void RebuildPhysicsBody()
    {
        if (PhysicsBody != null && SceneManager.CurrentScene?.PhysicsWorld != null)
        {
            SceneManager.CurrentScene.PhysicsWorld.Remove(PhysicsBody);
        }

        Vector2 centerPx = new Vector2(Transform.Position.X + Transform.Size.X / 2f, Transform.Position.Y + Transform.Size.Y / 2f);
        var centerMeters = (centerPx * GameConfig.MetersPerPixel).ToAether();

        float widthMeters = Transform.Size.X * GameConfig.MetersPerPixel;
        float heightMeters = Transform.Size.Y * GameConfig.MetersPerPixel;

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
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(-hw, hh));
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(hw, hh));
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(hw, -hh));
            }
            else if (_shape == ColliderShape.SlopeLeft)
            {
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(-hw, -hh));
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(-hw, hh));
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(hw, hh));
            }
            else if (_shape == ColliderShape.PlayerBox)
            {
                float chamfer = Math.Min(hw, hh) * 0.2f;

                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(-hw, -hh));
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(-hw, hh - chamfer));
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(-hw + chamfer, hh));
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(hw - chamfer, hh));
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(hw, hh - chamfer));
                vertices.Add(new nkast.Aether.Physics2D.Common.Vector2(hw, -hh));
            }

            PhysicsBody = SceneManager.CurrentScene.PhysicsWorld.CreatePolygon(
                vertices, 1f, centerMeters, 0f, _bodyType);
        }

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
    Climbing,
    Ragdoll
}

public class COG_PlayerController : Component
{
    public float Speed { get; set; } = 6f;
    public float JumpImpulse { get; set; } = 15f;
    public IKeyboard Keyboard { get; set; }
    public IMouse Mouse { get; set; }

    public PlayerState State { get; private set; } = PlayerState.Normal;
    private float _fallTimer = 0f;
    public float FallRagdollThreshold { get; set; } = 0.8f;

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

                    var otherFixture = contact.FixtureA.Body == _body ? contact.FixtureB : contact.FixtureA;
                    _currentGroundFriction = otherFixture.Friction;
                    break;
                }
            }
            contactEdge = contactEdge.Next;
        }

        // Remplace les Keyboard?.IsKeyPressed par les booléens de l'Interop :
        bool isJumpAction = Interop.SpacePressed || Interop.UpPressed ||
                            (MyGame.Cursors.Count > 0 && MyGame.Cursors[0].RightDown);

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
        float targetVelX = 0f;
        if (Interop.LeftPressed) targetVelX -= Speed;
        if (Interop.RightPressed) targetVelX += Speed;

        float lerpSpeed;
        if (!isGrounded)
            lerpSpeed = 5f;
        else if (_currentGroundFriction < 0.1f)
            lerpSpeed = 1.5f;
        else
            lerpSpeed = 25f;

        float newVelX = _body.LinearVelocity.X + (targetVelX - _body.LinearVelocity.X) * (lerpSpeed * deltaTime);
        _body.LinearVelocity = new nkast.Aether.Physics2D.Common.Vector2(newVelX, _body.LinearVelocity.Y);

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
                float randomSpin = (float)(rnd.NextDouble()) * 4f;
                _body.ApplyAngularImpulse(randomSpin);
            }
        }
        else
        {
            _fallTimer = 0f;
        }

        if (Interop.CPressed)
        {
            SwitchState(PlayerState.Climbing);
            _body.ApplyLinearImpulse(new nkast.Aether.Physics2D.Common.Vector2(0, -10f));
        }
    }

    private void HandleClimbingMode(float deltaTime, bool isGrounded)
    {
        _body.IgnoreGravity = false;

        if (Interop.NPressed)
        {
            SwitchState(PlayerState.Ragdoll);
            Random rnd = new Random();
            float randomSpin = (float)(rnd.NextDouble()) * 0.5f;
            _body.ApplyAngularImpulse(randomSpin);
        }
    }

    private void HandleRagdollMode(float deltaTime, bool isGrounded, bool isJumpAction)
    {
        _body.FixedRotation = false;

        if (isGrounded && Math.Abs(_body.LinearVelocity.X) < 1.5f && Math.Abs(_body.LinearVelocity.Y) < 1.5f)
        {
            if (isJumpAction && !_wasJumpPressed)
            {
                SwitchState(PlayerState.Normal);
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
                _body.FixedRotation = true;
                _body.Rotation = 0f;
                break;

            case PlayerState.Climbing:
                collider.Friction = 0f;
                collider.Restitution = 0f;
                _body.AngularDamping = 1f;
                _body.LinearDamping = 0.5f;
                _body.FixedRotation = false;
                break;

            case PlayerState.Ragdoll:
                collider.Friction = 0.3f;
                collider.Restitution = 0.3f;
                _body.AngularDamping = 1.0f;
                _body.LinearDamping = 0.4f;
                _body.FixedRotation = false;
                break;
        }
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
        }
    }
}

public class COG_RopeClimber : Component
{
    public float MaxTotalRopeLength { get; set; } = 15f;
    public float WinchSpeed { get; set; } = 8f;
    public float RopeStiffness { get; set; } = 150f;

    public class Rope
    {
        public int MouseId = -1; // Remplacement du Handle
        public Vector2 AnchorPointPx;
        public float DesiredLength;
        public bool IsAttached = false;
        public byte _id;
        public bool WasPullInput = false;

        public Rope(byte id) { _id = id; }
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

        // Mapping des IDs des souris Silk.NET
        if (MyGame.Cursors.Count > 0) LeftArm.MouseId = MyGame.Cursors[0].DeviceId;
        if (MyGame.Cursors.Count > 1) RightArm.MouseId = MyGame.Cursors[1].DeviceId;

        if (MyGame.Cursors.Count == 1) RightArm.MouseId = MyGame.Cursors[0].DeviceId;

        HandleArm(LeftArm, deltaTime);
        HandleArm(RightArm, deltaTime);
    }

    private void HandleArm(Rope arm, float deltaTime)
    {
        if (arm.MouseId == -1) return;

        var cursor = MyGame.Cursors.Find(c => c.DeviceId == arm.MouseId);
        if (cursor == null) return;

        Vector2 targetWorld = SceneManager.CurrentScene.Camera.ScreenToWorld(cursor.Position);

        bool pullInput;
        bool releaseInput;

        if (arm._id == 1)
        {
            pullInput = cursor.RightDown;
            releaseInput = cursor.LeftDown;
        }
        else
        {
            pullInput = cursor.LeftDown;
            releaseInput = cursor.RightDown;
        }

        bool pullJustPressed = pullInput && !arm.WasPullInput;
        arm.WasPullInput = pullInput;

        if (pullJustPressed)
        {
            if (arm.IsAttached) DetachRope(arm);
            TryAttachRope(arm, targetWorld);
        }

        if (cursor.MiddleDown) DetachRope(arm);

        if (arm.IsAttached)
        {
            if (pullInput) Winch(arm, 1f, deltaTime);
            if (releaseInput) Winch(arm, -1f, deltaTime);

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
            }
        }
    }

    private void DetachRope(Rope arm)
    {
        if (arm.IsAttached)
        {
            arm.IsAttached = false;
            arm.DesiredLength = 0f;
        }
    }

    private void Winch(Rope arm, float direction, float deltaTime)
    {
        arm.DesiredLength -= direction * WinchSpeed * deltaTime;
        if (arm.DesiredLength < 0.5f) arm.DesiredLength = 0.5f;
    }

    private void ApplyRopeForce(Rope arm)
    {
        var anchorMeters = (arm.AnchorPointPx * GameConfig.MetersPerPixel).ToAether();
        var playerMeters = _playerBody.Position;

        var direction = anchorMeters - playerMeters;
        float currentDistance = direction.Length();

        if (currentDistance > arm.DesiredLength && currentDistance > 0.01f)
        {
            direction.Normalize();

            float stretch = currentDistance - arm.DesiredLength;
            float forceMagnitude = RopeStiffness * stretch;

            var force = direction * forceMagnitude;
            _playerBody.ApplyForce(force);
        }
    }

    public override void Draw(SpriteBatch spriteBatch, float gameTime)
    {
        if (_playerBody == null) return;
        Vector3 physicalCenter = new Vector3(PlayerCenterPx.X, PlayerCenterPx.Y, 0);
        Texture pixelTex = AssetManager.GetTexture("assets/pixel.png");
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
    private const int BUFFER_SIZE = 1024;
    private const int SAMPLE_RATE = 22050;

    private Synthesizer _synth;
    private MidiFileSequencer _sequencer;

    private float[] _leftBuffer;
    private float[] _rightBuffer;
    private short[] _interleavedBuffer;

    private bool _isInitialized = false;
    private bool _isPlaying = false;

    public override void Awake() { }

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

            _isPlaying = PlayOnAwake;
            if (_isPlaying)
            {
                AudioManager.AL.SourcePlay(_sourceId);
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
            //FillBuffer(bufferId);
            AudioManager.AL.SourceQueueBuffers(_sourceId, 1, &bufferId);
            processed--;
        }

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
                if (!_isPlaying)
                {
                    _isPlaying = true;
                    AudioManager.AL.SourcePlay(_sourceId);
                }
                break;
            case "pause":
                _isPlaying = false;
                AudioManager.AL.SourcePause(_sourceId);
                break;
            case "stop":
                _isPlaying = false;
                AudioManager.AL.SourceStop(_sourceId);
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
}

//██████████████████████████████████
//████   PREFABS (Factoires)    ████
//██████████████████████████████████

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
        col.BodyType = BodyType.Dynamic;
        col.Friction = 0.0f;

        var sprite = AddComponent<COG_Sprite>();
        sprite.Color = GameConfig.PlayerColor;
        sprite.Mat = new SimpleMaterial
        {
            AtlasSize = new Vector2(0.125f, 0.125f),
            AtlasOffset = new Vector2(4 * 0.125f, 0f)
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
        col.Friction = 0.5f;
        col.Shape = slopeType;

        var sprite = AddComponent<COG_Sprite>();
        float tileSizeInAtlas = 64f / 512f;
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
        col.Friction = 0f;

        var sprite = AddComponent<COG_Sprite>();
        float tileSizeInAtlas = 64f / 512f;
        sprite.Mat = new StretchMaterial
        {
            TextureBaseSize = GameConfig.BaseTileSize,
            AtlasSize = new Vector2(tileSizeInAtlas, tileSizeInAtlas),
            AtlasOffset = new Vector2(tileOffset.X * tileSizeInAtlas, tileOffset.Y * tileSizeInAtlas)
        };
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