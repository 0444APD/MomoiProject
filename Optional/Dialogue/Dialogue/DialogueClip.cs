#if SYSTEM_DIALOGUE
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

public class DialogueClip : PlayableAsset {
    [Title("���̾�α� ����")]
    [LabelText("ȭ�� (Character ID)")]
    [ValueDropdown("GetCharacterNames")]
    public string speakerName;

    [LabelText("�ؽ�Ʈ ID")] public string textID;

    [Space(10f)]
    [FoldoutGroup("�� �Ӽ�", expanded: false)]
    [LabelText("�ؽ�Ʈ ��� ���")] public float textSpeed = 1f;

    [FoldoutGroup("�� �Ӽ�")]
    [LabelText("UI ���̵� �� �ӵ�")]
    public float fadeInDuration = 0.3f;

    [FoldoutGroup("�� �Ӽ�")]
    [LabelText("UI ���̵� �ƿ� �ӵ�")]
    public float fadeOutDuration = 0.17f;
    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) {
        var playable = ScriptPlayable<DialogueBehaviour>.Create(graph);
        var behaviour = playable.GetBehaviour();

        behaviour.speakerName = speakerName;
        behaviour.textID = textID;
        behaviour.textSpeed = textSpeed;
        behaviour.fadeInDuration = fadeInDuration;
        behaviour.fadeOutDuration = fadeOutDuration;

        return playable;
    }

    private IEnumerable<string> GetCharacterNames() {
#if UNITY_EDITOR
        var names = new List<string>();
        string[] guids = AssetDatabase.FindAssets("t:CharacterDialogueSO");
        foreach (var guid in guids) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<CharacterDialogueSO>(path);
            if (so != null && !string.IsNullOrEmpty(so.ID)) names.Add(so.ID);
        }
        if (names.Count == 0) names.Add("ĳ���� SO ����");
        return names;
#else
        return new List<string>();
#endif
    }
}
#endif