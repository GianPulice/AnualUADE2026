using System.Collections;
using UnityEngine;

/// <summary>
/// The light of a socket when the alarm goes off (Paso 1): flickering amber for a moment, then
/// fixed red. One job. Put it next to <see cref="SocketInteractable"/> on each of the three
/// sockets; whoever raises the alarm calls <see cref="Activate"/>.
///
/// NOT used by the escape since 22/09: the three cores of the safe zone stay green when the last
/// one goes in (Iñaki's call). Left on the sockets, inert, in case the alarm comes back.
///
/// Drives the same emissive slots as <see cref="SocketEmissionShift"/> (the setup copies them
/// across) and stops that component's fade to green first, so the two do not fight over the
/// colour. Red is used here on purpose although it is reserved to the Nemesis elsewhere: the
/// alarm is the one non-Nemesis moment of the script that calls for it (Paso 1).
/// </summary>
[DisallowMultipleComponent]
public class EscapeSocketAlarmLight : MonoBehaviour
{
    [Tooltip("Las mismas ranuras emisivas de SocketEmissionShift (Renderer + índice de material).")]
    [SerializeField] private SocketEmissionShift.Target[] targets = new SocketEmissionShift.Target[0];

    [Tooltip("Luz real del socket (opcional). Sigue el mismo color que el emisivo.")]
    [SerializeField] private Light lightSource;

    [ColorUsage(false, true)]
    [SerializeField] private Color amberColor = new Color(2.4f, 1.1f, 0.15f, 1f);

    [ColorUsage(false, true)]
    [SerializeField] private Color redColor = new Color(3f, 0.05f, 0.03f, 1f);

    [Tooltip("Segundos de parpadeo ámbar antes de quedar en rojo fijo.")]
    [SerializeField, Min(0f)] private float amberFlickerSeconds = 0.6f;

    [Tooltip("Parpadeos por segundo durante el ámbar.")]
    [SerializeField, Min(0.5f)] private float flickerRate = 9f;

    private static readonly int EmitColorId = Shader.PropertyToID("_EmissionColor");

    private MaterialPropertyBlock block;
    private Coroutine routine;

    /// <summary>The red is on (or on its way).</summary>
    public bool IsAlarmed { get; private set; }

    /// <summary>Flickers amber, then holds red. Idempotent.</summary>
    public void Activate()
    {
        if (IsAlarmed) return;
        IsAlarmed = true;

        // The insert just started SocketEmissionShift's fade to green: cut it, or it would keep
        // writing over the alarm colour for the next second.
        SocketEmissionShift shift = GetComponent<SocketEmissionShift>();
        if (shift != null) shift.StopAllCoroutines();

        routine = StartCoroutine(AlarmRoutine());
    }

    /// <summary>Red at once, no flicker. What a skip needs.</summary>
    public void ActivateNow()
    {
        IsAlarmed = true;

        SocketEmissionShift shift = GetComponent<SocketEmissionShift>();
        if (shift != null) shift.StopAllCoroutines();
        if (routine != null) StopCoroutine(routine);
        routine = null;

        Paint(redColor);
    }

    private IEnumerator AlarmRoutine()
    {
        float elapsed = 0f;
        while (elapsed < amberFlickerSeconds)
        {
            elapsed += Time.deltaTime;

            bool on = Mathf.Repeat(elapsed * flickerRate, 1f) < 0.5f;
            Paint(on ? amberColor : Color.black);
            yield return null;
        }

        Paint(redColor);
        routine = null;
    }

    private void Paint(Color color)
    {
        block ??= new MaterialPropertyBlock();

        for (int i = 0; i < targets.Length; i++)
        {
            Renderer renderer = targets[i].renderer;
            if (renderer == null) continue;

            // Read back first, so only the emission colour of this slot changes.
            renderer.GetPropertyBlock(block, targets[i].materialIndex);
            block.SetColor(EmitColorId, color);
            renderer.SetPropertyBlock(block, targets[i].materialIndex);
        }

        if (lightSource != null)
        {
            lightSource.enabled = color.maxColorComponent > 0.01f;
            lightSource.color = color / Mathf.Max(1f, color.maxColorComponent);
        }
    }
}
