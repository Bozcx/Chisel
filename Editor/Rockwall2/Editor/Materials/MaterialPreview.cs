using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Materials.Utils;
using System;
using System.Collections.Generic;

namespace Rockwall2.Editor.Materials;

public enum PreviewShape { Sphere, Cube, Plane }

public struct VertexPositionNormalTangentTexture : IVertexType
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 Tangent;
    public Vector3 Binormal;
    public Vector2 TextureCoordinate;

    public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Vector3, VertexElementUsage.Tangent, 0),
        new VertexElement(36, VertexElementFormat.Vector3, VertexElementUsage.Binormal, 0),
        new VertexElement(48, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
}

public class MaterialPreview : IEditorScene
{
    EditorHost host;
    readonly Control viewControl;

    Effect effect;
    Texture2D white, black, flatNormal;
    BasicEffect gizmoEffect;
    static readonly Color LightGizmoColor = new Color(255, 220, 120);
    static readonly Vector3 LightColor = new Vector3(1.0f, 0.97f, 0.9f);
    static readonly Vector3 AmbientColor = new Vector3(0.035f, 0.037f, 0.045f);
    readonly Dictionary<PreviewShape, (VertexBuffer vertices, IndexBuffer indices)> meshes = new();

    // Keyed by texture path, null when it couldn't be loaded so it isn't retried every frame
    readonly Dictionary<string, Texture2D> textures = new(StringComparer.OrdinalIgnoreCase);

    readonly Orbit3DCamera orbitCamera = new Orbit3DCamera();
    const float DefaultLightYaw = -35, DefaultLightPitch = 40;
    float lightYaw = DefaultLightYaw, lightPitch = DefaultLightPitch;
    bool draggingLight;

    public MaterialDocument Document;
    public PreviewShape Shape = PreviewShape.Sphere;

    public MaterialPreview(Control viewControl)
    {
        this.viewControl = viewControl;
    }

    public void ResetLight()
    {
        lightYaw = DefaultLightYaw;
        lightPitch = DefaultLightPitch;
    }

    // For when texture files change on disk
    public void ClearTextureCache() => textures.Clear();

    public void Attach(EditorHost host)
    {
        this.host = host;
        var gd = host.GraphicsDevice;

        effect = host.Content.Load<Effect>("Shaders/MaterialPreview");

        white = SolidTexture(gd, new Color(255, 255, 255, 255));
        black = SolidTexture(gd, new Color(0, 0, 0, 255));
        flatNormal = SolidTexture(gd, new Color(128, 128, 255, 255));
        gizmoEffect = new BasicEffect(gd);

        meshes[PreviewShape.Sphere] = BuildSphere(gd);
        meshes[PreviewShape.Cube] = BuildCube(gd);
        meshes[PreviewShape.Plane] = BuildPlane(gd);

        orbitCamera.FocusOn(Vector3.Zero, 1.1f);
        orbitCamera.RebuildMatrix();
    }

    public void Detach()
    {
    }

    public void Resize(int width, int height)
    {
        orbitCamera.AspectRatio = width / (float)Math.Max(1, height);
    }

    public void Update(GameTime gameTime)
    {
        if (!viewControl.IsEffectivelyVisible) return;

        bool pointerOver = viewControl.IsPointerOver;

        draggingLight = MouseManager.IsDown(MouseButton.Right) && (draggingLight || pointerOver);
        if (draggingLight)
        {
            lightYaw += (float)MouseManager.Delta.X * 0.5f;
            lightPitch = MathHelper.Clamp(lightPitch - (float)MouseManager.Delta.Y * 0.5f, -89, 89);
        }

        orbitCamera.blockOrbit = !pointerOver || draggingLight;
        orbitCamera.Update(gameTime);
    }

