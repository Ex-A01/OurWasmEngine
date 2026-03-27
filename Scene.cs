using nkast.Aether.Physics2D.Dynamics;
using System.Collections.Generic;
using System.Numerics;
using System;

public static class SceneManager
{
    public static Scene CurrentScene { get; private set; }

    public static void LoadScene(Scene newScene)
    {
        CurrentScene?.Dispose();
        CurrentScene = newScene;
    }
}

public class Scene : IDisposable
{
    public string Name { get; set; }
    public List<GameObject> GameObjects { get; private set; } = new List<GameObject>();

    // Chaque scène possède SA physique et SA caméra !
    public World PhysicsWorld { get; private set; }
    public Camera2D Camera { get; private set; }

    public Scene(string name, float gravity)
    {
        Name = name;
        PhysicsWorld = new World(new nkast.Aether.Physics2D.Common.Vector2(0f, gravity));
        Camera = new Camera2D(GameConfig.WindowWidth, GameConfig.WindowHeight);
    }

    public GameObject FindGameObjectByUid(string uid)
    {
        return GameObjects.Find(go => go.Uid == uid);
    }

    public void AddGameObject(GameObject go)
    {
        GameObjects.Add(go);
    }

    public void Update(float deltaTime)
    {
        PhysicsWorld.Step(deltaTime);
        foreach (var go in GameObjects) go.Update(deltaTime);

        // Nettoyage automatique des objets détruits
        GameObjects.RemoveAll(go =>
        {
            if (go.IsDestroyed)
            {
                var col = go.GetComponent<COG_Collider>();
                if (col != null && col.PhysicsBody != null) PhysicsWorld.Remove(col.PhysicsBody);
                return true;
            }
            return false;
        });

        // Hack temporaire pour suivre le joueur (sera remplacé par un composant CamFollow plus tard)
        var player = GameObjects.Find(g => g.Name == "Player");
        if (player != null)
        {
            Camera.Follow(new Vector2(player.Transform.Position.X, player.Transform.Position.Y), 5f, deltaTime);
        }
    }

    public void Draw(SpriteBatch spriteBatch, float gameTime)
    {
        foreach (var go in GameObjects) go.Draw(spriteBatch, gameTime);
    }

    public void Dispose()
    {
        PhysicsWorld.Clear();
        GameObjects.Clear();
    }
}