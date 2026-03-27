using System.Runtime.InteropServices;
using System.Numerics;

[StructLayout(LayoutKind.Sequential)]
public struct VertexPositionColorTexture
{
    public Vector3 Position;
    public Vector4 Color;
    public Vector2 TexCoords;

    public VertexPositionColorTexture(Vector3 position, Vector4 color, Vector2 texCoords)
    {
        Position = position;
        Color = color;
        TexCoords = texCoords;
    }
}