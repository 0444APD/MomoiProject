#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
using UnityEngine;
using UnityEngine.UI;

public class ImagePrefabController : MonoBehaviour {
    [Header("UI Components")]
    public CanvasGroup canvasGroup;
    public Image contentImage; // ���� �̹����� ǥ�õ� �ڽ� Image ������Ʈ
}
#endif