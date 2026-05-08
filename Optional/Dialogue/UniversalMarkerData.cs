#if SYSTEM_DIALOGUE
using UnityEngine;
using Sirenix.OdinInspector;
using DG.Tweening;
using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

// Ʈ�� ���� �������� Effect �߰�
public enum MarkerTrackType { None, Background, Standing, BgAlpha, Camera, Image, Effect }
public enum StandingActionType { Create, Exist }

public enum StandingTransformAction {
    [LabelText("����")] None,
    [LabelText("��ġ �̵�")] Move,
    [LabelText("Ư�� ����Ʈ")] Effect
}

public enum PositionTargetType { Preset, Coordinate }

public enum TweenEffectType { Shake, Bounce, FadeIn, FadeOut }

public static class PositionPresets {
    public static readonly Vector2 OffScreenLeft = new Vector2(-230f, 0f);
    public static readonly Vector2 OffScreenRight = new Vector2(230f, 0f);
    public static readonly Vector2 Left = new Vector2(-100f, 0f);
    public static readonly Vector2 Right = new Vector2(100f, 0f);
    public static readonly Vector2 Center = new Vector2(0f, 0f);

    public static IEnumerable<ValueDropdownItem<string>> GetDropdown() {
        return new ValueDropdownList<string>() {
            { "ȭ�� ���� ��", "OffScreenLeft" },
            { "ȭ�� ���� ��", "OffScreenRight" },
            { "���� ��ġ", "Left" },
            { "���� ��ġ", "Right" },
            { "�߾� ��ġ", "Center" }
        };
    }

    public static Vector2 GetPresetVector(string preset) {
        switch (preset) {
            case "OffScreenLeft": return OffScreenLeft;
            case "OffScreenRight": return OffScreenRight;
            case "Left": return Left;
            case "Right": return Right;
            case "Center": return Center;
            default: return Vector2.zero;
        }
    }
}

// ============================================
// [1] ��� ��Ŀ ������
// ============================================
[Serializable]
public class BgMarkerData {
    [Title("��� �̹��� ���� ����")]
    [LabelText("�̹��� ���")]
    [Sirenix.OdinInspector.FilePath(ParentFolder = "Assets", Extensions = ".png,.jpg")]
    public string bgFilePath;

    [HideInInspector] public Sprite targetBgSprite;

    [BoxGroup("���͹ڽ� ����")]
    [LabelText("���͹ڽ� ��� ����")]
    public bool useLetterbox = false;

    [BoxGroup("���͹ڽ� ����")]
    [ShowIf("useLetterbox")]
    [LabelText("��� ���͹ڽ� �β�")]
    public float topLetterboxHeight = 2.5f;

    [BoxGroup("���͹ڽ� ����")]
    [ShowIf("useLetterbox")]
    [LabelText("�ϴ� ���͹ڽ� �β�")]
    public float bottomLetterboxHeight = 2.5f;

    [FoldoutGroup("�� �Ӽ�", expanded: false)]
    [PropertyOrder(100)]
    [LabelText("���̾�α� ����(Prefix) ȿ���� ���")]
    public bool isPrefix = true;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(101)]
    [ShowIf("isPrefix")]
    [PropertyRange(0f, 1f)]
    [LabelText("�� ���� ���� ���� (0~1)")]
    public float prefixUnlockRatio = 0.9f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(102)]
    [LabelText("���� ���� �ð�(Duration)")]
    public float duration = 1f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(103)]
    [LabelText("����/���� (Ease)")]
    public Ease easeType = Ease.InOutQuad;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(104)]
    [LabelText("�����ƿ�(����) ���� �ð�")]
    public float holdBlackDuration = 0f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(105)]
    [LabelText("A -> ���� ��ȯ �ӵ�")]
    public float fadeToBlackDuration = 0f;
}

// ============================================
// [2] ���ĵ� ��Ŀ ������
// ============================================
[Serializable]
public class StandingMarkerData {
    [HideInInspector] public StandingActionType actionType = StandingActionType.Create;
    [HideInInspector] public string targetStandingID;

