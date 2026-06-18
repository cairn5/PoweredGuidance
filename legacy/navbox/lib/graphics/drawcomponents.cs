using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;



namespace lib.graphics;

// --- Helper methods extracted from RenderFrame ---
public partial class Visualizer
{

    private static void DrawEarth(int shaderProgram, int earthVao, int earthIndexCount, Matrix4 view, Matrix4 projection)
    {
        GL.UseProgram(shaderProgram);
        GL.BindVertexArray(earthVao);
        SetUniformColor(shaderProgram, 0.5f, 0.5f, 0.5f, 0.5f);
        Matrix4 earthTransform = Matrix4.CreateTranslation(Vector3.Zero) * view * projection;
        SetUniformMatrix(shaderProgram, "transform", earthTransform);
        GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
        GL.DrawElements(PrimitiveType.Triangles, earthIndexCount, DrawElementsType.UnsignedInt, 0);
        GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    private static void DrawVehicle(int shaderProgram, int vehicleVao, int vehicleIndexCount, Vector3 position, Matrix4 view, Matrix4 projection)
    {
        if (position == Vector3.Zero) return;
        GL.UseProgram(shaderProgram);
        GL.BindVertexArray(vehicleVao);
        SetUniformColor(shaderProgram, 1.0f, 1.0f, 1.0f, 1.0f);
        Matrix4 vehicleTransform = Matrix4.CreateTranslation(position) * view * projection;
        SetUniformMatrix(shaderProgram, "transform", vehicleTransform);
        GL.DrawElements(PrimitiveType.Triangles, vehicleIndexCount, DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    private static void DrawTrajectoryTrail(int shaderProgram, int trajVao, int trajVbo, List<Vector3> trajectoryHistory, Matrix4 view, Matrix4 projection)
    {
        if (trajectoryHistory.Count <= 1) return;
        float[] trajData = new float[trajectoryHistory.Count * 3];
        for (int i = 0; i < trajectoryHistory.Count; i++)
        {
            trajData[i * 3] = trajectoryHistory[i].X;
            trajData[i * 3 + 1] = trajectoryHistory[i].Y;
            trajData[i * 3 + 2] = trajectoryHistory[i].Z;
        }
        GL.UseProgram(shaderProgram);
        GL.BindBuffer(BufferTarget.ArrayBuffer, trajVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, trajData.Length * sizeof(float), trajData, BufferUsageHint.DynamicDraw);
        GL.BindVertexArray(trajVao);
        SetUniformColor(shaderProgram, 0.0f, 1.0f, 0.0f, 0.8f);
        Matrix4 trajTransform = view * projection;
        SetUniformMatrix(shaderProgram, "transform", trajTransform);
        GL.LineWidth(2.0f);
        GL.DrawArrays(PrimitiveType.LineStrip, 0, trajectoryHistory.Count);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    private static void DrawKeplerOrbit(int shaderProgram, int keplerVao, int keplerVbo, Simulator sim, Matrix4 view, Matrix4 projection)
    {
        float[] keplerData = Utils.GetKeplerPoints(sim.State.Kepler);
        if (keplerData.Length < 3) return;
        GL.UseProgram(shaderProgram);
        GL.BindBuffer(BufferTarget.ArrayBuffer, keplerVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, keplerData.Length * sizeof(float), keplerData, BufferUsageHint.DynamicDraw);
        GL.BindVertexArray(keplerVao);
        SetUniformColor(shaderProgram, 3.0f, 3.0f, 1.0f, 0.8f);
        Matrix4 keplerTransform = view * projection;
        SetUniformMatrix(shaderProgram, "transform", keplerTransform);
        GL.LineWidth(2.0f);
        GL.DrawArrays(PrimitiveType.LineStrip, 0, keplerData.Length / 3);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    private static void DrawSteeringVector(int shaderProgram, int steeringVao, int steeringVbo, Vector3 steering, Vector3 position, Matrix4 view, Matrix4 projection)
    {
        if (steering == Vector3.Zero || position == Vector3.Zero) return;
        Vector3 steeringEnd = position + Vector3.Normalize(steering) * 2000000f;
        float[] steeringData = {
            position.X, position.Y, position.Z,
            steeringEnd.X, steeringEnd.Y, steeringEnd.Z
        };
        GL.UseProgram(shaderProgram);
        GL.BindBuffer(BufferTarget.ArrayBuffer, steeringVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, steeringData.Length * sizeof(float), steeringData, BufferUsageHint.DynamicDraw);
        GL.BindVertexArray(steeringVao);
        SetUniformColor(shaderProgram, 1.0f, 0.0f, 0.0f, 1.0f);
        Matrix4 steeringTransform = view * projection;
        SetUniformMatrix(shaderProgram, "transform", steeringTransform);
        GL.LineWidth(3.0f);
        GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    private static void DrawTextInfoOverlay(int textShaderProgram, int fontAtlasTexture, Vector3 position, Vector3 velocity, double time, float mass, GuidanceMode guidanceMode)
    {
        string info = DrawTextInfo(position, velocity, time, mass, guidanceMode);
        BitmapAtlasTextRenderer.DrawText(info, -0.98f, 0.92f, 0.04f, 1f, 1f, 1f, textShaderProgram, fontAtlasTexture);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    private static void DrawTargetParams(GuidanceProgram guidance, Simulator sim, int textShaderProgram, int fontAtlasTexture)
    {
        // Check if target exists for the current active mode
        if (!guidance.Targets.ContainsKey(guidance.ActiveMode))
            return;
            
        IGuidanceTarget tgt = guidance.Targets[guidance.ActiveMode];
        if (tgt is UPFGTarget upfgTarget)
        {
            string info = "TGT PARAMS";
            BitmapAtlasTextRenderer.DrawText(info, -0.98f, 0.86f, 0.04f * 1.7f, 0f, 1f, 0f, textShaderProgram, fontAtlasTexture);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
            string simvars = upfgTarget.userOutput(sim);
            string[] lines = simvars.Split('\n');
            float y = 0.80f;
            float lineSpacing = 0.05f;
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed == "Count" || trimmed.StartsWith("Count:", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrWhiteSpace(line))
                {
                    BitmapAtlasTextRenderer.DrawText(line, -0.98f, y, 0.04f * 0.9f, 1f, 1f, 1f, textShaderProgram, fontAtlasTexture);
                    y -= lineSpacing;
                }
            }
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }
    }

    private static void DrawUpfgParams(GuidanceProgram guidance, Simulator sim, int textShaderProgram, int fontAtlasTexture)
    {
        IGuidanceMode mode = guidance.Modes[guidance.ActiveMode];
        if (mode is UpfgMode upfgMode)
        {
            string info = "UPFG PARAMS";
            BitmapAtlasTextRenderer.DrawText(info, -0.6f, 0.86f, 0.04f * 1.7f, 0.0f, 1.0f, 0.0f, textShaderProgram, fontAtlasTexture);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
            string simvars = upfgMode.userOutput(sim);
            string[] lines = simvars.Split('\n');
            float y = 0.80f;
            float lineSpacing = 0.05f;
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed == "Count" || trimmed.StartsWith("Count:", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrWhiteSpace(line))
                {
                    BitmapAtlasTextRenderer.DrawText(line, -0.6f, y, 0.04f, 1f, 1f, 1f, textShaderProgram, fontAtlasTexture);
                    y -= lineSpacing;
                }
            }
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }
    }
}