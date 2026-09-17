using UnityEngine;

/// <summary>
/// Switches for the wake-up cinematic (ARC_01a + ARC_01b, <see cref="WakeUpCinematicView"/>):
/// whether it plays at all and whether the player can skip it.
///
/// Read through <see cref="ArchitectVoiceController.WakeUpConfig"/>, so the HUD pieces that need it
/// (the cinematic and <see cref="WakeUpSkipPromptView"/>) share the one reference on the controller.
/// Without an asset assigned the cinematic plays and can be skipped with F.
/// </summary>
[CreateAssetMenu(fileName = "SO_WakeUpCinematicConfig", menuName = "Scriptable Objects/Architect/Wake-Up Cinematic Config")]
public class SO_WakeUpCinematicConfig : ScriptableObject
{
    [Tooltip("Off: the level starts with control. No black screen, no camera pan, no ARC_01a / ARC_01b.")]
    [SerializeField] private bool cinematicEnabled = true;

    [Header("Skip")]
    [Tooltip("Lets the player cut the cinematic with the skip key. The prompt only shows when this is on.")]
    [SerializeField] private bool skippable = true;

    [SerializeField] private KeyCode skipKey = KeyCode.F;

    [Tooltip("{0} = the skip key.")]
    [SerializeField] private string skipPromptFormat = "[Press {0} to skip]";

    public bool CinematicEnabled => cinematicEnabled;
    public bool Skippable => skippable;
    public KeyCode SkipKey => skipKey;
    public string SkipPromptText => string.Format(skipPromptFormat, skipKey);
}
