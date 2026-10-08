using UnityEngine;

/// <summary>
/// 摆在场景里的本关配置。只登记是否教学、两个出生点和精神体预设，不创建角色，不抢控制。
/// </summary>
public class SceneLevelSetup : MonoBehaviour
{
    [Header("本关性质")]
    [SerializeField, Tooltip("勾选后本关不创建精神体，实体自己受控")]
    bool teaching;
    [Header("出生点")]
    [SerializeField, Tooltip("实体进入本关后站立的位置")]
    Transform bodySpawn;
    [SerializeField, Tooltip("非教学关精神体的位置，不要和实体出生点重合")]
    Transform spiritSpawn;
    [Header("精神体")]
    [SerializeField, Tooltip("非教学关使用的精神体预设。教学关不会实例化它")]
    GameObject spiritPrefab;

    public bool Teaching => teaching;
    public GameObject SpiritPrefab => spiritPrefab;
    public Vector2 BodyPoint => Point(bodySpawn);
    public Vector2 SpiritPoint => Point(spiritSpawn);

    public static SceneLevelSetup Current { get; private set; }

    void Awake()
    {
        // 实际逻辑：后加载的场景配置覆盖上一关留下的登记。
        if (Current != null && Current != this)
            Debug.LogError("场景里有两份关卡配置，只保留后登记的一份。", this);
        Current = this;
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    Vector2 Point(Transform mark)
    {
        // 安全：没摆出生点时用配置物体自己的位置，避免空引用。
        return mark != null ? (Vector2)mark.position : transform.position;
    }
}
