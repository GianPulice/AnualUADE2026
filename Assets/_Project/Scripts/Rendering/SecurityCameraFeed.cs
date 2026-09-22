using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Makes a Cinemachine shot read as a security camera: while the shot is live, the frame goes
/// through <c>SecurityCamera.mat</c> (drawn by <see cref="SecurityFeedRendererFeature"/> on
/// PC_Renderer) with this camera's label, a blinking REC and a running date and time burnt into the
/// corners. On the escape: Cam_Slam and Cam_4_Gate.
///
/// Nothing has to call it. Every frame it renders a game camera, the renderer feature asks whether
/// that camera's brain has one of these on the air (<see cref="FindLive"/>), so the look comes and
/// goes on the very frame of the cut, whoever makes the cut — the escape director today. The first
/// moments after a cut in also carry a burst of noise, like a monitor switching channel.
///
/// Out of Play no shot of this kind is on the air (their priority keeps them off), which is what
/// <see cref="previewInEditMode"/> is for: seeing the material on the Game view while tuning it.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class SecurityCameraFeed : MonoBehaviour
{
    /// <summary>Characters per overlay line; the shader's text array holds two of them.</summary>
    private const int LineCapacity = 32;

    [Tooltip("Arriba a la izquierda. Letras, números y : - / . # > (los acentos se sacan; lo demás " +
             "sale como espacio). Máximo 32.")]
    [SerializeField] private string label = "CAM 01";

    [Tooltip("La fecha de abajo a la izquierda, tal cual. Vacío = la de hoy (dd-MM-yyyy).")]
    [SerializeField] private string date = "";

    [Tooltip("La hora del reloj al cargar la escena (HH:mm:ss). Después corre con el juego, así dos " +
             "cámaras de la misma partida marcan horas que cierran entre sí.")]
    [SerializeField] private string clockStart = "03:12:00";

    [Tooltip("Muestra este feed en el Game view fuera de Play, para ajustar SecurityCamera.mat. En " +
             "Play no hace nada: ahí manda qué cámara está al aire.")]
    [SerializeField] private bool previewInEditMode;

    private static readonly List<SecurityCameraFeed> Feeds = new List<SecurityCameraFeed>();

    // One feed is on screen at a time, so the text buffer is shared; its owner says whose it is.
    private static readonly float[] Text = new float[LineCapacity * 2];
    private static SecurityCameraFeed textOwner;

    private static readonly int TextId = Shader.PropertyToID("_SecurityFeedText");
    private static readonly int InfoId = Shader.PropertyToID("_SecurityFeedInfo");

    private CinemachineVirtualCameraBase shot;
    private float clockStartSeconds;
    private int textSecond = int.MinValue;
    private int labelLength;
    private int clockLength;
    private int lastShownFrame = int.MinValue;
    private float liveSince;

    private void OnEnable()
    {
        shot = GetComponent<CinemachineVirtualCameraBase>();
        clockStartSeconds = ParseClock(clockStart);
        if (!Feeds.Contains(this)) Feeds.Add(this);
    }

    private void OnDisable()
    {
        Feeds.Remove(this);
        if (textOwner == this) textOwner = null;
    }

    private void OnValidate()
    {
        clockStartSeconds = ParseClock(clockStart);
        textSecond = int.MinValue;
    }

    /// <summary>
    /// The feed to draw over <paramref name="camera"/> this frame, or null. In Play: the one whose
    /// shot is live on that camera's brain. Out of Play: the first one previewing.
    /// </summary>
    public static SecurityCameraFeed FindLive(Camera camera)
    {
        if (Feeds.Count == 0 || camera == null) return null;

        if (!Application.isPlaying)
        {
            for (int i = 0; i < Feeds.Count; i++)
                if (Feeds[i].previewInEditMode) return Feeds[i];
            return null;
        }

        if (!camera.TryGetComponent(out CinemachineBrain brain)) return null;

        for (int i = 0; i < Feeds.Count; i++)
        {
            SecurityCameraFeed feed = Feeds[i];
            if (feed.shot != null && brain.IsLiveChild(feed.shot, true)) return feed;
        }
        return null;
    }

    /// <summary>
    /// Publishes this feed's text and timing for the frame about to be drawn. Called by the renderer
    /// feature, once per frame the feed is on screen: a frame without a call is a frame off the air,
    /// so the next call is a cut in and restarts the cut noise.
    /// </summary>
    public void PushGlobals()
    {
        bool playing = Application.isPlaying;
        float now = playing ? Time.time : Time.realtimeSinceStartup;

        int frame = Time.frameCount;
        if (frame != lastShownFrame)
        {
            if (frame - lastShownFrame > 1) liveSince = now;
            lastShownFrame = frame;
        }

        int second = Mathf.FloorToInt(clockStartSeconds + (playing ? Time.timeSinceLevelLoad : now));
        if (second != textSecond || textOwner != this)
        {
            WriteText(second);
            textSecond = second;
            textOwner = this;
            Shader.SetGlobalFloatArray(TextId, Text);
        }

        // The preview shows the settled feed, not a cut that never happened.
        float onAir = playing ? now - liveSince : 1000f;
        Shader.SetGlobalVector(InfoId, new Vector4(labelLength, clockLength, onAir, 0f));
    }

    private void WriteText(int second)
    {
        labelLength = WriteLine(label, 0);

        string day = string.IsNullOrWhiteSpace(date)
            ? DateTime.Today.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)
            : date;
        int s = ((second % 86400) + 86400) % 86400;
        clockLength = WriteLine($"{day}  {s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}", LineCapacity);
    }

    private static int WriteLine(string text, int start)
    {
        int length = Mathf.Min(text != null ? text.Length : 0, LineCapacity);
        for (int i = 0; i < LineCapacity; i++) Text[start + i] = i < length ? GlyphOf(text[i]) : 0f;
        return length;
    }

    /// <summary>Index of <paramref name="c"/> in the shader's font (kGlyphs in
    /// SecurityCamera_HLSL.shader) — the two orders have to match. 0 is the space.</summary>
    private static float GlyphOf(char c)
    {
        c = char.ToUpperInvariant(c);
        if (c >= '0' && c <= '9') return 1 + (c - '0');
        if (c >= 'A' && c <= 'Z') return 11 + (c - 'A');

        switch (c)
        {
            case ':': return 37;
            case '-': return 38;
            case '/': return 39;
            case '.': return 40;
            case '#': return 41;
            case '>': return 42;
            case 'Á': case 'À': case 'Â': case 'Ä': return GlyphOf('A');
            case 'É': case 'È': case 'Ê': case 'Ë': return GlyphOf('E');
            case 'Í': case 'Ì': case 'Î': case 'Ï': return GlyphOf('I');
            case 'Ó': case 'Ò': case 'Ô': case 'Ö': return GlyphOf('O');
            case 'Ú': case 'Ù': case 'Û': case 'Ü': return GlyphOf('U');
            case 'Ñ': return GlyphOf('N');
            case 'Ç': return GlyphOf('C');
            default: return 0;
        }
    }

    private static float ParseClock(string value)
    {
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan time)
            ? (float)time.TotalSeconds
            : 0f;
    }
}
