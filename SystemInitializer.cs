#if GAME_SYSTEMS_READY
using UnityEngine;
using UnityEngine.AddressableAssets;

public static class SystemInitializer
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeBeforeAwake()
    {
        // 1. 어드레서블에서 동기(WaitForCompletion)로 TextTable SO를 로드합니다.
        //    (Addressables 주소가 "TextTable"로 등록되어 있다고 가정)
        TextTable tableSO = Addressables
            .LoadAssetAsync<TextTable>("TextTable")
            .WaitForCompletion();

        if (tableSO != null)
        {
            // 2. 찾은 SO를 넘겨주면 정적 싱글턴이 초기화됩니다.
            TextTable.InitRuntime(tableSO);
        }
        else
        {
            Debug.LogError("[SystemInitializer] TextTable SO 로드 실패. " +
                           "어드레서블에 'TextTable' 주소로 등록되었는지 확인하세요.");
        }

        if (!SaveManager.isManualSaveLoad)
        {
            SaveManager.Load(0);
        }
        else
        {
            Debug.Log("[SystemInitializer] 수동 저장/로드 모드이므로 자동 로드를 건너뜁니다.");
        }
    }
}
#endif
