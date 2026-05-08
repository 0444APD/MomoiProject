#if SYSTEM_DIALOGUE
using UnityEngine;
using UnityEngine.UI;

public class StandingPrefabController : MonoBehaviour {
    [Header("UI Components")]
    public CanvasGroup canvasGroup;
    public Image windowImage;
    public Image standingImage;
    public Image maskArea;

    private Transform originalStandingParent;

    private void Awake() {
        // ���� ���ĵ��� �ִ� �θ�(�ֻ��� ��Ʈ)�� ����صӴϴ�.
        if (standingImage != null) {
            originalStandingParent = standingImage.transform.parent;
        }
    }

    // â ��� Ȱ��ȭ/��Ȱ��ȭ �� ���̶�Ű ����
    public void SetWindowMode(bool isWindowMode) {
        if (isWindowMode) {
            windowImage.gameObject.SetActive(true);
            standingImage.transform.SetParent(maskArea.transform, true);
        }
        else {
            windowImage.gameObject.SetActive(false);
            standingImage.transform.SetParent(originalStandingParent, true);
        }
    }
}
#endif