#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using DG.Tweening;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.UI;

public class StandingManager : MonoBehaviour, INotificationReceiver, IEffectManager {
    public static StandingManager Instance;

    [Header("���ĵ� �Ϸ���Ʈ ����")]
    [SerializeField] private Transform standingContainer;
    [SerializeField] private GameObject standingPrefab; // StandingPrefabController�� ������ ���� ������

    public Dictionary<string, StandingObj> activeStandings = new Dictionary<string, StandingObj>();

    public MarkerTrackType TargetTrackType => MarkerTrackType.Standing;

    public class StandingObj {
        public StandingPrefabController controller;
        public GameObject gameObject => controller.gameObject;
        public Transform transform => controller.transform;
        public Image spriteRenderer => controller.standingImage;
        public Image windowRenderer => controller.windowImage;
        public bool isHighlight = true;
    }

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

        foreach (var standing in activeStandings.Values) Destroy(standing.gameObject);
        activeStandings.Clear();

        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        if (director != null && director.playableAsset is TimelineAsset timelineAsset) {
            foreach (var track in timelineAsset.GetOutputTracks()) {
                if (track is StandingTrack) {
                    var scrubMarkers = track.GetMarkers()
                        .OfType<UniversalMarker>()
                        .Where(m => m.time <= targetTime && m.currentTrackType == MarkerTrackType.Standing)
                        .OrderBy(m => m.time)
                        .ToList();

                    // [������ �κ�] Last() �ϳ��� �����ϴ� ���, 
                    // ���ź��� ��������� ��� ���ĵ� ���� ��Ŀ�� ���������� ���� ����Ͽ� ���¸� �Ϻ��� �������մϴ�.
                    foreach (var m in scrubMarkers) {
                        ExecuteMarkerAction(m, targetTime, forceDispatch: true);
                    }
                }
            }
        }
    }

    public void OnNotify(Playable origin, INotification notification, object context) {
        if (notification is UniversalMarker marker && marker.currentTrackType == MarkerTrackType.Standing) {
            PlayableDirector director = DialogueManager.Instance.ActiveDirector;
            double currentTime = director != null ? director.time : marker.time;
            ExecuteMarkerAction(marker, currentTime);
        }
    }

    public void ExecuteMarkerAction(UniversalMarker marker, double currentTime, bool forceDispatch = false) {
        if (!forceDispatch && lastProcessedMarker == marker && Mathf.Abs((float)(currentTime - lastProcessTime)) < 0.05f) return;
        lastProcessedMarker = marker;
        lastProcessTime = currentTime;

        var data = marker.standingData;
        float elapsedTime = Mathf.Max(0f, (float)(currentTime - marker.time));
        float duration = data.duration;

        PlayableDirector director = DialogueManager.Instance.ActiveDirector;
        bool isPlaying = Application.isPlaying && director != null && director.state == PlayState.Playing;

        if (data.actionType == StandingActionType.Create) {
            AddStanding(data.standingID, GetTargetPos(data), data.standingSprite, data.useWindowMode, data.windowSprite, data.isHighlight, duration, data.easeType, elapsedTime, isPlaying);
        }
        else {
            if (data.isDelete) {
                RemoveStanding(data.targetStandingID, duration, data.easeType, elapsedTime, isPlaying);
            }
            else {
                if (data.isUpdateExpression) ModifyStanding(data.targetStandingID, data.standingSprite, data.isUpdateHighlight, data.isHighlight, duration, data.easeType, elapsedTime, isPlaying);
                else if (data.isUpdateHighlight) ChangeHighlight(data.targetStandingID, data.isHighlight, duration, data.easeType, elapsedTime, isPlaying);

                if (data.isUpdateWindow) ChangeWindowSprite(data.targetStandingID, data.updateWindowSprite, duration, data.easeType, elapsedTime, isPlaying);

                if (data.transformAction == StandingTransformAction.Move) {
                    MoveStanding(data.targetStandingID, GetTargetPos(data), duration, data.easeType, elapsedTime, isPlaying);
                }
                else if (data.transformAction == StandingTransformAction.Effect) {
                    PlayEffect(data.targetStandingID, data.tweenType, data.shakeStrength, duration, elapsedTime, isPlaying);
                }
            }
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
                if (track is StandingTrack) {
                    var skippedMarkers = track.GetMarkers().OfType<UniversalMarker>().Where(m => m.time > prevTime && m.time <= targetTime && m.currentTrackType == MarkerTrackType.Standing).OrderBy(m => m.time).ToList();
                    if (skippedMarkers.Count > 0) ExecuteMarkerAction(skippedMarkers.Last(), targetTime);
                }
            }
        }
    }

    private Vector2 GetTargetPos(StandingMarkerData marker) {
        if (marker.posType == PositionTargetType.Coordinate) return marker.coordinate;
        if (marker.posType == PositionTargetType.Preset) return PositionPresets.GetPresetVector(marker.presetPosition);
        return Vector2.zero;
    }

    public void AddStanding(string standingID, Vector2 position, Sprite sprite, bool useWindowMode, Sprite windowSprite, bool isHighlight, float duration, Ease ease, float elapsedTime, bool isPlaying) {
        if (activeStandings.ContainsKey(standingID)) return;

        GameObject obj = Instantiate(standingPrefab, standingContainer);
        StandingPrefabController controller = obj.GetComponent<StandingPrefabController>();

        // ������ ��Ʈ�ѷ��� ���� â ��� ����
        controller.SetWindowMode(useWindowMode);
        if (useWindowMode && windowSprite != null) {
            controller.windowImage.sprite = windowSprite;
        }

        StandingObj standing = new StandingObj
        {
            controller = controller,
            isHighlight = isHighlight
        };

        standing.transform.position = new Vector3(position.x, position.y, standing.transform.position.z);
        standing.spriteRenderer.sprite = sprite;

        // ������(CanvasGroup) �ʱ�ȭ
        controller.canvasGroup.alpha = 1f;

        float targetRgb = isHighlight ? 1f : 0.7f;
        Color startColor = new Color(targetRgb, targetRgb, targetRgb, 0f);
        Color targetColor = new Color(targetRgb, targetRgb, targetRgb, 1f);

        standing.spriteRenderer.color = startColor;
        activeStandings.Add(standingID, standing);

        // â ������ ���̵���
        if (useWindowMode) {
            Color winStart = controller.windowImage.color; winStart.a = 0f; controller.windowImage.color = winStart;
            if (duration > 0f) {
                Tween winT = controller.windowImage.DOFade(1f, duration).SetEase(ease).SetUpdate(true).SetId("TimelineTweens").SetLink(standing.gameObject);
                if (elapsedTime >= duration) winT.Complete(true);
                else winT.Goto(elapsedTime, isPlaying);
            }
            else {
                Color winEnd = controller.windowImage.color; winEnd.a = 1f; controller.windowImage.color = winEnd;
            }
        }

        // ���ĵ� ���̵��� (RGB ����)
        if (duration > 0f) {
            Tween t = standing.spriteRenderer.DOColor(targetColor, duration).SetEase(ease).SetUpdate(true).SetId("TimelineTweens").SetLink(standing.gameObject);
            if (elapsedTime >= duration) t.Complete(true);
            else t.Goto(elapsedTime, isPlaying);
        }
        else {
            standing.spriteRenderer.color = targetColor;
        }
    }

    public void MoveStanding(string characterID, Vector2 targetPosition, float duration, Ease ease, float elapsedTime, bool isPlaying) {
        if (activeStandings.TryGetValue(characterID, out StandingObj standing)) {
            Vector3 targetPos3D = new Vector3(targetPosition.x, targetPosition.y, standing.transform.position.z);
            if (duration > 0f) {
                Tween t = standing.transform.DOMove(targetPos3D, duration).SetEase(ease).SetUpdate(true).SetId("TimelineTweens").SetLink(standing.gameObject);
                if (elapsedTime >= duration) t.Complete(true);
                else t.Goto(elapsedTime, isPlaying);
            }
            else {
                standing.transform.position = targetPos3D;
            }
        }
    }

    public void ModifyStanding(string standingID, Sprite newSprite, bool updateHighlight, bool isHighlight, float duration, Ease ease, float elapsedTime, bool isPlaying) {
        if (activeStandings.TryGetValue(standingID, out StandingObj standing)) {

            float startRgb = standing.isHighlight ? 1f : 0.7f;
            if (updateHighlight) standing.isHighlight = isHighlight;
            float targetRgb = standing.isHighlight ? 1f : 0.7f;

            if (duration > 0f) {
                // UI ���縦 ���� RectTransform ���
                GameObject tempObj = new GameObject("TempOldExpression", typeof(RectTransform));
                tempObj.transform.SetParent(standing.spriteRenderer.transform.parent, false);

                RectTransform tempRect = tempObj.GetComponent<RectTransform>();
                RectTransform targetRect = standing.spriteRenderer.rectTransform;
                tempRect.localPosition = targetRect.localPosition;
                tempRect.localScale = targetRect.localScale;
                tempRect.sizeDelta = targetRect.sizeDelta;
                tempRect.anchorMin = targetRect.anchorMin;
                tempRect.anchorMax = targetRect.anchorMax;
                tempRect.pivot = targetRect.pivot;

                Image tempSR = tempObj.AddComponent<Image>();
                tempSR.sprite = standing.spriteRenderer.sprite;
                tempSR.color = standing.spriteRenderer.color;

                // [UI ���� �ٽ�] ���� �̹����� ���� ���� �ε����� �����Ͽ� �� �̹����� �� ���� ���̵��� ������ ������ ����ϴ�.
                tempObj.transform.SetSiblingIndex(standing.spriteRenderer.transform.GetSiblingIndex());

                standing.spriteRenderer.sprite = newSprite;
                standing.spriteRenderer.color = new Color(startRgb, startRgb, startRgb, 0f);

                Tween t;
                if (updateHighlight) {
                    Tween oldT = tempSR.DOColor(new Color(targetRgb, targetRgb, targetRgb, tempSR.color.a), duration).SetEase(ease).SetUpdate(true).SetId("TimelineTweens").SetLink(standing.gameObject);
                    if (elapsedTime >= duration) oldT.Complete(true);
                    else oldT.Goto(elapsedTime, isPlaying);

                    t = standing.spriteRenderer.DOColor(new Color(targetRgb, targetRgb, targetRgb, 1f), duration).SetEase(ease).SetUpdate(true).SetId("TimelineTweens").SetLink(standing.gameObject).OnComplete(() => Destroy(tempObj));
                }
                else {
                    t = standing.spriteRenderer.DOFade(1f, duration).SetEase(ease).SetUpdate(true).SetId("TimelineTweens").SetLink(standing.gameObject).OnComplete(() => Destroy(tempObj));
                }

                if (elapsedTime >= duration) t.Complete(true);
                else t.Goto(elapsedTime, isPlaying);
            }
            else {
                standing.spriteRenderer.sprite = newSprite;
                standing.spriteRenderer.color = new Color(targetRgb, targetRgb, targetRgb, 1f);
            }
        }
    }

    public void ChangeHighlight(string standingID, bool isHighlight, float duration, Ease ease, float elapsedTime, bool isPlaying) {
        if (activeStandings.TryGetValue(standingID, out StandingObj standing)) {
            standing.isHighlight = isHighlight;
            float targetRgb = isHighlight ? 1f : 0.7f;
            Color targetColor = new Color(targetRgb, targetRgb, targetRgb, standing.spriteRenderer.color.a);

            if (duration > 0f) {
                Tween t = standing.spriteRenderer.DOColor(targetColor, duration).SetEase(ease).SetUpdate(true).SetId("TimelineTweens").SetLink(standing.gameObject);
                if (elapsedTime >= duration) t.Complete(true);
                else t.Goto(elapsedTime, isPlaying);
            }
            else {
                standing.spriteRenderer.color = targetColor;
            }
        }
    }

    public void ChangeWindowSprite(string standingID, Sprite newSprite, float duration, Ease ease, float elapsedTime, bool isPlaying) {
        if (activeStandings.TryGetValue(standingID, out StandingObj standing)) {
            if (standing.windowRenderer != null && standing.windowRenderer.gameObject.activeSelf) {
                if (duration > 0f) {
                    GameObject tempObj = new GameObject("TempOldWindow", typeof(RectTransform));
                    tempObj.transform.SetParent(standing.windowRenderer.transform.parent, false);

                    RectTransform tempRect = tempObj.GetComponent<RectTransform>();
                    RectTransform targetRect = standing.windowRenderer.rectTransform;
                    tempRect.localPosition = targetRect.localPosition;
                    tempRect.localScale = targetRect.localScale;
                    tempRect.sizeDelta = targetRect.sizeDelta;
                    tempRect.anchorMin = targetRect.anchorMin;
                    tempRect.anchorMax = targetRect.anchorMax;
                    tempRect.pivot = targetRect.pivot;

                    Image tempSR = tempObj.AddComponent<Image>();
                    tempSR.sprite = standing.windowRenderer.sprite;
                    tempSR.color = standing.windowRenderer.color;

                    tempObj.transform.SetSiblingIndex(standing.windowRenderer.transform.GetSiblingIndex());

                    standing.windowRenderer.sprite = newSprite;
                    Color c = standing.windowRenderer.color; c.a = 0f; standing.windowRenderer.color = c;

                    Tween t = standing.windowRenderer.DOFade(1f, duration).SetEase(ease).SetUpdate(true).SetId("TimelineTweens").SetLink(standing.gameObject).OnComplete(() => Destroy(tempObj));
                    if (elapsedTime >= duration) t.Complete(true);
                    else t.Goto(elapsedTime, isPlaying);
                }
                else {
                    standing.windowRenderer.sprite = newSprite;
                }
            }
        }
    }

    public void PlayEffect(string standingID, TweenEffectType effectType, float strength, float duration, float elapsedTime, bool isPlaying) {
        if (activeStandings.TryGetValue(standingID, out StandingObj standing)) {
            if (effectType == TweenEffectType.Shake && duration > 0f) {
                Tween t = standing.transform.DOShakePosition(duration, strength).SetUpdate(true).SetId("TimelineTweens").SetLink(standing.gameObject);
                if (elapsedTime >= duration) t.Complete(true);
                else t.Goto(elapsedTime, isPlaying);
            }
        }
    }

    public void RemoveStanding(string characterID, float duration, Ease ease, float elapsedTime, bool isPlaying) {
        if (activeStandings.TryGetValue(characterID, out StandingObj standing)) {
            activeStandings.Remove(characterID);

            if (duration > 0f) {
                // �θ��� CanvasGroup�� ��°�� ���̵�ƿ����� â�� ���ĵ��� �� ���� ����ϴ�.
                Tween t = standing.controller.canvasGroup.DOFade(0f, duration).SetEase(ease).SetUpdate(true).SetId("TimelineTweens").SetLink(standing.gameObject).OnComplete(() => Destroy(standing.gameObject));
                if (elapsedTime >= duration) t.Complete(true);
                else t.Goto(elapsedTime, isPlaying);
            }
            else {
                Destroy(standing.gameObject);
            }
        }
    }
}
#endif