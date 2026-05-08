#if SYSTEM_DIALOGUE
using UnityEngine;
using UnityEngine.Playables;

public class DialogueBehaviour : PlayableBehaviour {
    public string speakerName;
    public string textID;
    public float textSpeed;
    public float fadeInDuration;
    public float fadeOutDuration;

    private bool hasPaused = false;
    private bool isPlayed = false;
    private PlayableDirector director;

    public override void OnPlayableCreate(Playable playable) {
        director = playable.GetGraph().GetResolver() as PlayableDirector;
    }

    public override void OnBehaviourPlay(Playable playable, FrameData info) {
        if (!Application.isPlaying) return;
        hasPaused = false;
        isPlayed = true;

        if (DialogueManager.Instance != null) {
            DialogueManager.Instance.OnClipStart(speakerName, textID, textSpeed, fadeInDuration, fadeOutDuration, playable, director);
        }
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData) {
        if (!Application.isPlaying) return;

        // deltaTime ������ ���� �Ӱ谪 ������ ���� ���� ���밪(0.05d)���� ���⸦ ����
        if (playable.GetTime() < playable.GetDuration() - 0.05d) {
            hasPaused = false;
        }
        else if (!hasPaused) {
            hasPaused = true;
            if (DialogueManager.Instance != null) {
                DialogueManager.Instance.PauseAtClipEnd();
            }
        }
    }

    public override void OnBehaviourPause(Playable playable, FrameData info) {
        if (!Application.isPlaying) return;
        isPlayed = false;
        hasPaused = false;
        if (DialogueManager.Instance != null) {
            // ������ �ź���(playable)�� �����Ͽ� �˻�޵��� ����
            DialogueManager.Instance.OnDialogueClipExited(playable);
        }
    }
}
#endif