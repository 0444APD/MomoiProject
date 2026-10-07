# 직접 확인할 것 (Momoi Project)

GBible의 동작(규칙·저장·에디터 흐름·멀티의 주고받기)은 코드 흐름 점검과 오프라인 시험으로 확인했다(2026-10-06). 여기에는 **사람이 보고 들어야 알 수 있는 것**과 **Unity 에디터 밖(빌드·Steam·다른 컴퓨터)이 있어야 하는 것**만 남긴다. 항목마다 준비 → 단계 → 정상. 이상하면 항목 이름과 '이상하면'에 적힌 것을 알려 주면 된다. 확인된 항목은 지운다.

> 2026-10-04: 이 개발 프로젝트의 테스트 요소를 `MomoiProjectBackups/20261004-1851-test-cleanup/`로 옮겼다(되돌리는 법은 그 안의 README).

---

## 단계 0 — 준비

전제: `D:\Unity\Projects\UnitySystemProject`(Unity 6000.6.3f1)를 '처음 상태로 되돌리기'(설치 직후)로 돌린 뒤 시작 설정을 한 상태. 선택 기능은 입력(키 바꾸기)·Steam만 켜져 있고(다이얼로그 꺼짐), 언어는 한국어만, DB에는 플레이어 데이터 run·profile, 화면 설정 `screenSettings`(글꼴 빔), Steam(App ID 480)과 예전 스프라이트 규격 파일(`Assets/Dev/Spp/AA/NewSpritePreset.asset` · 그룹 `aa` — 에디터를 열면 틀 `aa`의 크기 줄로 옮겨진다, 1절 '스프라이트 틀' 1)뿐이다. 게임 씬은 `Assets/Dev/Scenes/SampleScene.unity` 하나, 테스트 씬은 아직 없다. 0-1~0-5를 먼저 하고, 빌드가 필요한 항목은 0-6.

### 0-1. 자주 쓰는 창
- **DB**: 메뉴 `MomoiProject › DB`(창 이름 'DB'). 왼쪽 사이드바 그룹(시작 파일 · 요소 · 개발 · DEV · 선택 기능 그룹 — 'DB' 그룹은 그 종류의 항목이 생겨야 보인다)에서 탭을 고르고, 항목 줄을 누르면 '상세' 창에 열린다. 칸 이름표는 코드 변수 이름(`font`)이고 마우스를 올리면 한글 이름(글꼴)이 보인다. 바꾼 것은 사이드바 아래 '저장  Ctrl+S'를 눌러야 에셋에 적용된다. 사이드바 맨 아래에 '검증 통과' / '경고 N건' / '검증 오류 N건 · 빌드가 막힙니다'.
- **구조 편집**: 로직 쪽 항목(설정·플레이어 데이터 …)은 개발자가 더할 수 없게 잠겨 있다(다이얼로그 그래프처럼 개발자가 만드는 데이터는 잠기지 않는다). 이 점검에서는 메뉴 `MomoiProject › 개발 › 구조 편집 열기(라이브러리 개발용)`로 켠다(메뉴에 체크 표시, 이 컴퓨터에만). 켜면 그 탭 오른쪽 위에 '+ 새로 만들기'가 생긴다.
- **모니터**: 메뉴 `MomoiProject › 모니터`(창 이름 '모니터'). 플레이 중 왼쪽 탭 타임라인·데이터·도전과제·로그·이벤트·행동·명령. '명령' 탭에 테스트 명령 버튼이 묶음별로 있다(테스트 패널과 같다). 멀티면 위쪽에서 세션(서버 · 클라이언트 · …)을 고른다.
- **Console**: `Window › General › Console`. 항목마다 시작 전에 Clear.
- **Game 창 크기**: Game 창 위쪽 해상도 목록에서 1920x1080(없으면 목록 아래 '+' → Fixed Resolution 1920 × 1080).
- **시험 파일 폴더**: 아래에서 탐색기로 파일을 넣는 `Assets\Dev\…` 폴더(Checks·Fonts·Audio·Art\Check)는 없으면 만든다(Dev 안의 폴더는 DB 'DEV' 탭이 된다).

### 0-2. 테스트 씬·테스트 패널
1. `Assets/Dev/Scenes/SampleScene.unity`를 연다.
2. 메뉴 `MomoiProject › 테스트 › 지금 씬을 복사해 테스트 씬 만들기` → 창 '…Test.unity를 만들었습니다…' → '확인'.
3. 메뉴 `MomoiProject › 테스트 › 테스트 씬 열기`.
4. Play → Game 창 왼쪽 위를 본다. Game 창을 한 번 누른 뒤 F1을 두 번(접기·펴기).
- 정상: 왼쪽 위에 '테스트 패널'이 펼쳐져 있다(맨 위 "슬롯 'test' · 시각 N초" · '접기', 그 아래 시간(×0.5 ×1 ×2 ×4 · '멈춤') · 'Undo (N)' · '새 판' · 멀티 · 리플레이 · 테스트 상황 · 테스트 명령 묶음(세션·저장·시간·Steam …), 길면 스크롤). F1(또는 '접기')로 접으면 '테스트(F1)' 버튼만 남고, 다시 F1(또는 그 버튼)로 편다. 아래 항목의 '패널'은 이 창이다. 멀티 설정은 Hierarchy '[테스트]' › 테스트 씬 컴포넌트(Multiplayer · Latency · Port · Address · Player Name …).
- 이상하면: Console 첫 줄.

### 0-3. 점검용 스크립트
GBible에는 고르기 창·슬롯 화면·연출·씬 전환 페이드·키 바꾸기 화면·위치 맞추기 물체 움직이기·Steam 방을 여는 버튼이 없다(게임 코드가 부르는 것이다). 그래서 버튼을 더하는 파일 하나를 넣는다.
1. 탐색기로 `D:\Unity\Projects\UnitySystemProject\Assets\Dev\Checks` 폴더를 만든다.
2. 메모장에 부록 A의 코드를 통째로 붙여 넣고 그 폴더에 `CheckCommands.cs`로 저장한다(파일 형식 '모든 파일', 인코딩 UTF-8). asmdef가 없는 폴더라 참조를 손대지 않아도 된다.
3. Unity로 돌아가 컴파일이 끝날 때까지 기다린다.
- 정상: Console에 오류가 없고, 테스트 씬 Play → 테스트 패널 아래쪽(모니터 › 명령에도) '점검' 묶음: 고르기 창 · 타격 ×3 · 실패 알림 · 슬롯 화면 · 연출 속도 1↔3 · 연출 건너뛰기 · 마지막 기록 · 씬 바꾸기 · 연출 버리기 · 전환 오류 켬↔끔 · 방 만들기 · 친구 초대 · 키 바꾸기(Jump) · 지금 키(Jump) · 키 되돌리기.
- 이상하면: Console 첫 오류 줄.

### 0-4. 점검용 설정 항목(DB › 요소 › 설정)
설정 화면의 토글·슬라이더·드롭다운과 언어·음량·전체 화면·해상도 점검에 쓴다.
1. 구조 편집을 켠다(0-1).
2. DB › 요소 › 설정 → '+ 새로 만들기' → ID 입력 → '만들기' → 상세 창에서 아래 표대로 칸을 고친다(칸을 눌러 고르거나 입력). 네 개를 만든다.

   | ID | `category` | `spec` › `kind` | 값 | `target` |
   |---|---|---|---|---|
   | `language` | 화면 | 선택지 | `choices` 비움 | 언어 |
   | `masterVolume` | 소리 | 숫자(슬라이더) | `defaultNumber` 1 · `min` 0 · `max` 1 · `step` 0 | 전체 음량 |
   | `fullscreen` | 화면 | 켜기/끄기 | `defaultToggle` 끔 | 전체 화면 |
   | `resolution` | 화면 | 선택지 | `choices` 비움 · `defaultChoice` 1280x720 | 해상도 |

3. 사이드바 아래 '저장  Ctrl+S'.
- 정상: 사이드바 맨 아래에 '검증 오류'가 없다(경고는 있어도 된다).
- 이상하면: 상세 창 위의 빨간 글.

### 0-5. Esc로 일시정지 메뉴
1. DB › 개발 › 화면 설정 → `screenSettings` → `pauseMenu`(Esc로 일시정지 메뉴) 켬 → '저장  Ctrl+S'.
- 정상: 테스트 씬 Play → Game 창을 누르고 Esc → 일시정지 메뉴가 뜬다.
- 이상하면: 메뉴가 안 뜨면 Game 창을 눌렀는지, Console 첫 줄.

