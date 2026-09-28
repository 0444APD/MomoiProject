#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
using UnityEngine.Timeline;

[TrackColor(1f, 0.5f, 0f)]
[TrackBindingType(typeof(EffectManager))]
public class EffectTrack : TrackAsset {
}
#endif