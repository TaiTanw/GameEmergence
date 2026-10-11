/// <summary>机关只提交压住／释放事实；目标自己持有终点、时长和唯一位移入口。</summary>
public interface IMechanismTarget
{
    bool TrySetMechanismActive(bool active);
    void RestoreMechanismDefaults();
}