    [ShowIf("IsCreateMode")]
    [BoxGroup("���ĵ� ����", centerLabel: true)]
    [PropertyOrder(1)]
    [ValueDropdown("@UniversalMarkerUtility.GetAllCharacterIDs()")]
    [OnValueChanged("OnCharacterChanged")]
    [LabelText("ĳ���� ����")]
    public string standingID;

    [ShowIf("IsCreateMode")]
    [BoxGroup("���ĵ� ����")]
    [PropertyOrder(2)]
    [ValueDropdown("@UniversalMarkerUtility.GetExpressionsDropdown(standingID)")]
    [LabelText("ǥ�� ����")]
    public string expressionID;

    [ShowIf("IsCreateMode")]
    [BoxGroup("â ��� ����")]
    [PropertyOrder(3)]
    [LabelText("â ��� ���")]
    public bool useWindowMode = false;

    [ShowIf("ShowWindowSprite")]
    [BoxGroup("â ��� ����")]
    [PropertyOrder(4)]
    [LabelText("â ������ �̹���")]
    public Sprite windowSprite;

    [ShowIf("IsExistMode")]
    [BoxGroup("���� ���ĵ� ���� ����")]
    [PropertyOrder(10)]
    [LabelText("ĳ���� ���� (�ֿ켱)")]
    [OnValueChanged("OnDeleteChanged")]
    public bool isDelete = false;

    [ShowIf("ShowExistOptions")]
    [BoxGroup("���� ���ĵ� ���� ����")]
    [PropertyOrder(11)]
    [LabelText("ǥ�� ����")]
    public bool isUpdateExpression = false;

    [ShowIf("ShowExistOptions")]
    [BoxGroup("���� ���ĵ� ���� ����")]
    [PropertyOrder(12)]
    [LabelText("���̶���Ʈ ����")]
    public bool isUpdateHighlight = false;

    [ShowIf("ShowExistOptions")]
    [BoxGroup("���� ���ĵ� ���� ����")]
    [PropertyOrder(13)]
    [LabelText("â �̹��� ����")]
    public bool isUpdateWindow = false;

    [ShowIf("ShowUpdateExpression")]
    [PropertyOrder(14)]
    [ValueDropdown("@UniversalMarkerUtility.GetExpressionsDropdown(targetStandingID)")]
    [OnValueChanged("OnTargetCharacterChanged")]
    [LabelText("������ ǥ��")]
    public string updateExpressionID;

    [ShowIf("ShowUpdateWindow")]
    [PropertyOrder(15)]
    [LabelText("������ â ������ �̹���")]
    public Sprite updateWindowSprite;

    [ShowIf("ShowHighlightToggle")]
    [BoxGroup("���ĵ� �ð� ȿ��")]
    [PropertyOrder(16)]
    [LabelText("���̶���Ʈ Ȱ��ȭ")]
    public bool isHighlight = true;

    [ShowIf("ShowExistOptions")]
    [Title("�̵� �� Ư�� ����Ʈ")]
    [PropertyOrder(20)]
    [EnumToggleButtons]
    [LabelText("Ʈ������ ����")]
    public StandingTransformAction transformAction = StandingTransformAction.None;

    [ShowIf("IsEffectMode")]
    [PropertyOrder(21)]
    public TweenEffectType tweenType;

    [ShowIf("IsShake")]
    [PropertyOrder(22)]
    [LabelText("���� ����")]
    public float shakeStrength = 1f;

    [ShowIf("ShowPositionFields")]
    [Title("��ġ ����")]
    [PropertyOrder(30)]
    [EnumToggleButtons]
    public PositionTargetType posType = PositionTargetType.Preset;

    [ShowIf("IsShowPreset")]
    [PropertyOrder(31)]
    [ValueDropdown("@PositionPresets.GetDropdown()")]
    [LabelText("��ġ ������")]
    public string presetPosition = "Center";

    [ShowIf("IsShowCoordinate")]
    [PropertyOrder(32)]
    [LabelText("��ǥ ��ǥ (X, Y)")]
    public Vector2 coordinate;

