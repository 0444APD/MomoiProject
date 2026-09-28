#if SYSTEM_DIALOGUE && GAME_SYSTEMS_READY && DOTWEEN_TEXTMESHPRO
using UnityEngine.Timeline;

[TrackColor(0.8f, 0.4f, 0.2f)]
[TrackBindingType(typeof(CameraManager))]
public class CameraTrack : TrackAsset {
}
#endif