### 0-6. 빌드(2절과 두 프로그램 항목)
1. `Edit › Project Settings › Player › Resolution and Presentation › Run In Background` 켬(한 컴퓨터에서 두 프로그램을 돌릴 때 뒤에 있는 쪽이 멈추지 않게).
2. 실시간 위치 점검을 할 거면 먼저 그 물체를 테스트 씬에 넣고 저장한다(1절 '실시간 위치' 1).
3. `File › Build Profiles` → Windows → Scene List 맨 위에 Test.unity(없으면 테스트 씬을 연 채 'Add Open Scenes' 뒤 맨 위로 끈다).
4. Development Build 켬 → Build → 빈 폴더(예: `D:\Builds\Check`).
5. 켜기: 명령 프롬프트에서 그 폴더로 가서(`cd /d D:\Builds\Check`) `UnitySystemProject.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 (인자…)`. 아래 항목의 명령줄에는 이 창 모드 인자를 빼고 적었다(늘 붙여서 켠다).
- 정상: 창 모드로 테스트 씬이 뜨고 왼쪽 위에 테스트 패널이 펼쳐져 있다(개발 빌드에서도 F1로 접고 편다). Steam을 쓰는 항목도 exe 옆에 steam_appid.txt를 두지 않는다 — 개발 빌드는 DB › 개발 › Steam의 App ID로 붙는다.
- 이상하면: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\UnitySystemProject\Player.log`의 첫 오류.

---

## 1. 눈·귀로 볼 것 (에디터)

### 게임 화면 — 기본 화면 모양·크기
준비: 0-2·0-3·0-4·0-5, Game 창 1920x1080.
1. 테스트 씬 Play → Game 창을 누르고 Esc.
2. 일시정지 메뉴(제목 '일시정지', 버튼 '계속하기'·'설정'·'게임 종료')에서 '설정'.
3. 설정 화면(제목 '설정', 분류 '화면'에 language·fullscreen·resolution, '소리'에 masterVolume 슬라이더와 % 값, 아래 '기본값으로'·'닫기')을 본다.
4. '기본값으로' → 확인 팝업('모든 설정을 기본값으로 되돌릴까요?', '아니오'·'예') → '아니오' → '닫기' → '계속하기'.
5. 테스트 패널 › 점검 › '슬롯 화면' → 슬롯 화면(제목 '슬롯', 슬롯마다 한 줄, '닫기') → '닫기'.
6. 점검 › '고르기 창' → 고르기 창(제목 '점검: 하나 고르기', 버튼 가·나·다) → '나'.
7. 점검 › '실패 알림'.
8. Game 창 해상도를 1280x720(또는 다른 비율)으로 바꿔 1~7을 한 번 더.
- 정상: 팝업(확인·고르기 창) 뒤는 어둡게 덮이고 창(설정·슬롯·일시정지)은 가운데 판으로 뜬다. 7은 화면 아래에 '점검용 실패 알림입니다.'가 2.5초 떴다 사라진다. 글·버튼이 잘리거나 겹치지 않고, 해상도를 바꿔도 같은 비율로 커지고 줄어든다.
- 이상하면: 어느 화면·해상도인지와 스크린숏, Console 첫 줄.

### 게임 화면 — 고르기 창이 빈 창으로 깜빡이지 않는다
준비: 0-2·0-3, 모니터 창.
1. 테스트 씬 Play.
2. Unity 위쪽 툴바의 일시정지(⏸, Ctrl+Shift+P).
3. 모니터 › 명령 › 점검 › '고르기 창'.
4. 툴바의 한 프레임 진행(⏭, Ctrl+Alt+P)을 한 번 누르고 Game 창을 본다.
5. 일시정지를 풀고 '나'로 닫는다. 2~5를 서너 번.
- 정상: 4의 한 프레임에 이미 제목과 가·나·다 버튼이 다 그려져 있다(빈 판만 있는 프레임이 없다).
- 이상하면: 빈 판이 보였는지, 몇 번째 프레임에서 채워졌는지.

### 게임 화면 — 언어를 바꾸면 열린 기본 화면 글자도 바로 바뀐다
준비: 0-2·0-4·0-5.
1. 메뉴 `MomoiProject › 시작 설정` → 언어에서 'English (en)' 체크 → '적용'(→ `Assets/MomoiProject/Data/Texts/Texts_en.asset`).
2. Project 창에서 `Texts_en`을 골라 Inspector › Entries에 여섯 줄(Key · Text)을 더하고 Ctrl+S: `momoi.ui.settings` Settings · `momoi.ui.resetAll` Reset All · `momoi.ui.close` Close · `momoi.ui.pause` Pause · `momoi.ui.resume` Resume · `momoi.ui.quit` Quit.
3. 테스트 씬 Play → Esc → '설정' → language 드롭다운에서 'English'.
4. 'Close'.
5. 다시 'Settings' → language를 '한국어'로(언어는 설정 파일에 남으므로 한국어로 끝낸다).
- 정상: 3에서 고르는 순간 열린 설정 화면이 제목 'Settings', 버튼 'Reset All'·'Close'로 바뀐다. 4 뒤 일시정지 메뉴는 'Pause'·'Resume'·'Settings'·'Quit'. 5에서 다시 '설정'·'일시정지' 한국어.
- 이상하면: 안 바뀐 글자와 그 화면(열려 있던 것인지 새로 연 것인지).

### 게임 화면 — 슬롯 화면의 플레이 시간·저장한 때
준비: 0-2·0-3.
1. 테스트 씬 Play → 패널 › 점검 › '슬롯 화면'. 첫 줄이 지금 슬롯 'test'(주황 버튼)다 — 처음이면 'test — 빈 슬롯'. 다른 줄은 누르지 않는다(그 슬롯으로 바뀐다).
2. '닫기' → 패널 › 저장 › '판 저장'.
3. '슬롯 화면' → 첫 줄을 적어 둔다(예: 'test —  (0:12) 2026-10-07 14:03') → '닫기'.
4. 1분쯤 기다렸다가 '슬롯 화면'.
- 정상: 4의 플레이 시간이 다시 저장하지 않았는데도 3보다 기다린 만큼 늘었고, 저장한 때(날짜·시각)는 3과 같다. 지금 슬롯 줄에는 '지우기'가 없다.
- 이상하면: 3·4의 줄 글자 그대로.

### 한글 글꼴 — DB 경고와 화면
준비: 0-2·0-5.
1. DB › 개발 › 화면 설정 → `screenSettings`.
2. 상세 창 위의 노란 경고를 본다.
3. (비교) 테스트 씬 Play → Esc → 일시정지 메뉴의 한글을 본다 → Play 멈춤.
4. 탐색기로 `C:\Windows\Fonts\malgun.ttf`(맑은 고딕)를 `Assets\Dev\Fonts`에 복사한다(시험용 — 출시에는 배포 가능한 글꼴).
5. 상세 창 `font` 칸에 Project 창의 malgun을 끌어 놓는다(또는 칸 오른쪽 동그라미로 고른다) → '저장  Ctrl+S'.
6. Play → Esc → '설정' → 점검 › '고르기 창'·'실패 알림'.
- 정상: 2에 '글꼴이 비어 있습니다. 한글이 든 글꼴(TTF·OTF)을 넣지 않으면 기기에 따라 한글이 빈 네모로 보입니다.'가 있고 5 뒤 사라진다(사이드바 경고 수가 하나 준다). 3은 □이거나 기본 글꼴, 6은 모든 화면(기본 화면·알림)이 맑은 고딕.
- 이상하면: 남은 경고 글, □로 남은 화면.

### 연출 체감 — 차례·속도·건너뛰기
준비: 0-2·0-3.
1. 테스트 씬 Play → 패널 › 점검 › '타격 ×3'.
2. '연출 속도 1↔3'(3배) → '타격 ×3' → 다시 '연출 속도 1↔3'(1배).
3. '타격 ×3'을 누르고 바로 '연출 건너뛰기' → 이어서 '타격 ×3'.
4. 패널 시간 '×4' → '타격 ×3' → '×1'.
- 정상: 1은 노란 숫자 10 → 20 → 30이 하나씩(앞이 끝나야 다음) 1초씩 떠오른다. 2는 세 배 빠르다. 3은 남은 숫자가 순식간에 끝나고 다음 '타격 ×3'은 보통 속도. 4는 게임 배속만큼 빠르다.
- 이상하면: 숫자가 겹쳐 뜨거나 속도·건너뛰기가 안 먹은 단계.

### 연출 체감 — 씬 흐름 페이드 동안 새 씬 값
준비: 0-3, 구조 편집 켬.
1. DB › 요소 › 플레이어 데이터 → '+ 새로 만들기' → ID `scene` → '만들기' → 상세 `valueType`을 눌러 '글자 (string)' → '저장  Ctrl+S'.
2. Project 창에서 SampleScene을 고르고 Ctrl+D → 이름 `CheckB` → 열어서 `GameObject › 2D Object › Sprites › Square`(Scale 10·10·1, Color 빨강) → Ctrl+S.
3. `File › Build Profiles` › Scene List에 SampleScene·CheckB가 켜져 있게 한다(없으면 그 씬을 연 채 'Add Open Scenes').
4. DB › 시작 파일 › Bootstrap › 씬 흐름(선택): '플레이어 데이터' = scene, '필드(씬 이름이 든 글자)' 비움, '빈 값일 때 씬' = (그대로)(바로 저장된다).
5. SampleScene을 열고 Play(테스트 씬은 씬을 바꾸지 않는다) → 모니터 › 명령 › 점검 › '씬 바꾸기'.
6. 다시 '씬 바꾸기'.
7. (페이드 중 버리기) '씬 바꾸기' → 검게 덮이는 동안(누른 뒤 0.5초 안) 점검 › '연출 버리기'.
8. (전환 연출 오류) 점검 › '전환 오류 켬↔끔'(알림 '전환 오류: 켬') → '씬 바꾸기' → 다시 '전환 오류 켬↔끔'(끔).
- 정상: 0.5초 동안 검게 덮이고 CheckB(빨간 판)가 열린 뒤 2초 동안 밝아진다. 밝아지기 시작할 때부터 오른쪽 위에 '새 씬 CheckB · scene = CheckB'가 보인다(다 밝아진 뒤에야 글이 생기면 이상). 6은 SampleScene으로 같은 모양. 7은 버려도 페이드가 6과 똑같이 끝까지 간다(검게 → 씬 열림 → 밝아짐 → 판이 사라짐) — 어두운 채 멈추거나 씬이 두 번 바뀌면 이상. 8은 Console에 빨간 '점검: 전환 연출 오류'와 노란 '[MomoiProject] 전환 연출(GameScenes.Transition)이 씬 '…'을(를) 열지 않고 끝나 전환 없이 엽니다(…)'가 뜨고, 검게 덮이지 않고 바로 씬이 바뀌며 '새 씬 …' 글이 보인다.
- 이상하면: 글이 비어 있던 때, 모니터 타임라인·Console 경고('씬 '…'을(를) 열 수 없습니다…'면 3을 다시), 7·8에서 화면이 멈춘 모양(검은 판이 남았는지·씬이 그대로인지).

### 소리 — 소리·브금 페이드·음량 슬라이더
준비: 0-2·0-4·0-5.
1. 탐색기로 `C:\Windows\Media\Alarm01.wav`·`Alarm02.wav`·`Windows Ding.wav`를 `Assets\Dev\Audio`에 복사한다.
2. 테스트 씬 Play → 메뉴 `MomoiProject › 오디오`(창 '오디오') → 클립 = Alarm01, 채널 = 브금 → '▶ 재생'.
3. 클립 = Alarm02 → '▶ 재생'.
4. 클립 = Windows Ding, 채널 = 효과음 → '▶ 재생'을 빠르게 여러 번.
5. Game 창을 누르고 Esc → '설정' → masterVolume 슬라이더를 천천히 끈다 → 50%쯤에서 놓고 '닫기'.
6. Play 멈춤 → 다시 Play → 2를 다시. 끝나면 100%로 돌린다(오디오 창의 '채널 음량' 막대는 설정과 따로 노니 쓰지 않는다).
- 정상: 2는 소리가 되풀이되고 창 아래 '브금: Alarm01'. 3은 1초 동안 Alarm01이 작아지며 Alarm02가 커진다(끊김 없이). 4는 효과음이 겹쳐 나고 브금은 계속. 5는 끄는 동안 크기가 바로 따라 바뀌고 %가 바뀐다(놓을 때 한 번에 바뀌지 않음). 6은 50% 크기로 시작.
- 이상하면: 단계, 소리가 끊긴 때, Console 경고.

### 다이얼로그 — 준비(한 번)
준비: 0-2. 구조 편집은 켜지 않는다(다이얼로그 그래프는 개발자가 짜는 데이터라 선택지 줄의 '+ 추가'가 늘 보여야 한다).
1. DB › 사이드바 '다이얼로그 시스템' › '기능 활성화' → '켜기' → 다시 컴파일되면 '스탠딩 프로필'·'장소' 탭과 메뉴 `MomoiProject › 다이얼로그 시스템`이 생긴다.
2. 메뉴 `MomoiProject › 다이얼로그 시스템 › UI 템플릿 만들기`(→ `Assets/MomoiProject/Data/DialogueUI/DialogueSystem.prefab` 등).
3. `Assets\Dev\Art\Check`에 그림을 넣는다: 배경 1장(예: `C:\Windows\Web\Wallpaper\Windows\img0.jpg`), 캐릭터 1장, 이미지 창 1장, 게임 화면용 1장, 창 모드용 같은 크기 PNG 2장(예: 400×500 — ① 테두리만 있고 가운데 투명 ② 꽉 찬 흰 사각형). 2D 프로젝트라 스프라이트로 들어온다.
4. DB › 다이얼로그 시스템 › 스탠딩 프로필 → '+ 새로 만들기' → ID `hero` → `expressions` 첫 줄의 `sprite` = 캐릭터 그림.
5. DB › 다이얼로그 시스템 › 장소 → '+ 새로 만들기' → ID `office` → `background` = 배경 그림 → '저장  Ctrl+S'. (이름을 보이려면 `Texts_ko` Entries에 `hero.name` 주인공 · `office.name` 사무실 — 없으면 ID가 보인다.)
6. 메뉴 `MomoiProject › 다이얼로그 시스템 › 그래프`(창 '다이얼로그 그래프') → 왼쪽 '+ 새로' → id `check` → '만들기'(시작 → '첫 대사'가 이어져 있다).
7. 그래프를 짠다. 점을 빈 곳으로 끌어 놓으면 노드 목록(노드 검색)이 뜨고, 고르면 이어진 채로 만들어진다. 칸은 노드 안에서 고친다.
   - '첫 대사' `text` = 게임 화면 위 글상자가 흐려 보이는지 봅니다. (화자 없음 = 해설)
   - '첫 대사'의 '다음' → '대사' ②: `speaker` = hero, `text` = 장소와 스탠딩. ②의 '진입' → '배경'(`location` = office), ②의 '진입' → '스탠딩 등장'.
   - ②의 '다음' → '대사' ③: `speaker` = hero, `text` = 창 모드. ③의 '진입' → '창 바꾸기'(`frame` = ①, `frameMask` = ②).
   - ③의 '다음' → '대사' ④: `speaker` = hero, `text` = 이미지 창과 선택지, `options` '+ 추가' 두 번(`text` = 가 · 나). 구조 편집을 켜지 않은 채로 '+ 추가'가 보여야 한다. ④의 '진입' → '이미지 표시'(`sprite` = 이미지 창 그림). 선택지 점은 잇지 않는다(끝으로 친다).
8. 테스트 씬을 열고 게임 화면용 그림을 Hierarchy에 끌어 놓아 화면 아래쪽까지 덮게 Scale을 키우고 Sprite Renderer › Order in Layer = -200(다이얼로그 배경보다 뒤). 씬은 저장하지 않아도 된다.
- 정상: 그래프 창 머리에 노란 '⚠' 글이 없다. 7의 `options` '+ 추가'가 구조 편집 없이 보이고 눌린다.
- 이상하면: 멈춘 단계, 그래프 머리의 '⚠' 글.

### 다이얼로그 — 미리보기 막대(재생·시간 이동)
준비: 다이얼로그 준비.
1. 그래프 창에서 노드를 고르지 않고 '▶ 선택한 노드부터'(플레이 중이 아니면 저절로 Play).
2. '⏸' → '▶'.
3. '다음 ▷'로 ②까지 넘긴다.
4. 시간 막대를 뒤로 끌었다 앞으로 끈다.
5. '자동' → '선택지까지 ⏭' → '■'.
6. ③ 노드를 눌러 고르고 '▶ 선택한 노드부터'.
- 정상: 1은 첫 대사부터 재생, 막대 옆에 '1.25초 · 타이핑'처럼 시간·상태. 2는 멈추고(버튼 '▶', '· 일시정지') 다시 간다. 4는 끈 시점의 화면(배경·스탠딩·글)이 되고 일시정지. 5는 '자동 ✓'로 줄이 스스로 넘어가고, ④ 선택지에서 멈추고, '■'로 끝난다. 6은 ③부터.
- 이상하면: 버튼 이름과 상태 글, 막대의 초.

### 다이얼로그 — 글상자 블러
준비: 다이얼로그 준비(8의 그림까지).
1. 테스트 씬에서 '▶ 선택한 노드부터'.
2. 첫 대사(배경 효과 없음)에서 글상자를 본다.
3. '다음 ▷'로 ②(배경 office가 깔림).
- 정상: 글상자(아래쪽 둥근 반투명 판) 뒤로 보이는 게임 그림이 판 안쪽만 흐리다. ②에서도 배경 그림이 판 뒤에서 흐리다.
- 이상하면: 판이 반투명할 뿐 뒤가 또렷한지(게임 그림 / 배경 그림 어느 쪽).

### 다이얼로그 — 모션
준비: 다이얼로그 준비.
1. '▶ 선택한 노드부터' → 다이얼로그가 열리는 순간을 본다.
2. 글이 다 나올 때까지 기다린다.
3. ④까지 넘겨 선택지를 띄우고 '가'를 누른다.
- 정상: 1은 글상자가 0.25초 동안 아래에서 올라오며 나타난다. 2는 오른쪽 아래 꺾쇠 표시가 깜빡이며 살짝 움직인다. 3은 선택지가 아래에서 올라오며 나타나고, 고른 선택지가 살짝 커졌다 돌아온다.
- 이상하면: 안 움직인 것.

### 다이얼로그 — 장소 표시
준비: 다이얼로그 준비.
1. '▶ 선택한 노드부터' → 첫 대사를 본다 → '다음 ▷'로 ②.
- 정상: 첫 대사에는 없다가 ②에서 배경이 office 그림으로 바뀌고 왼쪽 위에 장소 이름 상자(사무실, 이름 키가 없으면 office)가 뜬다.
- 이상하면: 안 뜬 것, 배경만 바뀌고 이름이 없는지.

### 다이얼로그 — 창 모드 스탠딩
준비: 다이얼로그 준비.
1. '▶ 선택한 노드부터' → '다음 ▷'로 ③.
- 정상: hero 스탠딩이 창 모양(꽉 찬 사각형) 안쪽만 보이고 테두리가 그 위에 그려진다. 창 밖으로 나간 캐릭터 부분은 안 보인다.
- 이상하면: 테두리만 보이는지·캐릭터가 통째로 보이는지, Console '[다이얼로그] … 창 모드를 지원하지 않습니다'.

### 다이얼로그 — 이미지 창
준비: 다이얼로그 준비.
1. '▶ 선택한 노드부터' → '다음 ▷'로 ④.
- 정상: 그림이 화면 가운데(조금 위)에 화면 높이 60% 크기로, 스탠딩보다 앞에 나타난다.
- 이상하면: 안 뜨거나 스탠딩 뒤에 깔리는지.

### 다이얼로그 — 로그 창
준비: 다이얼로그 준비.
1. ④까지 넘긴 뒤 글상자 오른쪽 위 '로그'를 누른다 → '✕'.
2. 글상자 위에서 마우스 휠을 위로 굴린다 → '✕'.
- 정상: '대사 로그' 창에 지난 줄(이름·글, 해설은 기울임)이 위에서 아래로 쌓여 있고, 보는 동안 다이얼로그가 멈춘다. '✕'로 닫힌다. 2도 같은 창이 열린다.
- 이상하면: 안 열리거나 빠진 줄.

### 다이얼로그 — 한글 폰트
준비: 다이얼로그 준비, `Assets/Dev/Fonts/malgun.ttf`('한글 글꼴' 4).
1. (비교) '▶ 선택한 노드부터' → 글이 □이거나 기본 글꼴인지 본다 → Play 멈춤.
2. Project 창 `Assets/MomoiProject/Data/DialogueUI/DialogueSystem.prefab` 더블클릭 → Hierarchy 'UI' → Inspector › Dialogue UI › Font = malgun → Ctrl+S → 프리팹 편집을 나간다.
3. 다시 재생 → 로그·선택지까지 본다.
- 정상: 2 뒤 Inspector 아래 '폰트가 비어 있습니다…' 안내가 사라지고, 3은 이름·대사·로그·선택지가 맑은 고딕.
- 이상하면: □로 남은 곳.

### 다이얼로그 — 그래프 창 마우스 조작
준비: 다이얼로그 준비.
1. 빈 곳 우클릭 → 노드 검색에 '그룹' → '그룹 상자' → 오른쪽 아래 모서리를 끌어 크기를 바꾼다.
2. 노드 두세 개를 상자 안으로 옮기고 상자 머리를 끈다.
3. 빈 곳을 끌어 사각형으로 노드 여럿을 고른다 → Shift를 누른 채 다른 곳을 끌어 더 고른다 → 고른 것을 함께 끈다.
4. Ctrl+Z를 몇 번.
- 정상: 1은 끄는 대로 커지고 줄며 200×120 밑으로는 안 준다. 2는 안의 노드가 함께 움직인다. 3은 끄는 동안 선택 사각형이 보이고 놓으면 겹친 노드(그룹 상자는 다 들어온 것)가 골라진다. 4는 한 단계씩 되돌아간다.
- 이상하면: 안 먹은 조작.

### 스프라이트 틀 — 크기 표·한꺼번에 굽기·예외 촬영
준비: 0-1·0-3(부록 B의 `CheckSeeds.cs`까지 넣는다). 그림 세 장을 `Assets\Dev\Art\Check`에 넣는다(예: `C:\Windows\Web\Wallpaper\Windows\img0.jpg`와 `C:\Windows\Web\Screen` 안의 그림 둘 — 2D 프로젝트라 스프라이트로 들어온다).
1. (예전 규격 옮기기) Unity를 열고 컴파일이 끝나면 Console을 본다 → Project 창에서 `Assets/MomoiProject/Data/SpriteGroup/aa`를 고른다.
2. (새 틀) Project 창 `Assets/Dev/Art/Check`에서 우클릭 › Create › MomoiProject › 스프라이트 틀 → 이름 `cardFrame` → Enter → Inspector를 본다.
3. (크기 표) '+ 크기 더하기'를 두 번 → 첫 줄: 이름 칸에 `cardFront`, 가로 600, 세로 900(칸마다 Enter) / 둘째 줄: 이름 `icon`, 오른쪽 '▾' › 128×128.
4. (잘못된 이름) '+ 크기 더하기' → 새 줄 이름을 `icon`으로 → Enter → 빨간 글을 본다 → 그 줄 '✕'.
5. (한 장 맞추기) Project 창의 img0을 Inspector '원본 넣기' 칸에 끌어 놓는다 → '스프라이트 맞춤 · img0' 창에서 캔버스를 눌러 중심을 옮기고 휠을 굴린다.
6. (예외 촬영) 창 위쪽 'icon' 단추 → 캔버스의 다른 곳을 누르고 휠 → '공통' 단추 → 캔버스를 한 번 더 누른다 → '적용 (모든 크기 굽기)'.
7. (구운 것 보기) Project 창 `Assets/MomoiProject/Data/Sprites/cardFrame/`의 `cardFront`·`icon` 폴더에서 png를 하나씩 골라 Inspector 아래 미리보기와 크기를 본다.
8. (예외 해제) 틀 Inspector '이 틀로 자른 원본' img0 줄의 '맞추기' → 'icon' → '예외 해제' → '적용 (모든 크기 굽기)'.
9. (여럿 한꺼번에) 나머지 그림 둘을 Project 창에서 Ctrl로 함께 골라 '원본 넣기'에 끌어 놓는다.
10. (크기 바꾸면 다시 굽기) 표의 cardFront 세로를 800으로 → Enter. 이어서 img0 줄 '맞추기' → 'icon'을 다른 곳으로(예외) → '적용' → 표의 icon을 '▾' › 64×64.
11. (크기 이름 바꾸기·빼기) icon 이름을 `thumb`로 → Enter → thumb 줄 '✕' → 확인 창 '빼기' → Ctrl+Z.
12. (필드에 연결된 칸) Ctrl+S(부록 B가 만든 적용 전 항목 적용) → DB › 요소 › 클래스 → CheckCard → 클래스 상세의 `art` 줄을 본다 → DB 사이드바 'DB' › 인스턴스 › `checkCard` → 상세의 `art` 칸에 img0을 끌어 놓는다 → 열린 맞춤 창에서 '적용 (모든 크기 굽기)'.
13. (모두 다시 굽기) 메뉴 `MomoiProject › 스프라이트 틀 모두 다시 굽기`.
- 정상:
  - 1: Console에 '[MomoiProject] 예전 스프라이트 규격 파일 1개를 틀의 크기 줄로 옮겼습니다(원래 파일은 MomoiProjectBackups/…-sprites)'가 한 번. aa의 Inspector는 '스프라이트 틀' 화면이고 크기 표에 예전 규격 이름 줄 하나. `Assets/Dev/Spp/AA/NewSpritePreset.asset`은 없고 그 백업 폴더에 있다.
  - 2·3: 맨 위 '스프라이트 틀', ID cardFrame. 칸을 벗어나면(Enter) 바로 저장되고, 표 아래 비율 상자(cardFront 세로로 긴 상자 · icon 정사각형)가 생긴다.
  - 4: "크기 이름 'icon'이(가) 겹칩니다."가 빨갛게 뜨고, '✕' 뒤 사라진다.
  - 5: 오른쪽 미리보기 두 장(cardFront·icon)이 누르고 굴리는 대로 함께 바뀐다. 캔버스 위 색 테두리 두 개가 자를 곳.
  - 6: 'icon' 상태에서는 icon 미리보기만 바뀌고 이름 옆에 '· 예외'. '공통'에서 누르면 cardFront만 바뀌고 icon은 그대로. '적용' 뒤 Console "'img0'을(를) 틀 cardFrame의 크기 2개로 구웠습니다." 틀 Inspector에 '이 틀로 자른 원본 1개' · img0 줄 '예외 1'.
  - 7: cardFront png는 600×900, icon png는 128×128이고 icon은 6에서 따로 잡은 곳이 잘려 있다.
  - 8: img0 줄의 '예외' 표시가 없어지고 icon png가 공통 맞춤으로 다시 구워진다.
  - 9: 맞춤 창 없이 Console '원본 2개를 틀 'cardFrame'의 모든 크기로 구웠습니다(이미지 4개)…', 목록이 3줄.
  - 10: Console "틀 'cardFrame'의 바뀐 크기로 3개를 다시 구웠습니다."(cardFront png가 600×800). icon을 바꾸면 노란 경고 '크기가 바뀌어 예외 촬영을 확인해야 합니다…'와 img0 줄의 '확인 필요' — '맞추기'에서 icon을 다시 누르고 '적용'하면 없어진다.
  - 11: 이름을 바꿔도 img0 줄의 '예외'가 남고 png가 `…/thumb/`로 다시 구워진다. '빼기' 뒤 thumb png는 `MomoiProjectBackups/<시각>-sprites/` 아래로 옮겨진다. Ctrl+Z로 thumb 줄이 돌아오고 다시 구워진다.
  - 12: 클래스 상세 `art` 줄에 '스프라이트 틀 cardFrame' · '미리보기 크기 cardFront'. 끌어 놓는 순간 맞춤 창이 열리고, 적용 뒤 상세 칸의 그림은 cardFront로 구운 모양. 틀 Inspector '쓰는 필드: CheckCard.art'.
  - 13: 모든 줄이 지금 맞춤 그대로 다시 구워지고 Console에 오류가 없다.
- 이상하면: 단계 번호, Console 줄, 구운 png의 크기·잘린 곳.

### 키 바꾸기
준비: 0-2·0-3(선택 기능 '입력(키 바꾸기)'은 켜져 있고, 스크립트가 Project-wide Actions로 `GameInput.Use`를 부른다).
1. 테스트 씬 Play → 패널 › 점검 › '지금 키(Jump)'.
2. '키 바꾸기(Jump)' → 마우스는 누르지 말고 키보드 J.
3. '키 바꾸기(Jump)' → E(같은 Player 맵의 Interact 키) → '지금 키(Jump)'.
4. '키 바꾸기(Jump)' → Esc → 알림을 본 뒤 Game 창을 누르고 Esc를 한 번 더 → 뜬 메뉴에서 '계속하기'.
5. (패드가 있으면) '키 바꾸기(Jump)' → 패드 버튼 하나.
6. Play 멈춤 → 다시 Play → '지금 키(Jump)'.
7. '키 되돌리기' → '지금 키(Jump)'.
- 정상: 1 'Jump = Space'. 2 '바뀜: Jump = J'. 3 "겹침: 'Interact'에서 이미 쓰는 키입니다"(겹침 표시) 뒤 Jump는 J 그대로. 4 '취소했습니다'만 뜨고 일시정지 메뉴는 열리지 않는다(키 바꾸기가 Esc를 취소로 쓰는 동안 화면의 뒤로는 막힌다). 바꾸기가 끝난 뒤 Esc는 다시 일시정지 메뉴를 연다. 5 패드 버튼 이름. 6 바꾼 키가 남아 있다. 7 'Jump = Space'.
- 이상하면: 단계와 알림 글, 4에서 일시정지 메뉴가 같이 열렸는지.

### 멀티 체감 — 시험 모드 지연
준비: 0-2·0-3, 모니터 창.
1. 테스트 씬 Play → 패널 › 멀티 › '시험 모드'.
2. '지연' 막대를 끌어 '지연 400ms'.
3. 점검 › '타격 ×3'.
4. 점검 › '고르기 창' → 뜨면 '가'.
5. 모니터 위쪽에서 '서버'·'클라이언트 · (이름)'을 번갈아 골라 타임라인을 본다.
6. '나가기(혼자 하기)'.
- 정상: 1 뒤 패널에 '시험 모드 · 내 번호 #1 · 핑 …ms', 명단, '지연'·'흔들림' 막대. 3은 숫자가 0.8초쯤 늦게 오지만 한 번씩 차례로. 4는 창이 늦게 뜨고, '가' 뒤 0.8초쯤 그대로 있다가 닫힌다(다시 열리거나 빈 창이 되지 않는다 — 닫히기 전에 또 누르면 '이미 답했거나 취소된 선택입니다.' 알림은 지연 때문). 5는 서버에 행동, 클라이언트에 받은 이벤트. Console 오류 없음.
- 이상하면: 단계·지연 값·Console 첫 줄.

### 멀티 체감 — 리플레이
준비: 0-2·0-3.
1. 테스트 씬 Play → 패널 › 리플레이 › '녹화(혼자·방장 = 모두, 클라이언트 = 들어갈 때부터)' 켬.
2. 멀티 › '시험 모드' → '지연 400ms' → 점검 › '타격 ×3' → '고르기 창' → '나'.
3. 리플레이 › '지금 녹화 · N초' 줄의 '저장' → 패널 아래 '저장: …\MomoiProject\Replays\(시각).gbreplay'.
4. 같은 줄 '틀기'(트는 동안 고르기 창은 누르지 않는다).
5. '한 칸' · '×2' · 위치 막대를 앞뒤로 끈다 → '그만 보기(혼자 하기)'.
6. 리플레이 목록에 생긴 파일 줄 '틀기' → '그만 보기(혼자 하기)'.
- 정상: 4는 패널이 '플레이어 #1 · 0.0 / N초'·위치 막대·'멈춤'으로 바뀌고, 화면이 2를 처음부터 같은 때에 다시 보여 준다(숫자 10·20·30, 고르기 창이 열렸다 닫힘). 5는 한 칸씩·두 배로 가고, 막대를 끌면 그 시점 화면. 6도 같다.
- 이상하면: 빠지거나 때가 다른 연출, Console.

### 실시간 위치(NetTransformSync)
준비: 0-2·0-3, 1의 물체를 넣은 뒤 0-6대로 빌드.
1. 테스트 씬에서 `GameObject › 2D Object › Sprites › Square` → 이름 `check1`, Position X -3 → Add Component › MomoiProject › 실시간 위치 맞추기 → Owner 1. Ctrl+D → 이름 `check2`, X 3, Owner 2(Entity Id는 비움 = 오브젝트 이름) → 씬 저장 → 빌드.
2. 에디터 테스트 씬 Play → 패널 › 멀티 › '방 생성 :7777'(내 번호 #1).
3. 명령 프롬프트: `UnitySystemProject.exe -momoi-join 127.0.0.1:7777 -momoi-name 빌드`(내 번호 #2).
4. 두 창을 나란히 두고 본다.
5. (선택) 에디터 Hierarchy check2 › 실시간 위치 맞추기 › Delay를 0과 0.3으로 바꿔 본다.
- 정상: 에디터는 check1, 빌드는 check2를 위아래로 움직이고(스크립트가 내 것만 움직인다), 상대 화면의 같은 물체가 끊김 없이 부드럽게(0.1초쯤 늦게) 따라온다. 5는 0이면 빠르지만 덜 부드럽고, 0.3이면 더 늦다.
- 이상하면: 어느 쪽 물체가 멈추거나 튀는지, 두 패널의 명단.

### 모니터 — 값 고치기는 행동 하나(Undo)
준비: 0-2, 모니터 창.
1. 테스트 씬 Play → 모니터 › 데이터 → `run`(또는 `profile`)을 열어 숫자 칸 하나를 고친다(숫자 칸이 없으면 이 항목은 건너뛴다).
2. 모니터 › 행동 탭과 테스트 패널의 'Undo (N)'을 본다 → 패널 'Undo'.
3. (DB › 로그 항목이 있으면) 모니터 › 로그 → 한 줄을 누른다 → 오른쪽 아래 값 칸에 숫자 → '직접 수정' → 패널 'Undo'.
4. 패널 › 멀티 › '시험 모드' → 모니터 위쪽에서 '클라이언트 · (이름)'을 고르고 1처럼 칸을 고쳐 본다 → 패널 '나가기(혼자 하기)'.
- 정상: 1·3 뒤 행동 탭에 '[개발] 모니터에서 … 고침'이 생기고 'Undo (N)'이 하나 늘며, 'Undo'로 원래 값이 된다. 4는 값이 바뀌지 않고 모니터 아래에 '멀티 클라이언트 세션의 값은 여기서 고치지 않습니다 — 위쪽에서 서버 세션을 골라 고치세요.'가 뜬다.
- 이상하면: 탭·단계, 모니터 아래 알림 글, Console.

### 저장 — 깨진 저장 파일은 바로 전 저장으로
준비: 0-2.
1. 테스트 씬 Play → 패널 › 점검 › '타격 ×3' → 저장 › '판 저장' → 점검 › '타격 ×3' → 저장 › '판 저장'(두 번 저장해야 바로 전 저장 `.bak`이 생긴다) → Play 멈춤.
2. 탐색기로 `%USERPROFILE%\AppData\LocalLow\DefaultCompany\UnitySystemProject\MomoiProject\slots\test`를 연다 → `run.json`을 메모장으로 열어 내용을 모두 지우고 저장(0바이트 — 쓰는 도중 꺼진 것과 같다).
3. Play → 패널 › 저장 › '판 불러오기' → Console·모니터 타임라인을 본다.
4. Play 멈춤 → 같은 폴더를 본다.
- 정상: 3에서 노란 경고 '판 불러오기 실패: 파일이 비어 있습니다 — 바로 전 저장(.bak)으로 이어 갑니다. 원본은 …run.broken-(시각).json에 남겨 둡니다.'가 뜨고 판이 이어진다(오류로 멈추지 않는다). 4의 폴더에 `run.json`·`run.json.bak`·`run.broken-….json`이 있다. 저장 중 생기는 `.tmp`는 남지 않는다.
- 이상하면: Console 첫 줄, 폴더의 파일 이름들.

### 에셋 관리 — 폴더 정돈·안 쓰는 것 옮기기의 확인 창과 되돌리기
준비: 0-1. `Assets\Dev`에 그림 하나를 아무 데나(예: `Assets\Dev\Misc\a.png` — Misc 폴더를 만든다), 소리 하나를 `Assets\Dev\Art\Check`에 넣는다(잘못된 자리).
1. 메뉴 `MomoiProject › 에셋 관리` → '폴더 정돈' 탭.
2. '모두 정돈(N)' → 확인 창의 글을 읽고 '옮기기'.
3. Project 창에서 옮겨진 자리를 본다 → '마지막 정돈 되돌리기' → 확인 창 '되돌리기'.
4. '안 쓰는 것' 탭 → 목록의 한 줄 '옮기기' → 확인 창 '옮기기' → '마지막 옮기기 되돌리기' → '되돌리기'.
- 정상: 1의 목록은 `Assets/Dev` 안 것만이고 하위 폴더 모양을 지킨다(예: `Dev/Misc/a.png` → `Dev/Art/Misc/a.png`, 소리 → `Dev/Audio/Check/…`). 2의 확인 창에 옮길 개수와 앞의 다섯 줄, 'MomoiProjectBackups/<시각>-tidy/moves.tsv'. 3에서 원래 자리로 돌아오고 그 파일을 쓰던 곳(DB 칸 등)이 끊기지 않는다(GUID 그대로). 4도 `_Unused`로 갔다가 돌아온다.
- 이상하면: 탭·단계, 확인 창 글, Console 경고.

### 스파인 스탠딩(스파인이 있는 프로젝트에서)
준비: spine-unity 런타임이 있는 프로젝트, 다이얼로그 준비, SkeletonAnimation이 붙은 캐릭터 프리팹. 이 프로젝트에는 스파인이 없어 한 번도 실행하지 못했다.
1. 그 프리팹에 Add Component → 'Spine Standing View'(검색) → `animations`에 스탠딩 ID ↔ 애니메이션 이름(같으면 비움).
2. DB › 다이얼로그 시스템 › 스탠딩 프로필 › 그 캐릭터: `standingPrefab` = 그 프리팹, `logIcon` = 얼굴 그림(정사각형), `expressions`의 스탠딩 ID = 애니메이션 이름(예: idle·smile) → '저장  Ctrl+S'.
3. 그래프: ②의 '진입' → '스탠딩 등장'(그 캐릭터), ③의 '진입' → '스탠딩 바꾸기'(`expression` = smile), ④의 `highlight` = 그대로, ④의 '진입' → '강조'(그 캐릭터, `on` 끔).
4. '▶ 선택한 노드부터'로 ④까지 → 로그 창.
- 정상: 스파인 캐릭터가 등장하고 smile로 섞이며 바뀐다. ④에서 어두워진다. 다른 스탠딩과 앞뒤 순서가 맞고 로그 창에 logIcon이 보인다.
- 이상하면: Console '[다이얼로그] … 스파인 컴포넌트(SkeletonAnimation 등)를 찾지 못했습니다' 등 첫 줄.

## 2. 빌드·Steam·다른 컴퓨터

### Steam(에디터)
준비: 이 PC에 Steam 설치·로그인, 0-2.
1. Steam을 켠 채 테스트 씬 Play.
2. Console을 본다.
3. Steam 친구 목록에서 내 상태를 본다.
4. Play 멈춤 → Steam을 완전히 끈다(작업 표시줄 Steam 아이콘 우클릭 › 종료) → 다시 Play → Console.
- 정상: 2 '[MomoiProject] Steam에 붙었습니다: (내 Steam 이름) · App ID 480. Steam 친구 목록에 이 앱을 하는 중으로 보입니다 (오버레이 Shift+Tab은 에디터에서는 뜨지 않습니다 — 빌드에서 확인).' 3은 Spacewar 하는 중. 4는 경고 하나 '[MomoiProject] Steam에 붙지 못해 Steam 없이 시작합니다(…)'뿐이고 오류는 없다.
- 이상하면: Console 첫 줄.

### Steam 오버레이(빌드)
준비: 0-6 빌드, Steam 켜 둠.
1. exe를 탐색기에서 바로 켠다 → 패널 머리의 '시각 N초'를 본다.
2. Shift+Tab → 10초쯤 둔다 → Shift+Tab으로 닫는다.
3. 안 뜨면: Steam › 라이브러리 › 게임 추가 › 비 Steam 게임 추가로 그 exe를 넣고 Steam에서 실행해 2를 다시.
- 정상: 1 뒤 Player.log에 '[MomoiProject] Steam에 붙었습니다: … App ID 480'(steam_appid.txt 없이). 2에서 오버레이가 뜨고 그동안 게임이 멈춘다(패널 버튼 '계속', 닫은 뒤 시각이 연 동안만큼 늘지 않았다). 닫으면 '멈춤'으로 돌아오고 시각이 다시 흐른다(에디터에서는 원래 뜨지 않는다).
- 이상하면: 안 뜨는지·떠도 안 멈추는지, Player.log의 '[MomoiProject] Steam' 줄.

### 빌드(PC, IL2CPP)
준비: Unity Hub › Installs › 6000.6.3f1 › Add modules › Windows Build Support (IL2CPP)(+ Visual Studio 'C++를 사용한 데스크톱 개발'), 0-3·0-4·0-5.
1. `Edit › Project Settings › Player › Other Settings › Scripting Backend` = IL2CPP → 0-6대로 빌드 → exe 켜기.
2. 판 저장·이어 하기: 패널 › 점검 › '슬롯 화면' → '1 — …' 줄을 누른다(머리 "슬롯 '1'") → 점검 › '타격 ×3' → 30초쯤 기다렸다 저장 › '판 저장' → exe를 끄고 다시 켬 → '슬롯 화면' → '1 — …' → 저장 › '판 불러오기'.
3. 변경 기록·Undo: 점검 › '고르기 창' → 'Undo (N)'.
4. 기록 문장: 점검 › '타격 ×3' → '마지막 기록'.
5. 전체 화면·해상도: Esc → '설정' → fullscreen 켬 → 끔 → resolution에서 다른 크기 → exe를 끄고 다시 켬.
6. `%USERPROFILE%\AppData\LocalLow\DefaultCompany\UnitySystemProject\Player.log`를 연다. 끝나면 Scripting Backend를 Mono로 돌려도 된다.
- 정상: 2는 불러온 뒤 머리의 시각이 0 근처가 아니라 저장(끈) 때의 값. 3은 고르기 창이 닫힌다. 4는 화면 아래 'Event: 점검 타격 (amount=30)'처럼 필드 값이 붙은 문장(값이 빠지면 이상). 5는 전체 화면 ↔ 창이 바로 바뀌고 해상도대로 창 크기가 바뀌며 다시 켜도 남는다. 6에 Exception·'[MomoiProject]' 오류가 없다.
- 이상하면: 단계와 Player.log 첫 오류. 5에서 다시 켰을 때 fullscreen 값과 실제 화면 모드(전체 화면/창)가 다르면 그것도 알린다.

### 빌드 — 코드 줄이기(Managed Stripping Level) High
준비: '빌드(PC, IL2CPP)'를 마친 뒤(IL2CPP 모듈).
1. Project 창 `Assets/MomoiProject/link.xml`이 있는지 본다(에디터를 열 때 저절로 생긴다).
2. `Edit › Project Settings › Player › Other Settings › Managed Stripping Level` = High → 0-6대로 빌드(Scripting Backend IL2CPP) → exe 켜기.
3. 패널 › 점검 › '타격 ×3' → '고르기 창' → '나' → 'Undo (N)'.
4. 패널 › 멀티 › '시험 모드' → '지연 400ms' → 점검 › '타격 ×3' → '나가기(혼자 하기)'.
5. Player.log를 연다. 끝나면 Managed Stripping Level을 원래(Minimal 등)로 돌려도 된다.
- 정상: 1 파일이 있다. 2의 빌드 중 Console에 'link.xml' 경고가 없다. 3·4가 에디터와 똑같이 돈다(숫자·고르기 창·Undo·시험 모드). 5에 'MissingMethodException'·'등록하지 못했습니다'·Exception이 없다.
- 이상하면: Player.log의 첫 Exception(형식 이름 포함), 빌드 중 Console 경고.

### 개발 빌드 — 테스트 상황이 함께 들어간다
준비: 0-2·0-6.
1. 에디터 테스트 씬 Play → 점검 › '타격 ×3' → 패널 › 테스트 상황 › 이름 칸 `check` → '지금 저장' → Play 멈춤(→ `Assets/MomoiProject/Data/Tests/check.json`).
2. 0-6대로 개발 빌드 → exe 켜기 → 패널 › 테스트 상황 목록.
3. `check` 줄 '불러오기'.
4. (출시 빌드) Development Build를 끄고 다른 빈 폴더로 빌드 → 빌드 폴더 `UnitySystemProject_Data\StreamingAssets`를 탐색기로 본다.
- 정상: 2의 목록에 `check`가 있고, 3은 1에서 저장한 모습으로 바뀐다. 4의 출시 빌드에는 `StreamingAssets\MomoiProject\Tests`가 없다(개발 빌드에는 있다).
- 이상하면: 목록이 비었는지, 불러오기 뒤 패널 아래 글, Player.log.

### 출시 빌드가 Steam으로 다시 켜진다(자기 App ID가 생긴 뒤)
준비: Steamworks의 자기 App ID, 그 앱이 Steam 라이브러리에 설치돼 있음.
1. DB › 개발 › Steam › `steam` › `appId` = 자기 App ID → '저장  Ctrl+S'(프로젝트 폴더 steam_appid.txt도 바뀐다).
2. `File › Build Profiles`에서 Development Build 끔 → 빌드(exe 옆에 steam_appid.txt를 두지 않는다).
3. exe를 탐색기에서 바로 켠다. 끝나면 `appId`를 480으로 돌린다.
- 정상: 창이 잠깐 떴다 꺼지고 Steam이 게임을 다시 켠다.
- 이상하면: 그냥 켜지면 Player.log의 '[MomoiProject] Steam' 줄.

### 서버 빌드
준비: Unity Hub 모듈 Windows Dedicated Server Build Support.
1. `File › Build Profiles` → Windows Server(Dedicated Server) → Switch Profile → Scene List 맨 위 Test.unity → Build → 빈 폴더(예: `D:\Builds\Server`).
2. 명령 프롬프트: `cd /d D:\Builds\Server` → `UnitySystemProject.exe -batchmode -nographics -momoi-server 7777 -logFile server.log`.
3. 10초 뒤 같은 폴더의 server.log를 연다.
4. (선택) 에디터 테스트 씬 Play → 패널 › 멀티 › 주소 `127.0.0.1:7777` › '방 참가'.
5. 명령 프롬프트 창에서 Ctrl+C로 끈다 → Build Profiles를 Windows로 되돌린다(Switch Profile).
- 정상: 3의 로그에 '[MomoiProject] 서버를 열었습니다 — 접속 주소: ws://…:7777'이 있고, 화면(UI)·UIDocument·PanelSettings 관련 오류·Exception이 없다. 4는 패널에 '클라이언트 · 내 번호 #1'(방장 없음).
- 이상하면: server.log의 첫 오류 줄.

### 직접 접속(빌드 + 에디터, 한 컴퓨터)
준비: 0-3, 0-6 빌드(Run In Background 켬).
1. 명령 프롬프트: `UnitySystemProject.exe -momoi-host -momoi-name 빌드` → Windows 방화벽 창이 뜨면 '허용'.
2. 에디터 테스트 씬 Play → 패널 › 멀티 › 주소 칸 `127.0.0.1:7777` → '방 참가'.
3. 빌드 패널 › 점검 › '타격 ×3' → 에디터 패널 › 점검 › '고르기 창' → 한쪽에서 '가'.
4. 빌드 패널 › '나가기(혼자 하기)'.
5. 다시 1·2 → 작업 관리자에서 빌드(UnitySystemProject)를 '작업 끝내기'. 끝나면 에디터는 '나가기(혼자 하기)'.
- 정상: 2 뒤 두 패널 명단 '#1 빌드'·'#2 (에디터 쪽 이름)', 에디터 '클라이언트 · 내 번호 #2 · 핑 …ms', 빌드 '방장 · 내 번호 #1'. 3은 숫자·고르기 창이 양쪽에 뜨고 한쪽에서 답하면 양쪽에서 닫힌다. 4 뒤 에디터에 바로 '서버 연결이 끝났습니다: 방이 닫혔습니다.'(다시 접속 알림 없음). 5 뒤 에디터에 '연결이 끊겼습니다 — 다시 접속하는 중(1/6)…' … '(6/6)…'이 이어지고 약 45초 뒤 '서버 연결이 끝났습니다: …'.
- 이상하면: 멈춘 단계, 에디터 패널 상태 글(Connecting 등), Console.

### 매칭큐(빌드 + 에디터)
준비: 0-3, 0-6 빌드(빌드 두 개를 켠다).
1. 명령 프롬프트 ①: `UnitySystemProject.exe -momoi-matchserver 7777 -momoi-players 2`(방화벽 '허용').
2. 명령 프롬프트 ②: `UnitySystemProject.exe` → 패널 › 멀티 › 주소 `127.0.0.1:7777` › '매칭 찾기(주소)' → '줄 서기'.
3. 에디터 테스트 씬 Play → Inspector '[테스트]' › 테스트 씬 › Player Name = 에디터(계정 = 이름이라 빌드와 달라야 한다. 플레이 중에 바꾸면 저장되지 않는다) → 패널 › '매칭 찾기(주소)' → '줄 서기'.
4. '매치 성사: …'가 뜨면 15초 안에 양쪽 '수락'.
5. 탐색기로 `%USERPROFILE%\AppData\LocalLow\DefaultCompany\UnitySystemProject\MomoiProject\server\accounts`.
- 정상: 2·3 뒤 패널에 '(이름) · 점수 1000 · 0승 0패 0무'. 4 뒤 둘 다 '클라이언트 · 내 번호 #1(또는 #2) · 핑 …'과 두 이름 명단, 서버 빌드 패널은 '매칭큐 서버 · 로비 0명 · 방 1개'와 그 방의 두 이름. 5에 계정 파일 둘.
- 이상하면: 멈춘 단계와 그 패널 글, 서버 빌드 패널 글.

### 다른 컴퓨터
준비: 두 컴퓨터에 같은 빌드(0-6), 둘 다 같은 Tailscale 계정에 로그인(또는 같은 공유기).
1. 방장 컴퓨터: `UnitySystemProject.exe -momoi-host -momoi-name 방장` → 방화벽 '허용' → 패널의 '접속 주소: ws://…:7777  ws://100.…:7777'에서 100.으로 시작하는 주소(Tailscale)를 적는다.
2. 다른 컴퓨터: `UnitySystemProject.exe -momoi-join 100.x.y.z:7777 -momoi-name 손님`.
3. 한쪽에서 점검 › '타격 ×3'·'고르기 창'.
- 정상: 두 패널 명단 '#1 방장'·'#2 손님', 3의 숫자·창이 양쪽에 뜬다(손님 쪽은 조금 늦게).
- 이상하면: 손님 패널 상태 글(Connecting에서 멈춤 등), 방화벽 허용 여부, 적은 주소.

### 잠깐 끊긴 네트워크(두 컴퓨터)
준비: '다른 컴퓨터'처럼 방에 들어간 상태(손님 = 와이파이·랜선을 끊을 컴퓨터).
1. (선택, 한 컴퓨터로 미리) 테스트 씬 시험 모드에서 패널 '끊김 시험(다시 접속)' → 아래 알림이 뜨는지 본다.
2. 손님 패널의 내 번호(#2)를 본다.
3. 손님 컴퓨터의 와이파이를 끄거나 랜선을 뺀다 → 10초(안 끊기면 20초) 기다린다 → 다시 켠다.
4. 한쪽에서 점검 › '타격 ×3'.
- 정상: 끊긴 동안 손님 화면 아래 '연결이 끊겼습니다 — 다시 접속하는 중(n/6)…', 다시 켠 뒤 '다시 접속했습니다.'. 손님 내 번호 #2 그대로, 방장 명단 2명(늘지 않음). 4가 양쪽에 보인다.
- 이상하면: 알림 글, 방장 명단(3명이 되었는지), 다시 들어간 번호.

### 사설 호스트(Steam, 두 컴퓨터·두 계정, App ID 480)
준비: 서로 친구인 Steam 계정 둘로 두 컴퓨터 로그인, 두 컴퓨터에 0-6 빌드.
1. 두 컴퓨터 모두 빌드를 켠다(손님도 먼저 켜 둔다).
2. 방장: 패널 › 점검 › '방 만들기'.
3. 방장: 점검 › '친구 초대' → Steam 초대 창에서 손님을 초대.
4. 손님: Steam 채팅·친구 목록의 초대에서 참가.
5. 방장: 멀티 › '나가기(혼자 하기)'.
- 정상: 2 뒤 방장 패널 '방장 · 내 번호 #1', '접속 주소: Steam 로비 …'. 4 뒤 두 패널 명단 2명, 손님 '클라이언트 · 내 번호 #2'. 5 뒤 손님 화면에 '서버 연결이 끝났습니다: 방이 닫혔습니다.'.
- 이상하면: 2의 접속 주소가 'ws://…'(직접 접속)면 Steam이 붙지 않은 것 — Player.log의 '[MomoiProject] Steam' 줄. 4에서 안 들어가면 손님 Player.log.

## 3. Unity 안에서만 도는 시험(버튼 한 번)

### Test Runner — EditMode·PlayMode 전체
준비: Play 멈춤, 점검용 스크립트(`Assets/Dev/Checks`)는 지운 상태(테스트에 점검용 형식이 섞이지 않게). 다이얼로그 시험까지 돌리려면 다이얼로그 기능을 켠 상태(1절 '다이얼로그 — 준비' 1). PlayMode 시험은 게임을 잠깐 켠다(이 프로젝트의 DB로) — 씬 흐름(1절 '씬 흐름 페이드')을 정해 두었으면 DB › 시작 파일 › Bootstrap › 씬 흐름의 '플레이어 데이터'를 (없음)으로 돌려 둔다(시험 도중 다른 씬으로 옮겨 가지 않게).
1. `Window › General › Test Runner` → 'EditMode' → 'Run All'.
2. 끝날 때까지 기다린다.
3. 같은 창 'PlayMode' → 'Run All' → 끝날 때까지 기다린다(게임이 켜졌다 꺼진다 — 1분 안쪽).
4. Console 검색 칸에 `MomoiProject`.
- 정상: 모든 테스트가 초록(통과), 도는 동안·끝난 뒤 Console에 `[MomoiProject]` 경고가 없다(테스트용 형식의 경고는 나오지 않는다). 단 PlayMode의 '연출이 0.5초 동안 나아가지 않아 넘어갑니다'·'연출이 0.3초 안에 끝나지 않아 넘어갑니다'는 시험이 일부러 내는 것이다. 에디터 전용 시험(DB 목록·저장·적용(Ctrl+S) 계획·ID 바꾸기·에셋 관리(넣을 것·정돈)·저장 변환 항목·Mirror 빌드(링크 파일)·다이얼로그 진행·테스트 상황·[ShowIf] 숨김 등)은 오프라인으로 돌릴 수 없어 이것만 남긴다. 에셋 관리 시험은 `Assets/__GBibleAssetToolTests`를 잠깐 만들었다가 지운다(끝난 뒤 남아 있으면 이상). PlayMode에는 다이얼로그를 꺼도 `PresentationPlayTests`(연출 대기열을 플레이 중에 돌려 봄: 씬 흐름이 죽는 모습을 기다림 · 멈춘 연출 넘기기와 일시정지 · 버리기의 finally·씬 열기·사라지는 오브젝트 · 배속을 세션에 알림 · 확인 창을 Esc로 닫을 때 다시 뜬 창)가 있다 — 화면 설정이 없는 실행이면 확인 창 시험 하나는 '건너뜀'(회색)으로 남는다(실패 아님).
- 이상하면: 실패한 테스트 이름과 메시지(Test Runner 아래 칸), 경고 첫 줄.

## 확인이 끝나면

- 시험하느라 만든 항목·스크립트(`Assets/Dev/Checks`(Editor 포함) · `Assets/Dev/Art`·`Audio`·`Fonts`·`Misc`의 시험 파일 · 스프라이트 틀 `cardFrame`과 `Assets/MomoiProject/Data/Sprites` · DB 설정 넷·`scene`·`hero`·`office`·`checkCard`·`rule.checkCard.art` · `Texts_en` · 테스트 상황 `check` · Test.unity·CheckB · 빌드 씬 목록 · Run In Background · Scripting Backend · Managed Stripping Level · 다이얼로그 기능)는 지우거나, 메뉴 `MomoiProject › 처음 상태로 되돌리기…`로 처음 상태(설치 직후)로 돌린다(저장 파일·PlayerPrefs도 지운다). 그 뒤 시작 설정을 다시 한다.
- 구조 편집을 켰으면 메뉴 `MomoiProject › 개발 › 구조 편집 열기(라이브러리 개발용)`로 다시 끈다(이 컴퓨터 설정이라 되돌리기로 꺼지지 않는다).

---

## 부록 A — 점검용 스크립트(`Assets/Dev/Checks/CheckCommands.cs`)

GBible API만 쓴다(오프라인 컴파일 확인함). 도메인 리로드를 끈 이 프로젝트 설정에 맞춰 플레이마다 다시 건다.

```csharp
// Assets/Dev/Checks/CheckCommands.cs — PendingChecks.md 점검용. 점검이 끝나면 Assets/Dev/Checks 폴더째 지운다.
// 테스트 패널(F1)·모니터 › 명령에 '점검' 묶음 버튼을 더하고, 연출·씬 전환 페이드·키 바꾸기·위치 맞추기 점검을 돕는다.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GBible;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if GBIBLE_INPUT
using UnityEngine.InputSystem;
#endif