    [ShowIf("IsShowCoordinate")]
    [PropertyOrder(33)]
    [Button("ȭ�� ������Ʈ (Scene View Ŭ��)", ButtonSizes.Medium)]
    private void PickCoordinate() {
#if UNITY_EDITOR
        UniversalMarkerUtility.StartPicking(pos => { coordinate = new Vector2(pos.x, 0f); GUI.changed = true; });
#endif
    }

    [FoldoutGroup("�� �Ӽ�", expanded: false)]
    [PropertyOrder(100)]
    [LabelText("���̾�α� ����(Prefix) ȿ���� ���")]
    public bool isPrefix = true;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(101)]
    [ShowIf("isPrefix")]
    [PropertyRange(0f, 1f)]
    [LabelText("�� ���� ���� ���� (0~1)")]
    public float prefixUnlockRatio = 0.9f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(102)]
    [LabelText("���� ���� �ð�(Duration)")]
    public float duration = 0.3f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(103)]
    [LabelText("����/���� (Ease)")]
    public Ease easeType = Ease.InOutQuad;

    [HideInInspector] public Sprite standingSprite;

    private bool IsCreateMode => actionType == StandingActionType.Create;
    private bool IsExistMode => actionType == StandingActionType.Exist;
    private bool ShowExistOptions => IsExistMode && !isDelete;

    private bool ShowUpdateExpression => ShowExistOptions && isUpdateExpression;
    private bool ShowUpdateHighlight => ShowExistOptions && isUpdateHighlight;
    private bool ShowUpdateWindow => ShowExistOptions && isUpdateWindow;

    private bool IsMoveMode => ShowExistOptions && transformAction == StandingTransformAction.Move;
    private bool IsEffectMode => ShowExistOptions && transformAction == StandingTransformAction.Effect;

    private bool ShowPositionFields => IsCreateMode || IsMoveMode;
    private bool IsShowPreset => ShowPositionFields && posType == PositionTargetType.Preset;
    private bool IsShowCoordinate => ShowPositionFields && posType == PositionTargetType.Coordinate;
    private bool IsShake => IsEffectMode && tweenType == TweenEffectType.Shake;

    private bool ShowWindowSprite => IsCreateMode && useWindowMode;
    private bool ShowHighlightToggle => IsCreateMode || ShowUpdateHighlight;

#if UNITY_EDITOR
    private void OnDeleteChanged() {
        if (isDelete) {
            isUpdateExpression = false;
            isUpdateHighlight = false;
            isUpdateWindow = false;
            transformAction = StandingTransformAction.None;
        }
    }

    private void OnCharacterChanged() {
        expressionID = "";
        UpdateSprite();
    }

    private void OnTargetCharacterChanged() {
        updateExpressionID = "";
        UpdateSprite();
    }

    public void UpdateSprite() {
        standingSprite = null;
        string targetID = actionType == StandingActionType.Create ? standingID : targetStandingID;
        string targetExp = actionType == StandingActionType.Create ? expressionID : updateExpressionID;

        if (string.IsNullOrEmpty(targetID) || string.IsNullOrEmpty(targetExp)) return;
        string[] guids = AssetDatabase.FindAssets($"t:CharacterDialogueSO {targetID}");
        if (guids.Length > 0) {
            var so = AssetDatabase.LoadAssetAtPath<CharacterDialogueSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (so != null) standingSprite = so.GetExpression(targetExp);
        }
    }
#endif
}

// ============================================
// [3] ��� ����(����) ��Ŀ ������
// ============================================
[Serializable]
public class BgAlphaMarkerData {
    [Title("��� ����(����) �������� ����")]
    [PropertyRange(0f, 100f)]
    [LabelText("���� �ۼ�Ʈ (%)")]
    public float alphaPercent = 25f;

