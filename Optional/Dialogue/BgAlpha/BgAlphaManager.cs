#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;
using DG.Tweening;
using System.Linq;
using System.Threading.Tasks;

public class BgAlphaManager : MonoBehaviour, INotificationReceiver, IEffectManager {
    public Image overlayImage;

    private Tween currentTween;
    private UniversalMarker lastProcessedMarker;
    private double lastProcessTime;

    public MarkerTrackType TargetTrackType => MarkerTrackType.BgAlpha;

    private void Awake() {
        SetAlpha(0f);
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
        if (notification is UniversalMarker marker && marker.currentTrackType == MarkerTrackType.BgAlpha) {
            PlayableDirector director = DialogueManager.Instance.ActiveDirector;
            double currentTime = director != null ? director.time : marker.time;
            ExecuteMarkerAction(marker, currentTime);
        }
    }

    // forceDispatch �߰�
    public void ExecuteMarkerAction(UniversalMarker marker, double currentTime, bool forceDispatch = false) {
        // forceDispatch�� �ƴ� ���� �ߺ� �˻� ����
        if (!forceDispatch && lastProcessedMarker == marker && Mathf.Abs((float)(currentTime - lastProcessTime)) < 0.05f) return;
        lastProcessedMarker = marker;
        lastProcessTime = currentTime;

        var data = marker.bgAlphaData;
        float elapsedTime = Mathf.Max(0f, (float)(currentTime - marker.time));
        float duration = data.duration;
        float targetAlpha = data.alphaPercent / 100f;

        currentTween?.Kill();

        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        bool isPlaying = Application.isPlaying && director != null && director.state == PlayState.Playing;

        if (duration > 0f) {
            currentTween = overlayImage.DOFade(targetAlpha, duration).SetEase(data.easeType).SetUpdate(true).SetId("TimelineTweens").SetLink(overlayImage.gameObject);

            if (elapsedTime >= duration) currentTween.Complete(true);
            else currentTween.Goto(elapsedTime, isPlaying);
        }
        else {
            SetAlpha(targetAlpha);
        }

        if (data.isPrefix && DialogueManager.Instance.HasLock(marker)) {
            float unlockTime = duration * data.prefixUnlockRatio;

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

    private void HandleSkip(double prevTime, double targetTime) {
        lastProcessedMarker = null; lastProcessTime = -1d;
        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        if (director != null && director.playableAsset is TimelineAsset timelineAsset) {
            foreach (var track in timelineAsset.GetOutputTracks()) {
                if (track is BgAlphaTrack) {
                    var skippedMarkers = track.GetMarkers().OfType<UniversalMarker>().Where(m => m.time > prevTime && m.time <= targetTime && m.currentTrackType == MarkerTrackType.BgAlpha).OrderBy(m => m.time).ToList();
                    if (skippedMarkers.Count > 0) ExecuteMarkerAction(skippedMarkers.Last(), targetTime);
                    break;
                }
            }
        }
    }

    private void HandleScrub(double targetTime) {
        lastProcessedMarker = null; lastProcessTime = -1d;
        currentTween?.Kill();
        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        if (director != null && director.playableAsset is TimelineAsset timelineAsset) {
            foreach (var track in timelineAsset.GetOutputTracks()) {
                if (track is BgAlphaTrack) {
                    var markers = track.GetMarkers().OfType<UniversalMarker>().Where(m => m.time <= targetTime && m.currentTrackType == MarkerTrackType.BgAlpha).OrderBy(m => m.time).ToList();
                    if (markers.Count > 0) {
                        ExecuteMarkerAction(markers.Last(), targetTime);
                    }
                    else SetAlpha(0f);
                    break;
                }
            }
        }
    }

    private void SetAlpha(float alpha) {
        if (overlayImage == null) return;
        Color c = overlayImage.color; c.a = alpha; overlayImage.color = c;
    }
}
#endif