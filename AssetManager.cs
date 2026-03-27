using Silk.NET.OpenGLES;
using System.Collections.Generic;

public static class AssetManager
{
    private static GL _gl;
    private static Dictionary<string, Texture> _textures = new Dictionary<string, Texture>();

    // À appeler une seule fois dans le OnLoad() de MyGame
    public static void Initialize(GL gl)
    {
        _gl = gl;
    }

    public static Texture GetTexture(string path)
    {
        if (_gl == null) throw new System.Exception("AssetManager non initialisé !");

        path = path.Replace('\\', '/');

        // 2. Si le chemin dans le JSON a oublié le dossier, on le corrige automatiquement
        if (!path.StartsWith("assets/"))
        {
            path = "assets/" + path;
        }

        if (_textures.TryGetValue(path, out var texture))
        {
            return texture;
        }

        // Si elle n'existe pas, on la charge et on la met en cache
        var newTexture = new Texture(_gl, path);
        _textures.Add(path, newTexture);
        return newTexture;
    }

    public static void DisposeAll()
    {
        foreach (var tex in _textures.Values)
        {
            tex.Dispose();
        }
        _textures.Clear();
    }
}