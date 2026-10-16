# 26. 오디오

모든 게임에 쓰는 코어 기능이다. 게임이 시작되면 오디오 오브젝트(`[오디오]`)가 자동으로 만들어져 씬이 바뀌어도 계속 돈다. 프리팹을 둘 필요가 없다. 게임 코드는 어디서든 `GameAudio.PlayBgm(clip)`처럼 부른다(네임스페이스 `GBible`, 어셈블리 `GBible.Unity`).

## 이 문서에서 쓰는 말

| 말 | 뜻 |
|---|---|
| 채널(`AudioChannel`) | 소리의 종류. 전체(`Master`) · 브금(`Bgm`) · 목소리(`Voice`) · 효과음(`Sfx`) · UI(`Ui`). 음량을 채널마다 따로 정한다. |
| 직렬 채널 | 한 번에 하나만 나는 채널(브금·목소리). 새 소리가 나오면 이전 소리가 페이드 아웃, 새 소리가 페이드 인(교차 페이드). |
| 병렬 채널 | 튼 만큼 동시에 나는 채널(효과음·UI). 끝난 자리는 다시 쓰고, 최대 수를 넘으면 가장 오래된 소리를 멈춘다. |
| 손잡이(`AudioHandle`) | 재생 중인 소리 하나. 멈추기·상태 확인에 쓴다. |
| 페이드 | 음량을 서서히 올리거나 내리는 것(초 단위). |

---

## 1. 채널과 동작 규칙

**한 줄 요약.** 실제 음량 = 전체 × 채널 × 소리 하나의 음량. 채널마다 음소거가 따로 있고, 플레이어가 바꾼 채널 음량은 기기에 저장된다.

| 채널 | 방식 | 기본 페이드 |
|---|---|---|
| 전체 | 모든 채널에 곱해지는 음량 | — |
| 브금 | 직렬. 같은 곡을 다시 틀면 이어서 음량만 맞춘다(`restart`면 처음부터). 반복 재생이 기본. | 1초 |
| 목소리 | 직렬. 새 목소리가 나오면 이전 목소리가 짧게 사라진다. | 0.15초 |
| 효과음 | 병렬(기본 최대 32). 위치가 있는 3D 효과음도 된다. | — |
| UI | 병렬, 음량만 따로 | — |

- 페이드와 재생은 **게임 시간과 상관없이** 흐른다(`Time.unscaledDeltaTime`). 일시정지·배속 중에도 브금은 그대로.
- 재생 여부는 자체 시계로 판단한다(오디오 장치 없는 테스트에서도 같게 동작).

---

## 2. 에디터에서

### 오디오 창

1. 메뉴 `MomoiProject → 오디오`(창 이름 "오디오"). 플레이 중이 아니어도 동작한다(에디터가 시간을 흘려 준다).
2. **미리 듣기**: **클립** 칸, **채널** 칸(전체를 고르면 효과음으로 재생) → **▶ 재생**(게임과 같은 방식: 브금·목소리는 교차 페이드, 효과음·UI는 겹쳐서), **■ 이 채널 정지**, **모두 정지**. 아래 상태 글 "브금: 이름 · 목소리: 없음 · 효과음 n개 · UI n개".
3. **채널 음량**: 채널마다 슬라이더. "플레이 중에는 플레이어 설정처럼 저장되고, 아니면 이 에디터에서만 적용됩니다. 기본값은 오디오 설정 에셋에서 정합니다."
4. 아래 **오디오 설정 만들기**(설정이 없을 때) / **오디오 설정 열기**.

### 프로젝트 창에서 바로 듣기

오디오 클립을 고르고 우클릭 → `MomoiProject → 오디오 재생`(효과음 채널) / `오디오 정지`(모두 정지).

### 오디오 설정(선택)

