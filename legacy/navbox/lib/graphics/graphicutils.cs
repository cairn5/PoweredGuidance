using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;



namespace lib.graphics;
// --- Helper methods ---

public partial class Visualizer
{
    private static void CreateSphere(out int vao, out int vbo, out int ebo, out int indexCount, float radius, int latSegments, int lonSegments)
    {
        List<float> vertices = new List<float>();
        List<uint> indices = new List<uint>();

        // Generate vertices
        for (int lat = 0; lat <= latSegments; lat++)
        {
            float theta = (float)Math.PI * lat / latSegments;
            float y = (float)Math.Cos(theta) * radius;
            float r = (float)Math.Sin(theta) * radius;

            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float phi = 2f * (float)Math.PI * lon / lonSegments;
                float x = (float)Math.Cos(phi) * r;
                float z = (float)Math.Sin(phi) * r;
                vertices.AddRange(new float[] { x, y, z });
            }
        }

        // Generate indices
        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                uint i0 = (uint)(lat * (lonSegments + 1) + lon);
                uint i1 = i0 + 1;
                uint i2 = i0 + (uint)(lonSegments + 1);
                uint i3 = i2 + 1;

                indices.AddRange(new uint[] { i0, i2, i1, i1, i2, i3 });
            }
        }

        indexCount = indices.Count;

        // Create OpenGL objects
        vao = GL.GenVertexArray();
        vbo = GL.GenBuffer();
        ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(), BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    private static Vector3 EciToGl(Vector3d eci)
    {
        // eci.X, eci.Y, eci.Z  -->  gl.Z, gl.X, gl.Y
        return new Vector3((float)eci.Z, (float)eci.X, (float)eci.Y);
    }

    private static void SetUniformColor(int shaderProgram, float r, float g, float b, float a)
    {
        int colorLoc = GL.GetUniformLocation(shaderProgram, "color");
        int alphaLoc = GL.GetUniformLocation(shaderProgram, "alpha");
        if (colorLoc >= 0) GL.Uniform3(colorLoc, r, g, b);
        if (alphaLoc >= 0) GL.Uniform1(alphaLoc, a);
    }

    private static void SetUniformMatrix(int shaderProgram, string name, Matrix4 matrix)
    {
        int location = GL.GetUniformLocation(shaderProgram, name);
        if (location >= 0) GL.UniformMatrix4(location, false, ref matrix);
    }

    private static void CheckShaderCompilation(int shader, string type)
    {
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
        if (status == 0)
        {
            string infoLog = GL.GetShaderInfoLog(shader);
            Console.WriteLine($"{type} shader compilation failed: {infoLog}");
        }
    }

    private static void CheckProgramLinking(int program)
    {
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0)
        {
            string infoLog = GL.GetProgramInfoLog(program);
            Console.WriteLine($"Shader program linking failed: {infoLog}");
        }
    }

    private static string DrawTextInfo(Vector3 position, Vector3 velocity, double time, float mass, GuidanceMode mode)
    {
        // Convert to more readable units
        float altitude = (position.Length - Constants.Re) / 1000f; // Altitude in km
        float speed = velocity.Length / 1000f; // Speed in km/s

        return $"T+{time:F1}s | Alt: {altitude:F1}km | Speed: {speed:F2}km/s | Mass: {mass:F0}kg | Mode: {mode}";
    }
    // Helper: draw text at (x, y) in NDC [-1,1], with scale (pixel size in NDC)
    // Modern OpenGL version: builds a VBO of points and draws with a shader
    private static void DrawText(string text, float x, float y, float scale, float r, float g, float b)
    {
        List<float> points = new();
        float cursorX = x;
        foreach (char c in text)
        {
            if (!BitmapFont8x8.Font.TryGetValue(c, out var glyph))
            {
                cursorX += 9 * scale * 2f / 1200f;
                continue;
            }
            for (int row = 0; row < 8; row++)
            {
                byte rowData = glyph[row];
                for (int col = 0; col < 8; col++)
                {
                    // Fix: flip col index so bits are drawn left-to-right
                    if (((rowData >> col) & 1) != 0)
                    {
                        float px = cursorX + col * scale * 2f / 1200f;
                        float py = y - row * scale * 2f / 800f;
                        points.Add(px);
                        points.Add(py);
                    }
                }
            }
            cursorX += 9 * scale * 2f / 1200f;
        }
    }
}



