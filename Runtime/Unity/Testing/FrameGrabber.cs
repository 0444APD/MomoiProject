using System;
using System.Collections;
using UnityEngine;

namespace GBible
{
    // 개발 도구 — 게임 화면(UI 포함)을 일정한 간격으로 잡아 넘긴다. 에디터의 플레이 요청('record' — PlayRequests)이 받아 mp4로 굽는다.
    // 출시 빌드에서는 쓰지 않는다(부르는 곳이 에디터뿐).
    // 영상의 시간은 실제 시간에 맞춘다: 영상 한 칸마다 그때의 화면을 넣고, 게임 프레임이 늦으면 같은 화면을 여러 칸에 넣는다
    // (그래서 0.14초 움직임은 영상에서도 0.14초 — 대신 프레임이 드물면 끊겨 보인다. 실제로 잡은 수는 Grabs).
    public sealed class FrameGrabber : MonoBehaviour
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Total { get; private set; }      // 영상 칸 수(초 × fps)
        public int Frames { get; private set; }     // 지금까지 넘긴 칸
        public int Grabs { get; private set; }      // 실제로 화면을 잡은 수
        public bool Running { get; private set; }

        double _interval;
        Action<Texture2D, double> _frame;
        Action _done;
        RenderTexture _full, _out;
        Texture2D _tex;

        // 녹화 시작(플레이 중에만). 칸마다 frame(그림, 영상 시각) — 그림은 다음 칸에서 다시 쓰므로 그 자리에서 쓴다. 끝나면(또는 플레이가 멈추면) done.
        public static FrameGrabber Begin(float seconds, int fps, int maxWidth, Action<Texture2D, double> frame, Action done)
        {
            var host = new GameObject("[MomoiProject] 녹화") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(host);
            var grabber = host.AddComponent<FrameGrabber>();
            fps = Mathf.Clamp(fps, 1, 60);
            var width = Mathf.Min(maxWidth, Screen.width);
            var height = Mathf.RoundToInt(width * (float)Screen.height / Mathf.Max(1, Screen.width));
            grabber.Width = Mathf.Max(2, width & ~1);     // 영상 부호화는 짝수 크기만
            grabber.Height = Mathf.Max(2, height & ~1);
            grabber.Total = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0.1f, seconds) * fps));
            grabber._interval = 1.0 / fps;
            grabber._frame = frame;
            grabber._done = done;
            grabber.Running = true;
            grabber.StartCoroutine(grabber.Run());
            return grabber;
        }

        IEnumerator Run()
        {
            var endOfFrame = new WaitForEndOfFrame();
            var start = -1.0;
            var next = 0.0;
            while (Frames < Total)
            {
                yield return endOfFrame;
                var now = Time.realtimeSinceStartupAsDouble;
                if (start < 0) start = now;
                var t = now - start;
                if (t + 1e-6 < next) continue;
                Grab();
                while (next <= t + 1e-6 && Frames < Total)
                {
                    _frame?.Invoke(_tex, next);
                    Frames++;
                    next += _interval;
                }
            }
            Stop();
        }

        void Grab()
        {
            if (_full == null || _full.width != Screen.width || _full.height != Screen.height)
            {
                if (_full != null) _full.Release();
                _full = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            }
            if (_out == null) _out = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
            if (_tex == null) _tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;   // Blit이 바꾼다 — 그 전 것으로 되돌린다
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
            // 그래픽 API에 따라 위아래가 뒤집혀 담긴다 — 줄일 때 바로 세운다.
            if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(_full, _out, new Vector2(1, -1), new Vector2(0, 1));
            else Graphics.Blit(_full, _out);
            RenderTexture.active = _out;
            _tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            _tex.Apply(false);
            RenderTexture.active = previous;
            Grabs++;
        }

        // 지금 끝낸다(남은 칸은 넣지 않는다). 여러 번 불러도 한 번만.
        public void Stop()
        {
            if (!Running) return;
            Running = false;
            try { _done?.Invoke(); }
            finally
            {
                Release();
                if (this != null) Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if (Running)
            {
                Running = false;
                _done?.Invoke();
            }
            Release();
        }

        void Release()
        {
            if (RenderTexture.active == _full || RenderTexture.active == _out) RenderTexture.active = null;
            if (_full != null) { _full.Release(); _full = null; }
            if (_out != null) { _out.Release(); _out = null; }
            if (_tex != null) { Destroy(_tex); _tex = null; }
        }
    }
}