**오디오 설정 만들기**는 `Assets/MomoiProject/Data/Resources/MomoiProject/GameAudioSettings.asset`을 만든다(직접 만들려면 `Create → MomoiProject → 오디오 설정`, 위치는 `Resources/MomoiProject/GameAudioSettings`). 없으면 기본값으로 동작한다.

| 칸 | 뜻 | 기본 |
|---|---|---|
| master · bgm · voice · sfx · ui | 채널별 기본 음량(플레이어가 바꾼 값이 저장돼 있으면 그것) | 1 · 0.8 · 1 · 1 · 1 |
| bgmFade · voiceFade | 직렬 채널이 바뀔 때 페이드 시간(초) | 1 · 0.15 |
| maxParallel | 병렬 채널이 한꺼번에 낼 수 있는 최대 소리 수 | 32 |
| bgmGroup · voiceGroup · sfxGroup · uiGroup | 채널별 믹서 그룹(이펙트·덕킹을 믹서에서 걸 때) | 없음 |
| saveVolumes | 플레이어가 바꾼 채널 음량을 기기에 저장(PlayerPrefs) | 켬 |

---

## 3. 코드로 쓰는 법 — 문법 요소를 하나씩

### 3.1 브금 `PlayBgm` / `StopBgm`

```csharp
using GBible;

GameAudio.PlayBgm(clip);                                   // 기본 페이드(설정값), 반복
GameAudio.PlayBgm(clip, volume: 0.7f, fade: 2f);           // 2초 교차 페이드
GameAudio.PlayBgm(clip, loop: false, restart: true);       // 한 번만, 같은 곡이어도 처음부터
GameAudio.StopBgm();                                       // 기본 페이드로 멈춤
GameAudio.StopBgm(0f);                                     // 바로 멈춤
```

시그니처: `PlayBgm(AudioClip clip, float volume = 1f, float fade = -1f, bool loop = true, bool restart = false)`. `fade`가 음수면 설정값.

### 3.2 목소리 `PlayVoice` / `StopVoice`

```csharp
GameAudio.PlayVoice(voiceClip);           // 이전 목소리는 짧게 사라진다
bool speaking = GameAudio.IsVoicePlaying;
GameAudio.StopVoice();
```

### 3.3 효과음 `PlaySfx` / `PlaySfxAt` · UI `PlayUi`

```csharp
GameAudio.PlaySfx(hitClip);                          // 음량·피치는 인자로
GameAudio.PlaySfx(hitClip, volume: 0.8f, pitch: 1.1f);
GameAudio.PlaySfxAt(explosionClip, transform.position);   // 3D(공간 소리)
GameAudio.PlayUi(clickClip);
```

### 3.4 채널을 값으로 `Play(clip, channel)`

```csharp
GameAudio.Play(clip, AudioChannel.Voice);   // Bgm·Voice·Ui·그 밖(Sfx)
```

### 3.5 손잡이 `AudioHandle`

재생 함수는 `AudioHandle`을 돌려준다(클립이 `null`이면 `null`).

```csharp
var handle = GameAudio.PlaySfx(longClip);
// …
handle?.Stop(0.3f);              // 0.3초 페이드 아웃
bool on = handle != null && handle.IsPlaying;
var source = handle?.Source;     // AudioSource를 더 만질 때(3D 거리 등)
```

`Channel`, `Clip`, `IsPlaying`(같은 자리가 다른 소리에 다시 쓰이면 `false`), `Source`, `Stop(fade)`.

### 3.6 멈추기 `Stop(channel)` / `StopAll()`

```csharp
GameAudio.Stop(AudioChannel.Sfx);        // 채널의 소리 모두
GameAudio.Stop(AudioChannel.Bgm, 1f);    // 페이드 1초
GameAudio.StopAll();                     // = Stop(AudioChannel.Master)
```

### 3.7 음량·음소거

