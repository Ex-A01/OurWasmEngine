using Silk.NET.Input;
using System.IO;
using System.Numerics;
using System.Text.Json;

public static class SceneLoader
{
    public static Scene LoadSceneFromJson(string filePath, IKeyboard keyboard, IMouse mouse)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException($"Le fichier {filePath} est introuvable !");

        string jsonString = File.ReadAllText(filePath);
        using JsonDocument doc = JsonDocument.Parse(jsonString);
        JsonElement root = doc.RootElement;

        string sceneName = root.GetProperty("Name").GetString();
        float gravity = root.GetProperty("Gravity").GetSingle();

        Scene scene = new Scene(sceneName, gravity);

        // CRUCIAL : On active la scène MAINTENANT, car quand on va ajouter les COG_Collider
        // ils vont appeler Awake() et chercher SceneManager.CurrentScene.PhysicsWorld !
        SceneManager.LoadScene(scene);

        JsonElement gameObjects = root.GetProperty("GameObjects");
        foreach (JsonElement goElement in gameObjects.EnumerateArray())
        {
            string goName = goElement.GetProperty("Name").GetString();
            GameObject go = new GameObject(goName);

            if (goElement.TryGetProperty("Uid", out JsonElement uidElement))
            {
                go.Uid = uidElement.GetString();
            }

            // 1. Gérer le Transform
            JsonElement transform = goElement.GetProperty("Transform");
            go.Transform.Position = new Vector3(transform.GetProperty("X").GetSingle(), transform.GetProperty("Y").GetSingle(), 0);
            go.Transform.Size = new Vector2(transform.GetProperty("W").GetSingle(), transform.GetProperty("H").GetSingle());

            // 2. Gérer les Composants
            if (goElement.TryGetProperty("Components", out JsonElement components))
            {
                foreach (JsonElement compElement in components.EnumerateArray())
                {
                    string type = compElement.GetProperty("Type").GetString();
                    ParseComponent(go, type, compElement, keyboard, mouse);
                }
            }

            scene.AddGameObject(go);
        }

        return scene;
    }

    private static void ParseComponent(GameObject go, string type, JsonElement data, IKeyboard kb, IMouse mouse)
    {
        switch (type)
        {
            case "COG_Collider":
                var col = go.AddComponent<COG_Collider>();
                if (data.TryGetProperty("BodyType", out var bType))
                {
                    if (bType.GetString() == "Dynamic") col.BodyType = nkast.Aether.Physics2D.Dynamics.BodyType.Dynamic;
                    else if (bType.GetString() == "Kinematic") col.BodyType = nkast.Aether.Physics2D.Dynamics.BodyType.Kinematic;
                    else col.BodyType = nkast.Aether.Physics2D.Dynamics.BodyType.Static;
                }

                // NOUVEAU : Lecture de la forme
                if (data.TryGetProperty("Shape", out var shapeProp))
                {
                    if (System.Enum.TryParse<ColliderShape>(shapeProp.GetString(), out var parsedShape))
                    {
                        col.Shape = parsedShape;
                    }
                }

                if (data.TryGetProperty("IsSolid", out var isSolid)) col.IsSolid = isSolid.GetBoolean();
                if (data.TryGetProperty("IsTrigger", out var isTrig)) col.IsTrigger = isTrig.GetBoolean();
                if (data.TryGetProperty("Friction", out var fric)) col.Friction = fric.GetSingle();
                if (data.TryGetProperty("Restitution", out var rest)) col.Restitution = rest.GetSingle();
                break;

            case "COG_Sprite":
                var spr = go.AddComponent<COG_Sprite>();
                if (data.TryGetProperty("TexturePath", out var tex)) spr.TexturePath = tex.GetString();
                if (data.TryGetProperty("LayerDepth", out var depth)) spr.LayerDepth = depth.GetSingle();
                if (data.TryGetProperty("IsVisible", out var vis)) spr.IsVisible = vis.GetBoolean();

                if (data.TryGetProperty("Color", out var cArr))
                    spr.Color = new Vector4(cArr[0].GetSingle(), cArr[1].GetSingle(), cArr[2].GetSingle(), cArr[3].GetSingle());

                if (data.TryGetProperty("MatType", out var matType) && matType.GetString() == "Stretch")
                {
                    var mat = new StretchMaterial();
                    if (data.TryGetProperty("TextureBaseSize", out var tbs)) mat.TextureBaseSize = tbs.GetSingle();
                    spr.Mat = mat;
                }

                if (data.TryGetProperty("AtlasOffset", out var oArr))
                    spr.Mat.AtlasOffset = new Vector2(oArr[0].GetSingle(), oArr[1].GetSingle());

                if (data.TryGetProperty("AtlasSize", out var sArr))
                    spr.Mat.AtlasSize = new Vector2(sArr[0].GetSingle(), sArr[1].GetSingle());
                break;

            case "COG_PlayerController":
                var ctrl = go.AddComponent<COG_PlayerController>();
                ctrl.Keyboard = kb;
                ctrl.Mouse = mouse;
                if (data.TryGetProperty("Speed", out var spd)) ctrl.Speed = spd.GetSingle();
                if (data.TryGetProperty("JumpImpulse", out var jmp)) ctrl.JumpImpulse = jmp.GetSingle();
                break;

            case "COG_RopeClimber":
                go.AddComponent<COG_RopeClimber>();
                break;

            case "COG_CameraZone":
                var camZone = go.AddComponent<COG_CameraZone>();
                camZone.Camera = SceneManager.CurrentScene.Camera;
                break;

            case "COG_CoinBehavior":
                go.AddComponent<COG_CoinBehavior>();
                break;

            case "COG_TriggerBehavior":
                var trigger = go.AddComponent<COG_TriggerBehavior>();
                if (data.TryGetProperty("TargetUid", out var targetUid)) trigger.TargetUid = targetUid.GetString();
                if (data.TryGetProperty("MessageToSend", out var msgToSend)) trigger.MessageToSend = msgToSend.GetString();
                if (data.TryGetProperty("TriggerOnce", out var trigOnce)) trigger.TriggerOnce = trigOnce.GetBoolean();
                break;

            case "COG_AudioSource":
                var audio = go.AddComponent<COG_AudioSource>();
                if (data.TryGetProperty("SoundFontPath", out var sfProp)) audio.SoundFontPath = sfProp.GetString();
                if (data.TryGetProperty("MidiPath", out var midiProp)) audio.MidiPath = midiProp.GetString();
                if (data.TryGetProperty("Volume", out var volProp)) audio.Volume = (float)volProp.GetDouble();
                if (data.TryGetProperty("Loop", out var loopProp)) audio.Loop = loopProp.GetBoolean();
                // NOUVEAU
                if (data.TryGetProperty("PlayOnAwake", out var playAwakeProp)) audio.PlayOnAwake = playAwakeProp.GetBoolean();
                break;

            default:
                Console.WriteLine($"[AVERTISSEMENT] Composant inconnu dans le JSON : {type}");
                break;
        }
    }

}