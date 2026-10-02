using UnityEngine;

namespace GBible
{
    // Unity 구조체(Vector3·Color …)를 Mirror에 등록한다. Mirror.Initialize가 __gb_register를 찾아 부른다(게임 시작 때 한 번).
    // 이 형식들은 Unity 어셈블리에 있어 생성기가 메서드를 넣을 수 없으므로 여기서 손으로 쓴다.
    static class UnityMirror
    {
        static void __gb_register()
        {
            ValueOps<Vector2>.Register((a, b) => a.x.Equals(b.x) && a.y.Equals(b.y), (w, v) => { w.Float(v.x); w.Float(v.y); }, r => new Vector2(r.Float(), r.Float()));
            ValueOps<Vector3>.Register((a, b) => a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z),
                (w, v) => { w.Float(v.x); w.Float(v.y); w.Float(v.z); }, r => new Vector3(r.Float(), r.Float(), r.Float()));
            ValueOps<Vector4>.Register((a, b) => a.Equals(b), (w, v) => { w.Float(v.x); w.Float(v.y); w.Float(v.z); w.Float(v.w); },
                r => new Vector4(r.Float(), r.Float(), r.Float(), r.Float()));
            ValueOps<Quaternion>.Register((a, b) => a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z) && a.w.Equals(b.w),
                (w, v) => { w.Float(v.x); w.Float(v.y); w.Float(v.z); w.Float(v.w); }, r => new Quaternion(r.Float(), r.Float(), r.Float(), r.Float()));
            ValueOps<Vector2Int>.Register((a, b) => a == b, (w, v) => { w.Int(v.x); w.Int(v.y); }, r => new Vector2Int(r.Int(), r.Int()));
            ValueOps<Vector3Int>.Register((a, b) => a == b, (w, v) => { w.Int(v.x); w.Int(v.y); w.Int(v.z); }, r => new Vector3Int(r.Int(), r.Int(), r.Int()));
            ValueOps<Color>.Register((a, b) => a.Equals(b), (w, v) => { w.Float(v.r); w.Float(v.g); w.Float(v.b); w.Float(v.a); },
                r => new Color(r.Float(), r.Float(), r.Float(), r.Float()));
            ValueOps<Color32>.Register((a, b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a,
                (w, v) => { w.Byte(v.r); w.Byte(v.g); w.Byte(v.b); w.Byte(v.a); }, r => new Color32(r.Byte(), r.Byte(), r.Byte(), r.Byte()));
            ValueOps<Rect>.Register((a, b) => a.Equals(b), (w, v) => { w.Float(v.x); w.Float(v.y); w.Float(v.width); w.Float(v.height); },
                r => new Rect(r.Float(), r.Float(), r.Float(), r.Float()));
            ValueOps<RectInt>.Register((a, b) => a.Equals(b), (w, v) => { w.Int(v.x); w.Int(v.y); w.Int(v.width); w.Int(v.height); },
                r => new RectInt(r.Int(), r.Int(), r.Int(), r.Int()));
            ValueOps<Bounds>.Register((a, b) => a == b, (w, v) =>
            {
                w.Float(v.center.x); w.Float(v.center.y); w.Float(v.center.z);
                w.Float(v.size.x); w.Float(v.size.y); w.Float(v.size.z);
            }, r => new Bounds(new Vector3(r.Float(), r.Float(), r.Float()), new Vector3(r.Float(), r.Float(), r.Float())));
        }
    }
}
