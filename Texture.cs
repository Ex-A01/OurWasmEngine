using Silk.NET.OpenGL;
using StbImageSharp;
using System;
using System.IO;

public class Texture : IDisposable
{
    private uint _handle;
    private GL _gl;

    public unsafe Texture(GL gl, string path)
    {
        _gl = gl;

        // 1. On demande à OpenGL de nous créer un emplacement pour la texture
        _handle = _gl.GenTexture();

        // On "sélectionne" cette texture pour travailler dessus
        _gl.BindTexture(TextureTarget.Texture2D, _handle);

        // 2. CRUCIAL : OpenGL lit les images de bas en haut (0,0 en bas à gauche).
        // Les PNG sont de haut en bas. Il faut donc inverser l'image au chargement !
        //StbImage.stbi_set_flip_vertically_on_load(1);

        // 3. On charge l'image depuis le disque (en forçant le format RGBA)
        ImageResult image = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);

        // 4. On envoie les pixels bruts (byte) dans la VRAM de la carte graphique
        fixed (byte* ptr = image.Data)
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0, // Niveau de Mipmap de base
                InternalFormat.Rgba,
                (uint)image.Width,
                (uint)image.Height,
                0, // Bordure (toujours 0)
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                ptr
            );
        }

        // 5. Configuration du rendu
        // Nearest = Rendu Pixel Art (contours nets). Si tu veux lisser/flouter, mets Linear.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        // Pour un Atlas, ClampToEdge évite que les pixels bavent sur les bords
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        // 6. On dé-sélectionne la texture pour ne pas la modifier par erreur plus tard
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    // Méthode pour dire à OpenGL "Utilise cette texture maintenant"
    public void Bind(TextureUnit textureSlot = TextureUnit.Texture0)
    {
        _gl.ActiveTexture(textureSlot);
        _gl.BindTexture(TextureTarget.Texture2D, _handle);
    }

    public void Dispose()
    {
        _gl.DeleteTexture(_handle);
    }
}