#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
using UnityEngine;
using UnityEngine.Timeline;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine.Playables;

#if UNITY_EDITOR
using UnityEditor;
using System.Linq;
using Sirenix.OdinInspector.Editor;
#endif

[CustomStyle("UniversalMarker")]
[System.ComponentModel.DisplayName("Universal Marker")]
public class UniversalMarker : Marker, INotification {
    public PropertyName id { get; }

    [HideInInspector] public MarkerTrackType currentTrackType = MarkerTrackType.None;

    [ShowIf("currentTrackType", MarkerTrackType.Background)]
    [HideLabel]
    public BgMarkerData bgData = new BgMarkerData();

    [ShowIf("currentTrackType", MarkerTrackType.Standing)]
    [HideLabel]
    public StandingMarkerData standingData = new StandingMarkerData();

    [ShowIf("currentTrackType", MarkerTrackType.BgAlpha)]
    [HideLabel]
    public BgAlphaMarkerData bgAlphaData = new BgAlphaMarkerData();

    [ShowIf("currentTrackType", MarkerTrackType.Camera)]
    [HideLabel]
    public CameraMarkerData cameraData = new CameraMarkerData();

    [ShowIf("currentTrackType", MarkerTrackType.Image)]
    [HideLabel]
    public ImageMarkerData imageData = new ImageMarkerData();

    // ����Ʈ ������ �߰�
    [ShowIf("currentTrackType", MarkerTrackType.Effect)]
    [HideLabel]
    public EffectMarkerData effectData = new EffectMarkerData();
}

#if UNITY_EDITOR
public static class UniversalMarkerUtility {
    public static string GetActiveStandingOnTrackUpTo(UniversalMarker currentMarker) {
        var track = currentMarker.parent as TrackAsset;
        if (track == null) return null;

        var markers = track.GetMarkers().OfType<UniversalMarker>().Where(m => m.currentTrackType == MarkerTrackType.Standing).OrderBy(m => m.time).ToList();
        string activeID = null;

        foreach (var m in markers) {
            if (m == currentMarker) break;
            if (m.standingData.actionType == StandingActionType.Create && !string.IsNullOrEmpty(m.standingData.standingID))
                activeID = m.standingData.standingID;
            else if (m.standingData.actionType == StandingActionType.Exist && m.standingData.isDelete)
                activeID = null;
        }
        return activeID;
    }

    public static IEnumerable<string> GetAllActiveStandingsUpTo(UniversalMarker currentMarker) {
        var activeStandings = new HashSet<string>();
        var timeline = UnityEditor.Timeline.TimelineEditor.inspectedAsset;
        if (timeline == null) return activeStandings.ToList();

        var allMarkers = new List<UniversalMarker>();
        foreach (var track in timeline.GetOutputTracks()) {
            if (track is StandingTrack) allMarkers.AddRange(track.GetMarkers().OfType<UniversalMarker>());
        }

        allMarkers = allMarkers.OrderBy(m => m.time).ToList();
        foreach (var m in allMarkers) {
            if (m == currentMarker || m.time >= currentMarker.time) break;
            if (m.standingData.actionType == StandingActionType.Create && !string.IsNullOrEmpty(m.standingData.standingID))
                activeStandings.Add(m.standingData.standingID);
            else if (m.standingData.actionType == StandingActionType.Exist && m.standingData.isDelete)
                activeStandings.Remove(m.standingData.targetStandingID);
        }
        return activeStandings.ToList();
    }

    public static bool HasActiveImageOnTrackUpTo(UniversalMarker currentMarker) {
        var track = currentMarker.parent as TrackAsset;
        if (track == null) return false;

        var markers = track.GetMarkers().OfType<UniversalMarker>().Where(m => m.currentTrackType == MarkerTrackType.Image).OrderBy(m => m.time).ToList();
        bool hasImage = false;

        foreach (var m in markers) {
            if (m == currentMarker) break;
            if (m.imageData.actionType == ImageActionType.Create) hasImage = true;
            else if (m.imageData.actionType == ImageActionType.Delete) hasImage = false;
        }
        return hasImage;
    }

