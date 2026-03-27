using System;
using System.IO;
using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

public class Shader : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;
    private bool _isDisposed = false;

    public Shader(GL gl, string vertexPath, string fragmentPath)
    {
        _gl = gl;

        string vertexSource = File.ReadAllText(vertexPath);
        string fragmentSource = File.ReadAllText(fragmentPath);

        uint vertex = LoadShader(ShaderType.VertexShader, vertexSource);
        uint fragment = LoadShader(ShaderType.FragmentShader, fragmentSource);

        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        _gl.LinkProgram(_handle);

        _gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            throw new Exception($"Error linking shader program: {_gl.GetProgramInfoLog(_handle)}");
        }

        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
    }

    public void Use() => _gl.UseProgram(_handle);

    public void SetUniform(string name, int value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"Uniform '{name}' not found in shader program.");
        }
        _gl.Uniform1(location, value);
    }

    public void Dispose()
    {
        // On vérifie qu'on n'a pas déjà détruit ce shader
        if (!_isDisposed)
        {
            // On supprime le programme de la mémoire de la carte graphique
            _gl.DeleteProgram(_handle);

            _isDisposed = true;

            // Indique au Garbage Collector qu'il n'a plus besoin de s'occuper de ça
            GC.SuppressFinalize(this);
        }
    }

    public void SetUniform(string name, float value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"Uniform '{name}' not found in shader program.");
        }
        _gl.Uniform1(location, value);
    }

    public void SetUniform(string name, Vector2 value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"Uniform '{name}' not found in shader program.");
        }
        _gl.Uniform2(location, value);
    }

    public void SetUniform(string name, Vector3 value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"Uniform '{name}' not found in shader program.");
        }
        _gl.Uniform3(location, value);
    }

    public void SetUniform(string name, Vector4 value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"Uniform '{name}' not found in shader program.");
        }
        _gl.Uniform4(location, value);
    }

    public void SetUniform(string name, Matrix4X4<float> value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"Uniform '{name}' not found in shader program.");
        }
        unsafe
        {
            _gl.UniformMatrix4(location, 1, false, (float*)&value);
        }
    }

    private uint LoadShader(ShaderType type, string source)
    {
        uint handle = _gl.CreateShader(type);
        _gl.ShaderSource(handle, source);
        _gl.CompileShader(handle);

        _gl.GetShader(handle, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            throw new Exception($"Error compiling {type} shader: {_gl.GetShaderInfoLog(handle)}");
        }

        return handle;
    }
}