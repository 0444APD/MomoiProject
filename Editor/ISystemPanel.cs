/// <summary>
/// 모든 시스템 패널이 구현해야 하는 인터페이스.
/// 이 인터페이스를 구현한 클래스는 GameSystemsWindow에 자동으로 등록된다.
/// </summary>
public interface ISystemPanel
{
    string Title { get; }
    int Order { get; }

    void OnEnable();
    void OnDisable();
    void OnGUI();
}