namespace MomoiChecks
{
    [Serializable, GameEvent("점검 타격")]
    public class CheckHit { public int amount; }

    [Serializable]
    public class CheckStrike : GameAction
    {
        public int amount;
        protected override void Run() => Game.Events.Publish(new CheckHit { amount = amount });
    }

    [Serializable]
    public class CheckAsk : GameAction
    {
        protected override void Run() => Game.Ask("점검: 하나 고르기",
            new[] { new ChoiceCandidate("a", "가"), new ChoiceCandidate("b", "나"), new ChoiceCandidate("c", "다") }, new CheckPicked());
    }

    [Serializable]
    public class CheckPicked : GameAction
    {
        [Chosen] public string picked;
        protected override void Run() { }
    }

    [Serializable]
    public class CheckFail : GameAction
    {
        protected override void Run() => throw Game.Fail("check.fail", "점검용 실패 알림입니다.");
    }

    // 스프라이트 틀 점검: Sprite 필드 하나짜리 원본 클래스(부록 B가 인스턴스 checkCard와 틀 연결을 만든다).
    [Serializable]
    public class CheckCard
    {
        public string title;
        public Sprite art;
    }

    [Serializable]
    public class CheckScene : GameAction
    {
        protected override void Run()
        {
            if (!Game.Session.Data.IsDefined("scene")) throw Game.Fail("check.scene", "DB › 요소 › 플레이어 데이터에 scene(글자)을 먼저 만드세요.");
            Game.Set("scene", Game.Get<string>("scene") == "CheckB" ? "SampleScene" : "CheckB");
        }
    }

