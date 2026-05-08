#if SYSTEM_DIALOGUE
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using DG.Tweening;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.UI;

public class BackgroundManager : MonoBehaviour, INotificationReceiver, IEffectManager {
    public static BackgroundManager Instance;

    [Header("��� ������")]
    public Image frontBg;
    public Image backBg;

    [Header("���͹ڽ� ������ (������ �ܻ� SpriteRenderer)")]
    public GameObject letterboxPrefab;

    public MarkerTrackType TargetTrackType => MarkerTrackType.Background;

    private Tween bgTween;
    private UniversalMarker lastProcessedMarker;
    private double lastProcessTime;
    private List<GameObject> activeLetterboxes = new List<GameObject>();

    private void Awake() {
        Instance = this;
        SetGroupAlpha(frontBg, 0f);
        SetGroupAlpha(backBg, 0f);
        DialogueManager.OnGlobalTimelineSkipped += HandleSkip;
        DialogueManager.OnTimelineScrubbed += HandleScrub;
    }

    private void Start() {
        if (DialogueManager.Instance != null) {
            DialogueManager.Instance.RegisterEffectManager(this);
        }
    }

    private void OnDestroy() {
        DialogueManager.OnGlobalTimelineSkipped -= HandleSkip;
        DialogueManager.OnTimelineScrubbed -= HandleScrub;
        if (DialogueManager.Instance != null) {
            DialogueManager.Instance.UnregisterEffectManager(this);
        }
    }

    public void OnNotify(Playable origin, INotification notification, object context) {
        if (notification is UniversalMarker marker && marker.currentTrackType == MarkerTrackType.Background) {
            PlayableDirector director = DialogueManager.Instance.ActiveDirector;
            double currentTime = director != null ? director.time : marker.time;
            ExecuteMarkerAction(marker, currentTime);
        }
    }

    // forceDispatch �߰�
    public void ExecuteMarkerAction(UniversalMarker marker, double currentTime, bool forceDispatch = false) {
        if (!forceDispatch && lastProcessedMarker == marker && Mathf.Abs((float)(currentTime - lastProcessTime)) < 0.05f) return;
        lastProcessedMarker = marker;
        lastProcessTime = currentTime;

        var data = marker.bgData;
        float elapsedTime = Mathf.Max(0f, (float)(currentTime - marker.time));
        float duration = data.duration;
        Sprite targetSprite = data.targetBgSprite;
        bool hasFront = frontBg.sprite != null && frontBg.color.a > 0.1f;
        bool hasTarget = targetSprite != null;

        bgTween?.Kill();

        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        bool isPlaying = Application.isPlaying && director != null && director.state == PlayState.Playing;

        List<GameObject> oldLetterboxes = new List<GameObject>(activeLetterboxes);
        activeLetterboxes.Clear();

        bool hasBlackTransition = hasFront && hasTarget && (data.fadeToBlackDuration > 0f || data.holdBlackDuration > 0f);

        if (hasTarget && data.useLetterbox) {
            foreach (var oldBox in oldLetterboxes) { if (oldBox != null) Destroy(oldBox); }
            oldLetterboxes.Clear();
            SpawnLetterboxes(data.topLetterboxHeight, data.bottomLetterboxHeight, hasBlackTransition ? 0f : 1f);
        }

        if (hasFront) {
            backBg.sprite = frontBg.sprite;
            SetGroupAlpha(backBg, frontBg.color.a);
        }
        else {
            backBg.sprite = null;
            SetGroupAlpha(backBg, 0f);
        }

        frontBg.sprite = targetSprite;
        SetGroupAlpha(frontBg, 0f);

        Sequence seq = DOTween.Sequence().SetUpdate(true).SetId("TimelineTweens").SetLink(gameObject);
        float totalSequenceDuration = duration;

        if (hasFront && hasTarget) {
            if (data.holdBlackDuration > 0f || data.fadeToBlackDuration > 0f) {
                totalSequenceDuration = data.fadeToBlackDuration + data.holdBlackDuration + duration;
                seq.Append(FadeGroup(backBg, 0f, data.fadeToBlackDuration, data.easeType));

                if (data.useLetterbox) {
                    seq.Insert(data.fadeToBlackDuration, FadeLetterboxes(1f, 0f));
                }

                if (data.holdBlackDuration > 0f) seq.AppendInterval(data.holdBlackDuration);
                seq.Append(FadeGroup(frontBg, 1f, duration, data.easeType));
            }
            else {
                seq.Append(FadeGroup(backBg, 0f, duration, data.easeType));
                seq.Join(FadeGroup(frontBg, 1f, duration, data.easeType));
            }
        }
        else if (hasFront && !hasTarget) {
            seq.Append(FadeGroup(backBg, 0f, duration, data.easeType));
        }
        else if (!hasFront && hasTarget) {
            seq.Append(FadeGroup(frontBg, 1f, duration, data.easeType));
        }

        seq.OnComplete(() => {
            backBg.sprite = null;
            foreach (var oldBox in oldLetterboxes) { if (oldBox != null) Destroy(oldBox); }
        });

        bgTween = seq;

        if (elapsedTime >= totalSequenceDuration) seq.Complete(true);
        else seq.Goto(elapsedTime, isPlaying);

        if (data.isPrefix && DialogueManager.Instance.HasLock(marker)) {
            float unlockTime = totalSequenceDuration * data.prefixUnlockRatio;
            if (elapsedTime >= unlockTime) {
                DialogueManager.Instance.RemoveLock(marker);
            }
            else {
                int token = DialogueManager.Instance.ScrubToken;
                float waitTime = unlockTime - elapsedTime;
                WaitAndUnlock(waitTime, token, marker);
            }
        }
    }

