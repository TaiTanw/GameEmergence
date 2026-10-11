using UnityEngine;

/// <summary>关内固定带电，与唯一环境宿主组合；不持有环境性质、背包或可编辑电开关。</summary>
[DisallowMultipleComponent]
public sealed class FixedElectricHazard : MonoBehaviour
{
    public const string ReadOnlyDescription = "固定特性：带电 · 接触致死 · 不可抽取或计算";
    internal bool IsHazardActive => isActiveAndEnabled && GetComponent<BasicPhysicalObject>() is BasicPhysicalObject host
        && host.IsEnvironmentActive;

    void OnEnable() => ElectricContact.Register(this, GetComponent<Collider2D>());
    void OnDisable() => ElectricContact.Unregister(this);

    public bool ValidateConfiguration(out string error)
    {
        error = GetComponents<Collider2D>().Length != 1 || GetComponents<BasicPhysicalObject>().Length != 1
            ? $"{name}：固定带电须与唯一环境宿主、唯一碰撞框在同一物体。" : null;
        return error == null;
    }
    void Start()
    {
        // 安全：错误组合拒绝危险源登记，不寻找其它物体替换。
        if (ValidateConfiguration(out string error)) return;
        Debug.LogError(error, this);
        ElectricContact.Unregister(this);
    }
}
