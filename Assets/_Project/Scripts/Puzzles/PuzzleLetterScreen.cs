using TMPro;
using UnityEngine;

/// <summary>
/// The small screen on the boiler (Central Puzzle 2 — SP1): a little block whose letter changes
/// like a combination, one letter per knock, in the colour of that lever. Built by
/// <see cref="BoilerInteractable"/>, which tells it what to show as the knocks play; the lever
/// panel carries the same letters and colours (<see cref="LeverSymbols"/>).
/// </summary>
public class PuzzleLetterScreen : MonoBehaviour
{
    private static readonly Color IdleColor = new Color(0.10f, 0.35f, 0.14f);

    private TextMeshPro text;

    /// <summary>Builds the block and its text under <paramref name="parent"/>.</summary>
    public static PuzzleLetterScreen Create(Transform parent, Vector3 localPosition, Quaternion localRotation,
                                            Vector2 size, Material bodyMaterial)
    {
        var root = new GameObject("SequenceScreen");
        root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        root.transform.localRotation = localRotation;
        PuzzleLetterScreen screen = root.AddComponent<PuzzleLetterScreen>();

        // The block: a dark box, so the letter stands out and it reads as a small display.
        const float depth = 0.06f;
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.layer = root.layer;
        Destroy(body.GetComponent<Collider>());   // never in the way of the crosshair
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(size.x, size.y, depth);
        body.transform.localPosition = new Vector3(0f, 0f, depth * 0.5f);
        Renderer bodyRenderer = body.GetComponent<Renderer>();
        if (bodyMaterial != null) bodyRenderer.sharedMaterial = bodyMaterial;
        var black = new MaterialPropertyBlock();   // pure black whatever the material
        black.SetColor("_BaseColor", Color.black);
        black.SetColor("_Color", Color.black);
        bodyRenderer.SetPropertyBlock(black);

        // The letter, on the front (-Z) face.
        var textGo = new GameObject("Letter", typeof(RectTransform));
        textGo.layer = root.layer;
        textGo.transform.SetParent(root.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.004f);
        ((RectTransform)textGo.transform).sizeDelta = new Vector2(size.x * 0.9f, size.y * 0.9f);

        screen.text = textGo.AddComponent<TextMeshPro>();
        screen.text.alignment = TextAlignmentOptions.Center;
        screen.text.fontStyle = FontStyles.Bold;
        screen.text.fontSize = size.y * 9f;   // 3D text: roughly 1 unit of size per 0.1 m of height
        screen.text.textWrappingMode = TextWrappingModes.NoWrap;
        screen.text.raycastTarget = false;

        screen.Clear();
        return screen;
    }

    /// <summary>Shows the letter and colour of a lever (counted from 1).</summary>
    public void Show(int lever)
    {
        text.text = LeverSymbols.Letter(lever);
        text.color = LeverSymbols.ColorOf(lever);
    }

    /// <summary>Between knocks and at rest: nothing lit.</summary>
    public void Clear()
    {
        text.text = "-";
        text.color = IdleColor;
    }
}
