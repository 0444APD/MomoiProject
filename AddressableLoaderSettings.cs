#if GAME_SYSTEMS_READY
using UnityEngine;
using System.Collections.Generic;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

[CreateAssetMenu(fileName = "AddressableLoaderSettings", menuName = "Tools/Addressable Loader Settings")]
public class AddressableLoaderSettings : ScriptableObject {
    [Header("��巹���� �׷����� ���� �ֻ��� ���� ��ε�")]
    [Tooltip("������Ʈ ���� ������ ������ �ּ���.")]
#if ODIN_INSPECTOR
    [FolderPath(RequireExistingPath = true)]
#endif
    public List<string> targetRootFolders = new List<string>();
}
#endif