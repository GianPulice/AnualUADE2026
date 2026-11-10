using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// TEMPORARY (deleted at the end of the session): renders the item glint to check colour and walls.
internal static class TmpGlintRender
{
    private const string Dir = @"C:\Users\IAKI~1\AppData\Local\Temp\claude\af640412bb5abeb3\38e5ec6a-329a-44b0-93c3-75460b37371a\scratchpad\";

    [MenuItem("Tools/Tmp/Glint Render")]
    private static void Run()
    {
        var log = new StringBuilder();
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Items/mat_item_glint.mat");
        var keys = AssetDatabase.LoadAssetAtPath<SO_GlintProfile>("Assets/_Project/ScriptableObjects/Highlight/SO_Glint_Keys.asset");
        var cat = AssetDatabase.LoadAssetAtPath<SO_ItemCategoryConfig>("Assets/_Project/ScriptableObjects/CategoryConfig/" +
            System.IO.Path.GetFileName(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:SO_ItemCategoryConfig")[0])));
        log.AppendLine($"material {mat != null} profile {keys != null} categories {cat != null}");

        Scene scene = EditorSceneManager.NewPreviewScene();
        var camGo = new GameObject("cam");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        Camera cam = camGo.AddComponent<Camera>();
        cam.scene = scene;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.05f;
        cam.allowHDR = true;
        var rt = new RenderTexture(800, 300, 24, RenderTextureFormat.ARGBHalf);
        cam.targetTexture = rt;

        Mesh quad = new Mesh();
        quad.SetVertices(new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) });
        quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
        quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        quad.bounds = new Bounds(Vector3.zero, Vector3.one * 1.5f);

        try
        {
            // 1. Colours: red, green, blue, yellow category hues at the keys' intensity (old resolve vs new).
            camGo.transform.SetPositionAndRotation(new Vector3(0, 0, -5), Quaternion.identity);
            Color[] hues = { new Color(1, 0.15f, 0.1f), new Color(0.2f, 1, 0.3f), new Color(0.2f, 0.45f, 1), new Color(1, 0.85f, 0.1f) };
            Color baseColor = new Color(12, 12, 12, 1);
            for (int row = 0; row < 2; row++)
            {
                for (int i = 0; i < hues.Length; i++)
                {
                    Color c;
                    if (row == 0) c = Color.Lerp(baseColor, hues[i], 0.75f);   // the old resolve
                    else c = Color.Lerp(Color.white, hues[i], 0.75f) * 12f;    // the new one
                    c.a = 1;
                    Draw(cam, mat, quad, new Vector3(-2.7f + i * 1.8f, row == 0 ? 0.55f : -0.55f, 0), 1f, c, 0.15f, row == 0);
                }
            }
            cam.Render();
            Save(rt, Dir + "glint_colours.png", log, "colours (top: old resolve+old blend, bottom: new)");

            // 2. The wall: 41 cm thick, the star 4 cm behind it, the camera on the far side at angles.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = new Vector3(0.205f, 0f, 0f);
            wall.transform.localScale = new Vector3(0.41f, 3f, 6f);
            var wallMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            wallMat.color = new Color(0.08f, 0.08f, 0.1f);
            wall.GetComponent<Renderer>().sharedMaterial = wallMat;
            Vector3 star = new Vector3(-0.045f, 0f, 0f);

            foreach (float angle in new[] { 0f, 35f, 60f })
            {
                foreach (float pull in new[] { 0.4f, 0.155f })
                {
                    Vector3 dir = Quaternion.Euler(0, -angle, 0) * Vector3.right;
                    camGo.transform.position = star + dir * 4f;
                    camGo.transform.LookAt(star);
                    float size = Mathf.Max(0.4f, 0.075f * 2f * 4f * Mathf.Tan(20f * Mathf.Deg2Rad));
                    Draw(cam, mat, quad, star, size, new Color(12, 3, 3, 1), pull, false);
                    cam.Render();
                    float lit = Brightness(rt);
                    log.AppendLine($"wall: camera {angle,2} deg off the wall's normal, pull {pull:0.000} m -> star pixels showing on the near face: {lit:0}");
                    if (angle == 35f) Save(rt, Dir + $"glint_wall_{(pull > 0.3f ? "old" : "new")}.png", log, $"wall 35 deg pull {pull}");
                }
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(quad);
            rt.Release();
        }

        File.WriteAllText(Dir + "glint_render.txt", log.ToString());
        Debug.Log("[TmpGlintRender] done\n" + log);
    }

    private static void Draw(Camera cam, Material mat, Mesh quad, Vector3 at, float size, Color colour, float pull, bool oldLook)
    {
        var block = new MaterialPropertyBlock();
        block.SetColor("_GlintColor", colour);
        block.SetFloat("_GlintAlpha", 1f);
        block.SetFloat("_GlintRotation", 0f);
        block.SetFloat("_GlintPull", pull);
        // The old look: rays not limited, no white split (ray peak huge, core white 0) = colour * shape, clipped.
        if (oldLook) { block.SetFloat("_RayPeak", 100f); block.SetFloat("_CoreWhite", 0f); }
        var rp = new RenderParams(mat) { camera = cam, matProps = block, layer = 0, shadowCastingMode = ShadowCastingMode.Off };
        Graphics.RenderMesh(rp, quad, 0, Matrix4x4.TRS(at, Quaternion.identity, Vector3.one * size));
    }

    private static float Brightness(RenderTexture rt)
    {
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = null;
        int lit = 0;
        foreach (Color c in tex.GetPixels()) if (c.r > 0.3f) lit++;
        Object.DestroyImmediate(tex);
        return lit;
    }

    private static void Save(RenderTexture rt, string path, StringBuilder log, string what)
    {
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = null;
        var px = tex.GetPixels();
        for (int i = 0; i < px.Length; i++) px[i] = new Color(Mathf.Clamp01(px[i].r), Mathf.Clamp01(px[i].g), Mathf.Clamp01(px[i].b), 1);
        var outTex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        outTex.SetPixels(px);
        File.WriteAllBytes(path, outTex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(outTex);
        log.AppendLine($"saved {what}: {path}");
    }
}
