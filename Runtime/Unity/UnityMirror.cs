using UnityEngine;

namespace GBible
{
    // Unity 구조체(Vector3·Color …)를 Mirror에 등록한다. Mirror.Initialize가 __gb_register를 찾아 부른다(게임 시작 때 한 번).
    // 이 형식들은 Unity 어셈블리에 있어 생성기가 메서드를 넣을 수 없으므로 여기서 손으로 쓴다.
    // 리플렉션으로만 불리므로 코드 줄이기(Managed Stripping Level)가 지우지 않게 [Preserve]를 붙인다(생성기가 만든 등록 메서드와 같다).
    // 성분은 필드처럼 비트로 비교한다(MirrorCode.Same — NaN은 같고 0과 -0은 다르다. == 는 1e-5 근사라 작은 변화를 놓치고 NaN 성분은 늘 다르다고 본다).
    static class UnityMirror
    {
        static bool Same(float a, float b) => MirrorCode.Same(a, b);
        static bool Same(Vector3 a, Vector3 b) => Same(a.x, b.x) && Same(a.y, b.y) && Same(a.z, b.z);

        [UnityEngine.Scripting.Preserve]
        static void __gb_register()
        {
            ValueOps<Vector2>.Register((a, b) => Same(a.x, b.x) && Same(a.y, b.y), (w, v) => { w.Float(v.x); w.Float(v.y); }, r => new Vector2(r.Float(), r.Float()));
            ValueOps<Vector3>.Register(Same, (w, v) => { w.Float(v.x); w.Float(v.y); w.Float(v.z); }, r => new Vector3(r.Float(), r.Float(), r.Float()));
            ValueOps<Vector4>.Register((a, b) => Same(a.x, b.x) && Same(a.y, b.y) && Same(a.z, b.z) && Same(a.w, b.w), (w, v) => { w.Float(v.x); w.Float(v.y); w.Float(v.z); w.Float(v.w); },
                r => new Vector4(r.Float(), r.Float(), r.Float(), r.Float()));
            ValueOps<Quaternion>.Register((a, b) => Same(a.x, b.x) && Same(a.y, b.y) && Same(a.z, b.z) && Same(a.w, b.w),
                (w, v) => { w.Float(v.x); w.Float(v.y); w.Float(v.z); w.Float(v.w); }, r => new Quaternion(r.Float(), r.Float(), r.Float(), r.Float()));
            ValueOps<Vector2Int>.Register((a, b) => a == b, (w, v) => { w.Int(v.x); w.Int(v.y); }, r => new Vector2Int(r.Int(), r.Int()));
            ValueOps<Vector3Int>.Register((a, b) => a == b, (w, v) => { w.Int(v.x); w.Int(v.y); w.Int(v.z); }, r => new Vector3Int(r.Int(), r.Int(), r.Int()));
            ValueOps<Color>.Register((a, b) => Same(a.r, b.r) && Same(a.g, b.g) && Same(a.b, b.b) && Same(a.a, b.a), (w, v) => { w.Float(v.r); w.Float(v.g); w.Float(v.b); w.Float(v.a); },
                r => new Color(r.Float(), r.Float(), r.Float(), r.Float()));
            ValueOps<Color32>.Register((a, b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a,
                (w, v) => { w.Byte(v.r); w.Byte(v.g); w.Byte(v.b); w.Byte(v.a); }, r => new Color32(r.Byte(), r.Byte(), r.Byte(), r.Byte()));
            ValueOps<Rect>.Register((a, b) => Same(a.x, b.x) && Same(a.y, b.y) && Same(a.width, b.width) && Same(a.height, b.height), (w, v) => { w.Float(v.x); w.Float(v.y); w.Float(v.width); w.Float(v.height); },
                r => new Rect(r.Float(), r.Float(), r.Float(), r.Float()));
            ValueOps<RectInt>.Register((a, b) => a.Equals(b), (w, v) => { w.Int(v.x); w.Int(v.y); w.Int(v.width); w.Int(v.height); },
                r => new RectInt(r.Int(), r.Int(), r.Int(), r.Int()));
            ValueOps<Bounds>.Register((a, b) => Same(a.center, b.center) && Same(a.extents, b.extents), (w, v) =>
            {
                w.Float(v.center.x); w.Float(v.center.y); w.Float(v.center.z);
                w.Float(v.size.x); w.Float(v.size.y); w.Float(v.size.z);
            }, r => new Bounds(new Vector3(r.Float(), r.Float(), r.Float()), new Vector3(r.Float(), r.Float(), r.Float())));
        }
    }
}
