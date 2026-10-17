using UnityEngine;

/// <summary>关内固定触碰致死，与唯一环境宿主组合；形态切换保留危险，不占环境性质槽。</summary>
[DisallowMultipleComponent]
public sealed class FixedContactHazard : MonoBehaviour
{
    public const string ReadOnlyDescription = "固定特性：接触即死 · 不可抽取或计算";
    internal bool IsHazardActive => isActiveAndEnabled && GetComponent<BasicPhysicalObject>() is BasicPhysicalObject host
        && host.IsEnvironmentActive;

    void OnEnable() => LethalContact.Register(this, GetComponent<Collider2D>());
    void OnDisable() => LethalContact.Unregister(this);

    public bool ValidateConfiguration(out string error)
    {
        // 安全：固定危险只接受同物体唯一宿主／几何，不搜索替代关联。
        error = GetComponents<Collider2D>().Length != 1 || GetComponents<BasicPhysicalObject>().Length != 1
            ? $"{name}：固定触碰致死须与唯一环境宿主、唯一碰撞框在同一物体。" : null;
        return error == null;
    }
    void Start()
    {
        // 安全：错误组合拒绝危险源登记，不寻找其它物体替换。
        if (ValidateConfiguration(out string error)) return;
        Debug.LogError(error, this);
        LethalContact.Unregister(this);
    }
}
