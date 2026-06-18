// visualiser.cs
//
// Real-time OpenGL visualization using OpenTK for .NET (cross-platform).
// Displays an animated sine wave, simulation time, arbitrary ASCII text, and a 3D wireframe sphere (latitude/longitude lines only).
// Uses modern OpenGL (shaders, VAO/VBO/EBO), a custom 8x8 bitmap font, and modular static classes for clarity and maintainability.
//
// Main features:
//   - Animated sine wave using dynamic VBO updates
//   - Simulation time and arbitrary text rendering with a bitmap font (GL_POINTS)
//   - 3D wireframe sphere (latitude/longitude lines) with aspect-correct rendering
//   - Robust OpenGL resource management and cross-platform compatibility
//   - Modular code: font and text rendering logic in separate static classes
//   - Comprehensive comments for all mad`jor sections and logic

using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;

namespace lib.graphics;

public partial class Visualizer
{
    // Entry point for real-time OpenGL visualization with live simulation data
    public static void PlotRealtimeSync(Simulator sim, GuidanceProgram guidance)
    {
        // --- Window setup ---
        var nativeWindowSettings = new NativeWindowSettings()
        {
            ClientSize = new Vector2i(1600, 800),
            Title = "Navbox",
        };

        var gameWindowSettings = new GameWindowSettings()
        {
            UpdateFrequency = 12.0,  // 60 FPS for smooth visualization
        };

        using var window = new GameWindow(gameWindowSettings, nativeWindowSettings);

        // --- OpenGL state variables ---
        int shaderProgram = 0;
        int textShaderProgram = 0;
        int fontAtlasTexture = 0;

        // Vehicle representation (small sphere)
        int vehicleVao = 0, vehicleVbo = 0, vehicleEbo = 0, vehicleIndexCount = 0;

        // Trajectory trail
        int trajVao = 0, trajVbo = 0;
        List<Vector3> trajectoryPoints = new List<Vector3>();

        // Earth sphere
        int earthVao = 0, earthVbo = 0, earthEbo = 0, earthIndexCount = 0;

        // Steering vector
        int steeringVao = 0, steeringVbo = 0;

        // kepler orbit 
        int keplerVao = 0, keplerVbo = 0;

        // overlay vao
        int overlayVao = 0, overlayVbo = 0;

        // --- OpenGL initialization ---
        window.Load += () =>
        {
            Console.WriteLine($"OpenGL Version: {GL.GetString(StringName.Version)}");
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Less);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.ClearColor(0.05f, 0.05f, 0.05f, 1f); // Dark grey space background

            // --- Shader setup ---
            string vertexShaderSource = @"
                #version 330 core
                layout(location = 0) in vec3 aPosition;
                uniform mat4 transform;
                void main()
                {
                    gl_Position = transform * vec4(aPosition, 1.0);
                }";

            string fragmentShaderSource = @"
                #version 330 core
                out vec4 FragColor;
                uniform vec3 color;
                uniform float alpha;
                void main()
                {
                    FragColor = vec4(color, alpha);
                }";

            // Compile and link shaders
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);
            CheckShaderCompilation(vertexShader, "vertex");

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
            GL.CompileShader(fragmentShader);
            CheckShaderCompilation(fragmentShader, "fragment");

            shaderProgram = GL.CreateProgram();
            GL.AttachShader(shaderProgram, vertexShader);
            GL.AttachShader(shaderProgram, fragmentShader);
            GL.LinkProgram(shaderProgram);
            CheckProgramLinking(shaderProgram);

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            // --- Create Earth sphere ---
            CreateSphere(out earthVao, out earthVbo, out earthEbo, out earthIndexCount, Constants.Re, 32, 72); // Current celestial body radius

            // --- Create small vehicle sphere ---
            CreateSphere(out vehicleVao, out vehicleVbo, out vehicleEbo, out vehicleIndexCount, 40000f, 8, 6); // Small vehicle representation

