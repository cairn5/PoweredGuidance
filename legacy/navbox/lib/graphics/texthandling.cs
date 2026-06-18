using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace lib.graphics;


// --- BitmapFont8x8: 8x8 ASCII bitmap font for text rendering ---
// Each character is 8x8 pixels, stored as 8 bytes (one per row, LSB leftmost)
// Covers all printable ASCII characters (32-127)

public static class BitmapFont8x8
{
    // Font dictionary: maps char to 8-byte bitmap
    public static readonly Dictionary<char, byte[]> Font = new()
    {
        [' '] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
        ['!'] = new byte[] { 0x18, 0x3C, 0x3C, 0x18, 0x18, 0x00, 0x18, 0x00 },
        ['"'] = new byte[] { 0x36, 0x36, 0x24, 0x00, 0x00, 0x00, 0x00, 0x00 },
        ['#'] = new byte[] { 0x36, 0x36, 0x7F, 0x36, 0x7F, 0x36, 0x36, 0x00 },
        ['$'] = new byte[] { 0x0C, 0x3E, 0x03, 0x1E, 0x30, 0x1F, 0x0C, 0x00 },
        ['%'] = new byte[] { 0x00, 0x63, 0x33, 0x18, 0x0C, 0x66, 0x63, 0x00 },
        ['&'] = new byte[] { 0x1C, 0x36, 0x1C, 0x6E, 0x3B, 0x33, 0x6E, 0x00 },
        ['\''] = new byte[] { 0x06, 0x06, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00 },
        ['('] = new byte[] { 0x18, 0x0C, 0x06, 0x06, 0x06, 0x0C, 0x18, 0x00 },
        [')'] = new byte[] { 0x06, 0x0C, 0x18, 0x18, 0x18, 0x0C, 0x06, 0x00 },
        ['*'] = new byte[] { 0x00, 0x36, 0x1C, 0x7F, 0x1C, 0x36, 0x00, 0x00 },
        ['+'] = new byte[] { 0x00, 0x08, 0x08, 0x3E, 0x08, 0x08, 0x00, 0x00 },
        [','] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x18, 0x18, 0x30, 0x00 },
        ['-'] = new byte[] { 0x00, 0x00, 0x00, 0x7E, 0x00, 0x00, 0x00, 0x00 },
        ['.'] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x18, 0x18, 0x00 },
        ['/'] = new byte[] { 0x00, 0x60, 0x30, 0x18, 0x0C, 0x06, 0x03, 0x00 },
        ['0'] = new byte[] { 0x3E, 0x63, 0x73, 0x7B, 0x6F, 0x67, 0x3E, 0x00 },
        ['1'] = new byte[] { 0x0C, 0x0E, 0x0F, 0x0C, 0x0C, 0x0C, 0x3F, 0x00 },
        ['2'] = new byte[] { 0x1E, 0x33, 0x30, 0x1C, 0x06, 0x33, 0x3F, 0x00 },
        ['3'] = new byte[] { 0x1E, 0x33, 0x30, 0x1C, 0x30, 0x33, 0x1E, 0x00 },
        ['4'] = new byte[] { 0x38, 0x3C, 0x36, 0x33, 0x7F, 0x30, 0x78, 0x00 },
        ['5'] = new byte[] { 0x3F, 0x03, 0x1F, 0x30, 0x30, 0x33, 0x1E, 0x00 },
        ['6'] = new byte[] { 0x1C, 0x06, 0x03, 0x1F, 0x33, 0x33, 0x1E, 0x00 },
        ['7'] = new byte[] { 0x3F, 0x33, 0x30, 0x18, 0x0C, 0x0C, 0x0C, 0x00 },
        ['8'] = new byte[] { 0x1E, 0x33, 0x33, 0x1E, 0x33, 0x33, 0x1E, 0x00 },
        ['9'] = new byte[] { 0x1E, 0x33, 0x33, 0x3E, 0x30, 0x18, 0x0E, 0x00 },
        [':'] = new byte[] { 0x00, 0x18, 0x18, 0x00, 0x00, 0x18, 0x18, 0x00 },
        [';'] = new byte[] { 0x00, 0x18, 0x18, 0x00, 0x18, 0x18, 0x30, 0x00 },
        ['<'] = new byte[] { 0x18, 0x0C, 0x06, 0x03, 0x06, 0x0C, 0x18, 0x00 },
        ['='] = new byte[] { 0x00, 0x00, 0x7E, 0x00, 0x7E, 0x00, 0x00, 0x00 },
        ['>'] = new byte[] { 0x06, 0x0C, 0x18, 0x30, 0x18, 0x0C, 0x06, 0x00 },
        ['?'] = new byte[] { 0x1E, 0x33, 0x30, 0x18, 0x0C, 0x00, 0x0C, 0x00 },
        ['@'] = new byte[] { 0x3E, 0x63, 0x7B, 0x7B, 0x7B, 0x03, 0x1E, 0x00 },
        ['A'] = new byte[] { 0x0C, 0x1E, 0x33, 0x33, 0x3F, 0x33, 0x33, 0x00 },
        ['B'] = new byte[] { 0x3F, 0x66, 0x66, 0x3E, 0x66, 0x66, 0x3F, 0x00 },
        ['C'] = new byte[] { 0x3C, 0x66, 0x03, 0x03, 0x03, 0x66, 0x3C, 0x00 },
        ['D'] = new byte[] { 0x1F, 0x36, 0x66, 0x66, 0x66, 0x36, 0x1F, 0x00 },
        ['E'] = new byte[] { 0x7F, 0x46, 0x16, 0x1E, 0x16, 0x46, 0x7F, 0x00 },
        ['F'] = new byte[] { 0x7F, 0x46, 0x16, 0x1E, 0x16, 0x06, 0x0F, 0x00 },
        ['G'] = new byte[] { 0x3C, 0x66, 0x03, 0x03, 0x73, 0x66, 0x7C, 0x00 },
        ['H'] = new byte[] { 0x33, 0x33, 0x33, 0x3F, 0x33, 0x33, 0x33, 0x00 },
        ['I'] = new byte[] { 0x1E, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x1E, 0x00 },
        ['J'] = new byte[] { 0x78, 0x30, 0x30, 0x30, 0x33, 0x33, 0x1E, 0x00 },
        ['K'] = new byte[] { 0x67, 0x66, 0x36, 0x1E, 0x36, 0x66, 0x67, 0x00 },
        ['L'] = new byte[] { 0x0F, 0x06, 0x06, 0x06, 0x46, 0x66, 0x7F, 0x00 },
        ['M'] = new byte[] { 0x63, 0x77, 0x7F, 0x7F, 0x6B, 0x63, 0x63, 0x00 },
        ['N'] = new byte[] { 0x63, 0x67, 0x6F, 0x7B, 0x73, 0x63, 0x63, 0x00 },
        ['O'] = new byte[] { 0x1C, 0x36, 0x63, 0x63, 0x63, 0x36, 0x1C, 0x00 },
        ['P'] = new byte[] { 0x3F, 0x66, 0x66, 0x3E, 0x06, 0x06, 0x0F, 0x00 },
        ['Q'] = new byte[] { 0x1E, 0x33, 0x33, 0x33, 0x3B, 0x1E, 0x38, 0x00 },
        ['R'] = new byte[] { 0x3F, 0x66, 0x66, 0x3E, 0x36, 0x66, 0x67, 0x00 },
        ['S'] = new byte[] { 0x1E, 0x33, 0x07, 0x0E, 0x38, 0x33, 0x1E, 0x00 },
        ['T'] = new byte[] { 0x3F, 0x2D, 0x0C, 0x0C, 0x0C, 0x0C, 0x1E, 0x00 },
        ['U'] = new byte[] { 0x33, 0x33, 0x33, 0x33, 0x33, 0x33, 0x3F, 0x00 },
        ['V'] = new byte[] { 0x33, 0x33, 0x33, 0x33, 0x33, 0x1E, 0x0C, 0x00 },
        ['W'] = new byte[] { 0x63, 0x63, 0x63, 0x6B, 0x7F, 0x77, 0x63, 0x00 },
        ['X'] = new byte[] { 0x63, 0x63, 0x36, 0x1C, 0x1C, 0x36, 0x63, 0x00 },
        ['Y'] = new byte[] { 0x33, 0x33, 0x33, 0x1E, 0x0C, 0x0C, 0x1E, 0x00 },
        ['Z'] = new byte[] { 0x7F, 0x73, 0x19, 0x0C, 0x26, 0x67, 0x7F, 0x00 },
        ['['] = new byte[] { 0x1E, 0x06, 0x06, 0x06, 0x06, 0x06, 0x1E, 0x00 },
        ['\\'] = new byte[] { 0x03, 0x06, 0x0C, 0x18, 0x30, 0x60, 0x40, 0x00 },
        [']'] = new byte[] { 0x1E, 0x18, 0x18, 0x18, 0x18, 0x18, 0x1E, 0x00 },
        ['^'] = new byte[] { 0x08, 0x1C, 0x36, 0x63, 0x00, 0x00, 0x00, 0x00 },
        ['_'] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF },
        ['`'] = new byte[] { 0x0C, 0x0C, 0x18, 0x00, 0x00, 0x00, 0x00, 0x00 },
        ['a'] = new byte[] { 0x00, 0x00, 0x1E, 0x30, 0x3E, 0x33, 0x6E, 0x00 },
        ['b'] = new byte[] { 0x07, 0x06, 0x06, 0x3E, 0x66, 0x66, 0x3B, 0x00 },
        ['c'] = new byte[] { 0x00, 0x00, 0x1E, 0x33, 0x03, 0x33, 0x1E, 0x00 },
        ['d'] = new byte[] { 0x38, 0x30, 0x30, 0x3E, 0x33, 0x33, 0x6E, 0x00 },
        ['e'] = new byte[] { 0x00, 0x00, 0x1E, 0x33, 0x3F, 0x03, 0x1E, 0x00 },
        ['f'] = new byte[] { 0x1C, 0x36, 0x06, 0x0F, 0x06, 0x06, 0x0F, 0x00 },
        ['g'] = new byte[] { 0x00, 0x00, 0x6E, 0x33, 0x33, 0x3E, 0x30, 0x1F },
        ['h'] = new byte[] { 0x07, 0x06, 0x36, 0x6E, 0x66, 0x66, 0x67, 0x00 },
        ['i'] = new byte[] { 0x0C, 0x00, 0x0E, 0x0C, 0x0C, 0x0C, 0x1E, 0x00 },
        ['j'] = new byte[] { 0x30, 0x00, 0x38, 0x30, 0x30, 0x33, 0x33, 0x1E },
        ['k'] = new byte[] { 0x07, 0x06, 0x66, 0x36, 0x1E, 0x36, 0x67, 0x00 },
        ['l'] = new byte[] { 0x0E, 0x0C, 0x0C, 0x0C, 0x0C, 0x0C, 0x1E, 0x00 },
        ['m'] = new byte[] { 0x00, 0x00, 0x33, 0x7F, 0x7F, 0x6B, 0x63, 0x00 },
        ['n'] = new byte[] { 0x00, 0x00, 0x1B, 0x37, 0x33, 0x33, 0x33, 0x00 },
        ['o'] = new byte[] { 0x00, 0x00, 0x1E, 0x33, 0x33, 0x33, 0x1E, 0x00 },
        ['p'] = new byte[] { 0x00, 0x00, 0x3B, 0x66, 0x66, 0x3E, 0x06, 0x0F },
        ['q'] = new byte[] { 0x00, 0x00, 0x6E, 0x33, 0x33, 0x3E, 0x30, 0x78 },
        ['r'] = new byte[] { 0x00, 0x00, 0x3B, 0x6E, 0x66, 0x06, 0x0F, 0x00 },
        ['s'] = new byte[] { 0x00, 0x00, 0x3E, 0x03, 0x1E, 0x30, 0x1F, 0x00 },
        ['t'] = new byte[] { 0x08, 0x0C, 0x3E, 0x0C, 0x0C, 0x2C, 0x18, 0x00 },
        ['u'] = new byte[] { 0x00, 0x00, 0x33, 0x33, 0x33, 0x33, 0x6E, 0x00 },
        ['v'] = new byte[] { 0x00, 0x00, 0x33, 0x33, 0x33, 0x1E, 0x0C, 0x00 },
        ['w'] = new byte[] { 0x00, 0x00, 0x63, 0x6B, 0x7F, 0x7F, 0x36, 0x00 },
        ['x'] = new byte[] { 0x00, 0x00, 0x63, 0x36, 0x1C, 0x36, 0x63, 0x00 },
        ['y'] = new byte[] { 0x00, 0x00, 0x33, 0x33, 0x33, 0x3E, 0x30, 0x1F },
        ['z'] = new byte[] { 0x00, 0x00, 0x3F, 0x19, 0x0C, 0x26, 0x3F, 0x00 },
        ['{'] = new byte[] { 0x38, 0x0C, 0x0C, 0x07, 0x0C, 0x0C, 0x38, 0x00 },
        ['|'] = new byte[] { 0x18, 0x18, 0x18, 0x00, 0x18, 0x18, 0x18, 0x00 },
        ['}'] = new byte[] { 0x07, 0x0C, 0x0C, 0x38, 0x0C, 0x0C, 0x07, 0x00 },
        ['~'] = new byte[] { 0x6E, 0x3B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
    };
}