    [FoldoutGroup("�� �Ӽ�", expanded: false)]
    [PropertyOrder(100)]
    [LabelText("���̾�α� ����(Prefix) ȿ���� ���")]
    public bool isPrefix = true;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(101)]
    [ShowIf("isPrefix")]
    [PropertyRange(0f, 1f)]
    [LabelText("�� ���� ���� ���� (0~1)")]
    public float prefixUnlockRatio = 0.9f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(102)]
    [LabelText("���� ���� �ð�(Duration)")]
    public float duration = 1f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(103)]
    [LabelText("����/���� (Ease)")]
    public Ease easeType = Ease.InOutQuad;
}

// ============================================
// [4] ī�޶� ��Ŀ ������
// ============================================
public enum CameraActionType {
    [LabelText("�� ��/�ƿ�")] Zoom,
    [LabelText("��ġ �̵�")] Move,
    [LabelText("����")] Reset
}

[Serializable]
public class CameraMarkerData {
    [Title("ī�޶� ����")]
    [EnumToggleButtons]
    [HideLabel]
    public CameraActionType actionType = CameraActionType.Zoom;

    [ShowIf("ShowPositionFields")]
    [Title("��ǥ �߾� ��ġ")]
    [PropertyOrder(10)]
    [EnumToggleButtons]
    public PositionTargetType posType = PositionTargetType.Preset;

    [ShowIf("IsShowPreset")]
    [PropertyOrder(11)]
    [ValueDropdown("@PositionPresets.GetDropdown()")]
    [LabelText("��ġ ������")]
    public string presetPosition = "Center";

    [ShowIf("IsShowCoordinate")]
    [PropertyOrder(12)]
    [LabelText("��ǥ ��ǥ (X, Y)")]
    public Vector2 coordinate;

    [ShowIf("IsShowCoordinate")]
    [PropertyOrder(13)]
    [Button("ȭ�� ������Ʈ (Scene View Ŭ��)", ButtonSizes.Medium)]
    private void PickCoordinate() {
#if UNITY_EDITOR
        UniversalMarkerUtility.StartPicking(pos => { coordinate = new Vector2(pos.x, pos.y); GUI.changed = true; });
#endif
    }

    [ShowIf("IsZoomMode")]
    [Title("�� ����")]
    [PropertyOrder(20)]
    [PropertyRange(10f, 300f)]
    [LabelText("�� ���� (%)")]
    public float zoomPercent = 100f;

    [FoldoutGroup("�� �Ӽ�", expanded: false)]
    [PropertyOrder(100)]
    [LabelText("���̾�α� ����(Prefix) ȿ���� ���")]
    public bool isPrefix = true;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(101)]
    [ShowIf("isPrefix")]
    [PropertyRange(0f, 1f)]
    [LabelText("�� ���� ���� ���� (0~1)")]
    public float prefixUnlockRatio = 0.9f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(102)]
    [LabelText("���� ���� �ð�(Duration)")]
    public float duration = 1f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(103)]
    [LabelText("����/���� (Ease)")]
    public Ease easeType = Ease.InOutQuad;

    private bool IsZoomMode => actionType == CameraActionType.Zoom;
    private bool IsMoveMode => actionType == CameraActionType.Move;
    private bool ShowPositionFields => IsZoomMode || IsMoveMode;
    private bool IsShowPreset => ShowPositionFields && posType == PositionTargetType.Preset;
    private bool IsShowCoordinate => ShowPositionFields && posType == PositionTargetType.Coordinate;
}

// ============================================
// [5] �̹��� ��� ��Ŀ ������
// ============================================
public enum ImageActionType { Create, Delete }

[Serializable]
public class ImageMarkerData {
    [HideInInspector] public string trackID;
    [HideInInspector] public ImageActionType actionType = ImageActionType.Create;

    [ShowIf("IsCreateMode")]
    [Title("�̹��� ��� ����")]
    [LabelText("�̹��� ���")]
    [Sirenix.OdinInspector.FilePath(ParentFolder = "Assets", Extensions = ".png,.jpg")]
    public string imagePath;

    [HideInInspector] public Sprite targetSprite;

    [ShowIf("IsCreateMode")]
    [Title("��ġ ����")]
    [PropertyOrder(10)]
    [EnumToggleButtons]
    public PositionTargetType posType = PositionTargetType.Preset;