```csharp
GameAudio.SetVolume(AudioChannel.Master, 0.8f);          // 0~1, 저장(설정의 saveVolumes가 켜져 있으면)
GameAudio.SetVolume(AudioChannel.Bgm, 0.5f, save: false); // 저장하지 않고
float bgm = GameAudio.Volume(AudioChannel.Bgm);
GameAudio.SetMuted(AudioChannel.Voice, true);
bool muted = GameAudio.IsMuted(AudioChannel.Voice);
float real = GameAudio.Effective(AudioChannel.Sfx);      // 전체 × 채널(음소거면 0)
```

`GameAudio.VolumeChanged`(`Action<AudioChannel>`) — 음량·음소거가 바뀌면 쏜다. 설정 화면이 받아 슬라이더를 맞춘다.

### 3.8 상태 읽기

```csharp
AudioClip now = GameAudio.CurrentBgm;
AudioClip voice = GameAudio.CurrentVoice;
int sfxCount = GameAudio.Playing(AudioChannel.Sfx);   // 채널에서 지금 나는 소리 수(Master면 전체)
var settings = GameAudio.Settings;                    // 설정 에셋(없으면 null)
```

### 3.9 테스트·도구

`GameAudio.Tick(초)`는 시간을 직접 흘린다(오디오 오브젝트가 매 프레임 부른다. 테스트는 직접 부른다). `GameAudio.Shutdown()`은 오디오 오브젝트를 없앤다.

---

## 4. 상황별 예제

### 4.1 씬마다 브금

```csharp
using GBible;
using UnityEngine;

public sealed class SceneMusic : MonoBehaviour
{
    [SerializeField] AudioClip music;

    void Start() => GameAudio.PlayBgm(music);   // 같은 곡이면 끊기지 않고 이어진다
}
```

### 4.2 설정 화면의 음량 슬라이더

**코드 없이 — DB › 설정의 음량 칸.** 플레이어가 음량을 바꾸는 곳은 DB › 설정에 종류 `숫자(슬라이더)`(0~1), 적용 대상 `브금 음량`(또는 전체·목소리·효과음·UI 음량)인 칸으로 두고, 기본 설정 화면(`GameScreens.Settings`, 35장 2-5)이 슬라이더를 그린다. 값을 바꾸면 라이브러리가 `GameAudio.SetVolume(채널, 값, save: false)`로 바로 적용하고, 값은 PlayerPrefs가 아니라 설정 파일(`settings.json`)에 남아 다음에 켤 때 그 값으로 시작한다(`31-Slots-Settings-Input.md` 2-4·5-4). 그 채널을 다른 곳에서 `GameAudio.SetVolume`으로 바꾸면 다음 실행 때 설정 값으로 돌아가므로, 음량 설정을 둔 채널은 설정(`GameSettings.Set`)으로만 바꾼다.

설정 칸이 없는 채널을 직접 만든 슬라이더로 바꿀 때만 `GameAudio.Volume`·`SetVolume`(기기에 저장)·`VolumeChanged`(3.7)를 쓴다.

### 4.3 일시정지 메뉴에서 효과음만 끄기

```csharp
public static class PauseMenuAudio
{
    public static void Open()  { GBible.GameAudio.SetMuted(GBible.AudioChannel.Sfx, true); }
    public static void Close() { GBible.GameAudio.SetMuted(GBible.AudioChannel.Sfx, false); }
}
```

### 4.4 긴 효과음을 중간에 멈추기 (차지 소리)

```csharp
using GBible;
using UnityEngine;

public sealed class ChargeSound : MonoBehaviour
{
    [SerializeField] AudioClip chargeClip;
    AudioHandle _charge;

    public void BeginCharge() => _charge = GameAudio.PlaySfx(chargeClip);
    public void Release() => _charge?.Stop(0.2f);   // 이미 끝났으면 아무 일도 없다
}
```

효과음·UI 채널의 반복 재생은 **지원하지 않는다**(재생 함수에 반복 인자가 없고, 자리는 클립 길이가 지나면 끝난 것으로 본다). 끝없이 도는 소리는 브금 채널(`PlayBgm`, 기본 반복)을 쓰거나, 그 오브젝트에 직접 `AudioSource`를 둔다.