            // --- Create trajectory VAO ---
            trajVao = GL.GenVertexArray();
            trajVbo = GL.GenBuffer();
            GL.BindVertexArray(trajVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, trajVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, 0, IntPtr.Zero, BufferUsageHint.DynamicDraw); // Empty buffer initially
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);

            // --- Create steering vector VAO ---
            steeringVao = GL.GenVertexArray();
            steeringVbo = GL.GenBuffer();
            GL.BindVertexArray(steeringVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, steeringVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, 6 * sizeof(float), new float[6], BufferUsageHint.DynamicDraw); // Two points (line)
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);

            // --- Create kepler orbit VAO ---
            int numpoints = 400;
            keplerVao = GL.GenVertexArray();
            keplerVbo = GL.GenBuffer();
            GL.BindVertexArray(keplerVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, keplerVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, numpoints * sizeof(float), new float[numpoints], BufferUsageHint.DynamicDraw); // Two points (line)
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);

            //create overlay vao
            overlayVao = GL.GenVertexArray();
            overlayVbo = GL.GenBuffer();
            GL.BindVertexArray(overlayVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, overlayVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, 0, IntPtr.Zero, BufferUsageHint.DynamicDraw); // Empty buffer initially
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);


            // --- Text shader setup ---
            {
                string textVertexShaderSource = @"
                    #version 330 core
                    layout(location = 0) in vec2 aPosition;
                    layout(location = 1) in vec2 aTexCoord;
                    out vec2 TexCoord;
                    void main() {
                        gl_Position = vec4(aPosition, 0.0, 1.0);
                        TexCoord = aTexCoord;
                    }
                ";
                string textFragmentShaderSource = @"
                    #version 330 core
                    in vec2 TexCoord;
                    out vec4 FragColor;
                    uniform sampler2D fontAtlas;
                    uniform vec3 color;
                    void main() {
                        float alpha = texture(fontAtlas, TexCoord).r;
                        if (alpha < 0.01)
                            discard;
                        FragColor = vec4(color, alpha);
                    }
                ";
                int v = GL.CreateShader(ShaderType.VertexShader);
                GL.ShaderSource(v, textVertexShaderSource);
                GL.CompileShader(v);
                CheckShaderCompilation(v, "text vertex");
                int f = GL.CreateShader(ShaderType.FragmentShader);
                GL.ShaderSource(f, textFragmentShaderSource);
                GL.CompileShader(f);
                CheckShaderCompilation(f, "text fragment");
                textShaderProgram = GL.CreateProgram();
                GL.AttachShader(textShaderProgram, v);
                GL.AttachShader(textShaderProgram, f);
                GL.LinkProgram(textShaderProgram);
                CheckProgramLinking(textShaderProgram);
                GL.DeleteShader(v);
                GL.DeleteShader(f);
            }
            // --- Load bitmap font atlas texture ---
            // Try different possible paths for the font atlas
            string[] possiblePaths = {
                "lib/graphics/fonts/B612.png",
                "navbox/lib/graphics/fonts/B612.png",
                Path.Combine(Directory.GetCurrentDirectory(), "lib/graphics/fonts/B612.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "navbox/lib/graphics/fonts/B612.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib/graphics/fonts/B612.png")
            };
            
            string fontPath = "";
            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    fontPath = path;
                    break;
                }
            }
            
            if (string.IsNullOrEmpty(fontPath))
            {
                throw new FileNotFoundException("Could not find B612.png font atlas file. Tried paths: " + string.Join(", ", possiblePaths));
            }
            
            fontAtlasTexture = BitmapAtlasTextRenderer.LoadFontAtlasTexture(fontPath);
        };

        // --- Main render loop ---
        window.RenderFrame += (FrameEventArgs args) =>
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Get current simulation state
            var (position, velocity, time, mass) = Handler.GetCurrentState();
            var trajectoryHistory = Handler.GetTrajectoryHistory();
            var (steering, guidanceMode) = Handler.GetGuidanceInfo();

            float aspectRatio = window.Size.X / (float)window.Size.Y;
            Matrix4 projection =  Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(45f), aspectRatio, 100f, 50000000f);
            // Shift all 3D objects to the right in NDC (screen space)
            Matrix4 ndcShift = Matrix4.CreateTranslation(new Vector3(0.2f, 0f, 0f)); // 0.4 NDC units right
            Matrix4 shiftedProjection = ndcShift * projection;
            Vector3 cameraOffset = new Vector3(-000000f, 0f, 0);
            Vector3 cameraPos = (Vector3)position * 3 + cameraOffset;
            Matrix4 view = Matrix4.LookAt(cameraPos, (Vector3)position, Vector3.UnitY) * Matrix4.CreateTranslation(new Vector3(7000000f, 0f, 0f));

            DrawEarth(shaderProgram, earthVao, earthIndexCount, view, shiftedProjection);
            DrawVehicle(shaderProgram, vehicleVao, vehicleIndexCount, (Vector3)position, view, shiftedProjection);
            DrawTrajectoryTrail(shaderProgram, trajVao, trajVbo, trajectoryHistory.Select(v => new Vector3(v.X, v.Y, v.Z)).ToList(), view, shiftedProjection);
            DrawKeplerOrbit(shaderProgram, keplerVao, keplerVbo, sim, view, shiftedProjection);
            DrawSteeringVector(shaderProgram, steeringVao, steeringVbo, (Vector3)steering, (Vector3)position, view, shiftedProjection);
            DrawTextInfoOverlay(textShaderProgram, fontAtlasTexture, (Vector3)position, (Vector3)velocity, time, mass, guidanceMode);
            DrawTargetParams(guidance, sim, textShaderProgram, fontAtlasTexture);
            DrawUpfgParams(guidance, sim, textShaderProgram, fontAtlasTexture);

            // Set graph scale and position parameters
            Vector3 graphScale = new Vector3(0.15f, 0.25f, 1f);
            Vector3 graphTranslation = new Vector3(-0.65f, -0.6f, 0f);
            DrawGraphs(shaderProgram, overlayVao, overlayVbo, sim, view, projection, textShaderProgram, fontAtlasTexture, graphScale, graphTranslation);



            window.SwapBuffers();
        };

        window.UpdateFrame += (FrameEventArgs args) =>
        {
            var input = window.KeyboardState;
            if (input.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape))
            {
                window.Close();
            }
        };

        // --- Cleanup ---
        window.Unload += () =>
        {
            GL.DeleteVertexArray(earthVao);
            GL.DeleteBuffer(earthVbo);
            GL.DeleteBuffer(earthEbo);
            GL.DeleteVertexArray(vehicleVao);
            GL.DeleteBuffer(vehicleVbo);
            GL.DeleteBuffer(vehicleEbo);
            GL.DeleteVertexArray(trajVao);
            GL.DeleteBuffer(trajVbo);
            GL.DeleteVertexArray(steeringVao);
            GL.DeleteBuffer(steeringVbo);
            GL.DeleteProgram(shaderProgram);
            GL.DeleteProgram(textShaderProgram);
        };

        window.Run();
    }
