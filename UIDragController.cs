#if GAME_SYSTEMS_READY

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Sirenix.OdinInspector;

public class UIDragController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler {

    [Title("UI ���� ���")]
    [Tooltip("�������� ��ġ�� ��ȣ�ۿ� ������ ����ϴ� ������ �ʴ� ��Ŀ")]
    public RectTransform hitboxRect;
    [Tooltip("������ ���� ���̰�, �ε巴�� hitbox�� ����ٴϴ� �ð� ���")]
    public RectTransform visualRect;

    [Title("���� ���� (���� ����ġ)")]
    public bool canMove = true;      // Visual�� Hitbox�� �ڵ����� ������ ����
    public bool canInteract = true;  // ���콺 �Է��� ������ ����
    [Tooltip("��� �� ���� ��ġ�� ���ư��� �ʰ� �� �ڸ��� �ӹ����� ����")]
    public bool isFreeMove = false;

    [Title("���׳�ƽ (Magnet) ����")]
    public bool canMagnet = false;
    [Tooltip("���׳� ��ü �ȿ��� ������� �� �ش� ��ü�� �ͼ�(Snap)���� ����")]
    public bool snapToMagnet = true;
    [Tooltip("���׳�ƽ ����� �� ������Ʈ���� �±� ���")]
    [SerializeField] List<string> magnetTags = new List<string> { "Slot", "Tile" };

    [Title("ȣ�� (Hover) ����")]
    [SerializeField] float hoverScalePercent = 10f;
    [Tooltip("���콺�� �÷��� �� Visual�� �ӽ÷� �̵���ų �θ� ��ü (UI �ֻ�� ����� ��)")]
    [SerializeField] Transform hoverParent;

    [Title("�̵� �� ���� (Lerp) ����")]
    [SerializeField] float scaleLerpSpeed = 15f;
    [SerializeField] float moveLerpSpeed = 20f;

    [Title("ƿƮ (Tilt) ����")]
    [SerializeField] bool enableTilt = true;
    [SerializeField] float tiltAmount = 5f;
    [SerializeField] float tiltLerpSpeed = 10f;
    [SerializeField] float maxTiltAngle = 15f;

    // --- ���� ���� ���� ---
    [Title("���� ���� (������)")]
    [ReadOnly, ShowInInspector] private Vector2 dragStartPos;
    [ReadOnly, ShowInInspector] private Vector3 targetScale = Vector3.one;
    [ReadOnly, ShowInInspector] private bool isDragging = false;
    [ReadOnly, ShowInInspector] private GameObject currentMagnetTarget = null;
    [ReadOnly, ShowInInspector] private GameObject hoveredMagnet = null; // �巡�� �� �ӹ��� �ִ� ���׳�
    [ReadOnly, ShowInInspector] private GameObject snappedMagnet = null; // ����Ͽ� �ͼӵ� ������ ���׳�
    [ReadOnly, ShowInInspector] private Transform originalParent; // hoverParent �̵� �� ���� �θ�

    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private Vector3 lastFramePos;
    private Vector2 moveVelocity;

    // --- �ܺ� ���� �̺�Ʈ ---
    public event Action<UIDragController> OnClickEvent;
    public event Action<UIDragController> OnHoverEnterEvent;
    public event Action<UIDragController> OnHoverExitEvent;
    public event Action<UIDragController> OnBeginDragEvent;
    public event Action<UIDragController, PointerEventData> OnDragEvent;
    public event Action<UIDragController> OnEndDragEvent;

    public event Action<UIDragController, GameObject> OnMagnetEnterEvent;


    private void Awake() {
        canvasGroup = hitboxRect.GetComponent<CanvasGroup>();
        if (canvasGroup == null) {
            canvasGroup = hitboxRect.gameObject.AddComponent<CanvasGroup>();
        }

        canvas = GetComponentInParent<Canvas>();
        originalParent = visualRect.parent;
    }

    private void Update() {

        // 1. ũ�� ���� (Lerp)
        visualRect.localScale = Vector3.Lerp(visualRect.localScale, targetScale, Time.deltaTime * scaleLerpSpeed);

        // 2. ��ġ ���� (Visual�� Hitbox�� ����)
        visualRect.position = Vector3.Lerp(visualRect.position, hitboxRect.position, Time.deltaTime * moveLerpSpeed);

        // 3. ƿƮ ����
        if (enableTilt) {
            UpdateTiltEffect();
        }
    }

    private void UpdateTiltEffect() {
        Vector3 currentPos = hitboxRect.position;
        moveVelocity = (currentPos - lastFramePos);

        float tiltZ = -moveVelocity.x * tiltAmount;
        float tiltX = moveVelocity.y * tiltAmount;

        tiltZ = Mathf.Clamp(tiltZ, -maxTiltAngle, maxTiltAngle);
        tiltX = Mathf.Clamp(tiltX, -maxTiltAngle, maxTiltAngle);

        Quaternion targetRotation = Quaternion.Euler(tiltX, 0, tiltZ);
        visualRect.rotation = Quaternion.Lerp(visualRect.rotation, targetRotation, Time.deltaTime * tiltLerpSpeed);

        lastFramePos = currentPos;
    }

    #region ���콺 ��ȣ�ۿ� �������̽�

    public void OnPointerClick(PointerEventData eventData) {
        if (!canInteract || isDragging) return;
        if (eventData.button == PointerEventData.InputButton.Left) {
            OnClickEvent?.Invoke(this);
        }
    }

