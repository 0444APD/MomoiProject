#if SYSTEM_DIALOGUE
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "Core/Character Dialogue SO")]
public class CharacterDialogueSO : SerializedScriptableObject // ��ųʸ� ����ȭ�� ���� SerializedScriptableObject ���
{
    [Title("ĳ���� �⺻ ����")]
    [LabelText("ĳ���� ���� ID")]
    public string ID => name;

    [LabelText("�⺻ ǥ�� �̸�")]
    public string defaultName; // (�ٱ��� �ý����� ���ٸ� String Key�� ��ü ����)

    [Title("ǥ�� �� ���ĵ� �Ϸ���Ʈ")]
    [InfoBox("ǥ�� ID ����: Normal, Smile, Angry, Sad ��")]
    [DictionaryDrawerSettings(KeyLabel = "ǥ�� ID", ValueLabel = "���ĵ� ��������Ʈ")]
    public Dictionary<string, Sprite> expressions = new Dictionary<string, Sprite>();

    public string GetID() => name;

    // �Ŵ����� Behaviour���� �����ϰ� ���ĵ� �̹����� �������� ���� �Լ�
    public Sprite GetExpression(string expressionID) {
        if (expressions.TryGetValue(expressionID, out Sprite sprite)) {
            return sprite;
        }

        Debug.LogWarning($"[CharacterDialogueSO] '{ID}' ĳ���Ϳ��� '{expressionID}' ǥ��/���ĵ��� �����ϴ�!");
        return null;
    }
}
#endif