    public void Draw(GameTime gameTime)
    {
        var gd = host.GraphicsDevice;
        gd.Clear(new Color(22, 22, 24));

        if (Document == null || effect == null) return;

        var (vertices, indices) = meshes[Shape];

        gd.DepthStencilState = DepthStencilState.Default;
        gd.RasterizerState = Document.NoCull ? RasterizerState.CullNone : RasterizerState.CullCounterClockwise;
        gd.BlendState = Document.Transparent ? BlendState.NonPremultiplied : BlendState.Opaque;

        float yaw = MathHelper.ToRadians(lightYaw), pitch = MathHelper.ToRadians(lightPitch);
        var lightDir = new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw));

        effect.Parameters["World"]?.SetValue(Matrix.Identity);
        effect.Parameters["View"]?.SetValue(orbitCamera.viewMatrix);
        effect.Parameters["Projection"]?.SetValue(orbitCamera.projectionMatrix);
        effect.Parameters["CameraPosition"]?.SetValue(orbitCamera.position);
        effect.Parameters["LightDirection"]?.SetValue(lightDir);
        effect.Parameters["LightColor"]?.SetValue(LightColor);
        effect.Parameters["AmbientColor"]?.SetValue(AmbientColor);
        effect.Parameters["shine"]?.SetValue(Document.Reflectivity);
        effect.Parameters["AlphaClip"]?.SetValue(Document.AlphaClip ? 1f : 0f);
        effect.Parameters["Transparent"]?.SetValue(Document.Transparent ? 1f : 0f);
        effect.Parameters["MainTex"]?.SetValue(GetTexture(MaterialDocument.AlbedoKey, white));
        effect.Parameters["SpecTex"]?.SetValue(GetTexture(MaterialDocument.SpecularKey, black));
        effect.Parameters["NormalTex"]?.SetValue(GetTexture(MaterialDocument.NormalKey, flatNormal));

        gd.SetVertexBuffer(vertices);
        gd.Indices = indices;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, indices.IndexCount / 3);
        }

        DrawLightGizmo(gd, lightDir);
    }

    // Small bulb where the light is, with a line pointing at the preview object
    void DrawLightGizmo(GraphicsDevice gd, Vector3 lightDir)
    {
        const float distance = 2.2f;
        Vector3 bulbPosition = lightDir * distance;

        gd.BlendState = BlendState.Opaque;
        gd.RasterizerState = RasterizerState.CullCounterClockwise;
        gd.DepthStencilState = DepthStencilState.Default;

        gizmoEffect.View = orbitCamera.viewMatrix;
        gizmoEffect.Projection = orbitCamera.projectionMatrix;
        gizmoEffect.LightingEnabled = false;
        gizmoEffect.TextureEnabled = false;

        var (vertices, indices) = meshes[PreviewShape.Sphere];
        gizmoEffect.VertexColorEnabled = false;
        gizmoEffect.DiffuseColor = LightGizmoColor.ToVector3();
        gizmoEffect.World = Matrix.CreateScale(0.07f) * Matrix.CreateTranslation(bulbPosition);
        gd.SetVertexBuffer(vertices);
        gd.Indices = indices;
        foreach (var pass in gizmoEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, indices.IndexCount / 3);
        }

        // Stops short of the object so it doesn't poke through the surface
        var line = new[]
        {
            new VertexPositionColor(bulbPosition, LightGizmoColor),
            new VertexPositionColor(lightDir * 1.35f, LightGizmoColor * 0.4f),
        };
        gizmoEffect.VertexColorEnabled = true;
        gizmoEffect.DiffuseColor = Vector3.One;
        gizmoEffect.World = Matrix.Identity;
        foreach (var pass in gizmoEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserPrimitives(PrimitiveType.LineList, line, 0, 1);
        }
    }

    Texture2D GetTexture(string key, Texture2D fallback)
    {
        string path = Document.GetTexturePath(key).Trim();
        if (path.Length == 0) return fallback;

        if (!textures.TryGetValue(path, out var texture))
        {
            texture = MaterialLibrary.TextureExists(path)
                ? MaterialLibrary.LoadTexture(path)
                : null;
            textures[path] = texture;
        }
        return texture ?? fallback;
    }

    static Texture2D SolidTexture(GraphicsDevice gd, Color color)
    {
        var texture = new Texture2D(gd, 1, 1);
        texture.SetData(new[] { color });
        return texture;
    }

    // Binormal points along +V, which is down the texture
    static VertexPositionNormalTangentTexture Vertex(Vector3 position, Vector3 normal, Vector3 tangent, Vector3 binormal, Vector2 uv) => new()
    {
        Position = position,
        Normal = normal,
        Tangent = tangent,
        Binormal = binormal,
        TextureCoordinate = uv,
    };

    // Orders each triangle so it faces along its vertex normals, front faces are clockwise in XNA
    static void AddTriangle(List<int> indices, List<VertexPositionNormalTangentTexture> vertices, int a, int b, int c)
    {
        Vector3 cross = Vector3.Cross(vertices[b].Position - vertices[a].Position, vertices[c].Position - vertices[a].Position);
        Vector3 normal = vertices[a].Normal + vertices[b].Normal + vertices[c].Normal;

        if (Vector3.Dot(cross, normal) > 0) indices.AddRange(new[] { a, c, b });
        else indices.AddRange(new[] { a, b, c });
    }

    static (VertexBuffer, IndexBuffer) Upload(GraphicsDevice gd, List<VertexPositionNormalTangentTexture> vertices, List<int> indices)
    {
        var vb = new VertexBuffer(gd, VertexPositionNormalTangentTexture.VertexDeclaration, vertices.Count, BufferUsage.WriteOnly);
        vb.SetData(vertices.ToArray());
        var ib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits, indices.Count, BufferUsage.WriteOnly);
        ib.SetData(indices.ToArray());
        return (vb, ib);
    }

    static (VertexBuffer, IndexBuffer) BuildSphere(GraphicsDevice gd)
    {
        const int slices = 64, stacks = 32;
        var vertices = new List<VertexPositionNormalTangentTexture>();
        var indices = new List<int>();

        for (int i = 0; i <= stacks; i++)
        {
            float theta = i / (float)stacks * MathF.PI;
            for (int j = 0; j <= slices; j++)
            {
                float phi = j / (float)slices * MathF.Tau;
                var normal = new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Cos(theta), MathF.Sin(theta) * MathF.Sin(phi));
                var tangent = new Vector3(-MathF.Sin(phi), 0, MathF.Cos(phi));
                var binormal = new Vector3(MathF.Cos(theta) * MathF.Cos(phi), -MathF.Sin(theta), MathF.Cos(theta) * MathF.Sin(phi));

                // Wraps the texture twice around so it isn't stretched
                vertices.Add(Vertex(normal, normal, tangent, binormal, new Vector2(j / (float)slices * 2, i / (float)stacks)));
            }
        }

        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                int a = i * (slices + 1) + j, b = a + slices + 1;
                if (i != 0) AddTriangle(indices, vertices, a, b, a + 1);
                if (i != stacks - 1) AddTriangle(indices, vertices, a + 1, b, b + 1);
            }
        }

        return Upload(gd, vertices, indices);
    }

    static void AddQuad(List<VertexPositionNormalTangentTexture> vertices, List<int> indices, Vector3 normal, Vector3 binormal, float halfSize, Vector3 center, float uvScale)
    {
        // Tangent picked so the texture reads the right way round from outside
        var tangent = Vector3.Cross(normal, binormal);
        int start = vertices.Count;

        for (int v = 0; v <= 1; v++)
            for (int u = 0; u <= 1; u++)
            {
                var position = center + tangent * ((u * 2 - 1) * halfSize) + binormal * ((v * 2 - 1) * halfSize);
                vertices.Add(Vertex(position, normal, tangent, binormal, new Vector2(u, v) * uvScale));
            }

        AddTriangle(indices, vertices, start, start + 1, start + 2);
        AddTriangle(indices, vertices, start + 1, start + 3, start + 2);
    }

    static (VertexBuffer, IndexBuffer) BuildCube(GraphicsDevice gd)
    {
        var vertices = new List<VertexPositionNormalTangentTexture>();
        var indices = new List<int>();

        foreach (var normal in new[] { Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right })
            AddQuad(vertices, indices, normal, Vector3.Down, 0.8f, normal * 0.8f, 1);
        AddQuad(vertices, indices, Vector3.Up, Vector3.Backward, 0.8f, Vector3.Up * 0.8f, 1);
        AddQuad(vertices, indices, Vector3.Down, Vector3.Forward, 0.8f, Vector3.Down * 0.8f, 1);

        return Upload(gd, vertices, indices);
    }

    static (VertexBuffer, IndexBuffer) BuildPlane(GraphicsDevice gd)
    {
        var vertices = new List<VertexPositionNormalTangentTexture>();
        var indices = new List<int>();

        AddQuad(vertices, indices, Vector3.Up, Vector3.Backward, 1.5f, Vector3.Zero, 2);
        // Underside, nudged down so it doesn't z-fight the top when No cull is on
        AddQuad(vertices, indices, Vector3.Down, Vector3.Forward, 1.5f, Vector3.Down * 0.001f, 2);

        return Upload(gd, vertices, indices);
    }
}
