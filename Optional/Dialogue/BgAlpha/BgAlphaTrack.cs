#if SYSTEM_DIALOGUE
using UnityEngine.Timeline;
using UnityEngine;

[TrackColor(0.1f, 0.1f, 0.1f)]
[TrackBindingType(typeof(BgAlphaManager))]
public class BgAlphaTrack : TrackAsset { }
#endif