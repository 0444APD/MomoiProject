using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GBible
{
    // 변경 기록 한 줄의 종류. 경로 끝(필드·요소·컬렉션)에서 무엇을 했는지.
    public enum MirrorOp : byte
    {
        Set = 0,        // 값을 바꾼다(숫자·글자·객체 통째·null)
        ListAdd = 1,    // 리스트 끝에 n개를 더한다
        ListCut = 2,    // 리스트를 n개로 줄인다
        DictSet = 3,    // 딕셔너리 키에 값을 넣는다(새 키)
        DictRemove = 4, // 딕셔너리 키를 뺀다
        Layout = 5,     // 객체 리스트의 배치를 바꾼다(남은 요소는 옛 번호 구간으로, 새 요소는 통째로)
    }

    // ChangeSet: Sync 한 번에서 바뀐 곳 전체.
    //   Forward  = 바뀐 곳마다 경로 + 새 값. 받는 쪽(다른 플레이어)에 보내 그대로 적용하면 같아진다.
    //   Backward = 바뀐 곳마다 경로 + 옛 값. 거꾸로 적용하면 Sync 전으로 돌아간다(되돌리기).
    // 경로는 필드 번호·리스트 번호·딕셔너리 키라서 짧다(공격 한 번이 수십 바이트).
    // Describe가 켜져 있으면 사람이 읽는 변경 목록(Changes: 경로 이름·옛 값·새 값)도 함께 만든다(기록·모니터용).
    public sealed class ChangeSet
    {
        enum SegKind : byte
        {
            Field = 0,
            Index = 1,
            Key = 2,
        }

        struct Seg
        {
            public SegKind Kind;
            public int Index;
            public string Name;
            public object Key;
            public object Element;
            public Action<MirrorWriter, object> WriteKey;
        }

        public string Key;
        public MirrorWriter Forward = new MirrorWriter();
        public MirrorWriter Backward = new MirrorWriter();
        public readonly List<int> BackwardStarts = new List<int>();
        public int Count;
        public bool Describe;
        public readonly List<DataChange> Changes = new List<DataChange>();
        // 사람이 읽는 경로에서 리스트 요소(객체)를 번호 대신 내부 ID로 보일 때(예: items[#12].level). 저장소가 채운다.
        public Func<object, long> ElementId;
        // 뒤로 기록에 쓴 원래 객체들(빠지거나 갈아 끼워진 객체). 되돌릴 때 새로 만들지 않고 이 인스턴스에 값을 다시 채운다
        // (게임 코드가 쥔 인스턴스가 그대로 돌아온다).
        public readonly List<object> Originals = new List<object>();

        Seg[] _path = new Seg[32];
        int _depth;

        public bool IsEmpty => Count == 0;

        public void Clear()
        {
            Forward.Clear();
            Backward.Clear();
            BackwardStarts.Clear();
            Changes.Clear();
            Originals.Clear();
            Count = 0;
            _depth = 0;
        }

        // 지금 내용을 새 ChangeSet으로 떼어 내고(정확한 크기로 복사) 이것은 비운다. 계속 쓰는 작업용 기록에서 결과만 남길 때.
        public ChangeSet Detach()
        {
            var copy = new ChangeSet
            {
                Key = Key,
                Forward = new MirrorWriter(Forward.Length),
                Backward = new MirrorWriter(Backward.Length),
                Count = Count,
                Describe = Describe,
                ElementId = ElementId,
            };
            copy.Forward.Bytes(Forward.Data, 0, Forward.Length);
            copy.Backward.Bytes(Backward.Data, 0, Backward.Length);
            copy.BackwardStarts.AddRange(BackwardStarts);
            copy.Changes.AddRange(Changes);
            copy.Originals.AddRange(Originals);
            Clear();
            return copy;
        }

        // 같은 데이터의 다음 기록을 이어 붙인다(한 행동 안의 여러 Sync를 Undo 단위 하나로). 원래 객체 번호는 뒤로 밀린다.
        public void Append(ChangeSet next)
        {
            if (next == null || next.IsEmpty) return;
            var shift = Backward.Length;
            Forward.Bytes(next.Forward.Data, 0, next.Forward.Length);
            if (next.Originals.Count > 0 && Originals.Count > 0)
            {
                // 원래 객체 번호(뒤로 기록 안)는 각 기록의 것이라 합치면 겹친다: 합칠 때 다시 쓰지 않고 따로 둔다.
                _appended ??= new List<(int start, int offset)>();
                _appended.Add((shift, Originals.Count));
            }
            Backward.Bytes(next.Backward.Data, 0, next.Backward.Length);
            foreach (var start in next.BackwardStarts) BackwardStarts.Add(start + shift);
            Changes.AddRange(next.Changes);
            Originals.AddRange(next.Originals);
            Count += next.Count;
        }

        List<(int start, int offset)> _appended;

        // 뒤로 기록의 position에 있는 원래 객체 번호가 Originals의 어디서부터인지.
        internal int OriginalsOffset(int position)
        {
            if (_appended == null) return 0;
            var offset = 0;
            foreach (var (start, value) in _appended)
                if (position >= start) offset = value;
            return offset;
        }

        // ── 생성 코드·Mirror가 부르는 경로 쌓기 ──

        void Push(Seg seg)
        {
            if (_depth == _path.Length) Array.Resize(ref _path, _depth * 2);
            _path[_depth++] = seg;
        }

        public void PushField(int index, string name) => Push(new Seg { Kind = SegKind.Field, Index = index, Name = name });
        public void PushIndex(int index, object element = null) => Push(new Seg { Kind = SegKind.Index, Index = index, Element = element });
        public void PushKey(object key, Action<MirrorWriter, object> writeKey) => Push(new Seg { Kind = SegKind.Key, Key = key, WriteKey = writeKey });
        public void Pop() => _depth--;

        // 지금 경로에 기록 한 줄을 시작한다. 이어서 Forward·Backward에 각자의 내용(값)을 쓴다.
        public void Begin(MirrorOp forward, MirrorOp backward)
        {
            Count++;
            WritePath(Forward, forward);
            BackwardStarts.Add(Backward.Length);
            WritePath(Backward, backward);
        }

        void WritePath(MirrorWriter w, MirrorOp op)
        {
            w.Byte((byte)op);
            w.UVar((ulong)_depth);
            for (var i = 0; i < _depth; i++)
            {
                var s = _path[i];
                w.Byte((byte)s.Kind);
                if (s.Kind == SegKind.Key) s.WriteKey(w, s.Key);
                else w.UVar((ulong)s.Index);
            }
        }

        // 사람이 읽는 변경 한 줄(Describe일 때만 부른다).
        public void Note(DataChangeKind kind, object oldValue, object newValue, string extra = null)
        {
            var path = PathText();
            if (extra != null) path += extra;
            Changes.Add(new DataChange { Kind = kind, Path = path, Old = oldValue, New = newValue });
        }

        // 리스트 요소 표시: 객체면 내부 ID([#12]), 아니면 번호([3]).
        public string ElementText(int index, object element) =>
            element != null && ElementId != null && !(element is string) && !element.GetType().IsValueType
                ? $"[#{ElementId(element).ToString(CultureInfo.InvariantCulture)}]"
                : $"[{index.ToString(CultureInfo.InvariantCulture)}]";

        // 예: units[#3].hp, stats[str]
        public string PathText()
        {
            var b = new StringBuilder();
            for (var i = 0; i < _depth; i++)
            {
                var s = _path[i];
                switch (s.Kind)
                {
                    case SegKind.Field:
                        if (b.Length > 0) b.Append('.');
                        b.Append(s.Name);
                        break;
                    case SegKind.Index:
                        b.Append(ElementText(s.Index, s.Element));
                        break;
                    default:
                        b.Append('[').Append(Convert.ToString(s.Key, CultureInfo.InvariantCulture)).Append(']');
                        break;
                }
            }
            return b.ToString();
        }
    }
}
