using System.Numerics;
// On donne un alias pour éviter les collisions de noms dans ce fichier
using AetherVec2 = nkast.Aether.Physics2D.Common.Vector2;

public static class PhysicsExtensions
{
    // Convertit un Vector2 System.Numerics vers Aether
    public static AetherVec2 ToAether(this Vector2 vec)
    {
        return new AetherVec2(vec.X, vec.Y);
    }

    // Convertit un Vector2 Aether vers System.Numerics
    public static Vector2 ToNumeric(this AetherVec2 vec)
    {
        return new Vector2(vec.X, vec.Y);
    }
}