    public static class CheckCommands
    {
        const string Group = "점검";
        static IDisposable _sceneWatch;
        static Label _sceneLabel;
        static bool _failFade;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Hook()
        {
            _sceneWatch = null;
            _sceneLabel = null;
            _failFade = false;
            GBibleRuntime.Started += session =>
            {
                Register(session);
                session.Events.SubscribeAfterAction<CheckHit>(e => GameView.Present(Number(e.amount)));
            };
            GameNet.ServerStarted += server => Register(server.Session);   // 시험 모드·방장·서버: 클라이언트가 누른 명령은 서버에서 돈다
            GameScenes.Transition = Fade;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterSceneLoad()
        {
#if GBIBLE_INPUT
            if (InputSystem.actions != null) GameInput.Use(InputSystem.actions);   // Project-wide Actions(Assets/Settings/InputSystem_Actions)
#endif
            GameView.Play(MoveSquares());
        }

        static void Register(GameSession s)
        {
            s.Debug.Register(Group, "고르기 창", () => s.Do(new CheckAsk()));
            s.Debug.Register(Group, "타격 ×3", () => { for (var i = 1; i <= 3; i++) s.Do(new CheckStrike { amount = i * 10 }); });
            s.Debug.Register(Group, "실패 알림", () => GameScreens.Do(() => new CheckFail()));
            s.Debug.Register(Group, "슬롯 화면", () =>
            {
                var names = new List<string> { GBibleRuntime.Slot, "1", "2" }.Distinct().ToList();
                GameScreens.Open(GameScreens.Slots, new SlotsRequest { count = names.Count, names = names });
            });
            s.Debug.Register(Group, "연출 속도 1↔3", () => GameView.PresentationSpeed = GameView.PresentationSpeed > 1f ? 1f : 3f);
            s.Debug.Register(Group, "연출 건너뛰기", GameView.SkipPresentation);
            s.Debug.Register(Group, "마지막 기록", () =>
            {
                foreach (var entry in s.Journal.Entries.Where(e => e.Kind == JournalKind.Event || e.Kind == JournalKind.Data).Reverse().Take(3))
                    GameScreens.Toast(entry.Kind + ": " + entry.Message, 8f);
            });
            s.Debug.Register(Group, "씬 바꾸기", () => s.Do(new CheckScene()));
            s.Debug.Register(Group, "연출 버리기", GameView.ClearPresentation);
            s.Debug.Register(Group, "전환 오류 켬↔끔", () => GameScreens.Toast((_failFade = !_failFade) ? "전환 오류: 켬" : "전환 오류: 끔"));
            s.Debug.Register(Group, "방 만들기", () => GameNet.CreateRoom(4));
            s.Debug.Register(Group, "친구 초대", GameNet.InviteToRoom);
#if GBIBLE_INPUT
            s.Debug.Register(Group, "키 바꾸기(Jump)", RebindJump);
            s.Debug.Register(Group, "지금 키(Jump)", () => GameScreens.Toast("Jump = " + GameInput.BindingText(GameInput.Find("Player/Jump"), 0)));
            s.Debug.Register(Group, "키 되돌리기", () => GameInput.ResetBindings());
#endif
        }

#if GBIBLE_INPUT
        static void RebindJump()
        {
            var jump = GameInput.Find("Player/Jump");
            GameScreens.Toast("바꿀 키를 누르세요(Esc: 취소)", 5f);
            GameInput.Rebind(jump, 0, result => GameScreens.Toast(
                result.Done ? "바뀜: Jump = " + GameInput.BindingText(jump, 0)
                : result.Cancelled ? "취소했습니다"
                : "겹침: '" + result.Conflict.name + "'에서 이미 쓰는 키입니다", 5f));
        }
#endif

        static Label MakeLabel(string text, int size, Color color, float left, float top)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.left = left;
            label.style.top = top;
            label.style.fontSize = size;
            label.style.color = color;
            return label;
        }