    public static IEnumerable<string> GetAllCharacterIDs() {
        var ids = new List<string>();
        string[] guids = AssetDatabase.FindAssets("t:CharacterDialogueSO");
        foreach (var guid in guids) {
            var so = AssetDatabase.LoadAssetAtPath<CharacterDialogueSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so != null) ids.Add(so.ID);
        }
        return ids;
    }

    public static IEnumerable<string> GetExpressionsDropdown(string targetID) {
        if (string.IsNullOrEmpty(targetID)) return new List<string>();
        string[] guids = AssetDatabase.FindAssets($"t:CharacterDialogueSO {targetID}");
        if (guids.Length > 0) {
            var so = AssetDatabase.LoadAssetAtPath<CharacterDialogueSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (so != null) return so.expressions.Keys;
        }
        return new List<string>();
    }

    private static System.Action<Vector2> onCoordinatePicked;
    public static void StartPicking(System.Action<Vector2> onPicked) {
        onCoordinatePicked = onPicked;
        SceneView.duringSceneGui += OnSceneGUI;
    }
    private static void OnSceneGUI(SceneView sceneView) {
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0) {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Plane plane = new Plane(Vector3.forward, Vector3.zero);
            if (plane.Raycast(ray, out float enter)) {
                Vector3 hitPoint = ray.GetPoint(enter);
                onCoordinatePicked?.Invoke(new Vector2(hitPoint.x, hitPoint.y));
            }
            SceneView.duringSceneGui -= OnSceneGUI;
            e.Use();
        }
    }
}

[CustomEditor(typeof(UniversalMarker))]
public class UniversalMarkerEditor : OdinEditor {
    private string cachedBgPath = "NONE";
    private string cachedCharID = "NONE";
    private string cachedExpID = "NONE";
    private string cachedImgPath = "NONE";

