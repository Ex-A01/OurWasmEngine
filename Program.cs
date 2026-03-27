using Silk.NET.Windowing;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Maths;
using Silk.NET.Windowing.Sdl;
using Silk.NET.Input.Sdl;


Console.WriteLine("Hello, World!");

SdlWindowing.Use();
SdlInput.Use();

MyGame game = new MyGame();
game.Run();