        // 연출: 숫자 하나가 1초 동안 떠오른다(GameView.DeltaTime — 연출 속도·건너뛰기·게임 배속을 따른다).
        static IEnumerator Number(int amount)
        {
            var label = MakeLabel(amount.ToString(), 96, Color.yellow, 900, 600);
            GameScreens.Overlay?.Add(label);
            for (var t = 0f; t < 1f; t += GameView.DeltaTime)
            {
                label.style.top = 600 - t * 300;
                yield return null;
            }
            label.RemoveFromHierarchy();
        }

        // 씬 전환: 0.5초 동안 검게 → 씬 열기 → 2초 동안 밝게. '전환 오류'를 켜면 씬을 열기 전에 예외를 낸다(GameScenes가 전환 없이 연다).
        static IEnumerator Fade(IEnumerator load)
        {
            if (_failFade) throw new InvalidOperationException("점검: 전환 연출 오류");
            var curtain = new VisualElement { pickingMode = PickingMode.Ignore };
            curtain.style.position = Position.Absolute;
            curtain.style.left = 0;
            curtain.style.top = 0;
            curtain.style.right = 0;
            curtain.style.bottom = 0;
            curtain.style.backgroundColor = Color.black;
            GameScreens.Overlay?.Add(curtain);
            for (var t = 0f; t < 1f; t += Time.unscaledDeltaTime * 2f) { curtain.style.opacity = t; yield return null; }
            curtain.style.opacity = 1f;
            yield return load;
            for (var t = 1f; t > 0f; t -= Time.unscaledDeltaTime * 0.5f) { curtain.style.opacity = t; yield return null; }
            curtain.RemoveFromHierarchy();
        }