    public override void OnInspectorGUI() {
        UniversalMarker marker = (UniversalMarker)target;
        TrackAsset track = marker.parent as TrackAsset;

        if (track is BackgroundTrack) marker.currentTrackType = MarkerTrackType.Background;
        else if (track is StandingTrack) marker.currentTrackType = MarkerTrackType.Standing;
        else if (track.GetType().Name == "BgAlphaTrack") marker.currentTrackType = MarkerTrackType.BgAlpha;
        else if (track is CameraTrack) marker.currentTrackType = MarkerTrackType.Camera;
        else if (track is ImageTrack) marker.currentTrackType = MarkerTrackType.Image;
        else if (track is EffectTrack) marker.currentTrackType = MarkerTrackType.Effect;
        else marker.currentTrackType = MarkerTrackType.None;

        if (marker.currentTrackType == MarkerTrackType.Standing) {
            string activeID = UniversalMarkerUtility.GetActiveStandingOnTrackUpTo(marker);
            bool isChanged = false;

            if (!string.IsNullOrEmpty(activeID)) {
                if (marker.standingData.actionType != StandingActionType.Exist) { marker.standingData.actionType = StandingActionType.Exist; isChanged = true; }
                if (marker.standingData.targetStandingID != activeID) { marker.standingData.targetStandingID = activeID; isChanged = true; }
            }
            else {
                if (marker.standingData.actionType != StandingActionType.Create) { marker.standingData.actionType = StandingActionType.Create; marker.standingData.targetStandingID = ""; isChanged = true; }
            }
            if (isChanged) EditorUtility.SetDirty(marker);

            GUILayout.Space(5);
            if (marker.standingData.actionType == StandingActionType.Exist) {
                GUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label("Target:", EditorStyles.boldLabel, GUILayout.Width(80));
                GUIStyle nameStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
                nameStyle.normal.textColor = new Color(0.3f, 0.8f, 1f);
                GUILayout.Label(marker.standingData.targetStandingID, nameStyle);
                GUILayout.EndHorizontal();
            }
            else {
                GUIStyle errorStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
                errorStyle.normal.textColor = Color.red;
                GUILayout.Label("Empty track, Create Mode", errorStyle);
            }
        }
        else if (marker.currentTrackType == MarkerTrackType.Image) {
            if (track != null) marker.imageData.trackID = track.name;

            bool hasImage = UniversalMarkerUtility.HasActiveImageOnTrackUpTo(marker);
            bool isChanged = false;

            if (hasImage) {
                if (marker.imageData.actionType != ImageActionType.Delete) { marker.imageData.actionType = ImageActionType.Delete; isChanged = true; }
            }
            else {
                if (marker.imageData.actionType != ImageActionType.Create) { marker.imageData.actionType = ImageActionType.Create; isChanged = true; }
            }

            if (isChanged) EditorUtility.SetDirty(marker);

            GUILayout.Space(5);
            if (marker.imageData.actionType == ImageActionType.Delete) {
                GUIStyle deleteStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
                deleteStyle.normal.textColor = new Color(1f, 0.4f, 0.4f);
                GUILayout.Label("Track Image: Delete Mode", deleteStyle);
            }
            else {
                GUIStyle createStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
                createStyle.normal.textColor = new Color(0.4f, 1f, 0.4f);
                GUILayout.Label("Track Image: Create Mode", createStyle);
            }
        }

        GUILayout.Space(5);

        EditorGUI.BeginChangeCheck();

        serializedObject.Update();
        SerializedProperty timeProp = serializedObject.FindProperty("m_Time");
        if (timeProp != null) EditorGUILayout.PropertyField(timeProp, new GUIContent("Time"));
        serializedObject.ApplyModifiedProperties();

        GUILayout.Space(5);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Space(5);

        this.Tree.UpdateTree();
        foreach (var child in this.Tree.RootProperty.Children) {
            if (child.Name == "m_Time" || child.Name == "m_Script") continue;
            child.Draw();
        }

        this.Tree.ApplyChanges();
        bool isOdinChanged = EditorGUI.EndChangeCheck();

        bool needsSpriteUpdate = false;

        if (marker.currentTrackType == MarkerTrackType.Background) {
            if (cachedBgPath != marker.bgData.bgFilePath) {
                cachedBgPath = marker.bgData.bgFilePath;
                needsSpriteUpdate = true;
            }
        }
        else if (marker.currentTrackType == MarkerTrackType.Standing) {
            string currentTargetID = marker.standingData.actionType == StandingActionType.Create ? marker.standingData.standingID : marker.standingData.targetStandingID;
            string currentTargetExp = marker.standingData.actionType == StandingActionType.Create ? marker.standingData.expressionID : marker.standingData.updateExpressionID;

            if (cachedCharID != currentTargetID || cachedExpID != currentTargetExp) {
                cachedCharID = currentTargetID;
                cachedExpID = currentTargetExp;
                needsSpriteUpdate = true;
            }
        }
        else if (marker.currentTrackType == MarkerTrackType.Image) {
            if (cachedImgPath != marker.imageData.imagePath) {
                cachedImgPath = marker.imageData.imagePath;
                needsSpriteUpdate = true;
            }
        }

        if (isOdinChanged || needsSpriteUpdate) {

            if (needsSpriteUpdate) {
                if (marker.currentTrackType == MarkerTrackType.Background) {
                    marker.bgData.targetBgSprite = string.IsNullOrEmpty(marker.bgData.bgFilePath) ? null : AssetDatabase.LoadAssetAtPath<Sprite>("Assets/" + marker.bgData.bgFilePath);
                }
                else if (marker.currentTrackType == MarkerTrackType.Standing) {
                    Sprite expectedStd = null;
                    if (!string.IsNullOrEmpty(cachedCharID) && !string.IsNullOrEmpty(cachedExpID)) {
                        string[] guids = AssetDatabase.FindAssets($"t:CharacterDialogueSO {cachedCharID}");
                        if (guids.Length > 0) {
                            var so = AssetDatabase.LoadAssetAtPath<CharacterDialogueSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
                            if (so != null) expectedStd = so.GetExpression(cachedExpID);
                        }
                    }
                    marker.standingData.standingSprite = expectedStd;
                }
                else if (marker.currentTrackType == MarkerTrackType.Image) {
                    marker.imageData.targetSprite = string.IsNullOrEmpty(marker.imageData.imagePath) ? null : AssetDatabase.LoadAssetAtPath<Sprite>("Assets/" + marker.imageData.imagePath);
                }
            }

            EditorUtility.SetDirty(marker);
            Repaint();
        }

        Sprite previewSprite = null;
        if (marker.currentTrackType == MarkerTrackType.Background) previewSprite = marker.bgData.targetBgSprite;
        else if (marker.currentTrackType == MarkerTrackType.Standing && (marker.standingData.actionType == StandingActionType.Create || (marker.standingData.actionType == StandingActionType.Exist && marker.standingData.isUpdateExpression))) {
            previewSprite = marker.standingData.standingSprite;
        }
        else if (marker.currentTrackType == MarkerTrackType.Image && marker.imageData.actionType == ImageActionType.Create) {
            previewSprite = marker.imageData.targetSprite;
        }

        if (previewSprite != null && previewSprite.texture != null) {
            GUILayout.Space(10);
            float aspect = (float)previewSprite.texture.width / previewSprite.texture.height;
            float targetWidth = EditorGUIUtility.currentViewWidth - 30f;
            Rect rect = GUILayoutUtility.GetRect(targetWidth, targetWidth / aspect);
            GUI.DrawTexture(rect, previewSprite.texture, ScaleMode.ScaleToFit);
        }
    }
}
#endif
#endif