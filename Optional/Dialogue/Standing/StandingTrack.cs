#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
using UnityEngine.Timeline;

[TrackColor(0.2f, 0.8f, 0.4f)]
[TrackBindingType(typeof(StandingManager))]
public class StandingTrack : TrackAsset {
}

#endif