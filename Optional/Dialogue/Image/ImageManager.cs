#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using DG.Tweening;
using System.Linq;
using System.Threading.Tasks;

public class ImageManager : MonoBehaviour, INotificationReceiver, IEffectManager {
    public static ImageManager Instance;

    [Header("�̹��� ��� ����")]
    [SerializeField] private Transform imageContainer;
    [SerializeField] private GameObject imageWindowPrefab; // ImagePrefabController�� ������ â ������

    public Dictionary<string, ImagePrefabController> activeImages = new Dictionary<string, ImagePrefabController>();

    public MarkerTrackType TargetTrackType => MarkerTrackType.Image;

    private UniversalMarker lastProcessedMarker;
    private double lastProcessTime;

    private void Awake() {
        Instance = this;
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

    private void HandleScrub(double targetTime) {
        lastProcessedMarker = null; lastProcessTime = -1d;

        foreach (var img in activeImages.Values) Destroy(img.gameObject);
        activeImages.Clear();

        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        if (director != null && director.playableAsset is TimelineAsset timelineAsset) {
            foreach (var track in timelineAsset.GetOutputTracks()) {
                if (track is ImageTrack) {
                    var scrubMarkers = track.GetMarkers()
                        .OfType<UniversalMarker>()
                        .Where(m => m.time <= targetTime && m.currentTrackType == MarkerTrackType.Image)
                        .OrderBy(m => m.time)
                        .ToList();

                    // ���ź��� ���� ������ ���� ����
                    foreach (var m in scrubMarkers) {
                        ExecuteMarkerAction(m, targetTime, forceDispatch: true);
                    }
                }
            }
        }
    }

    public void OnNotify(Playable origin, INotification notification, object context) {
        if (notification is UniversalMarker marker && marker.currentTrackType == MarkerTrackType.Image) {
            PlayableDirector director = DialogueManager.Instance.ActiveDirector;
            double currentTime = director != null ? director.time : marker.time;
            ExecuteMarkerAction(marker, currentTime);
        }
    }

    public void ExecuteMarkerAction(UniversalMarker marker, double currentTime, bool forceDispatch = false) {
        if (!forceDispatch && lastProcessedMarker == marker && Mathf.Abs((float)(currentTime - lastProcessTime)) < 0.05f) return;
        lastProcessedMarker = marker;
        lastProcessTime = currentTime;

        var data = marker.imageData;
        float elapsedTime = Mathf.Max(0f, (float)(currentTime - marker.time));
        float duration = data.duration;

        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        bool isPlaying = Application.isPlaying && director != null && director.state == PlayState.Playing;

        if (data.actionType == ImageActionType.Create) {
            AddImage(data.trackID, GetTargetPos(data), data.targetSprite, duration, data.easeType, elapsedTime, isPlaying);
        }
        else if (data.actionType == ImageActionType.Delete) {
            RemoveImage(data.trackID, duration, data.easeType, elapsedTime, isPlaying);
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
                if (track is ImageTrack) {
                    var skippedMarkers = track.GetMarkers().OfType<UniversalMarker>().Where(m => m.time > prevTime && m.time <= targetTime && m.currentTrackType == MarkerTrackType.Image).OrderBy(m => m.time).ToList();
                    if (skippedMarkers.Count > 0) ExecuteMarkerAction(skippedMarkers.Last(), targetTime);
                }
            }
        }
    }

    private Vector2 GetTargetPos(ImageMarkerData marker) {
        if (marker.posType == PositionTargetType.Coordinate) return marker.coordinate;
        if (marker.posType == PositionTargetType.Preset) return PositionPresets.GetPresetVector(marker.presetPosition);
        return Vector2.zero;
    }

    public void AddImage(string trackID, Vector2 position, Sprite sprite, float duration, Ease ease, float elapsedTime, bool isPlaying) {
        if (activeImages.ContainsKey(trackID)) return;

        GameObject obj = Instantiate(imageWindowPrefab, imageContainer);
        ImagePrefabController controller = obj.GetComponent<ImagePrefabController>();

        controller.transform.position = new Vector3(position.x, position.y, controller.transform.position.z);
        controller.contentImage.sprite = sprite;

        controller.canvasGroup.alpha = 0f;
        activeImages.Add(trackID, controller);

        if (duration > 0f) {
            Tween t = controller.canvasGroup.DOFade(1f, duration).SetEase(ease).SetUpdate(true).SetId("TimelineTweens").SetLink(obj);
            if (elapsedTime >= duration) t.Complete(true);
            else t.Goto(elapsedTime, isPlaying);
        }
        else {
            controller.canvasGroup.alpha = 1f;
        }
    }

    public void RemoveImage(string trackID, float duration, Ease ease, float elapsedTime, bool isPlaying) {
        if (activeImages.TryGetValue(trackID, out ImagePrefabController controller)) {
            activeImages.Remove(trackID);

            if (duration > 0f) {
                Tween t = controller.canvasGroup.DOFade(0f, duration).SetEase(ease).SetUpdate(true).SetId("TimelineTweens").SetLink(controller.gameObject).OnComplete(() => Destroy(controller.gameObject));
                if (elapsedTime >= duration) t.Complete(true);
                else t.Goto(elapsedTime, isPlaying);
            }
            else {
                Destroy(controller.gameObject);
            }
        }
    }
}
#endif