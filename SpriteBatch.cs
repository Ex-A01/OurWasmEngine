using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Numerics;

public class SpriteBatch : IDisposable
{
    private GL _gl;
    private const int MAX_SPRITES = 2000;
    private const int MAX_VERTICES = MAX_SPRITES * 4;
    private const int MAX_INDICES = MAX_SPRITES * 6;

    private uint _vao;
    private uint _vbo;
    private uint _ebo;

    private VertexPositionColorTexture[] _vertices;
    private int _spriteCount = 0;

    private Texture _currentTexture;
    private Shader _currentShader;

    // --- NOUVEAU : STRUCTURE DE STOCKAGE TEMPORAIRE ---
    // Cette structure retient toutes les infos d'un dessin avant qu'on génère la géométrie
    private struct SpriteInfo : IComparable<SpriteInfo>
    {
        public Texture Texture;
        public Vector2 Position;
        public Vector2 Size;
        public Vector4 Color;
        public float Rotation;
        public Vector2 Origin;
        public Vector2 UVOffset;
        public Vector2 UVSize;
        public float Depth;

        // C'est grâce à ça que la liste saura comment se trier !
        public int CompareTo(SpriteInfo other)
        {
            // On trie par profondeur (du plus petit au plus grand)
            // Si les profondeurs sont égales, on trie par texture (optimisation GPU extrême !)
            int depthCompare = Depth.CompareTo(other.Depth);
            if (depthCompare == 0 && Texture != null && other.Texture != null)
            {
                return Texture.GetHashCode().CompareTo(other.Texture.GetHashCode());
            }
            return depthCompare;
        }
    }

    private List<SpriteInfo> _spritesToDraw = new List<SpriteInfo>();
    // ------------------------------------------------

    public unsafe SpriteBatch(GL gl)
    {
        // ... (GARDE TON CODE EXACT D'INITIALISATION DU CONSTRUCTEUR ICI : _vao, _vbo, _ebo, _vertices, etc.)
        _gl = gl;
        _vertices = new VertexPositionColorTexture[MAX_VERTICES];

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(MAX_VERTICES * sizeof(VertexPositionColorTexture)), null, BufferUsageARB.DynamicDraw);

        uint vertexSize = (uint)sizeof(VertexPositionColorTexture);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, vertexSize, (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, vertexSize, (void*)(3 * sizeof(float)));

        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, vertexSize, (void*)(7 * sizeof(float)));

        uint[] indices = new uint[MAX_INDICES];
        uint offset = 0;
        for (int i = 0; i < MAX_INDICES; i += 6)
        {
            indices[i + 0] = offset + 0;
            indices[i + 1] = offset + 1;
            indices[i + 2] = offset + 2;
            indices[i + 3] = offset + 2;
            indices[i + 4] = offset + 3;
            indices[i + 5] = offset + 0;
            offset += 4;
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* iPtr = indices)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(MAX_INDICES * sizeof(uint)), iPtr, BufferUsageARB.StaticDraw);
        }

        _gl.BindVertexArray(0);
    }

    public void Begin(Shader shader, Matrix4X4<float> projection, Matrix4X4<float> view)
    {
        _currentShader = shader;
        _currentShader.Use();
        _currentShader.SetUniform("uProjection", projection);
        _currentShader.SetUniform("uView", view);

        _spritesToDraw.Clear(); // NOUVEAU : On vide la liste au début !
    }

    // NOUVEAU : La méthode Draw ne dessine plus, elle enregistre !
    // Ajout du paramètre "depth" avec 0f par défaut
    public void Draw(Texture texture, Vector2 position, Vector2 size, Vector4 color, float rotation = 0f, Vector2? origin = null, Vector2? uvOffset = null, Vector2? uvSize = null, float depth = 0f)
    {
        _spritesToDraw.Add(new SpriteInfo
        {
            Texture = texture,
            Position = position,
            Size = size,
            Color = color,
            Rotation = rotation,
            Origin = origin ?? new Vector2(size.X / 2f, size.Y / 2f),
            UVOffset = uvOffset ?? Vector2.Zero,
            UVSize = uvSize ?? Vector2.One,
            Depth = depth
        });
    }

    // NOUVEAU : C'est le End() qui fait tout le travail CPU !
    public void End()
    {
        // 1. On trie tous les sprites du fond vers l'avant !
        _spritesToDraw.Sort();

        // 2. On génère la géométrie dans le bon ordre
        foreach (var sprite in _spritesToDraw)
        {
            if (_spriteCount >= MAX_SPRITES || (_currentTexture != null && _currentTexture != sprite.Texture))
            {
                Flush();
            }

            _currentTexture = sprite.Texture;

            float left = -sprite.Origin.X;
            float right = sprite.Size.X - sprite.Origin.X;
            float top = -sprite.Origin.Y;
            float bottom = sprite.Size.Y - sprite.Origin.Y;

            float cos = MathF.Cos(sprite.Rotation);
            float sin = MathF.Sin(sprite.Rotation);

            Vector3 tl = new Vector3(sprite.Position.X + (left * cos - top * sin), sprite.Position.Y + (left * sin + top * cos), 0);
            Vector3 tr = new Vector3(sprite.Position.X + (right * cos - top * sin), sprite.Position.Y + (right * sin + top * cos), 0);
            Vector3 bl = new Vector3(sprite.Position.X + (left * cos - bottom * sin), sprite.Position.Y + (left * sin + bottom * cos), 0);
            Vector3 br = new Vector3(sprite.Position.X + (right * cos - bottom * sin), sprite.Position.Y + (right * sin + bottom * cos), 0);

            int vertexIndex = _spriteCount * 4;

            _vertices[vertexIndex + 0] = new VertexPositionColorTexture(tr, sprite.Color, new Vector2(sprite.UVOffset.X + sprite.UVSize.X, sprite.UVOffset.Y));
            _vertices[vertexIndex + 1] = new VertexPositionColorTexture(br, sprite.Color, new Vector2(sprite.UVOffset.X + sprite.UVSize.X, sprite.UVOffset.Y + sprite.UVSize.Y));
            _vertices[vertexIndex + 2] = new VertexPositionColorTexture(bl, sprite.Color, new Vector2(sprite.UVOffset.X, sprite.UVOffset.Y + sprite.UVSize.Y));
            _vertices[vertexIndex + 3] = new VertexPositionColorTexture(tl, sprite.Color, new Vector2(sprite.UVOffset.X, sprite.UVOffset.Y));

            _spriteCount++;
        }

        // On vide la dernière fournée
        Flush();
    }

    // Le Flush reste exactement le même qu'avant !
    private unsafe void Flush()
    {
        if (_spriteCount == 0 || _currentTexture == null) return;

        _currentTexture.Bind(TextureUnit.Texture0);
        _currentShader.SetUniform("uTexture", 0);

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        fixed (VertexPositionColorTexture* ptr = _vertices)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(_spriteCount * 4 * sizeof(VertexPositionColorTexture)), ptr);
        }

        _gl.DrawElements(PrimitiveType.Triangles, (uint)(_spriteCount * 6), DrawElementsType.UnsignedInt, (void*)0);

        _spriteCount = 0;
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
    }
}