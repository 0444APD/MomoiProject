#if GAME_SYSTEMS_READY
using System;
using UnityEngine;

public static class LanguageManager {
    // �⺻ �� ��Ʈ�� �⺻ ��� �� �̸�(��: "Kr")�� �Ȱ��� �����ּ���.
    public static string CurrentLanguage { get; private set; } = "Kr";

    public static Action OnLanguageChanged;

    public static void ChangeLanguage(string newLang) {
        if (CurrentLanguage == newLang) return;

        CurrentLanguage = newLang;
        OnLanguageChanged?.Invoke();
    }
}
#endif