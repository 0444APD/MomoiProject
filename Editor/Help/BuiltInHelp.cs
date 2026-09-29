using System.Collections.Generic;
using GameSystems.Data;
using GameSystems.EditorTools.UI;

namespace GameSystems.EditorTools
{
    // 기본 기능의 도움말. 기능을 추가하거나 동작을 바꾸면 이 파일(또는 그 기능의 IHelpProvider)도 함께 고친다.
    // 문체: 매뉴얼보다 약간 친절하게, 비유 없이. 첫 문단에 핵심, 이어서 사용법·주의점.
    sealed class BuiltInHelp : IHelpProvider
    {
        public IEnumerable<HelpTopic> Topics()
        {
            // ── 안내 ─────────────────────────────────────────────
            yield return new HelpTopic
            {
                Id = HelpIds.Setup, Category = "안내", Order = 0, Title = "시작하기",
                Summary = "새 프로젝트에서 설정할 항목을 순서대로 안내합니다.",
                Body = "도감 창의 '시작하기' 탭은 새 프로젝트를 쓸 수 있는 상태로 만들기까지의 단계를 순서대로 보여 줍니다. 각 단계에는 설명, 현재 상태, 바로 가기 버튼이 있습니다.\n\n" +
                       "## 진행 방식\n" +
                       "- 위에서부터 차례로 진행하는 것을 권장합니다. 뒤 단계가 앞 단계에서 만든 항목을 고르기 때문입니다.\n" +
                       "- 상태는 도감 내용을 보고 자동으로 판정합니다. 자동 판정이 없는 단계는 '완료로 표시'를 누릅니다.\n" +
                       "- 필요 없는 단계는 '건너뛰기'로 표시할 수 있습니다. 언제든 되돌릴 수 있습니다.\n\n" +
                       "## 코드와 설정의 경계\n" +
                       "클래스와 게임 로직은 코드로 작성합니다. 이름(ID), 키워드와 필드 연결, 규격, 수치, 도전과제 같은 나머지는 모두 에디터(도감·키워드 연결 창)에서 설정합니다.",
                Related = new[] { "guide:codex", "window:help" },
            };
            yield return new HelpTopic
            {
                Id = "window:help", Category = "안내", Order = 1, Title = "도움말 창",
                Summary = "모든 기능의 설명을 모아 둔 창입니다. 메뉴 Game Systems › 도움말.",
                Body = "모든 창·탭·블록의 설명을 한곳에서 찾아볼 수 있습니다. 왼쪽 목록에서 고르거나 위의 검색 칸에 이름을 입력합니다.\n\n" +
                       "각 화면의 '?' 버튼을 누르면 해당 항목이 이 창에서 열립니다. 버튼에 마우스를 올리면 요약만 볼 수 있습니다.",
            };
            yield return new HelpTopic
            {
                Id = "guide:codex", Category = "안내", Order = 2, Title = "도감",
                Summary = "플레이어 데이터·키워드·필드 규칙·수치·도전과제·템플릿·프리팹 등 게임의 정의 데이터를 만들고 고치는 곳입니다.",
                Body = "도감은 게임이 시작되기 전부터 정해져 있는 데이터를 관리합니다. 메뉴 Game Systems › 도감으로 엽니다.\n\n" +
                       "## 화면 구성\n" +
                       "- 왼쪽: 도감 종류 목록. 종류 옆 숫자는 항목 수, '오류'는 검증 오류 수입니다.\n" +
                       "- 가운데: 선택한 종류의 항목. 시트·세로 카드·가로 카드·바 중에서 보기를 고릅니다.\n" +
                       "- 항목을 클릭(시트는 더블클릭)하면 상세 화면이 열립니다. 저장 전까지는 사본을 고치므로 '되돌리기'로 취소할 수 있습니다.\n\n" +
                       "## 검색\n" +
                       "이름·ID로 찾습니다. `tag:boss`는 기본 태그 칸의 키워드로, `invalid`는 검증 오류가 있는 항목만 거릅니다.\n\n" +
                       "## 새 종류 추가\n" +
                       "코드에서 GameData를 상속한 클래스를 만들면 자동으로 목록에 나타납니다. [Database(\"이름\", Group = \"분류\")]로 표시 이름과 분류를 정합니다.",
                Related = new[] { "guide:ids", "guide:layout", "guide:validation" },
            };
            yield return new HelpTopic
            {
                Id = "guide:ids", Category = "안내", Order = 3, Title = "ID와 식별자 코드(GameIds)",
                Summary = "모든 항목의 ID는 만들 때 한 번 짓고, 이후에는 목록에서 고릅니다. 코드는 생성된 GameIds를 씁니다.",
                Body = "ID는 저장 파일, 코드, 조건식이 함께 쓰는 식별자입니다. 항목을 만들 때 한 번 입력하고, 다른 곳에서는 드롭다운에서 고릅니다. 직접 타이핑하지 않으므로 오타가 생기지 않습니다.\n\n" +
                       "## 형식\n" +
                       "영문 소문자, 숫자, 밑줄(_)을 씁니다. 분류는 점(.)으로 나눕니다. 예: `player.inventory`, `kills_total`.\n\n" +
                       "## ID 바꾸기\n" +
                       "상세 화면의 'ID 바꾸기'를 쓰면 그 ID를 가리키는 모든 곳이 함께 바뀝니다. 바꾸기 전에 영향받는 곳의 목록을 보여 줍니다.\n\n" +
                       "## GameIds\n" +
                       "도감이 바뀌면 `GameIds.cs`가 자동으로 다시 만들어집니다. 코드에서는 문자열 대신 `GameIds.PlayerData.Gold`, `GameIds.Keyword.MonsterTag.Boss`처럼 씁니다. 플레이어 데이터는 `StateKey<int>`처럼 값 형식까지 붙어 나오므로 잘못된 형식으로 읽으면 컴파일 오류가 납니다.\n\n" +
                       "수동으로 다시 만들려면 메뉴 Game Systems › 식별자 코드 다시 만들기.",
                Related = new[] { "guide:saves" },
            };
            yield return new HelpTopic
            {
                Id = "guide:store", Category = "안내", Order = 4, Title = "저장소(상태)",
                Summary = "게임 중 바뀌는 모든 의미 있는 값을 담는 곳입니다. 값은 Set·Modify로만 바꿉니다.",
                Body = "저장소(StateStore)는 플레이어 데이터, 판 진행 상태, 누적 기록 등 게임 중 바뀌는 값을 모두 담습니다. 지역 변수 같은 일시적인 값만 예외입니다.\n\n" +
                       "## 규칙\n" +
                       "- 값을 바꿀 때는 반드시 `Set`(통째로 교체) 또는 `Modify`(안쪽을 고침)를 씁니다.\n" +
                       "- 그래야 변경 기록, Undo, 저장, 모니터 표시, 도전과제 검사가 모두 자동으로 따라옵니다.\n" +
                       "- 숫자·글자부터 클래스, 리스트, 딕셔너리까지 어떤 형식이든 같은 방법으로 다룹니다.\n\n" +
                       "## 구역\n" +
                       "- 이번 판(Session): 새 판을 시작하면 초기화됩니다.\n" +
                       "- 프로필(Meta): 판이 바뀌어도 유지됩니다(해금, 누적 기록 등).\n\n" +
                       "자세한 코드 예제는 Documentation~/StateStore.md를 참고하세요.",
                Related = new[] { "data:" + typeof(StateKeyData).FullName, "guide:actions" },
            };
            yield return new HelpTopic
            {
                Id = "guide:actions", Category = "안내", Order = 5, Title = "행동과 Undo",
                Summary = "행동은 실행하는 즉시 확정됩니다. Undo는 이미 실행한 행동을 뒤에서부터 되돌립니다.",
                Body = "플레이어 행동은 `session.Do(행동)`으로 실행합니다. 실행하는 즉시 결과가 반영되며, 보류 상태는 없습니다.\n\n" +
                       "## Undo\n" +
                       "- 행동 기록 방식(턴제 등): 마지막 행동부터 하나씩 되돌립니다. 몇 번이든 가능합니다.\n" +
                       "- 스냅샷 방식(실시간): 일정 간격으로 찍은 스냅샷으로 되감습니다. 호출 방법은 같은 `Undo()`입니다.\n" +
                       "- 도전과제 달성과 보상은 되돌리지 않습니다.\n\n" +
                       "방식은 SessionOptions.History에서 정합니다(Actions / Snapshots / None).",
                Related = new[] { "monitor:Timeline", "monitor:Actions" },
            };
            yield return new HelpTopic
            {
                Id = "guide:saves", Category = "안내", Order = 6, Title = "저장과 개발·출시 상태",
                Summary = "개발 중에는 호환되지 않는 저장을 지우고, 출시 후에는 이어받습니다.",
                Body = "저장소의 값은 프로필(Meta)과 판(Session)으로 나뉘어 저장됩니다.\n\n" +
                       "## 개발 중\n" +
                       "ID 이름이나 데이터 구조가 바뀌어 기존 저장과 맞지 않으면, 변환하지 않고 저장을 지운 뒤 새 프로필로 시작합니다. 개발 중에는 변환 코드를 작성할 필요가 없습니다.\n\n" +
                       "## 출시 후\n" +
                       "메뉴 Game Systems › 출시됨을 켜면, 이후의 ID 변경 이력이 기록되어 예전 저장이 새 ID로 이어집니다. 구조를 바꿀 때는 SessionOptions.Migrations에 변환 단계를 추가합니다.",
                Related = new[] { "guide:ids" },
            };
            yield return new HelpTopic
            {
                Id = "guide:conditions", Category = "안내", Order = 7, Title = "조건식",
                Summary = "도전과제 조건, 수치 반영 조건, 상태 값 검사에 쓰는 조건을 블록으로 조립합니다.",
                Body = "조건식은 블록을 쌓아 만듭니다. '모두 만족(AND)', '하나라도 만족(OR)', '아님(NOT)' 블록 안에 다른 블록을 넣을 수 있고, 몇 단계든 중첩할 수 있습니다. 괄호가 필요한 식도 중첩으로 표현합니다.\n\n" +
                       "## 표시\n" +
                       "편집 칸은 `[변수] [>=] [값]`처럼 코드 형태로 보입니다. 칸을 클릭해 값을 고르고, 마우스를 올리면 자연어 설명이 나옵니다.\n\n" +
                       "## 목록 검사\n" +
                       "'목록 항목 검사' 블록은 인벤토리 같은 목록의 각 항목에 조건을 적용합니다. 만족하는 항목이 하나라도 있는지, 모두인지, 하나도 없는지, 몇 개인지를 고릅니다.",
                Related = new[] { "guide:dual", "data:" + typeof(AchievementData).FullName },
            };
            yield return new HelpTopic
            {
                Id = "guide:dual", Category = "안내", Order = 8, Title = "자연어·코드 표시 (Alt)",
                Summary = "도감과 로그의 설명은 기본이 자연어입니다. Alt를 누르고 있는 동안 코드 형태로 바뀝니다.",
                Body = "조건 요약, 수치 반영 규칙, 타임라인 기록 등은 평소에 자연어 문장으로 보입니다. Alt 키를 누르고 있는 동안에는 `kills_total >= 10` 같은 코드 형태로 바뀝니다. 키를 떼면 다시 자연어로 돌아옵니다.",
            };
            yield return new HelpTopic
            {
                Id = "guide:explorer", Category = "안내", Order = 9, Title = "탐색기",
                Summary = "클래스·리스트·딕셔너리를 폴더처럼 열어 보고 고치는 공용 화면입니다.",
                Body = "도감 항목, 템플릿, 현재 세션의 값, 이벤트 발생기가 모두 같은 탐색기를 씁니다.\n\n" +
                       "## 사용법\n" +
                       "- 클래스나 목록을 클릭하면 폴더처럼 안으로 들어갑니다. 위쪽 경로를 눌러 되돌아갑니다.\n" +
                       "- 리스트는 줄 추가·삭제, 딕셔너리는 한 줄에 키와 값 한 쌍으로 표시됩니다.\n" +
                       "- 클래스 목록은 카드 격자로 볼 수 있습니다.\n" +
                       "- 필드 규칙이 걸린 칸은 드롭다운으로 고릅니다.\n" +
                       "- 바꾼 값은 '적용'을 눌러야 반영됩니다.",
                Related = new[] { "guide:layout", "data:" + typeof(FieldRuleData).FullName },
            };
            yield return new HelpTopic
            {
                Id = "guide:layout", Category = "안내", Order = 10, Title = "상세 화면 배치",
                Summary = "종류마다 상세 화면에 어떤 블록을 어디에 둘지 정합니다.",
                Body = "상세 화면은 12칸 격자 위에 블록을 배치해 그립니다. 종류마다 기본 배치가 있으며, '배치 편집'에서 바꿀 수 있습니다.\n\n" +
                       "## 블록\n" +
                       "- 스프라이트 나열, 데이터 나열, 특정 스프라이트, 필드 카드, 클래스 카드 등이 있습니다.\n" +
                       "- 다른 블록이 따로 보여 주는 필드는 '데이터 나열'에서 빠집니다.\n\n" +
                       "배치는 ProjectSettings/GameSystems/Layouts.json에 저장되어 팀 전체가 공유합니다.\n\n" +
                       "## 새 블록 만들기(프로그래머)\n" +
                       "GsLayoutBlock을 상속하고 [LayoutBlock(\"id\", \"이름\", Description = \"설명\")]을 붙이면 목록에 나타납니다. Description은 필수입니다.",
            };
            yield return new HelpTopic
            {
                Id = "guide:validation", Category = "안내", Order = 11, Title = "검증",
                Summary = "도감 전체를 검사해 오류와 경고를 보여 줍니다. 오류가 있으면 빌드가 막힙니다.",
                Body = "도감이 바뀔 때마다 다음 항목을 자동으로 검사합니다.\n\n" +
                       "- ID 형식과 중복\n" +
                       "- 끊긴 참조(지워진 항목을 가리키는 값)\n" +
                       "- 필드 규칙의 필수 값 누락\n" +
                       "- 조건식의 필드 경로\n" +
                       "- 스프라이트 예외 촬영의 확인 필요 표시\n\n" +
                       "결과는 도감 왼쪽 아래에 표시됩니다. 검색 칸에 `invalid`를 입력하면 오류가 있는 항목만 볼 수 있습니다.",
            };
            yield return new HelpTopic
            {
                Id = "guide:time", Category = "안내", Order = 12, Title = "시간 제어",
                Summary = "플레이 중 일시정지, 한 프레임 진행, 배속을 조절합니다.",
                Body = "모니터 창 위쪽의 시간 조절 버튼으로 게임 시간을 다룹니다(플레이 중에만 보입니다).\n\n" +
                       "- 일시정지: 게임 시간을 멈춥니다. 에디터 UI는 계속 동작합니다.\n" +
                       "- 한 프레임: 일시정지 상태에서 한 프레임만 진행합니다.\n" +
                       "- 배속: 느리게·빠르게 진행합니다.\n\n" +
                       "코드에서는 `GameTime.Pause(이유)`, `GameTime.SetSpeed(배율)`을 씁니다. 여러 곳에서 동시에 일시정지를 걸어도 이유별로 따로 풀립니다.",
                Related = new[] { "window:monitor" },
            };
            yield return new HelpTopic
            {
                Id = "window:monitor", Category = "안내", Order = 13, Title = "모니터 창",
                Summary = "플레이 중 게임 상태를 보고, 값을 고치고, 이벤트·명령을 실행하는 창입니다.",
                Body = "메뉴 Game Systems › 모니터로 엽니다. 플레이 모드에서 세션이 시작되면 내용이 채워집니다.\n\n" +
                       "## 탭\n" +
                       "- 타임라인: 일어난 일의 기록\n" +
                       "- 데이터: 현재 저장소의 모든 값\n" +
                       "- 도전과제·수치: 진행 상황\n" +
                       "- 이벤트: 이벤트를 직접 발생\n" +
                       "- 행동: 실행된 행동 목록\n" +
                       "- 명령: 디버그 명령 버튼\n\n" +
                       "위쪽 'Undo' 버튼은 마지막 행동(또는 스냅샷)을 되돌립니다.",
                Related = new[] { "guide:time", "guide:actions" },
            };

            // ── 도감 종류 ───────────────────────────────────────
            yield return Data<StateKeyData>("플레이어 데이터",
                "저장소에 담을 값 하나를 정의합니다. ID가 곧 키이고, 값 형식과 구역을 정합니다.",
                "플레이어 데이터는 게임 중 바뀌는 값의 이름표입니다. 여기서 만든 키는 게임이 시작될 때 저장소에 자동으로 등록됩니다.\n\n" +
                "## 필드\n" +
                "- 구역: 이번 판(새 판에서 초기화) 또는 프로필(계속 유지).\n" +
                "- 값 형식: 숫자·글자·참거짓·enum 같은 단순 값, 또는 프로젝트의 클래스·리스트·딕셔너리.\n" +
                "- 기본값: 단순 값일 때만 입력합니다. 클래스는 그 클래스의 초기값을 씁니다.\n\n" +
                "## 코드에서\n" +
                "`State.Get(GameIds.PlayerData.Gold)`로 읽고, `State.Set(...)`이나 `State.Modify(...)`로 바꿉니다.",
                "guide:store", "guide:ids");
            yield return Data<KeywordGroupData>("키워드 그룹",
                "키워드의 부모입니다. 필드에 그룹을 연결하면 그 필드는 그룹의 키워드에서 고릅니다.",
                "식별용 string 값(등급·부위·태그 등)은 모두 키워드이고, 키워드는 그룹 아래에 둡니다.\n\n" +
                "## 예\n" +
                "- 등급: 일반·레어·전설 → 장비의 등급(string) 필드에 연결하면 하나만 고릅니다.\n" +
                "- 장비 태그: 근접·원거리·도트 → 장비의 태그(string 목록) 필드에 연결하면 여러 개를 고릅니다.\n\n" +
                "하나인지 여러 개인지는 필드 형식으로 정해집니다. 연결은 보통 '키워드 연결' 창에서 합니다. 상세 화면의 '키워드' 블록에서 키워드를 추가하고, 이 그룹을 쓰는 필드를 확인할 수 있습니다.\n\n" +
                "코드에서는 `GameIds.Keyword.그룹.키워드` 형태로 씁니다(예: `GameIds.Keyword.Grade.Legendary`).",
                "window:keywords", "data:" + typeof(KeywordData).FullName);
            yield return Data<KeywordData>("키워드",
                "식별용 값 하나입니다. 필드에는 키워드의 ID가 저장됩니다.",
                "키워드는 반드시 하나의 그룹에 속합니다. 필드에는 ID가 저장되므로 표시 이름을 바꿔도 기존 값은 그대로입니다. ID를 바꿀 때는 'ID 바꾸기'를 쓰면 참조가 함께 바뀝니다.\n\n" +
                "조건식에서는 `[태그] [포함] [boss]`, `[등급] [=] [legendary]`처럼 비교합니다. 도감 검색의 `tag:이름`은 도감 항목의 기본 태그 칸을 거릅니다.\n\n" +
                "여러 개를 한 번에 추가하려면 키워드 그룹 상세 화면이나 키워드 연결 창의 '+ 키워드'를 쓰세요. 한 줄에 'ID 표시이름'을 하나씩 입력합니다.",
                "data:" + typeof(KeywordGroupData).FullName, "guide:conditions");
            yield return new HelpTopic
            {
                Id = "window:keywords", Category = "안내", Order = 3, Title = "키워드 연결 창",
                Summary = "필드와 키워드 그룹을 두 창에서 함께 보며 연결합니다. 메뉴 Game Systems › 키워드 연결.",
                Body = "왼쪽에는 키워드를 담을 수 있는 필드(string, string 목록)가 클래스별로, 오른쪽에는 키워드 그룹이 나옵니다.\n\n" +
                       "## 기본 흐름\n" +
                       "- 왼쪽에서 필드를 고릅니다.\n" +
                       "- 오른쪽에서 알맞은 그룹의 '이 필드에 연결'을 누릅니다.\n" +
                       "- 알맞은 그룹이 없으면 왼쪽 아래 '+ 새 그룹 만들어 연결'로 그룹과 키워드를 한 번에 만들고 연결합니다.\n\n" +
                       "## 알아 둘 점\n" +
                       "- string 필드는 하나, string 목록 필드는 여러 개를 고릅니다.\n" +
                       "- 연결하면 필드 규칙이 자동으로 만들어집니다. 필수 여부처럼 추가 설정이 필요하면 도감의 필드 규칙에서 고칩니다.\n" +
                       "- '모든 키워드 허용'은 그룹을 정하지 않고 모든 키워드에서 고르게 합니다.\n" +
                       "- '연결 안 된 것만'을 켜면 아직 규칙이 없는 필드만 보입니다.\n" +
                       "- 편집 칸에서 오른쪽 클릭 › '키워드 연결…'로도 이 창을 열 수 있습니다. 키워드 드롭다운 끝의 '+ 새 키워드'는 연결된 그룹에 바로 추가합니다.",
                Related = new[] { "data:" + typeof(KeywordGroupData).FullName, "data:" + typeof(KeywordData).FullName, "data:" + typeof(FieldRuleData).FullName },
            };
            yield return Data<FieldRuleData>("필드 규칙",
                "'A 클래스의 a 필드는 B에서 고른다'를 코드 없이 연결합니다. 키워드 연결 창이 자동으로 만듭니다.",
                "필드 규칙은 클래스의 string 필드에 값의 출처를 연결합니다. 규칙이 걸린 칸은 도감·탐색기·이벤트 발생기·조건식 어디서든 드롭다운이 되고, ID 바꾸기와 검증도 이 규칙을 따릅니다.\n\n" +
                "키워드 연결은 대부분 '키워드 연결' 창에서 하며, 이 탭은 확인과 유지보수용입니다.\n\n" +
                "## 필드\n" +
                "- 클래스, 필드: 규칙을 걸 대상. 상속받은 필드는 선언한 클래스에 걸리며 모든 하위 클래스에 적용됩니다.\n" +
                "- 값 출처: 키워드, 도감 항목, 플레이어 데이터, 자유 입력 중 하나.\n" +
                "- 키워드 그룹 / 도감 종류: 출처에 따라 고를 범위. 키워드 그룹을 비우면 모든 키워드에서 고릅니다.\n" +
                "- 필수: 켜면 값이 비었을 때 검증 오류가 납니다. 모니터의 데이터 탭에도 '빈 필수 값'으로 표시됩니다.\n\n" +
                "## 스프라이트 필드\n" +
                "Sprite 필드에는 '스프라이트 규격 묶음'과 '미리보기 규격'을 연결합니다. 원본을 넣으면 맞춤 창이 열리고, 묶음의 모든 규격으로 잘라 저장됩니다.",
                "window:keywords", "data:" + typeof(SpritePresetGroupData).FullName);
            yield return Data<SpritePresetGroupData>("스프라이트 규격 묶음",
                "한 용도의 스프라이트 규격들을 묶습니다(예: 캐릭터 일러 = 스탠딩A·스탠딩B·아이콘).",
                "규격 묶음은 원본 이미지 하나에서 잘라 낼 크기들의 모음입니다. 필드 규칙으로 스프라이트 필드에 묶음을 연결하면, 그 필드에 원본을 넣을 때 묶음의 모든 규격이 한 번에 만들어집니다.\n\n" +
                "## 흐름\n" +
                "- 묶음을 만들고, '스프라이트 규격'에서 이 묶음에 속한 크기를 추가합니다.\n" +
                "- 필드 규칙에서 스프라이트 필드에 이 묶음을 연결합니다.\n" +
                "- 탐색기에서 그 필드에 원본을 넣으면 맞춤 창이 열립니다. 클릭으로 중심, 휠로 배율을 정하고 '적용'을 누릅니다.\n\n" +
                "결과는 Assets/GameSystemsData/Sprites 아래에 PNG로 저장됩니다. 게임 코드에서는 `SpriteSpecs.Get(원본, 규격 ID)`로 가져옵니다.",
                "data:" + typeof(SpritePresetData).FullName, "data:" + typeof(FieldRuleData).FullName);
            yield return Data<SpritePresetData>("스프라이트 규격",
                "출력 크기 하나입니다. 크기를 바꾸면 이 규격으로 만든 이미지가 모두 다시 만들어집니다.",
                "규격은 묶음과 가로·세로 픽셀 크기로 정의합니다.\n\n" +
                "## 예외 촬영\n" +
                "맞춤 창에서 특정 규격만 따로 위치를 잡을 수 있습니다. 이 기록은 영구 보관됩니다. 나중에 규격 크기가 바뀌면 해당 예외에 '확인 필요'가 표시되므로 다시 잡아 주세요.\n\n" +
                "상세 화면의 '규격 사용 현황'에서 이 규격으로 만든 이미지 수와 예외 목록을 확인하고 바로 다시 맞출 수 있습니다.",
                "data:" + typeof(SpritePresetGroupData).FullName);
            yield return Data<StatData>("수치",
                "처치 수·획득 골드·최고 콤보처럼 이벤트에서 자동으로 집계되는 값입니다.",
                "수치는 이벤트가 발생할 때 반영 규칙에 따라 자동으로 올라갑니다. 게임 코드는 이벤트만 발생시키면 됩니다.\n\n" +
                "## 필드\n" +
                "- 범위: 이번 판(새 판에서 초기화) 또는 누적.\n" +
                "- 집계 방식: 합계, 최고치, 최저치, 마지막 값.\n" +
                "- 반영 규칙: 어떤 이벤트의 어떤 값을 몇 배로 반영할지 정합니다. 조건을 걸어 특정 이벤트만 셀 수 있습니다(예: 태그 boss 포함 → 보스 처치만).\n\n" +
                "반영할 값을 비워 두면 이벤트 횟수를 셉니다.",
                "data:" + typeof(AchievementData).FullName, "monitor:Stats");
            yield return Data<AchievementData>("도전과제",
                "조건을 만족하면 즉시 달성되고 보상이 지급됩니다. 달성은 Undo로 되돌리지 않습니다.",
                "도전과제는 달성 조건과 보상으로 이루어집니다. 조건은 수치·상태 값이 바뀔 때마다 검사되며, 만족하는 순간 달성됩니다.\n\n" +
                "## 필드\n" +
                "- 달성 조건: 조건식 블록으로 조립합니다. 중첩한 AND/OR/NOT을 쓸 수 있습니다.\n" +
                "- 보상: 달성 시 지급할 항목. 수치 획득 등이 있습니다.\n\n" +
                "달성과 보상은 프로필에 저장되고, 행동을 Undo해도 유지됩니다.",
                "guide:conditions", "monitor:Achievements");
            yield return Data<TemplateData>("템플릿",
                "프로젝트 클래스의 미리 채운 인스턴스입니다(더미 아이템, 테스트용 몬스터 설정 등).",
                "템플릿은 코드로 만든 클래스에 값을 미리 채워 둔 것입니다. 형식을 고르면 빈 인스턴스가 만들어지고, 탐색기에서 값을 채웁니다.\n\n" +
                "게임이나 테스트 코드에서는 `CreateInstance`로 사본을 받습니다. 원본은 바뀌지 않습니다.",
                "guide:explorer", "data:" + typeof(PrefabData).FullName);
            yield return Data<PrefabData>("프리팹",
                "도감에 등록한 프리팹입니다. 컴포넌트 필드를 탐색기로 보고 고칩니다.",
                "프리팹 항목은 프리팹 에셋에 연결됩니다. 상세 화면의 '프리팹 컴포넌트' 블록에서 스크립트 컴포넌트의 필드를 고치면 프리팹 에셋에 바로 저장됩니다(Undo 가능).\n\n" +
                "## 삭제\n" +
                "도감에서는 지울 수 없습니다. '프로젝트에서 보기'로 에셋 위치로 이동해 프로젝트 창에서 지우세요. 에셋이 지워지면 이 항목도 자동으로 정리됩니다.");

            // ── 모니터 탭 ───────────────────────────────────────
            yield return Monitor("Timeline", "타임라인", 0,
                "게임에서 일어난 일을 시간 순서로 보여 줍니다. 원인별로 묶어 펼쳐 볼 수 있습니다.",
                "행동, 이벤트, 값 변경, 도전과제 달성, 스냅샷, Undo가 모두 기록됩니다.\n\n" +
                "## 보기\n" +
                "- 묶어 보기: 한 행동이 일으킨 변경들이 그 행동 아래에 접혀 있습니다. 화살표를 눌러 펼칩니다.\n" +
                "- 기록을 선택하면 원인과 결과의 연결이 강조됩니다.\n" +
                "- 기록에 담긴 값은 '열기'로 탐색기에서 봅니다.\n" +
                "- 플레이어 데이터로 거르면 그 값과 관련된 기록만 남습니다.\n" +
                "- 검색 칸은 자연어와 코드 형태 모두로 찾습니다.\n\n" +
                "Undo를 하면 'Undo' 기록 아래에 되돌린 값들이 묶여 남습니다.");
            yield return Monitor("State", "데이터", 1,
                "현재 세션과 프로필의 모든 값을 탐색기로 보고 고칩니다.",
                "저장소의 값이 폴더로 나뉘어 표시됩니다.\n\n" +
                "- 빈 필수 값: 필드 규칙에서 필수로 정했지만 비어 있는 값\n" +
                "- 판 데이터 / 프로필 데이터: 구역별 플레이어 데이터 값\n" +
                "- 수치, 도전과제\n\n" +
                "여기서 고친 값도 `Set`을 거치므로 타임라인에 기록되고 Undo할 수 있습니다.",
                "guide:explorer", "guide:store");
            yield return Monitor("Achievements", "도전과제", 2,
                "도전과제별 달성 여부와 진행도를 보고, 테스트용으로 달성 처리합니다.",
                "카드마다 조건 문장과 현재 진행도(현재 값 / 목표)가 표시됩니다. 카드를 선택하면 오른쪽 아래에서 다음을 실행할 수 있습니다.\n\n" +
                "- 이벤트로 달성: 부족한 수치만큼 실제 이벤트를 발생시킵니다(게임과 같은 경로). Undo하면 수치는 돌아가고 달성은 유지됩니다.\n" +
                "- 강제 달성: 조건·이벤트 없이 달성 처리만 합니다.\n" +
                "- 초기화: 달성 표시만 지웁니다. 수치는 그대로입니다.",
                "data:" + typeof(AchievementData).FullName);
            yield return Monitor("Stats", "수치", 3,
                "수치의 현재 값을 보고, 테스트용으로 값을 바꿉니다.",
                "표에서 수치를 고르면 아래에서 값을 바꿀 수 있습니다.\n\n" +
                "- 이벤트로 더하기(기록): 반영 규칙에 맞는 실제 이벤트를 발생시킵니다. 다른 수치와 도전과제도 게임에서처럼 함께 움직입니다.\n" +
                "- 직접 수정: 값만 덮어씁니다. 이벤트·다른 수치·반영 규칙은 움직이지 않습니다.",
                "data:" + typeof(StatData).FullName);
            yield return Monitor("Events", "이벤트", 4,
                "이벤트를 골라 값을 채우고 직접 발생시킵니다.",
                "게임 코드를 거치지 않고 이벤트를 발생시켜 수치 반영이나 도전과제를 시험할 때 씁니다. 이벤트의 필드는 탐색기로 채우며, 필드 규칙이 걸린 칸은 드롭다운으로 고릅니다.",
                "guide:explorer");
            yield return Monitor("Actions", "행동", 5,
                "`session.Do`로 실행한 행동의 목록과 상태(완료·되돌림·실패)를 보여 줍니다.",
                "행동마다 실행 결과가 표시됩니다. 되돌린 행동은 '되돌림'으로 남습니다.",
                "guide:actions");
            yield return Monitor("Commands", "명령", 6,
                "게임 코드가 등록한 디버그 명령을 버튼으로 실행합니다.",
                "코드에서 `session.Debug`에 명령을 등록하면 여기 버튼으로 나타납니다. 버튼에 마우스를 올리면 명령 설명이 보입니다.");
        }

        static HelpTopic Data<T>(string title, string summary, string body, params string[] related) where T : GameData => new HelpTopic
        {
            Id = HelpIds.Data(typeof(T)),
            Category = "도감 종류",
            Order = DataTypes.Find(typeof(T))?.Order ?? 100,
            Title = title,
            Summary = summary,
            Body = body,
            Related = related,
        };

        static HelpTopic Monitor(string tab, string title, int order, string summary, string body, params string[] related) => new HelpTopic
        {
            Id = HelpIds.Monitor(tab),
            Category = "모니터 탭",
            Order = order,
            Title = title,
            Summary = summary,
            Body = body,
            Related = related,
        };
    }
}