    [ShowIf("IsShowPreset")]
    [PropertyOrder(11)]
    [ValueDropdown("@PositionPresets.GetDropdown()")]
    [LabelText("��ġ ������")]
    public string presetPosition = "Center";

    [ShowIf("IsShowCoordinate")]
    [PropertyOrder(12)]
    [LabelText("��ǥ ��ǥ (X, Y)")]
    public Vector2 coordinate;

    [ShowIf("IsShowCoordinate")]
    [PropertyOrder(13)]
    [Button("ȭ�� ������Ʈ (Scene View Ŭ��)", ButtonSizes.Medium)]
    private void PickCoordinate() {
#if UNITY_EDITOR
        UniversalMarkerUtility.StartPicking(pos => { coordinate = new Vector2(pos.x, pos.y); GUI.changed = true; });
#endif
    }

    [FoldoutGroup("�� �Ӽ�", expanded: false)]
    [PropertyOrder(100)]
    [LabelText("���̾�α� ����(Prefix) ȿ���� ���")]
    public bool isPrefix = true;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(101)]
    [ShowIf("isPrefix")]
    [PropertyRange(0f, 1f)]
    [LabelText("�� ���� ���� ���� (0~1)")]
    public float prefixUnlockRatio = 0.9f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(102)]
    [LabelText("���� ���� �ð�(Duration)")]
    public float duration = 0.5f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(103)]
    [LabelText("����/���� (Ease)")]
    public Ease easeType = Ease.InOutQuad;

    private bool IsCreateMode => actionType == ImageActionType.Create;
    private bool IsShowPreset => IsCreateMode && posType == PositionTargetType.Preset;
    private bool IsShowCoordinate => IsCreateMode && posType == PositionTargetType.Coordinate;
}

// ============================================
// [6] ����Ʈ ��� ��Ŀ ������ (�ű�)
// ============================================
[Serializable]
public class EffectMarkerData {
    [Title("����Ʈ ������ �Ҵ�")]
    [LabelText("����Ʈ ������ (������Ʈ ����)")]
    [AssetsOnly] // ���� �ִ� ������Ʈ�� �ƴ� ���¸� �Ҵ� ����
    public GameObject effectPrefab;

    [Title("��ġ ����")]
    [PropertyOrder(10)]
    [EnumToggleButtons]
    public PositionTargetType posType = PositionTargetType.Preset;

    [ShowIf("IsShowPreset")]
    [PropertyOrder(11)]
    [ValueDropdown("@PositionPresets.GetDropdown()")]
    [LabelText("��ġ ������")]
    public string presetPosition = "Center";

    [ShowIf("IsShowCoordinate")]
    [PropertyOrder(12)]
    [LabelText("��ǥ ��ǥ (X, Y)")]
    public Vector2 coordinate;

    [ShowIf("IsShowCoordinate")]
    [PropertyOrder(13)]
    [Button("ȭ�� ������Ʈ (Scene View Ŭ��)", ButtonSizes.Medium)]
    private void PickCoordinate() {
#if UNITY_EDITOR
        UniversalMarkerUtility.StartPicking(pos => { coordinate = new Vector2(pos.x, pos.y); GUI.changed = true; });
#endif
    }

    [FoldoutGroup("�� �Ӽ�", expanded: false)]
    [PropertyOrder(100)]
    [LabelText("���̾�α� ����(Prefix) ȿ���� ���")]
    public bool isPrefix = false;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(101)]
    [ShowIf("isPrefix")]
    [PropertyRange(0f, 1f)]
    [LabelText("�� ���� ���� ���� (0~1)")]
    public float prefixUnlockRatio = 0.9f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(102)]
    [LabelText("����Ʈ ����/���� �ð�(Duration)")]
    public float duration = 2f;

    [FoldoutGroup("�� �Ӽ�")]
    [PropertyOrder(103)]
    [LabelText("����/���� (Ease)")]
    public Ease easeType = Ease.InOutQuad;

    private bool IsShowPreset => posType == PositionTargetType.Preset;
    private bool IsShowCoordinate => posType == PositionTargetType.Coordinate;
}
#endif