#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using DG.Tweening;
using System.Linq;
using System.Threading.Tasks;

public class CameraManager : MonoBehaviour, INotificationReceiver, IEffectManager {
    public static CameraManager Instance;

    [Header("ī�޶� ����")]
    [SerializeField] private Camera targetCamera;

    public MarkerTrackType TargetTrackType => MarkerTrackType.Camera;

    private UniversalMarker lastProcessedMarker;
    private double lastProcessTime;
    private Tween currentTween;

    private float defaultOrthoSize;
    private Vector3 defaultPosition;

    private void Awake() {
        Instance = this;
        if (targetCamera == null) targetCamera = Camera.main;

        if (targetCamera != null) {
            defaultOrthoSize = targetCamera.orthographicSize;
            defaultPosition = targetCamera.transform.position;
        }

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

        // ī�޶� ���¸� �ʱ� ����Ʈ ������ �������� ���� ���� ����
        if (targetCamera != null) {
            targetCamera.transform.position = defaultPosition;
            targetCamera.orthographicSize = defaultOrthoSize;
        }

        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        if (director != null && director.playableAsset is TimelineAsset timelineAsset) {
            foreach (var track in timelineAsset.GetOutputTracks()) {
                if (track is CameraTrack) {
                    var scrubMarkers = track.GetMarkers()
                        .OfType<UniversalMarker>()
                        .Where(m => m.time <= targetTime && m.currentTrackType == MarkerTrackType.Camera)
                        .OrderBy(m => m.time)
                        .ToList();

                    // ī�޶� ���� ������ ������ ���� ����
                    foreach (var m in scrubMarkers) {
                        ExecuteMarkerAction(m, targetTime, forceDispatch: true);
                    }
                }
            }
        }
    }

    public void OnNotify(Playable origin, INotification notification, object context) {
        if (notification is UniversalMarker marker && marker.currentTrackType == MarkerTrackType.Camera) {
            PlayableDirector director = DialogueManager.Instance.ActiveDirector;
            double currentTime = director != null ? director.time : marker.time;
            ExecuteMarkerAction(marker, currentTime);
        }
    }

    public void ExecuteMarkerAction(UniversalMarker marker, double currentTime, bool forceDispatch = false) {
        if (!forceDispatch && lastProcessedMarker == marker && Mathf.Abs((float)(currentTime - lastProcessTime)) < 0.05f) return;
        lastProcessedMarker = marker;
        lastProcessTime = currentTime;

        if (targetCamera == null) return;

        var data = marker.cameraData;
        float elapsedTime = Mathf.Max(0f, (float)(currentTime - marker.time));
        float duration = data.duration;

        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        bool isPlaying = Application.isPlaying && director != null && director.state == PlayState.Playing;

        currentTween?.Kill();

        Vector3 targetPos = targetCamera.transform.position;
        float targetSize = targetCamera.orthographicSize;

        // ����: �� 200% -> ī�޶�� ���� ũ��� �������� �ϹǷ� ������ ����
        if (data.actionType == CameraActionType.Zoom) {
            Vector2 pos2D = GetTargetPos(data);
            targetPos = new Vector3(pos2D.x, pos2D.y, defaultPosition.z);
            targetSize = defaultOrthoSize * (100f / data.zoomPercent);
        }
        else if (data.actionType == CameraActionType.Move) {
            Vector2 pos2D = GetTargetPos(data);
            targetPos = new Vector3(pos2D.x, pos2D.y, defaultPosition.z);
        }
        else if (data.actionType == CameraActionType.Reset) {
            targetPos = defaultPosition;
            targetSize = defaultOrthoSize;
        }

        if (duration > 0f) {
            Sequence seq = DOTween.Sequence().SetUpdate(true).SetId("TimelineTweens").SetLink(targetCamera.gameObject);

            seq.Join(targetCamera.transform.DOMove(targetPos, duration).SetEase(data.easeType));
            seq.Join(targetCamera.DOOrthoSize(targetSize, duration).SetEase(data.easeType));

            currentTween = seq;

            if (elapsedTime >= duration) seq.Complete(true);
            else seq.Goto(elapsedTime, isPlaying);
        }
        else {
            targetCamera.transform.position = targetPos;
            targetCamera.orthographicSize = targetSize;
        }

        // ���� ���� ��(Lock) ���� ����
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
                if (track is CameraTrack) {
                    var skippedMarkers = track.GetMarkers().OfType<UniversalMarker>().Where(m => m.time > prevTime && m.time <= targetTime && m.currentTrackType == MarkerTrackType.Camera).OrderBy(m => m.time).ToList();
                    if (skippedMarkers.Count > 0) ExecuteMarkerAction(skippedMarkers.Last(), targetTime);
                }
            }
        }
    }

    private Vector2 GetTargetPos(CameraMarkerData marker) {
        if (marker.posType == PositionTargetType.Coordinate) return marker.coordinate;
        if (marker.posType == PositionTargetType.Preset) return PositionPresets.GetPresetVector(marker.presetPosition);
        return Vector2.zero;
    }
}
#endif