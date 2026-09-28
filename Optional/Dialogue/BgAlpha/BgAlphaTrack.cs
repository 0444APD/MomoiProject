#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
using UnityEngine.Timeline;
using UnityEngine;

[TrackColor(0.1f, 0.1f, 0.1f)]
[TrackBindingType(typeof(BgAlphaManager))]
public class BgAlphaTrack : TrackAsset { }
#endif