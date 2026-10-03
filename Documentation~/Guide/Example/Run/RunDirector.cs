// 카드 던전 — 런 스크립트(씬의 MonoBehaviour). 입력을 행동으로 바꿔 보내는 일만 한다.
// 화면은 데이터를 읽어 그리기만 하고, 값을 바꾸는 일은 모두 행동(Game.Do)으로 한다.
using GBible;
using GBible.Ids;
using UnityEngine;

namespace CardDungeon
{
    public class RunDirector : MonoBehaviour
    {
        static GameApi Game => GBibleRuntime.Game;

        void Start()
        {
            // 저장된 판이 있으면 이어 하고, 없으면 새 판.
            if (!GBibleRuntime.LoadRun(GBibleRuntime.Session) || !Game.Data().Run.IsActive) Game.Do(new StartRun());
        }

        // 버튼·드래그 같은 입력이 부르는 창구. 실패(에너지 부족 등)는 예외로 오고, 바뀐 값은 모두 취소되어 있다.
        public bool TryPlay(string cardId, string targetId, out string error)
        {
            error = null;
            try
            {
                Game.Do(new PlayCard { target = cardId, targetId = targetId });
                return true;
            }
            catch (System.InvalidOperationException e)
            {
                error = e.Message;
                return false;
            }
        }

        public void EndTurn() => Game.Do(new EndTurn());

        public void Undo() => GBibleRuntime.Session.Undo();

        void OnApplicationPause(bool paused)
        {
            if (paused) GBibleRuntime.SaveRun(GBibleRuntime.Session);
        }
    }
}