        // 새 씬이 열리면(페이드 인 전) 그 씬의 값 표시를 만든다 — 씬의 화면이 GameView.Redraw로 그리는 것과 같다.
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _sceneWatch?.Dispose();
            _sceneWatch = null;
            _sceneLabel?.RemoveFromHierarchy();
            var session = GBibleRuntime.Session;
            var overlay = GameScreens.Overlay;
            if (session == null || overlay == null || !session.Data.IsDefined("scene") || string.IsNullOrEmpty(session.Data.Get<string>("scene"))) return;
            var label = _sceneLabel = MakeLabel("", 48, Color.white, 1000, 60);
            overlay.Insert(0, label);   // 페이드 판 아래
            var name = scene.name;
            _sceneWatch = GameView.Redraw(() => label.text = $"새 씬 {name} · scene = {GBibleRuntime.Session?.Data.Get<string>("scene")}", "scene");
        }

        // 위치 맞추기: 이름이 check1·check2인 물체 중 이 프로그램 것(IsMine)을 위아래로 움직인다.
        static IEnumerator MoveSquares()
        {
            var names = new[] { "check1", "check2" };
            while (true)
            {
                foreach (var name in names)
                {
                    var go = GameObject.Find(name);
                    var sync = go != null ? go.GetComponent<NetTransformSync>() : null;
                    if (sync == null || !sync.IsMine) continue;
                    var p = go.transform.position;
                    go.transform.position = new Vector3(p.x, Mathf.Sin(Time.unscaledTime * 2f) * 3f, p.z);
                }
                yield return null;
            }
        }
    }
}
```

## 부록 B — 점검용 DB 반영 스크립트(`Assets/Dev/Checks/Editor/CheckSeeds.cs`)

스프라이트 틀 점검(1절)에 쓴다. 에디터 전용이라 `Editor` 폴더에 둔다(0-3과 같은 방법으로 `Assets\Dev\Checks\Editor` 폴더를 만들고 저장). 컴파일이 끝나면 DB에 '적용 전' 항목이 생긴다 — 스프라이트 틀 항목 12에서 Ctrl+S로 적용한다.

```csharp
// Assets/Dev/Checks/Editor/CheckSeeds.cs — PendingChecks.md 점검용(스프라이트 틀). 점검이 끝나면 Assets/Dev/Checks 폴더째 지운다.
using GBible.Data;
using GBible.EditorTools;
using MomoiChecks;

static class CheckSeeds
{
    // CheckCard.art를 틀 cardFrame(미리보기 크기 cardFront)에 잇고, 그 클래스의 인스턴스 checkCard를 만든다(적용 전 → Ctrl+S).
    [DbSeed("1")]
    static void SpriteFrameCheck()
    {
        var db = EditorDatabase.Instance;
        if (db.FindById("rule.checkCard.art") == null)
        {
            var rule = (FieldRuleData)EditorDatabase.Staging.Add(typeof(FieldRuleData), "rule.checkCard.art");
            rule.targetType = typeof(CheckCard).FullName;
            rule.field = nameof(CheckCard.art);
            rule.source = FieldRuleSource.Free;
            rule.spriteFrame = "cardFrame";
            rule.previewSize = "cardFront";
        }
        if (db.FindById("checkCard") == null)
        {
            var item = (InstanceData)EditorDatabase.Staging.Add(typeof(InstanceData), "checkCard");
            item.value = new CheckCard { title = "점검 카드" };
        }
    }
}
```