### 4.5 보스 등장: 브금 교체 후 대사

```csharp
GameAudio.PlayBgm(bossTheme, fade: 0.5f);
GameAudio.PlayVoice(bossLine);
```

---

## 5. 다이얼로그와의 연결

다이얼로그 시스템(`24-Dialogue.md`)은 이 오디오를 쓴다.

- '배경 음악' 효과 → 브금 채널(효과의 진행 시간 = 페이드 시간). 다이얼로그가 끝나면 다이얼로그 전의 브금으로 돌아간다(`DialogueStage.restoreMusicOnEnd`). 브금을 정하지 않은 다이얼로그는 게임 브금을 건드리지 않는다.
- '효과음' 효과 → 효과음 채널.
- 대사 노드의 '목소리' → 목소리 채널. 목소리가 없는 대사에서는 이전 목소리가 멈추고, 자동 진행은 목소리가 끝날 때까지 기다린다.

---

## 6. 주의할 점 / 자주 하는 실수

- **규칙에서 부르지 않는다**: `GameAudio`는 화면 쪽 기능이라 행동·Block·`[BindTo]` 안에서 부르지 않고 Client 코드에서 부른다(관례 1절). 행동 결과의 효과음(맞음·획득)은 행동이 끝난 뒤의 연출(`SubscribeAfterAction` → `GameView.Present`·`Play`, 32장) 안에서 튼다 — 실패한 행동·미리보기에서는 소리가 나지 않는다.
- **클립 자리**: 소리 파일은 개발자가 넣는다. AI는 DB 반영 스크립트에서 `AssetRequests.Audio(경로, 쓰는 곳, 초)`로 임시 무음 파일과 넣을 것을 만들고, 그 경로의 클립을 DB 칸에 넣어 코드가 읽게 한다(39장 3절).
- **클립이 `null`**: 재생 함수는 아무것도 하지 않고 `null`을 돌려준다. 손잡이를 쓸 때 `?.`로 받는다.
- **오래된 손잡이**: 병렬 채널은 끝난 자리를 다시 쓴다. 끝난 소리의 손잡이로 `Stop`해도 다른 소리를 멈추지 않는다(`IsPlaying`이 `false`).
- **`handle.Source`의 `clip`·`loop`·`pitch`를 바꾸지 않는다**: 자리가 끝나는 시점은 재생을 시작할 때 클립 길이·피치로 정해진다. 바꾸면 소리가 끊기거나 자리가 다른 소리에 넘어간다. 3D 거리 같은 공간 설정만 만진다.
- **음소거는 저장되지 않는다**: `SetMuted`는 기기에 남지 않는다. 게임 설정으로 남기려면 DB › 설정에 켜기/끄기 칸(적용 대상 없음)을 두고 `GameSettings.Changed`에서 `SetMuted`를 부른다(`31-Slots-Settings-Input.md` 5-3과 같은 모양). 음량(`SetVolume`)은 저장된다.
- **음량 설정과 같이 쓰기**: DB › 설정에 그 채널의 음량 칸이 있으면 게임이 켜질 때 설정 값으로 맞춰진다. 저장된 PlayerPrefs 음량은 쓰이지 않는다(4.2).
- **게임 시간과 무관**: `GameTime.Pause`로 게임을 멈춰도 소리는 계속 난다. 멈추려면 `Stop`·음소거를 쓴다.
- **에디터 미리 듣기와 플레이**: 플레이 모드에 들어가거나 나올 때, 스크립트를 다시 불러올 때 미리 듣기 오디오 오브젝트를 정리한다.
- **병렬 최대 수**: 효과음이 32개(설정)를 넘으면 가장 오래된 소리가 끊긴다. 많이 겹치는 소리는 볼륨·빈도를 조절하거나 `maxParallel`을 올린다.