// --- TextRenderer: modular static class for drawing text using BitmapFont8x8 ---
// Provides a reusable DrawText method for rendering ASCII text as GL_POINTS
public static class TextRenderer
{
    // DrawText: draws ASCII text using the 8x8 bitmap font at the given NDC position, scale, and color
    // Arguments:
    //   text           - The string to render (ASCII, printable)
    //   x, y           - NDC coordinates for the start of the text (top-left)
    //   scale          - Point size (pixel size of each font dot)
    //   r, g, b        - Color (0-1)
    //   shaderProgram  - OpenGL shader program to use (must have 'color' uniform)
    public static void DrawText(string text, float x, float y, float scale, float r, float g, float b, int shaderProgram)
    {
        var font8x8 = BitmapFont8x8.Font;
        List<float> points = new();
        float cursorX = x;
        foreach (char c in text)
        {
            if (!font8x8.TryGetValue(c, out var glyph))
            {
                cursorX += 9 * scale * 2f / 1200f;
                continue;
            }
            for (int row = 0; row < 8; row++)
            {
                byte rowData = glyph[row];
                for (int col = 0; col < 8; col++)
                {
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
        if (points.Count == 0) return;
        int textVboTmp = GL.GenBuffer();
        int textVaoTmp = GL.GenVertexArray();
        GL.BindVertexArray(textVaoTmp);
        GL.BindBuffer(BufferTarget.ArrayBuffer, textVboTmp);
        GL.BufferData(BufferTarget.ArrayBuffer, points.Count * sizeof(float), points.ToArray(), BufferUsageHint.StreamDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.UseProgram(shaderProgram);
        int colorLoc = GL.GetUniformLocation(shaderProgram, "color");
        if (colorLoc >= 0) GL.Uniform3(colorLoc, r, g, b);
        GL.PointSize(scale);
        GL.DrawArrays(PrimitiveType.Points, 0, points.Count / 2);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
        GL.DeleteBuffer(textVboTmp);
        GL.DeleteVertexArray(textVaoTmp);
    }
}

// --- BitmapAtlasTextRenderer: renders text using a bitmap font atlas (5x19 grid, ASCII order) ---
public static class BitmapAtlasTextRenderer
{
    // Font atlas properties
    private const int GlyphWidth = 20;
    private const int GlyphHeight = 36;
    private const int AtlasCols = 19;
    private const int AtlasRows = 5;
    private const int FirstChar = 32; // ASCII space

    // Loads the font atlas PNG as an OpenGL texture using ImageSharp
    public static int LoadFontAtlasTexture(string path)
    {
        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(path);
        int width = image.Width;
        int height = image.Height;
        var pixels = new byte[width * height * 4];
        image.CopyPixelDataTo(pixels);
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        return tex;
    }

    // Draws text using the font atlas at (x, y) in NDC, with scale (height in NDC), color, shader, and texture
    public static void DrawText(string text, float x, float y, float scale, float r, float g, float b, int shaderProgram, int fontAtlasTexture)
    {
        if (string.IsNullOrEmpty(text)) return;
        float aspect = GlyphWidth / (float)GlyphHeight;
        float glyphW = scale * aspect;
        float glyphH = scale;
        int maxQuads = text.Length;
        float[] vertices = new float[maxQuads * 4 * 4]; // 4 verts per quad, 4 floats (pos.xy, uv.xy)
        int[] indices = new int[maxQuads * 6]; // 6 indices per quad
        int quad = 0;
        float cursorX = x;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            int charIdx = c - FirstChar;
            if (charIdx < 0 || charIdx >= AtlasCols * AtlasRows) { cursorX += glyphW; continue; }
            int col = charIdx % AtlasCols;
            int row = charIdx / AtlasCols;
            float u0 = col / (float)AtlasCols;
            float v0 = row / (float)AtlasRows;
            float u1 = (col + 1) / (float)AtlasCols;
            float v1 = (row + 1) / (float)AtlasRows;
            float x0 = cursorX;
            float y0 = y;
            float x1 = cursorX + glyphW;
            float y1 = y - glyphH;
            int vbase = quad * 16;
            vertices[vbase + 0] = x0; vertices[vbase + 1] = y0; vertices[vbase + 2] = u0; vertices[vbase + 3] = v0;
            vertices[vbase + 4] = x1; vertices[vbase + 5] = y0; vertices[vbase + 6] = u1; vertices[vbase + 7] = v0;
            vertices[vbase + 8] = x1; vertices[vbase + 9] = y1; vertices[vbase + 10] = u1; vertices[vbase + 11] = v1;
            vertices[vbase + 12] = x0; vertices[vbase + 13] = y1; vertices[vbase + 14] = u0; vertices[vbase + 15] = v1;
            int ibase = quad * 6;
            indices[ibase + 0] = quad * 4 + 0;
            indices[ibase + 1] = quad * 4 + 1;
            indices[ibase + 2] = quad * 4 + 2;
            indices[ibase + 3] = quad * 4 + 2;
            indices[ibase + 4] = quad * 4 + 3;
            indices[ibase + 5] = quad * 4 + 0;
            quad++;
            cursorX += 0.6f * glyphW;
        }
        if (quad == 0) return;
        int vbo = GL.GenBuffer();
        int vao = GL.GenVertexArray();
        int ebo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, quad * 4 * 4 * sizeof(float), vertices, BufferUsageHint.StreamDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, quad * 6 * sizeof(int), indices, BufferUsageHint.StreamDraw);
        GL.UseProgram(shaderProgram);
        int colorLoc = GL.GetUniformLocation(shaderProgram, "color");
        if (colorLoc >= 0) GL.Uniform3(colorLoc, r, g, b);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, fontAtlasTexture);
        int texLoc = GL.GetUniformLocation(shaderProgram, "fontAtlas");
        if (texLoc >= 0) GL.Uniform1(texLoc, 0);
        GL.DrawElements(PrimitiveType.Triangles, quad * 6, DrawElementsType.UnsignedInt, 0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
        GL.DeleteBuffer(vbo);
        GL.DeleteBuffer(ebo);
        GL.DeleteVertexArray(vao);
    }
}