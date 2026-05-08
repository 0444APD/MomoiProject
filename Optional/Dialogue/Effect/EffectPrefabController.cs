#if SYSTEM_DIALOGUE
using UnityEngine;

public class EffectPrefabController : MonoBehaviour {
    // Ÿ�Ӷ��ο��� ���� ����Ʈ�� ����Ǿ�� �� �ð�(��)�� ���޹޽��ϴ�.
    public void SyncTime(float elapsedTime) {
        // [����] ��ƼŬ �ý��� ���� ����ȭ
        // ParticleSystem[] particles = GetComponentsInChildren<ParticleSystem>();
        // foreach (var p in particles) {
        //     p.Simulate(elapsedTime, true, true);
        // }

        // [����] �ִϸ����� ����ȭ
        // Animator anim = GetComponentInChildren<Animator>();
        // anim.Play("EffectClip", 0, elapsedTime / clipLength);
    }
}
#endif