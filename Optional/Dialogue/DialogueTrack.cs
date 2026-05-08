#if SYSTEM_DIALOGUE
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

// =======================================================
// [1] ���̾�α� (��ȭ) Ʈ��
// =======================================================
[TrackColor(0.2f, 0.6f, 1f)]
[TrackClipType(typeof(DialogueClip))]
public class DialogueTrack : TrackAsset { }
#endif