    public void OnPointerEnter(PointerEventData eventData) {
        if (!canInteract || isDragging) return;

        targetScale = Vector3.one * (1f + (hoverScalePercent / 100f));

        if (hoverParent != null) {
            visualRect.SetParent(hoverParent, true);
        }
        else {
            visualRect.SetAsLastSibling();
        }

        OnHoverEnterEvent?.Invoke(this);
    }

    public void OnPointerExit(PointerEventData eventData) {
        if (!canInteract || isDragging) return;

        targetScale = Vector3.one;

        if (hoverParent != null) {
            visualRect.SetParent(originalParent, true);
            visualRect.SetAsLastSibling(); // ���� �θ� �ȿ����� �� ������
        }

        OnHoverExitEvent?.Invoke(this);
    }

    public void OnBeginDrag(PointerEventData eventData) {
        if (!canInteract || !canMove) return;

        isDragging = true;
        dragStartPos = hitboxRect.position;
        canvasGroup.blocksRaycasts = false; // �巡�� �� �ڽ��� �����ϰ� ���� Ÿ���� Raycast�ϱ� ����

        if (hoverParent != null && visualRect.parent != hoverParent) {
            visualRect.SetParent(hoverParent, true);
        }

        OnBeginDragEvent?.Invoke(this);
    }

    public void OnDrag(PointerEventData eventData) {
        if (!canInteract || !isDragging || !canMove) return;

        GameObject newMagnet = null;

        if (canMagnet) {
            newMagnet = GetHoveredObjectWithTags(eventData, magnetTags);
        }

        // ���ο� ���׳� ��ü�� �����ߴ��� �˻�
        if (newMagnet != hoveredMagnet) {
            hoveredMagnet = newMagnet;
            if (hoveredMagnet != null) {
                // ���ο� ��ü�� ���� �� �̺�Ʈ �ߵ�
                OnMagnetEnterEvent?.Invoke(this, hoveredMagnet);
            }
        }

        currentMagnetTarget = hoveredMagnet;

        if (currentMagnetTarget != null) {
            hitboxRect.position = currentMagnetTarget.transform.position;
        }
        else {
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                canvas.transform as RectTransform, eventData.position, eventData.pressEventCamera, out Vector3 globalMousePos)) {
                hitboxRect.position = globalMousePos;
            }
        }

        OnDragEvent?.Invoke(this, eventData);
    }

    public void OnEndDrag(PointerEventData eventData) {
        if (!canInteract || !canMove) return;

        isDragging = false;
        canvasGroup.blocksRaycasts = true;

        if (hoverParent != null) {
            visualRect.SetParent(originalParent, true);
            visualRect.SetAsLastSibling();
        }

        // ���׳� �ͼ�(Snap) ó�� ����
        if (snapToMagnet) {
            if (currentMagnetTarget != null) {
                // ���������� ���׳� �ȿ��� ��� -> ��� �� ����
                snappedMagnet = currentMagnetTarget;
                hitboxRect.position = snappedMagnet.transform.position;
            }
            else if (snappedMagnet != null) {
                // ����� ��������� ������ �ͼӵ� ���׳��� ���� -> �ش� ��ġ�� ����
                hitboxRect.position = snappedMagnet.transform.position;
            }
            else if (!isFreeMove) {
                // �ͼӵ� ��ü�� ���� ���� �̵��� �ƴ� -> �巡�� ���� ��ġ�� ����
                hitboxRect.position = dragStartPos;
            }
        }
        else {
            // �ͼ� ����� �� �� ��� ���� ����
            if (!isFreeMove) {
                hitboxRect.position = dragStartPos;
            }
        }

        // ���� �ʱ�ȭ
        currentMagnetTarget = null;
        hoveredMagnet = null;

        OnEndDragEvent?.Invoke(this);
    }

    #endregion

    #region ��ƿ��Ƽ �Լ�

    // ���콺 Ŀ�� ��ġ���� Raycast�� ���� ������ �±� ��� �� �ϳ��� ��ġ�ϴ� ù ��° ������Ʈ�� ��ȯ
    private GameObject GetHoveredObjectWithTags(PointerEventData eventData, List<string> tags) {
        if (tags == null || tags.Count == 0) return null;

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        foreach (RaycastResult result in results) {
            if (tags.Contains(result.gameObject.tag)) {
                return result.gameObject;
            }
        }
        return null;
    }

    // �ܺ�(��: ī�� �Ŵ���, �κ��丮 �ý���)���� ������ ��ġ�� ������ �� ���
    public void SetPosition(Vector2 newPos) {
        if (!canMove) return;
        hitboxRect.position = newPos;
    }

    public void ForceSetPosition(Vector2 newPos) {
        hitboxRect.position = newPos;
        visualRect.position = newPos; 
    }

    // ���콺 ���� Ż�� ó�� (ī�尡 ������ ���ư��ų� �ı��� �� �ܻ� ������)
    public void ForceHoverExit() {
        if (isDragging) return; // �巡�� ���̸� ����
        targetScale = Vector3.one;
        if (hoverParent != null && visualRect.parent == hoverParent) {
            visualRect.SetParent(originalParent, true);
        }
    }

    // �ڵ�� Ư�� ���׳� ��ü�� ���� �ͼӽ��Ѿ� �� �� ���
    public void ForceSnapToMagnet(GameObject targetMagnet) {
        snappedMagnet = targetMagnet;
        if (!canMove) return;
        if (targetMagnet != null) {
            hitboxRect.position = targetMagnet.transform.position;
        }
    }

    // ���׳� �ͼ� ���¸� ������ ������ �� ���
    public void ClearMagnetSnap() {
        snappedMagnet = null;
    }

    #endregion
}

#endif