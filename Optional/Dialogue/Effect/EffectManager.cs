#if SYSTEM_DIALOGUE
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using System.Linq;
using System.Threading.Tasks;

public class EffectManager : MonoBehaviour, INotificationReceiver, IEffectManager {
    public static EffectManager Instance;

    [Header("����Ʈ ���� ����")]
    [SerializeField] private Transform effectContainer;

    // ��Ŀ �ν��Ͻ� ��ü�� Ű������ �����Ͽ� �����ֱ⸦ �����մϴ�.
    public Dictionary<UniversalMarker, EffectPrefabController> activeEffects = new Dictionary<UniversalMarker, EffectPrefabController>();

    public MarkerTrackType TargetTrackType => MarkerTrackType.Effect;

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
        // ������ ���� �ִ� ��ȸ�� ����Ʈ�� ���� �����ϴ�.
        foreach (var effect in activeEffects.Values) {
            if (effect != null) Destroy(effect.gameObject);
        }
        activeEffects.Clear();

        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        if (director != null && director.playableAsset is TimelineAsset timelineAsset) {
            foreach (var track in timelineAsset.GetOutputTracks()) {
                if (track is EffectTrack) {
                    var scrubMarkers = track.GetMarkers()
                        .OfType<UniversalMarker>()
                        .Where(m => m.time <= targetTime && m.currentTrackType == MarkerTrackType.Effect)
                        .OrderBy(m => m.time)
                        .ToList();

                    // ��ũ���� ���� �������� �̹� ����(duration)�� ���� ����Ʈ�� ��Ȱ��Ű�� �ʰ� �н��մϴ�.
                    foreach (var m in scrubMarkers) {
                        float elapsedTime = (float)(targetTime - m.time);
                        if (elapsedTime >= 0 && elapsedTime < m.effectData.duration) {
                            ExecuteMarkerAction(m, targetTime, forceDispatch: true);
                        }
                    }
                }
            }
        }
    }

    public void OnNotify(Playable origin, INotification notification, object context) {
        if (notification is UniversalMarker marker && marker.currentTrackType == MarkerTrackType.Effect) {
            PlayableDirector director = DialogueManager.Instance.ActiveDirector;
            double currentTime = director != null ? director.time : marker.time;
            ExecuteMarkerAction(marker, currentTime);
        }
    }

    public void ExecuteMarkerAction(UniversalMarker marker, double currentTime, bool forceDispatch = false) {
        var data = marker.effectData;
        float elapsedTime = Mathf.Max(0f, (float)(currentTime - marker.time));
        float duration = data.duration;

        // �̹� ����� ������ �� ��Ŀ�� �ð� �Ѿ�ϴ�.
        if (elapsedTime >= duration) return;

        // �ߺ� ���� ��� (�̹� ȭ�鿡 �� �ִٸ� ����)
        if (!forceDispatch && activeEffects.ContainsKey(marker)) return;

        if (data.effectPrefab != null) {
            GameObject obj = Instantiate(data.effectPrefab, effectContainer);
            EffectPrefabController controller = obj.GetComponent<EffectPrefabController>();

            if (controller == null) {
                controller = obj.AddComponent<EffectPrefabController>();
            }

            Vector2 pos = GetTargetPos(data);
            obj.transform.position = new Vector3(pos.x, pos.y, obj.transform.position.z);

            activeEffects[marker] = controller;

            // ������ ���ο� �ð� ����ȭ�� �����մϴ�.
            controller.SyncTime(elapsedTime);

            // ���� ����(duration - elapsedTime)��ŭ ����ߴٰ� ������ �����ϵ��� �����մϴ�.
            DestroyEffectAfterDuration(marker, controller, duration - elapsedTime);
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

    private async void DestroyEffectAfterDuration(UniversalMarker marker, EffectPrefabController controller, float remainingTime) {
        int token = DialogueManager.Instance.ScrubToken;
        await Task.Delay(Mathf.RoundToInt(remainingTime * 1000f));

        // ������ ���� Ÿ�Ӷ��� ���� ����(Scrub)�� �߻����� �ʾ��� ���� ���� ���� ó���մϴ�.
        if (Application.isPlaying && DialogueManager.Instance != null && DialogueManager.Instance.ScrubToken == token) {
            if (controller != null && controller.gameObject != null) {
                Destroy(controller.gameObject);
            }
            if (activeEffects.ContainsKey(marker)) {
                activeEffects.Remove(marker);
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
        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        if (director != null && director.playableAsset is TimelineAsset timelineAsset) {
            foreach (var track in timelineAsset.GetOutputTracks()) {
                if (track is EffectTrack) {
                    var skippedMarkers = track.GetMarkers()
                        .OfType<UniversalMarker>()
                        .Where(m => m.time > prevTime && m.time <= targetTime && m.currentTrackType == MarkerTrackType.Effect)
                        .OrderBy(m => m.time)
                        .ToList();

                    foreach (var m in skippedMarkers) {
                        ExecuteMarkerAction(m, targetTime, forceDispatch: true);
                    }
                }
            }
        }
    }

    private Vector2 GetTargetPos(EffectMarkerData marker) {
        if (marker.posType == PositionTargetType.Coordinate) return marker.coordinate;
        if (marker.posType == PositionTargetType.Preset) return PositionPresets.GetPresetVector(marker.presetPosition);
        return Vector2.zero;
    }
}
#endif