    private async void WaitAndUnlock(float waitTime, int token, UniversalMarker marker) {
        await Task.Delay(Mathf.RoundToInt(waitTime * 1000f));
        if (Application.isPlaying && DialogueManager.Instance != null && DialogueManager.Instance.ScrubToken == token) {
            DialogueManager.Instance.RemoveLock(marker);
        }
    }

    private void SpawnLetterboxes(float topH, float botH, float initialAlpha) {
        if (letterboxPrefab == null || Camera.main == null) return;
        float camOrtho = Camera.main.orthographicSize;
        float camWidth = camOrtho * 2f * Camera.main.aspect;

        if (topH > 0f) {
            GameObject topObj = Instantiate(letterboxPrefab, this.transform);
            Image sr = topObj.GetComponent<Image>();
            if (sr != null && sr.sprite != null) {
                topObj.transform.localScale = new Vector3((camWidth + 2f) / sr.sprite.bounds.size.x, topH / sr.sprite.bounds.size.y, 1f);
                topObj.transform.localPosition = new Vector3(0f, camOrtho - (topH / 2f), 0f);
                Color c = sr.color; c.a = initialAlpha; sr.color = c;
                activeLetterboxes.Add(topObj);
            }
        }
        if (botH > 0f) {
            GameObject botObj = Instantiate(letterboxPrefab, this.transform);
            Image sr = botObj.GetComponent<Image>();
            if (sr != null && sr.sprite != null) {
                botObj.transform.localScale = new Vector3((camWidth + 2f) / sr.sprite.bounds.size.x, botH / sr.sprite.bounds.size.y, 1f);
                botObj.transform.localPosition = new Vector3(0f, -camOrtho + (botH / 2f), 0f);
                Color c = sr.color; c.a = initialAlpha; sr.color = c;
                activeLetterboxes.Add(botObj);
            }
        }
    }

    private Tween FadeLetterboxes(float targetAlpha, float duration) {
        Sequence s = DOTween.Sequence().SetUpdate(true);
        foreach (var lb in activeLetterboxes) {
            if (lb != null) {
                var sr = lb.GetComponent<Image>();
                s.Join(sr.DOFade(targetAlpha, duration).SetUpdate(true));
            }
        }
        return s;
    }

    private void SetGroupAlpha(Image root, float alpha) {
        if (root == null) return;
        Color c = root.color; c.a = alpha; root.color = c;
    }

    private Tween FadeGroup(Image root, float targetAlpha, float duration, Ease ease) {
        if (root == null) return DOTween.Sequence();
        return root.DOFade(targetAlpha, duration).SetEase(ease).SetUpdate(true).SetLink(root.gameObject);
    }

    private void HandleSkip(double prevTime, double targetTime) {
        lastProcessedMarker = null; lastProcessTime = -1d;
        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        if (director != null && director.playableAsset is TimelineAsset timelineAsset) {
            foreach (var track in timelineAsset.GetOutputTracks()) {
                if (track is BackgroundTrack) {
                    var skippedMarkers = track.GetMarkers().OfType<UniversalMarker>().Where(m => m.time > prevTime && m.time <= targetTime && m.currentTrackType == MarkerTrackType.Background).OrderBy(m => m.time).ToList();
                    if (skippedMarkers.Count > 0) ExecuteMarkerAction(skippedMarkers.Last(), targetTime);
                    break;
                }
            }
        }
    }

    private void HandleScrub(double targetTime) {
        lastProcessedMarker = null; lastProcessTime = -1d;
        bgTween?.Kill();

        frontBg.sprite = null; SetGroupAlpha(frontBg, 0f);
        backBg.sprite = null; SetGroupAlpha(backBg, 0f);
        foreach (var lb in activeLetterboxes) { if (lb != null) Destroy(lb); }
        activeLetterboxes.Clear();

        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        if (director != null && director.playableAsset is TimelineAsset timelineAsset) {
            foreach (var track in timelineAsset.GetOutputTracks()) {
                if (track is BackgroundTrack) {
                    var markers = track.GetMarkers().OfType<UniversalMarker>().Where(m => m.time <= targetTime && m.currentTrackType == MarkerTrackType.Background).OrderBy(m => m.time).ToList();
                    if (markers.Count > 0) ExecuteMarkerAction(markers.Last(), targetTime);
                    break;
                }
            }
        }
    }
}
#endif