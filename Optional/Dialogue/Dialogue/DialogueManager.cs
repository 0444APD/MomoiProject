#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
using DG.Tweening;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour {
    public static DialogueManager Instance;
    public static event Action<double, double> OnGlobalTimelineSkipped;
    public static event Action<double> OnTimelineScrubbed;

    private double lastDirectorTime = 0d;
    private bool isInternalSkip = false;
    private int scrubToken = 0;
    private PlayState lastDirectorState = PlayState.Paused;

    private HashSet<UniversalMarker> activeLocks = new HashSet<UniversalMarker>();
    public int ScrubToken => scrubToken;

    private List<IEffectManager> effectManagers = new List<IEffectManager>();

    public enum DialogueState { Inactive, WaitingLock, Playing, PausedAtEnd, SkipCooldown }
    public DialogueState currentState = DialogueState.Inactive;

    [Header("UI Reference")]
    [SerializeField] private GameObject dialogueBox;
    [SerializeField] private CanvasGroup dialogueCanvasGroup;
    [SerializeField] private Button fullScreenClickArea;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI dialogueText;

    [Header("Settings")]
    public float skipCooldownTime = 0.5f;

    [SerializeField] private PlayableDirector activeDirector;
    public PlayableDirector ActiveDirector => activeDirector;
    private Playable activePlayable;
    private bool IsTyping => currentTextTween != null && currentTextTween.IsActive() && !currentTextTween.IsComplete();

    private Tween currentTextTween;
    private Tween dialogueFadeTween;
    private bool isDialogueClipRunning = false;

    private string currentSpeaker;
    private string currentTextID;
    private float currentTextSpeed;
    private float currentFadeInDuration = 0.3f;
    private float currentFadeOutDuration = 0.3f;

    private void Awake() {
        Instance = this;
        fullScreenClickArea.onClick.AddListener(OnScreenClicked);
        OnGlobalTimelineSkipped += HandleGlobalSkip;
        if (dialogueCanvasGroup != null) dialogueCanvasGroup.alpha = 0f;
    }

    private void OnDestroy() {
        OnGlobalTimelineSkipped -= HandleGlobalSkip;
    }

    public void RegisterEffectManager(IEffectManager manager) {
        if (!effectManagers.Contains(manager)) effectManagers.Add(manager);
    }

    public void UnregisterEffectManager(IEffectManager manager) {
        effectManagers.Remove(manager);
    }

    private void Update() {
        if (activeDirector != null && !isInternalSkip) {
            if (activeDirector.state != lastDirectorState) {
                if (activeDirector.state == PlayState.Playing) DOTween.Play("TimelineTweens");
                else DOTween.Pause("TimelineTweens");
                lastDirectorState = activeDirector.state;
            }

            double currentTime = activeDirector.time;
            double delta = currentTime - lastDirectorTime;

            if (delta < -0.01d || Math.Abs(delta) > Time.unscaledDeltaTime + 0.1d) {
                HandleScrub(currentTime);
            }

            lastDirectorTime = currentTime;
        }
    }

    private void HandleScrub(double currentTime) {
        scrubToken++;

        activeLocks.Clear();
        if (currentTextTween != null) currentTextTween.Kill();
        dialogueText.text = "";
        currentState = DialogueState.Inactive;

        if (activeDirector.playableGraph.IsValid()) {
            activeDirector.playableGraph.GetRootPlayable(0).SetSpeed(1d);
        }
        HideDialogueInstantly();

        OnTimelineScrubbed?.Invoke(currentTime);

        if (isDialogueClipRunning && activePlayable.IsValid()) {
            ProcessTimelineState(currentTime);
        }
    }

    public void OnClipStart(string speaker, string textID, float speed, float fadeIn, float fadeOut, Playable playable, PlayableDirector director) {
        activeDirector = director;
        activePlayable = playable;
        currentSpeaker = speaker;
        currentTextID = textID;
        currentTextSpeed = speed;
        currentFadeInDuration = fadeIn;
        currentFadeOutDuration = fadeOut;
        isDialogueClipRunning = true;

        ProcessTimelineState(director.time);
    }

    private void ProcessTimelineState(double currentTime) {
        scrubToken++;
        activeLocks.Clear();

        if (!activePlayable.IsValid()) return;

        double localTime = activePlayable.GetTime();
        double clipStart = currentTime - localTime;
        double clipEnd = clipStart + activePlayable.GetDuration();

        var clipMarkers = GetMarkersInRange(clipStart, clipEnd);

        foreach (var m in clipMarkers) {
            if (IsPrefixMarker(m) && Math.Abs(m.time - clipStart) <= 0.05d) {
                activeLocks.Add(m);
            }
        }

        bool isAtHead = localTime < 0.05d;
        if (activeLocks.Count > 0 && isAtHead) {
            activeDirector.playableGraph.GetRootPlayable(0).SetSpeed(0d);
            currentState = DialogueState.WaitingLock;
        }

        foreach (var m in clipMarkers.Where(m => m.time <= currentTime)) {
            DispatchMarker(m, currentTime);
        }

        CheckAndStartDialogue();
    }

    private IEnumerable<UniversalMarker> GetMarkersInRange(double start, double end) {
        if (activeDirector == null || !(activeDirector.playableAsset is TimelineAsset timelineAsset))
            return Enumerable.Empty<UniversalMarker>();

        var markers = new List<UniversalMarker>();
        foreach (var track in timelineAsset.GetOutputTracks()) {
            markers.AddRange(track.GetMarkers().OfType<UniversalMarker>().Where(m => m.time >= start - 0.05d && m.time <= end));
        }
        return markers;
    }

    private bool IsPrefixMarker(UniversalMarker m) {
        if (m.currentTrackType == MarkerTrackType.BgAlpha) return m.bgAlphaData.isPrefix;
        if (m.currentTrackType == MarkerTrackType.Background) return m.bgData.isPrefix;
        if (m.currentTrackType == MarkerTrackType.Standing) return m.standingData.isPrefix;
        return false;
    }

    private void DispatchMarker(UniversalMarker m, double currentTime) {
        foreach (var manager in effectManagers) {
            if (manager.TargetTrackType == m.currentTrackType) {
                manager.ExecuteMarkerAction(m, currentTime, true);
            }
        }
    }

    public bool HasLock(UniversalMarker marker) {
        return activeLocks.Contains(marker);
    }

    public void RemoveLock(UniversalMarker marker) {
        if (activeLocks.Contains(marker)) {
            activeLocks.Remove(marker);
            CheckAndStartDialogue();
        }
    }

    private void CheckAndStartDialogue() {
        if (isDialogueClipRunning && activeLocks.Count == 0) {

            bool isAtEnd = false;
            if (activePlayable.IsValid()) {
                isAtEnd = activePlayable.GetTime() >= activePlayable.GetDuration() - 0.05d;
            }

            if (currentState == DialogueState.Inactive || currentState == DialogueState.WaitingLock) {

                if (isAtEnd) {
                    currentState = DialogueState.PausedAtEnd;
                    if (activeDirector != null && activeDirector.playableGraph.IsValid()) {
                        activeDirector.playableGraph.GetRootPlayable(0).SetSpeed(0d);
                    }
                }
                else {
                    currentState = DialogueState.Playing;
                    if (activeDirector != null && activeDirector.playableGraph.IsValid()) {
                        activeDirector.playableGraph.GetRootPlayable(0).SetSpeed(1d);
                    }
                }

                SetDialogueActive(true, true);
                SetSpeakerName(currentSpeaker);

                float elapsedTime = (float)(activePlayable.IsValid() ? activePlayable.GetTime() : 0d);
                PrintText(currentTextID, currentTextSpeed, elapsedTime);
            }
        }
    }

    public void PauseAtClipEnd() {
        if (activeDirector != null && isDialogueClipRunning) {
            currentState = DialogueState.PausedAtEnd;
            if (activeDirector.playableGraph.IsValid()) {
                activeDirector.playableGraph.GetRootPlayable(0).SetSpeed(0d);
            }
        }
    }

    public void OnScreenClicked() {
        if (activeDirector == null) return;

        if (currentState == DialogueState.PausedAtEnd) {
            if (IsTyping) CompleteTextTween();
            else {
                currentState = DialogueState.Inactive;
                activeDirector.playableGraph.GetRootPlayable(0).SetSpeed(1d);
                SetDialogueActive(false);
            }
        }
        else if (currentState == DialogueState.Playing && activePlayable.IsValid()) {
            CompleteTextTween();
            double skipTime = activePlayable.GetDuration() - 0.05d;
            if (activePlayable.GetTime() < skipTime) {
                double prevTime = activeDirector.time;
                double targetTime = prevTime + (skipTime - activePlayable.GetTime());

                OnGlobalTimelineSkipped?.Invoke(prevTime, targetTime);

                isInternalSkip = true;
                activeDirector.time = targetTime;
                activeDirector.Evaluate();
                isInternalSkip = false;
                lastDirectorTime = targetTime;

                EnterSkipCooldown();
            }
        }
    }

    private async void EnterSkipCooldown() {
        currentState = DialogueState.SkipCooldown;
        SetDialogueActive(true, false);
        activeDirector.playableGraph.GetRootPlayable(0).SetSpeed(0d);

        await Task.Delay(Mathf.RoundToInt(skipCooldownTime * 1000f));

        if (currentState == DialogueState.SkipCooldown) {
            currentState = DialogueState.PausedAtEnd;
            SetDialogueActive(true, true);
        }
    }

    // �ź���(playable) �˻� ��� ���� �߰�
    public void OnDialogueClipExited(Playable playable) {
        // [�ٽ�] ���� ��ϵ� Ŭ���� �ƴϸ� ����(�ʱ�ȭ) ������ �����մϴ�. (B�� A�� ���� �� ����)
        if (!activePlayable.IsValid() || !activePlayable.Equals(playable)) return;

        isDialogueClipRunning = false;
        if (currentState != DialogueState.WaitingLock) {
            currentState = DialogueState.Inactive;
            SetDialogueActive(false);
        }
    }

    public void SetDialogueActive(bool isActive, bool enableClickArea = false) {
        fullScreenClickArea.gameObject.SetActive(isActive && enableClickArea);
        bool isPlaying = Application.isPlaying && activeDirector != null && activeDirector.state == PlayState.Playing;

        if (isActive) {
            dialogueBox.SetActive(true);
            if (dialogueCanvasGroup.alpha < 1f) {
                dialogueFadeTween?.Kill();
                dialogueFadeTween = dialogueCanvasGroup.DOFade(1f, currentFadeInDuration).SetEase(Ease.OutQuad).SetUpdate(true).SetId("TimelineTweens").SetLink(dialogueCanvasGroup.gameObject);
                if (!isPlaying) dialogueFadeTween.Pause();
            }
        }
        else {
            if (dialogueBox.activeSelf) {
                dialogueFadeTween?.Kill();
                dialogueFadeTween = dialogueCanvasGroup.DOFade(0f, currentFadeOutDuration).SetUpdate(true).SetId("TimelineTweens").SetLink(dialogueCanvasGroup.gameObject).OnComplete(() => { dialogueBox.SetActive(false); });
                if (!isPlaying) dialogueFadeTween.Pause();
            }
        }
    }

    private void HideDialogueInstantly() {
        dialogueFadeTween?.Kill();
        if (dialogueCanvasGroup != null) dialogueCanvasGroup.alpha = 0f;
        if (dialogueBox != null) dialogueBox.SetActive(false);
        if (fullScreenClickArea != null) fullScreenClickArea.gameObject.SetActive(false);
    }

    private void HandleGlobalSkip(double prev, double target) {
        if (dialogueFadeTween != null && dialogueFadeTween.IsActive()) dialogueFadeTween.Complete();
    }

    public void SetSpeakerName(string name) { nameText.text = name; }

    public void PrintText(string text, float speed, float elapsedTime) {
        if (currentTextTween != null && currentTextTween.IsActive()) currentTextTween.Kill();
        dialogueText.text = "";

        if (string.IsNullOrEmpty(text)) text = "";

        float duration = text.Length * 0.05f / speed;
        currentTextTween = dialogueText.DOText(text, duration).SetEase(Ease.Linear).SetUpdate(true).SetId("TimelineTweens").SetLink(dialogueText.gameObject);

        bool isPlaying = Application.isPlaying && activeDirector != null && activeDirector.state == PlayState.Playing;

        if (elapsedTime >= duration) currentTextTween.Complete(true);
        else currentTextTween.Goto(elapsedTime, isPlaying);
    }

    public void CompleteTextTween() { if (IsTyping) currentTextTween.Complete(); }
}

public interface IEffectManager {
    MarkerTrackType TargetTrackType { get; }
    void ExecuteMarkerAction(UniversalMarker marker, double currentTime, bool forceDispatch = false);
}
#endif