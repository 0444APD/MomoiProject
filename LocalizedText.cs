#if GAME_SYSTEMS_READY
using UnityEngine;
using TMPro;
using System.Collections.Generic;

[RequireComponent(typeof(TextMeshProUGUI))]
public class LocalizedText : MonoBehaviour {
    [SerializeField] private string textKey; // �����Ϳ��� �Ҵ��� �� ���� Ű
    private Dictionary<string, string> currentArgs; // ���� ġȯ�� ������ ĳ��
    private TextMeshProUGUI tmp;

    private void Awake() {
        tmp = GetComponent<TextMeshProUGUI>();
    }

    private void OnEnable() {
        LanguageManager.OnLanguageChanged += UpdateText;
        UpdateText();
    }

    private void OnDisable() {
        LanguageManager.OnLanguageChanged -= UpdateText;
    }

    // === [�߰���] ��Ÿ�ӿ� �ܺ�(���� �Ŵ��� ��)���� ȣ���� ���� �ؽ�Ʈ ���� �Լ� ===
    public void SetDynamicText(string key, Dictionary<string, string> args = null) {
        textKey = key;
        currentArgs = args;
        UpdateText(); // ���� ��� �ؽ�Ʈ ����
    }

    private void UpdateText() {
        if (string.IsNullOrEmpty(textKey) || tmp == null) return;

        // ĳ���� �� Ű�� args�� TextManager�� �Ѱ� ���� ���ڿ��� �޽��ϴ�.
        tmp.text = TextTable.Get(textKey, currentArgs);
    }
}
#endif