using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The parts of a reward box that move or glow: the lid and the two latches (animated, from the
/// model's own clips through BoxController), the two locks hanging off the front (they spring open
/// and fall, procedurally: the model has no animation for them), the glow material on the locks and
/// the screen, the box's light and fog beacon, and the item model shown inside (<c>itemVisual</c>).
///
/// Only primitives live here. WHEN each one happens is <see cref="PuzzleRewardBox"/>'s; every step
/// also has an instant version, so a skipped cinematic or a box that loads already open lands on
/// exactly the pose the slow path would have reached.
///
/// The glow is written per material slot with a property block, which is why every part that
/// carries the glow material is under an <see cref="ExcludeFromHighlight"/>: the crosshair highlight
/// would otherwise put the colour it captured in its Awake back on those slots.
/// </summary>
[DisallowMultipleComponent]
public class RewardBoxRig : MonoBehaviour
{
    public enum Side { Right, Left }

    private const string LidLayer = "Lid";
    private const string LatchRightLayer = "LatchRight";
    private const string LatchLeftLayer = "LatchLeft";
    private const string OpenState = "Open";

    [Header("Animation")]
    [Tooltip("Runs BoxController: one layer per part (Lid, LatchRight, LatchLeft), each with a Closed " +
             "and an Open state. Tools > Puzzles > Set Up Reward Boxes builds it.")]
    [SerializeField] private Animator animator;
    [Tooltip("Only read for its length: the controller is what plays it.")]
    [SerializeField] private AnimationClip lidClip;
    [SerializeField] private AnimationClip latchRightClip;
    [SerializeField] private AnimationClip latchLeftClip;

    [Header("Locks")]
    [Tooltip("The lock that opens first: on the right seen from the front, next to the right latch.")]
    [SerializeField] private Transform lockRight;
    [SerializeField] private Transform lockLeft;

    [Header("Glow")]
    [Tooltip("The glowing material (Brillo). Every slot under the box that uses it follows the box's state.")]
    [SerializeField] private Material glowMaterial;

    [Header("Light")]
    [SerializeField] private Light boxLight;
    [Tooltip("What makes the light readable through the fog from any distance.")]
    [SerializeField] private FogBeacon beacon;

    [Header("Item")]
    [Tooltip("What shows inside the box once it opens. Drag the item's model onto Item Model in the " +
             "box's inspector, or put it under this object by hand. Switched off until the box opens, " +
             "however it is left in the editor.")]
    [SerializeField] private GameObject itemVisual;
    [Tooltip("Largest side, in metres, the box's inspector scales a dragged-in item model to.")]
    // Only read by PuzzleRewardBoxEditor and RewardBoxSetup through SerializedProperty, which the
    // compiler can't see, hence CS0414.
#pragma warning disable CS0414
    [SerializeField, Min(0.01f)] private float itemFitSize = 0.2f;
#pragma warning restore CS0414

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private enum GlowOwner { Body, LockRight, LockLeft }

    private struct GlowSlot
    {
        public Renderer Renderer;
        public int Index;
        public GlowOwner Owner;
    }

    private struct LockFall
    {
        public bool Computed;
        public Vector3 RestPosition;
        public Quaternion RestRotation;
        public Vector3 EndPosition;
        public Quaternion EndRotation;
    }

    private readonly List<GlowSlot> glowSlots = new List<GlowSlot>();
    private MaterialPropertyBlock block;
    private LockFall fallRight;
    private LockFall fallLeft;

    public float LidSeconds => lidClip != null ? lidClip.length : 1f;
    public float LatchSeconds(Side side)
    {
        AnimationClip clip = side == Side.Right ? latchRightClip : latchLeftClip;
        return clip != null ? clip.length : 0.3f;
    }

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        CollectGlowSlots();

        // Rest poses read before anything can move them.
        fallRight = new LockFall { RestPosition = PositionOf(lockRight), RestRotation = RotationOf(lockRight) };
        fallLeft = new LockFall { RestPosition = PositionOf(lockLeft), RestRotation = RotationOf(lockLeft) };

        if (itemVisual != null) itemVisual.SetActive(false);
    }

    // ── States ──────────────────────────────────────────────────────────────

    /// <summary>Everything red and the light on: the look of a box nobody has opened.</summary>
    public void ShowLocked(SO_RewardBoxConfig config)
    {
        SetGlow(GlowOwner.Body, config.LockedColor, config.LockedEmission);
        SetGlow(GlowOwner.LockRight, config.LockedColor, config.LockedEmission);
        SetGlow(GlowOwner.LockLeft, config.LockedColor, config.LockedEmission);
        SetLight(true, config.LockedColor, config.LockedLightIntensity);
    }

    /// <summary>The screen and the light turn green: both locks are off.</summary>
    public void ShowUnlocked(SO_RewardBoxConfig config, bool lightOn)
    {
        SetGlow(GlowOwner.Body, config.UnlockedColor, config.UnlockedEmission);
        SetLight(lightOn, config.UnlockedColor, config.UnlockedLightIntensity);
    }

    /// <summary>
    /// The end of the whole opening at once: latches and lid open, both locks on the floor, glow
    /// green. For a skip and for a box that loads already open.
    /// </summary>
    public void SnapOpen(SO_RewardBoxConfig config, bool lightOn)
    {
        PlayLayer(LatchRightLayer, 1f);
        PlayLayer(LatchLeftLayer, 1f);
        PlayLayer(LidLayer, 1f);
        if (HasController) animator.Update(0f);

        SnapLockDown(Side.Right, config);
        SnapLockDown(Side.Left, config);
        ShowUnlocked(config, lightOn);
    }

    public void SetLightOn(bool on)
    {
        if (boxLight != null) boxLight.enabled = on;
        if (beacon != null) beacon.enabled = on;
    }

    // ── Steps ───────────────────────────────────────────────────────────────

    /// <summary>Starts a latch opening and turns its lock's LEDs green. Returns the clip's length.</summary>
    public float OpenLatch(Side side, SO_RewardBoxConfig config)
    {
        PlayLayer(side == Side.Right ? LatchRightLayer : LatchLeftLayer, 0f);
        SetGlow(OwnerOf(side), config.UnlockedColor, config.UnlockedEmission);
        return LatchSeconds(side);
    }

    /// <summary>Starts the lid opening. Returns the clip's length.</summary>
    public float OpenLid()
    {
        PlayLayer(LidLayer, 0f);
        return LidSeconds;
    }

    /// <summary>
    /// The lock springs up off its hasp and falls to whatever is below the box, tumbling forward.
    /// Stops where it is when <paramref name="cut"/> says so; <see cref="SnapOpen"/> then lands it.
    /// </summary>
    public IEnumerator DropLock(Side side, SO_RewardBoxConfig config, Func<bool> cut)
    {
        Transform lockPart = side == Side.Right ? lockRight : lockLeft;
        if (lockPart == null) yield break;

        LockFall fall = EnsureFall(side, config);
        Detach(lockPart);

        Vector3 hopTop = fall.RestPosition + Vector3.up * config.LockHopHeight;
        float elapsed = 0f;
        while (elapsed < config.LockHopSeconds)
        {
            if (cut()) yield break;
            elapsed += Time.deltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(elapsed / config.LockHopSeconds) * Mathf.PI * 0.5f);
            lockPart.position = Vector3.Lerp(fall.RestPosition, hopTop, k);
            yield return null;
        }

        // Free fall from the top of the hop: the height decides the time, so a lock over a tall
        // pedestal lands as fast as gravity says and not in a fixed number of seconds.
        float drop = Mathf.Max(0.01f, hopTop.y - fall.EndPosition.y);
        float fallSeconds = Mathf.Sqrt(2f * drop / config.LockGravity);
        elapsed = 0f;
        while (elapsed < fallSeconds)
        {
            if (cut()) yield break;
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / fallSeconds);

            Vector3 position = Vector3.Lerp(hopTop, fall.EndPosition, k);
            position.y = Mathf.Max(fall.EndPosition.y, hopTop.y - 0.5f * config.LockGravity * elapsed * elapsed);
            lockPart.SetPositionAndRotation(position, Quaternion.Slerp(fall.RestRotation, fall.EndRotation, k));
            yield return null;
        }

        SnapLockDown(side, config);
        PlaySound(config.LockLandSoundId, fall.EndPosition);
    }

    // ── Item ────────────────────────────────────────────────────────────────

    /// <summary>Shows the item model inside the box (nothing when none is assigned). Hidden by the
    /// closed lid until it opens.</summary>
    public void ShowItem()
    {
        if (itemVisual != null) itemVisual.SetActive(true);
    }

    public void HideItem()
    {
        if (itemVisual != null) itemVisual.SetActive(false);
    }

    // ── Internals ───────────────────────────────────────────────────────────

    private bool HasController => animator != null && animator.runtimeAnimatorController != null;

    private void PlayLayer(string layerName, float normalizedTime)
    {
        if (!HasController) return;
        int layer = animator.GetLayerIndex(layerName);
        if (layer < 0) return;
        animator.Play(OpenState, layer, normalizedTime);
    }

    private void SnapLockDown(Side side, SO_RewardBoxConfig config)
    {
        Transform lockPart = side == Side.Right ? lockRight : lockLeft;
        if (lockPart == null) return;

        LockFall fall = EnsureFall(side, config);
        Detach(lockPart);
        lockPart.SetPositionAndRotation(fall.EndPosition, fall.EndRotation);
        SetGlow(OwnerOf(side), config.UnlockedColor, config.DroppedLockEmission);
    }

    /// <summary>
    /// Where the lock comes to rest, worked out the first time it is needed (the floor under it may
    /// belong to a scene that loads after this one). The same pose every time, so a skip lands it
    /// exactly where the fall would have.
    /// </summary>
    private LockFall EnsureFall(Side side, SO_RewardBoxConfig config)
    {
        LockFall fall = side == Side.Right ? fallRight : fallLeft;
        if (fall.Computed) return fall;

        Transform lockPart = side == Side.Right ? lockRight : lockLeft;
        float sign = side == Side.Right ? 1f : -1f;

        // The box's front is +Z: the locks hang on it. "Right" is the right seen from the front.
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        Vector3 viewerRight = Vector3.Cross(Vector3.up, -forward);

        fall.EndRotation = Quaternion.AngleAxis(12f * sign, Vector3.up) *
                           Quaternion.AngleAxis(config.LockTumbleDegrees, -viewerRight) *
                           fall.RestRotation;

        Vector3 landing = fall.RestPosition + forward * config.LockForwardKick + viewerRight * (0.02f * sign);

        // Probed where it lands, not where it hangs: kicked off the front, a lock can clear the
        // edge of whatever the box stands on and go all the way to the floor.
        Vector3 centreOffset = lockPart.TryGetComponent(out Renderer lockRenderer)
            ? lockRenderer.bounds.center - fall.RestPosition
            : Vector3.zero;
        float floorY = FloorBelow(landing + centreOffset, fall.RestPosition.y - 1f);
        landing.y = floorY - LowestPointOffset(lockPart, fall.EndRotation) + 0.002f;

        fall.EndPosition = landing;
        fall.Computed = true;

        if (side == Side.Right) fallRight = fall;
        else fallLeft = fall;
        return fall;
    }

    /// <summary>Height of the first solid thing under <paramref name="origin"/> that is not this box or a body.</summary>
    private float FloorBelow(Vector3 origin, float fallbackY)
    {
        int mask = Physics.DefaultRaycastLayers;
        foreach (string bodyLayer in new[] { "Player", "Nemesis" })
        {
            int layer = LayerMask.NameToLayer(bodyLayer);
            if (layer >= 0) mask &= ~(1 << layer);
        }

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 5f, mask, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        float floor = fallbackY;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.distance >= best) continue;
            best = hit.distance;
            floor = hit.point.y;
        }
        return floor;
    }

    /// <summary>How far below its pivot the lock's lowest corner sits once turned to <paramref name="rotation"/>.</summary>
    private static float LowestPointOffset(Transform lockPart, Quaternion rotation)
    {
        if (!lockPart.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) return 0f;

        Bounds bounds = filter.sharedMesh.bounds;
        Vector3 scale = lockPart.lossyScale;
        float lowest = float.PositiveInfinity;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, Corner(i));
            lowest = Mathf.Min(lowest, (rotation * Vector3.Scale(corner, scale)).y);
        }
        return lowest;
    }

    private static Vector3 Corner(int i) =>
        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);

    private void CollectGlowSlots()
    {
        glowSlots.Clear();
        if (glowMaterial == null) return;

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != glowMaterial) continue;
                glowSlots.Add(new GlowSlot { Renderer = renderer, Index = i, Owner = OwnerOf(renderer.transform) });
            }
        }
    }

    private GlowOwner OwnerOf(Transform part)
    {
        if (lockRight != null && part.IsChildOf(lockRight)) return GlowOwner.LockRight;
        if (lockLeft != null && part.IsChildOf(lockLeft)) return GlowOwner.LockLeft;
        return GlowOwner.Body;
    }

    private static GlowOwner OwnerOf(Side side) => side == Side.Right ? GlowOwner.LockRight : GlowOwner.LockLeft;

    private void SetGlow(GlowOwner owner, Color baseColor, Color emission)
    {
        foreach (GlowSlot slot in glowSlots)
        {
            if (slot.Owner != owner || slot.Renderer == null) continue;
            slot.Renderer.GetPropertyBlock(block, slot.Index);
            block.SetColor(BaseColorId, baseColor);
            block.SetColor(EmissionColorId, emission);
            slot.Renderer.SetPropertyBlock(block, slot.Index);
        }
    }

    private void SetLight(bool on, Color color, float intensity)
    {
        if (boxLight != null)
        {
            boxLight.color = color;
            boxLight.intensity = intensity;
            boxLight.enabled = on;
        }
        if (beacon != null)
        {
            beacon.color = color;
            beacon.enabled = on;
        }
    }

    /// <summary>
    /// Takes a falling lock out of the box's hierarchy, keeping its pose. A box placed under a
    /// non-uniformly scaled parent (a BoxSupport) carries that scale, and a child turning inside it
    /// would shear as it tumbles; at the scene root it turns as a rigid piece. Still in the box's
    /// scene, so it unloads with it.
    /// </summary>
    private static void Detach(Transform lockPart)
    {
        if (lockPart.parent != null) lockPart.SetParent(null, true);
    }

    private static Vector3 PositionOf(Transform t) => t != null ? t.position : Vector3.zero;
    private static Quaternion RotationOf(Transform t) => t != null ? t.rotation : Quaternion.identity;

    private static void PlaySound(string soundId, Vector3 position)
    {
        if (string.IsNullOrWhiteSpace(soundId) || !AudioManager.Exists) return;
        AudioManager.Instance.PlaySFX(soundId, position);
    }
}
