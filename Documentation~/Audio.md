# 오디오

모든 게임에 쓰는 코어 기능이다. 게임이 시작되면 오디오 오브젝트(`[오디오]`)가 자동으로 만들어지고 씬이 바뀌어도 계속 돈다. 프리팹을 둘 필요가 없다.

## 채널

| 채널 | 방식 |
|---|---|
| 전체 | 모든 채널에 곱해지는 음량 |
| 브금 | 한 번에 한 곡(직렬). 곡이 바뀌면 이전 곡이 페이드 아웃, 새 곡이 페이드 인(기본 1초). 같은 곡을 다시 틀면 이어서 음량만 맞춘다. |
| 목소리 | 한 번에 하나(직렬). 새 목소리가 나오면 이전 목소리가 짧게(기본 0.15초) 사라진다. |
| 효과음 | 튼 만큼 모두 동시에(병렬). 끝난 자리는 다시 쓰고, 최대 수(기본 32)를 넘으면 가장 오래된 소리를 멈춘다. 위치가 있는 3D 효과음도 된다. |
| UI | 효과음과 같은 방식, 음량만 따로. |

실제 음량 = 전체 × 채널 × 소리 하나의 음량. 음소거는 채널마다 따로 된다. 페이드와 재생은 게임 시간과 상관없이 흐른다(일시정지 중에도 브금은 계속).

**음량을 어디에 저장하나.** 플레이어 설정 화면의 음량은 DB › 설정 항목(적용 대상 = 전체 음량 · 브금 음량 · 목소리 음량 · 효과음 음량 · UI 음량)으로 둔다 — 값은 설정 파일(`<persistentDataPath>/MomoiProject/settings.json`, `GameSettings`)에 저장되고, 게임이 켜질 때와 바꿀 때 `GameAudio.SetVolume(…, save: false)`로 넣는다(PlayerPrefs에 쓰지 않는다). 코드가 `GameAudio.SetVolume(채널, 값)`을 바로 부르면 PlayerPrefs에 저장된다(오디오 설정의 음량 저장 여부를 따른다).

## 코드

```csharp
GameAudio.PlayBgm(clip);                 // 페이드 시간·반복·처음부터 다시는 인자로
GameAudio.StopBgm();
GameAudio.PlayVoice(clip);
GameAudio.PlaySfx(clip);                 // 음량·피치
GameAudio.PlaySfxAt(clip, position);     // 3D
GameAudio.PlayUi(clip);
GameAudio.Play(clip, AudioChannel.Sfx);  // 채널을 값으로
var handle = GameAudio.PlaySfx(clip); handle.Stop(0.3f);   // 효과음은 한 번 재생(반복 재생은 지원하지 않는다 — 반복음은 배경음 채널로)
GameAudio.Stop(AudioChannel.Sfx);  GameAudio.StopAll();
GameAudio.SetVolume(AudioChannel.Master, 0.8f);  GameAudio.SetMuted(AudioChannel.Bgm, true);
GameAudio.CurrentBgm; GameAudio.IsVoicePlaying; GameAudio.Playing(AudioChannel.Sfx);
```

## 설정(선택)

`MomoiProject › 오디오` 창의 '오디오 설정 만들기'로 `Assets/MomoiProject/Data/Resources/MomoiProject/GameAudioSettings.asset`을 만든다(있으면 '오디오 설정 열기'): 채널별 기본 음량(기본 전체 1 · 브금 0.8 · 나머지 1), 브금·목소리 페이드 시간(1초 · 0.15초), 병렬 최대 수(32), 채널별 믹서 그룹(이펙트·덕킹을 믹서에서 걸 때), 음량 저장 여부. 없으면 기본값으로 동작한다.

## 에디터에서 듣기

`MomoiProject › 오디오` 창: 클립과 채널을 골라 재생(게임과 같은 방식: 브금·목소리는 교차 페이드, 효과음·UI는 겹쳐서), 채널 정지·모두 정지, 채널 음량, 지금 나는 소리. 프로젝트 창에서 오디오 클립을 우클릭 › `MomoiProject › 오디오 재생 / 오디오 정지`. 플레이 중이 아니어도 된다.

## 다이얼로그

다이얼로그의 '배경 음악' 효과는 브금 채널로 나가고(효과의 진행 시간 = 페이드 시간), 다이얼로그가 끝나면 다이얼로그 전의 브금으로 돌아간다(`DialogueStage.restoreMusicOnEnd`). 브금을 정하지 않은 다이얼로그는 게임 브금을 건드리지 않는다. '효과음' 효과는 효과음 채널, 대사 노드의 '목소리'는 목소리 채널로 나가며 목소리가 없는 대사에서는 이전 목소리가 멈춘다. 자동 진행은 목소리가 끝날 때까지 기다린다.
