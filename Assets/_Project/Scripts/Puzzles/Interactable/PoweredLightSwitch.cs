using UnityEngine;

/// <summary>
/// Switch of the SP2 (boxes) room. Without power (SP1 not completed) it looks grey, still takes the
/// interaction highlight, makes no sound and says it needs power, and its lights (<see cref="objects"/>)
/// start switched off. Once SP1 is completed it toggles them: on activates every object in the array,
/// off deactivates them again, with a click each time.
///
/// The grey goes through the renderers' property block (_BaseMap swapped for a grey texture, own
/// emission off), read-modify-write like <see cref="ItemProximityHighlight"/> does, so the
/// highlight keeps working on top of it.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PoweredLightSwitch : BaseRangeInteractable
{
    [Tooltip("The lights. Deactivated at start while SP1 is not completed; the switch toggles them.")]
    [SerializeField] private GameObject[] objects = new GameObject[0];

    [Tooltip("SP1 completed: the switch has power. Set automatically when the puzzle below completes.")]
    [SerializeField] private bool sp1Completed;

    [PuzzleId]
    [SerializeField] private string sp1PuzzleId = "sp1_panel_electrico";

    [Tooltip("ON: if this button's GameObject is inactive (activeSelf false) it does nothing — no " +
             "toggle, no look change. For a second button that shares the same objects.")]
    [SerializeField] private bool ignoreIfInactive = true;

    [Header("Look without power")]
    [Tooltip("Renderers greyed out while there is no power. Empty = every renderer under this object.")]
    [SerializeField] private Renderer[] renderers = new Renderer[0];
    [SerializeField] private Color unpoweredColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Header("Text")]
    [SerializeField] private string turnOnPrompt = "Turn on";
    [SerializeField] private string turnOffPrompt = "Turn off";
    [SerializeField] private string noPowerInfo = "You need to turn on the energy first";

    [Header("Audio")]
    [SoundId] [SerializeField] private string clickSoundId = "sfx_elevator_button_01";

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // On = the lights are active. Read from the objects themselves, not stored: two buttons sharing
    // the same array would otherwise each keep their own idea of it and undo each other.
    private bool IsOn
    {
        get
        {
            foreach (GameObject go in objects)
                if (go != null) return go.activeSelf;
            return false;
        }
    }

    private bool Ignored => ignoreIfInactive && !gameObject.activeSelf;
    private MaterialPropertyBlock block;
    private Texture2D greyTexture;

    protected override void Awake()
    {
        base.Awake();
        if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>(true);
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
    }

    private void Start()
    {
        if (PuzzleStateManager.Exists && PuzzleStateManager.Instance.IsPuzzleCompleted(sp1PuzzleId))
            sp1Completed = true;

        // No power yet: the room starts dark. Left as authored when SP1 is already done.
        if (!sp1Completed) SetLights(false);

        ApplyLook();
    }

    private void OnDestroy()
    {
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
        if (greyTexture != null) Destroy(greyTexture);
    }

    private void HandlePuzzleCompleted(string puzzleId)
    {
        if (puzzleId != sp1PuzzleId || Ignored) return;
        sp1Completed = true;
        ApplyLook();
        InteractionEvents.RequestPromptRefresh();
    }

    // ── IInteractable ───────────────────────────────────────────────────────

    public override string GetInteractText() => IsOn ? turnOffPrompt : turnOnPrompt;

    // Shown by the prompt in its grey info style while the switch cannot be used.
    public override string GetInfoText() => sp1Completed ? string.Empty : noPowerInfo;

    protected override bool CanInteractInCloseRange() => sp1Completed && !Ignored;

    public override bool IsRepeatable() => true;

    protected override void OnInteract()
    {
        if (!sp1Completed || Ignored) return;

        SetLights(!IsOn);

        if (!string.IsNullOrWhiteSpace(clickSoundId) && AudioManager.Exists)
            AudioManager.Instance.PlaySFX(clickSoundId, transform.position);

        InteractionEvents.RequestPromptRefresh();
    }

    // No OnInteractAttemptBlocked override: without power, pressing it is silent.

    private void SetLights(bool on)
    {
        foreach (GameObject go in objects)
            if (go != null) go.SetActive(on);
    }

    // ── Look ────────────────────────────────────────────────────────────────

    private void ApplyLook()
    {
        block ??= new MaterialPropertyBlock();

        if (!sp1Completed && greyTexture == null)
        {
            greyTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "SwitchUnpowered" };
            greyTexture.SetPixel(0, 0, unpoweredColor);
            greyTexture.Apply(false, true);
        }

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;

            Material[] materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material m = materials[i];
                if (m == null) continue;

                r.GetPropertyBlock(block, i);

                if (m.HasProperty(BaseMapId))
                {
                    Texture authored = m.GetTexture(BaseMapId);
                    block.SetTexture(BaseMapId, sp1Completed
                        ? (authored != null ? authored : Texture2D.whiteTexture)
                        : greyTexture);
                }

                if (m.HasProperty(EmissionColorId))
                    block.SetColor(EmissionColorId, sp1Completed ? m.GetColor(EmissionColorId) : Color.black);

                r.SetPropertyBlock(block, i);
            }
        }
    }
}
