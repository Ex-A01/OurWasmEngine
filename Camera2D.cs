using System.Numerics;

public class OldCamera2D
{
    public Vector2 Position { get; set; } = Vector2.Zero;
    public float Zoom { get; set; } = 1f;
    public float Rotation { get; set; } = 0f;

    private float _screenWidth;
    private float _screenHeight;

    public OldCamera2D(float screenWidth, float screenHeight)
    {
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
    }

    public Matrix4x4 GetViewMatrix()
    {
        // 1. On déplace le monde dans la direction opposée à la caméra
        var translation = Matrix4x4.CreateTranslation(-Position.X, -Position.Y, 0f);

        // 2. On applique la rotation (autour de l'axe Z pour la 2D)
        var rotation = Matrix4x4.CreateRotationZ(Rotation);

        // 3. On applique le zoom
        var scale = Matrix4x4.CreateScale(Zoom, Zoom, 1f);

        // 4. On décale le résultat pour que la caméra "regarde" le centre de l'écran 
        // (Sinon, la cible de la caméra serait collée en haut à gauche de la fenêtre)
        var centerScreen = Matrix4x4.CreateTranslation(_screenWidth / 2f, _screenHeight / 2f, 0f);

        // L'ordre des multiplications est strictement de gauche à droite !
        return translation * rotation * scale * centerScreen;
    }

    public MyGame.AABB? CurrentBounds { get; set; }

    // Fonction utilitaire pour un suivi de caméra fluide
    public void Follow(Vector2 targetPosition, float lerpSpeed, float dt)
    {
        // 1. Calcul de la position désirée avec le Lerp
        Vector2 desiredPosition = Vector2.Lerp(Position, targetPosition, lerpSpeed * dt);

        // 2. Application des limites si la caméra est dans une CamZone
        if (CurrentBounds.HasValue)
        {
            var bounds = CurrentBounds.Value;

            // On calcule la moitié de l'écran (en prenant en compte le zoom pour être robuste)
            float halfWidth = (_screenWidth / 2f) / Zoom;
            float halfHeight = (_screenHeight / 2f) / Zoom;

            // On définit les limites réelles pour le CENTRE de la caméra
            float minX = bounds.Left + halfWidth;
            // Math.Max évite un crash si la zone est plus petite que l'écran
            float maxX = Math.Max(minX, bounds.Right - halfWidth);

            float minY = bounds.Top + halfHeight;
            float maxY = Math.Max(minY, bounds.Bottom - halfHeight);

            // On restreint (clamp) la position désirée
            desiredPosition.X = Math.Clamp(desiredPosition.X, minX, maxX);
            desiredPosition.Y = Math.Clamp(desiredPosition.Y, minY, maxY);
        }

        Position = desiredPosition;
    }
}