// nt textShaderProgram, int fontAtlasTexture, Vector3 position, Vector3 velocity, double time, float mass, GuidanceMode guidanceMode
    private static void DrawGraphs(int shaderProgram, int overlayVao, int overlayVbo, Simulator sim, Matrix4 view, Matrix4 projection, int textShaderProgram, int fontAtlasTexture, Vector3 graphScale, Vector3 graphTranslation)
    {
        if (sim.History == null || sim.History.Count == 0)
            return;
        int n = Math.Min(sim.History.Count, 1000); // Cap the history to prevent performance issues
        if (n == 0) return; // Additional safety check
        // --- Original Downrange vs Altitude Graph ---
        float xMin = 0f, xMax = 2000000f; // meters
        float yMin = 0f, yMax = 300000f;  // meters
        float vMin = 0f, vMax = 8000f;    // velocity in m/s (adjust as needed)
        // --- Draw white grid for downrange/altitude graph ---
        int numXGrid = 10, numYGrid = 6;
        Matrix4 transform = Matrix4.CreateScale(graphScale) * Matrix4.CreateTranslation(graphTranslation);
        GL.UseProgram(shaderProgram);
        GL.BindVertexArray(overlayVao);
        SetUniformColor(shaderProgram, 1f, 1f, 1f, 0.15f);
        SetUniformMatrix(shaderProgram, "transform", transform);
        GL.LineWidth(1.0f);
        // Vertical grid lines
        for (int i = 0; i <= numXGrid; i++)
        {
            float x = -1f + 2f * i / (float)numXGrid;
            float[] gridLine = new float[] { x, -1f, 0f, x, 1f, 0f };
            GL.BindBuffer(BufferTarget.ArrayBuffer, overlayVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, gridLine.Length * sizeof(float), gridLine, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        }
        // Horizontal grid lines
        for (int i = 0; i <= numYGrid; i++)
        {
            float y = -1f + 2f * i / (float)numYGrid;
            float[] gridLine = new float[] { -1f, y, 0f, 1f, y, 0f };
            GL.BindBuffer(BufferTarget.ArrayBuffer, overlayVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, gridLine.Length * sizeof(float), gridLine, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        }
        GL.BindVertexArray(0);
        GL.UseProgram(0);
        float[] trajData = new float[n * 3];
        float[] velData = new float[n * 3];
        for (int i = 0; i < n && i < sim.History.Count; i++)
        {
            SimState state = Utils.ECItoECEF(sim.History[i]);
            float downrange = Utils.CalcDownRange(state, Utils.ECItoECEF(sim.History[0])); // meters
            float alt = (float)sim.History[i].Misc["altitude"];
            float vel = sim.History[i].Misc.ContainsKey("velocity") ? (float)sim.History[i].Misc["velocity"] : state.v.Length();
            float x_ndc = 2f * (downrange - xMin) / (xMax - xMin) - 1f;
            float y_ndc = 2f * (alt - yMin) / (yMax - yMin) - 1f;
            float v_ndc = 2f * (vel - vMin) / (vMax - vMin) - 1f;
            x_ndc = MathF.Max(-1f, MathF.Min(1f, x_ndc));
            // y_ndc = MathF.Max(-1f, MathF.Min(1f, y_ndc));
            trajData[i * 3] = x_ndc;
            trajData[i * 3 + 1] = y_ndc;
            trajData[i * 3 + 2] = 0f;
            velData[i * 3] = x_ndc;
            velData[i * 3 + 1] = v_ndc;
            velData[i * 3 + 2] = 0f;
        }
        // Draw altitude vs downrange (green)
        GL.UseProgram(shaderProgram);
        GL.BindBuffer(BufferTarget.ArrayBuffer, overlayVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, trajData.Length * sizeof(float), trajData, BufferUsageHint.DynamicDraw);
        GL.BindVertexArray(overlayVao);
        SetUniformColor(shaderProgram, 0.0f, 1.0f, 0.0f, 0.8f);
        transform = Matrix4.CreateScale(graphScale) * Matrix4.CreateTranslation(graphTranslation);
        SetUniformMatrix(shaderProgram, "transform", transform);
        GL.LineWidth(2.0f);
        GL.DrawArrays(PrimitiveType.LineStrip, 0, n);
        // Draw velocity vs downrange (blue)
        GL.BufferData(BufferTarget.ArrayBuffer, velData.Length * sizeof(float), velData, BufferUsageHint.DynamicDraw);
        SetUniformColor(shaderProgram, 0.2f, 0.4f, 1.0f, 0.8f);
        GL.LineWidth(2.0f);
        GL.DrawArrays(PrimitiveType.LineStrip, 0, n);
        DrawAxesAndTicks(shaderProgram, overlayVao, overlayVbo, xMin, xMax, yMin, yMax, textShaderProgram, fontAtlasTexture, graphScale, graphTranslation, "km", "km");
        GL.BindVertexArray(0);
        GL.UseProgram(0);

        // --- Latitude vs Longitude Graph ---
        float latMin = -90f, latMax = 90f;
        float lonMin = -180f, lonMax = 180f;
        // --- Draw white grid for lat/long graph ---
        int numLonGrid = 12, numLatGrid = 6;
        Vector3 latLongGraphScale = graphScale * 1f;
        Vector3 latLongGraphTranslation = new Vector3(graphTranslation.X + graphScale.X + 0.2f, graphTranslation.Y, graphTranslation.Z);
        Matrix4 latLongTransform = Matrix4.CreateScale(latLongGraphScale) * Matrix4.CreateTranslation(latLongGraphTranslation);
        GL.UseProgram(shaderProgram);
        GL.BindVertexArray(overlayVao);
        SetUniformColor(shaderProgram, 1f, 1f, 1f, 0.15f);
        SetUniformMatrix(shaderProgram, "transform", latLongTransform);
        GL.LineWidth(1.0f);
        // Vertical grid lines (longitude)
        for (int i = 0; i <= numLonGrid; i++)
        {
            float x = -1f + 2f * i / (float)numLonGrid;
            float[] gridLine = new float[] { x, -1f, 0f, x, 1f, 0f };
            GL.BindBuffer(BufferTarget.ArrayBuffer, overlayVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, gridLine.Length * sizeof(float), gridLine, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        }
        // Horizontal grid lines (latitude)
        for (int i = 0; i <= numLatGrid; i++)
        {
            float y = -1f + 2f * i / (float)numLatGrid;
            float[] gridLine = new float[] { -1f, y, 0f, 1f, y, 0f };
            GL.BindBuffer(BufferTarget.ArrayBuffer, overlayVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, gridLine.Length * sizeof(float), gridLine, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        }
        GL.BindVertexArray(0);
        GL.UseProgram(0);
        float[] latLongData = new float[n * 3];
        for (int i = 0; i < n; i++)
        {
            float lat = MathHelper.RadToDeg*(float)sim.History[i].Misc["latitude"];
            float lon = MathHelper.RadToDeg*(float)sim.History[i].Misc["longitude"];
            float x_ndc = 2f * (lon - lonMin) / (lonMax - lonMin) - 1f;
            float y_ndc = 2f * (lat - latMin) / (latMax - latMin) - 1f;
            // x_ndc = MathF.Max(-1f, MathF.Min(1f, x_ndc));
            // y_ndc = MathF.Max(-1f, MathF.Min(1f, y_ndc));
            latLongData[i * 3] = x_ndc;
            latLongData[i * 3 + 1] = y_ndc;
            latLongData[i * 3 + 2] = 0f;
        }
        GL.UseProgram(shaderProgram);
        GL.BindBuffer(BufferTarget.ArrayBuffer, overlayVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, latLongData.Length * sizeof(float), latLongData, BufferUsageHint.DynamicDraw);
        GL.BindVertexArray(overlayVao);
        SetUniformColor(shaderProgram, 0.0f, 1.0f, 0.0f, 0.8f);
        SetUniformMatrix(shaderProgram, "transform", latLongTransform);
        GL.LineWidth(2.0f);
        GL.DrawArrays(PrimitiveType.LineStrip, 0, n);
        DrawAxesAndTicks(shaderProgram, overlayVao, overlayVbo, lonMin, lonMax, latMin, latMax, textShaderProgram, fontAtlasTexture, latLongGraphScale, latLongGraphTranslation, "deg", "deg");
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    // Draws x and y axes with ticks and tick labels (uses DrawText for labels)
    // Now takes textShaderProgram and fontAtlasTexture as parameters
    private static void DrawAxesAndTicks(int shaderProgram, int overlayVao, int overlayVbo, float xMin, float xMax, float yMin, float yMax, int textShaderProgram, int fontAtlasTexture, Vector3 graphScale, Vector3 graphTranslation, string xLabel, string yLabel)
    {
        // Axes: x from (-1, -1) to (1, -1), y from (-1, -1) to (-1, 1)
        float[] axes = new float[] {
            -1f, -1f, 0f,   1f, -1f, 0f, // x axis
            -1f, -1f, 0f,  -1f,  1f, 0f  // y axis
        };
        // Use the same transform as the trajectory line
        Matrix4 transform = Matrix4.CreateScale(graphScale) * Matrix4.CreateTranslation(graphTranslation);
        GL.BindBuffer(BufferTarget.ArrayBuffer, overlayVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, axes.Length * sizeof(float), axes, BufferUsageHint.DynamicDraw);
        GL.BindVertexArray(overlayVao);
        SetUniformColor(shaderProgram, 1.0f, 1.0f, 1.0f, 1.0f);
        // SetUniformMatrix(shaderProgram, "transform", transform);
        GL.LineWidth(2.5f);
        SetUniformMatrix(shaderProgram, "transform", transform);
        GL.DrawArrays(PrimitiveType.Lines, 0, 4);
        // Draw ticks
        int numXTicks = 10, numYTicks = 6;
        float tickLen = 0.025f; // in NDC
        // X ticks (bottom)
        for (int i = 0; i <= numXTicks; i++)
        {
            float x = -1f + 2f * i / numXTicks;
            float[] tick = new float[] { x, -1f, 0f, x, -1f + tickLen, 0f };
            GL.BufferData(BufferTarget.ArrayBuffer, tick.Length * sizeof(float), tick, BufferUsageHint.DynamicDraw);
            SetUniformMatrix(shaderProgram, "transform", transform);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
            float[] gridLine = new float[] { x, -1f, 0f, x, 1f, 0f };
            GL.LineWidth(1.0f);
            SetUniformColor(shaderProgram, 1f, 1f, 1f, 0.15f);
            GL.BufferData(BufferTarget.ArrayBuffer, gridLine.Length * sizeof(float), gridLine, BufferUsageHint.DynamicDraw);
            SetUniformMatrix(shaderProgram, "transform", transform);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
            SetUniformColor(shaderProgram, 1.0f, 1.0f, 1.0f, 1.0f);
            float userX = xMin + (x + 1f) * 0.5f * (xMax - xMin);
            string label = $"{userX / 100000f:0}";
            // Manually apply scale and translation to label position
            float labelX = x * graphScale.X + graphTranslation.X;
            float labelY = (-1f - 0.06f) * graphScale.Y + graphTranslation.Y;
            DrawTextSafe(label, labelX, labelY, 0.04f, 1f, 1f, 1f, textShaderProgram, fontAtlasTexture);
        }
        // Y ticks (left)
        for (int i = 0; i <= numYTicks; i++)
        {
            float y = -1f + 2f * i / numYTicks;
            float[] tick = new float[] { -1f, y, 0f, -1f + tickLen, y, 0f };
            GL.BufferData(BufferTarget.ArrayBuffer, tick.Length * sizeof(float), tick, BufferUsageHint.DynamicDraw);
            SetUniformMatrix(shaderProgram, "transform", transform);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
            float[] gridLine = new float[] { -1f, y, 0f, 1f, y, 0f };
            GL.LineWidth(1.0f);
            SetUniformColor(shaderProgram, 1f, 1f, 1f, 0.15f);
            GL.BufferData(BufferTarget.ArrayBuffer, gridLine.Length * sizeof(float), gridLine, BufferUsageHint.DynamicDraw);
            SetUniformMatrix(shaderProgram, "transform", transform);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
            SetUniformColor(shaderProgram, 1.0f, 1.0f, 1.0f, 1.0f);
            float userY = yMin + (y + 1f) * 0.5f * (yMax - yMin);
            string label = $"{userY / 10000f:0}";
            // Manually apply scale and translation to label position
            float labelX = (-1f - 0.10f) * graphScale.X + graphTranslation.X;
            float labelY = (y - 0.02f) * graphScale.Y + graphTranslation.Y;
            DrawTextSafe(label, labelX, labelY, 0.04f, 1f, 1f, 1f, textShaderProgram, fontAtlasTexture);
        }
        // Draw axis labels
        // X axis label
        float xLabelX = 0f * graphScale.X + graphTranslation.X;
        float xLabelY = (-1.18f) * graphScale.Y + graphTranslation.Y;
        DrawTextSafe(xLabel, xLabelX, xLabelY, 0.045f, 1f, 1f, 1f, textShaderProgram, fontAtlasTexture);
        // Y axis label (rotated not supported, so just place left)
        float yLabelX = (-1.22f) * graphScale.X + graphTranslation.X;
        float yLabelY = 0f * graphScale.Y + graphTranslation.Y;
        DrawTextSafe(yLabel, yLabelX, yLabelY, 0.045f, 1f, 1f, 1f, textShaderProgram, fontAtlasTexture);
        // Restore state
        GL.BindVertexArray(0);
    }

    // Helper to draw text at NDC position (x, y), with scale and color, safely restoring OpenGL state
    private static void DrawTextSafe(string text, float x, float y, float scale, float r, float g, float b, int textShaderProgram, int fontAtlasTexture)
    {
        // Save current program and VAO
        int prevProgram, prevVao;
        GL.GetInteger(GetPName.CurrentProgram, out prevProgram);
        GL.GetInteger(GetPName.VertexArrayBinding, out prevVao);
        // Use text shader and font atlas
        BitmapAtlasTextRenderer.DrawText(text, x, y, scale, r, g, b, textShaderProgram, fontAtlasTexture);
        // Restore previous state
        GL.UseProgram(prevProgram);
        GL.BindVertexArray(prevVao);
    }
}
