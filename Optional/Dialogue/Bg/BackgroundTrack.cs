#if SYSTEM_DIALOGUE
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[TrackColor(0.2f, 0.6f, 0.8f)]
[TrackBindingType(typeof(BackgroundManager))] // Ʈ���� �Ŵ����� ���ε��ϵ��� ����
public class BackgroundTrack : TrackAsset {
    // ���� �� Ʈ���� Ŭ��(TrackClipType)�� ������ ������, ��Ŀ�� �����մϴ�.
}

#endif