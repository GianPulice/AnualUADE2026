using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Company intro video played by the Bootstrapper before the main menu. It cannot be skipped.
///
/// Builds its own Screen Space Overlay canvas at runtime (black background + RawImage fed by a
/// RenderTexture) so the video is drawn after the camera renderer features / post-processing and
/// does not get the PSX treatment. The canvas lives in the Bootstrap scene, so the black stays on
/// screen until the Bootstrapper unloads it, hiding the menu while it loads.
///
/// The audio goes through an AudioSource routed to the mixer, so it respects the volume settings
/// the AudioManager applies when Data loads (call this after Data is loaded).
/// </summary>
public class IntroVideoSequence : MonoBehaviour
{
    [Header("Video")]
    [SerializeField] private VideoClip clip;
    [Tooltip("Mixer group the video audio is routed to.")]
    [SerializeField] private AudioMixerGroup audioGroup;

    [Header("Fades (seconds, unscaled)")]
    [SerializeField] private float fadeInDuration = 0.5f;
    [SerializeField] private float fadeOutDuration = 0.6f;
    [Tooltip("Black kept on screen after the fade out, before the menu starts loading.")]
    [SerializeField] private float holdBlackAfter = 0.2f;

    [Header("Safety")]
    [Tooltip("If the video fails to prepare within this time, the intro is skipped.")]
    [SerializeField] private float prepareTimeout = 5f;

#if UNITY_EDITOR
    [Tooltip("Editor only: play the intro when entering Play Mode.")]
    [SerializeField] private bool playInEditor = true;
#endif

    private const int CanvasSortingOrder = 32000;

    private RenderTexture renderTexture;

    public async UniTask PlayAsync()
    {
#if UNITY_EDITOR
        if (!playInEditor) return;
#endif
        if (clip == null)
        {
            Debug.LogWarning("[IntroVideo] No VideoClip assigned. Skipping the intro.");
            return;
        }

        var token = this.GetCancellationTokenOnDestroy();

        renderTexture = new RenderTexture((int)clip.width, (int)clip.height, 0);
        renderTexture.Create();

        CanvasGroup videoGroup = BuildCanvas();
        AudioListener tempListener = EnsureAudioListener();
        VideoPlayer player = BuildPlayer();

        bool failed = false;
        bool finished = false;
        player.errorReceived += (_, message) =>
        {
            Debug.LogError($"[IntroVideo] {message}");
            failed = true;
        };
        player.loopPointReached += _ => finished = true;

        try
        {
            player.Prepare();
            bool timedOut = await UniTask
                .WaitUntil(() => player.isPrepared || failed, cancellationToken: token)
                .TimeoutWithoutException(TimeSpan.FromSeconds(prepareTimeout), DelayType.UnscaledDeltaTime);
            if (timedOut || failed)
            {
                Debug.LogWarning("[IntroVideo] The video could not be prepared. Skipping the intro.");
                return;
            }

            player.Play();
            await FadeAsync(videoGroup, 0f, 1f, fadeInDuration, token);
            await UniTask.WaitUntil(() => finished || failed, cancellationToken: token);
            await FadeAsync(videoGroup, 1f, 0f, fadeOutDuration, token);
            await UniTask.Delay(TimeSpan.FromSeconds(holdBlackAfter), ignoreTimeScale: true, cancellationToken: token);
        }
        finally
        {
            if (player != null)
            {
                player.Stop();
                Destroy(player.gameObject);
            }
            if (tempListener != null) Destroy(tempListener.gameObject);
        }
    }

    private CanvasGroup BuildCanvas()
    {
        var canvasGO = new GameObject("IntroVideoCanvas", typeof(RectTransform), typeof(Canvas));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;

        var background = CreateFullScreen<Image>("Background", canvasGO.transform);
        background.color = Color.black;
        background.raycastTarget = false;

        var video = CreateFullScreen<RawImage>("Video", canvasGO.transform);
        video.texture = renderTexture;
        video.raycastTarget = false;

        // Keep the clip aspect ratio; the black background fills the letterbox.
        var fitter = video.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = (float)clip.width / clip.height;

        var group = video.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        return group;
    }

    private static T CreateFullScreen<T>(string name, Transform parent) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(T));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return go.GetComponent<T>();
    }

    private VideoPlayer BuildPlayer()
    {
        var go = new GameObject("IntroVideoPlayer");
        go.transform.SetParent(transform, false);

        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = audioGroup;

        var player = go.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.isLooping = false;
        player.skipOnDrop = true;
        player.source = VideoSource.VideoClip;
        player.clip = clip;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = renderTexture;
        player.audioOutputMode = VideoAudioOutputMode.AudioSource;
        player.controlledAudioTrackCount = 1;
        player.EnableAudioTrack(0, true);
        player.SetTargetAudioSource(0, source);
        return player;
    }

    // Nothing in Bootstrap/Data has a camera, so there is no AudioListener until the menu loads.
    private static AudioListener EnsureAudioListener()
    {
        if (FindAnyObjectByType<AudioListener>() != null) return null;
        return new GameObject("IntroAudioListener").AddComponent<AudioListener>();
    }

    private static async UniTask FadeAsync(CanvasGroup group, float from, float to, float duration, System.Threading.CancellationToken token)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, t / duration);
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
        group.alpha = to;
    }

    private void OnDestroy()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}
