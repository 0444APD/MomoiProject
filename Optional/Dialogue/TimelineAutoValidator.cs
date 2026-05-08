#if UNITY_EDITOR && SYSTEM_DIALOGUE
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;
using System.Collections.Generic;
using System.Linq;

public class TimelineAutoValidator : UnityEditor.AssetModificationProcessor {

    static string[] OnWillSaveAssets(string[] paths) {
        bool hasPlayableChanges = false;
        foreach (string path in paths) {
            if (path.EndsWith(".playable")) {
                hasPlayableChanges = true;
                break;
            }
        }
        if (hasPlayableChanges) {
            CheckAllTimelines();
        }
        return paths;
    }

    [MenuItem("Tools/Ÿ�Ӷ��� ��ü �ϰ� �˻�")]
    public static void CheckAllTimelines() {
        string[] guids = AssetDatabase.FindAssets("t:TimelineAsset");
        int errorCount = 0;

        foreach (string guid in guids) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TimelineAsset timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);

            if (timeline != null) {
                if (!ValidateTimeline(timeline, out string errorMsg)) {
                    Debug.LogError($"<color=red>[Ÿ�Ӷ��� ����]</color> ���ϸ�: {timeline.name}\n���: {path}\n����: \n{errorMsg}", timeline);
                    errorCount++;
                }
            }
        }

        if (errorCount > 0) {
            Debug.LogWarning($"[�˻� �Ϸ�] {errorCount}���� Ÿ�Ӷ��� ���� ������ �߰ߵǾ����ϴ�! �ܼ� â�� ���� ���� �޽����� Ȯ���ϼ���.");
        }
        else {
            Debug.Log($"<color=green>[Ÿ�Ӷ��� �ڵ� �˻�]</color> ��� ���� ���� ����.");
        }
    }

    private static bool ValidateTimeline(TimelineAsset timeline, out string errorMessage) {
        int dialogueCount = 0; int cameraCount = 0; int bgCount = 0;
        List<string> errors = new List<string>();
        bool isModified = false;

        foreach (TrackAsset track in timeline.GetOutputTracks()) {
            if (track.GetType().Name == "DialogueTrack") dialogueCount++;
            else if (track is CameraTrack) cameraCount++;
            else if (track is BackgroundTrack) bgCount++;

            else if (track is StandingTrack) {
                var trackMarkers = track.GetMarkers().OfType<UniversalMarker>().OrderBy(m => m.time).ToList();
                string trackActiveID = null;

                foreach (var marker in trackMarkers) {
                    var data = marker.standingData;
                    if (string.IsNullOrEmpty(trackActiveID)) {
                        if (data.actionType != StandingActionType.Create) {
                            data.actionType = StandingActionType.Create;
                            data.targetStandingID = "";
                            isModified = true;
                        }
                        if (string.IsNullOrWhiteSpace(data.standingID)) errors.Add($"[{marker.time:F2}��] {track.name}: ���ĵ� ���� �� ĳ���� ID�� �������� �ʾҽ��ϴ�.");
                        else trackActiveID = data.standingID;

                        data.UpdateSprite();
                        if (data.standingSprite == null && !string.IsNullOrWhiteSpace(data.standingID)) errors.Add($"[{marker.time:F2}��] {track.name}: ��������Ʈ ����.");
                    }
                    else {
                        if (data.actionType != StandingActionType.Exist || data.targetStandingID != trackActiveID) {
                            data.actionType = StandingActionType.Exist;
                            data.targetStandingID = trackActiveID;
                            isModified = true;
                        }

                        if (data.isDelete) trackActiveID = null;
                        else {
                            if (data.isUpdateExpression) {
                                data.UpdateSprite();
                                if (data.standingSprite == null) errors.Add($"[{marker.time:F2}��] {track.name}: ������ ǥ�� ��������Ʈ ����.");
                            }
                        }
                    }
                }
            }
            else if (track is ImageTrack) {
                var trackMarkers = track.GetMarkers().OfType<UniversalMarker>().OrderBy(m => m.time).ToList();
                bool hasImage = false;

                foreach (var marker in trackMarkers) {
                    var data = marker.imageData;
                    if (data.trackID != track.name) {
                        data.trackID = track.name;
                        isModified = true;
                    }

                    if (!hasImage) {
                        if (data.actionType != ImageActionType.Create) {
                            data.actionType = ImageActionType.Create;
                            isModified = true;
                        }
                        if (string.IsNullOrWhiteSpace(data.imagePath)) {
                            errors.Add($"[{marker.time:F2}��] {track.name}: �̹��� ��ΰ� �������� �ʾҽ��ϴ�.");
                        }
                        hasImage = true;
                    }
                    else {
                        if (data.actionType != ImageActionType.Delete) {
                            data.actionType = ImageActionType.Delete;
                            isModified = true;
                        }
                        hasImage = false;
                    }
                }
            }
            else if (track is EffectTrack) {
                var trackMarkers = track.GetMarkers().OfType<UniversalMarker>();
                foreach (var marker in trackMarkers) {
                    if (marker.effectData.effectPrefab == null) {
                        errors.Add($"[{marker.time:F2}��] {track.name}: ����Ʈ �������� �Ҵ���� �ʾҽ��ϴ�.");
                    }
                }
            }
        }

        if (dialogueCount > 1 || cameraCount > 1 || bgCount > 1) {
            errors.Add($"[Ʈ�� �ߺ� ����] ��ȭ/ī�޶�/��� Ʈ���� 1���� ���˴ϴ�.");
        }

        if (isModified) {
            EditorUtility.SetDirty(timeline);
        }

        if (errors.Count > 0) {
            errorMessage = string.Join("\n", errors);
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
}
#endif