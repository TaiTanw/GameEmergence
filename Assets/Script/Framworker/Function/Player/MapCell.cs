using UnityEngine;

/// <summary>手工地图格配置；自身世界坐标为中心，宽高使用世界单位，不参与物理触发。</summary>
public sealed class MapCell : MonoBehaviour
{
    [SerializeField, Min(.01f), Tooltip("世界 X 方向宽度；框线与查询都不乘物体缩放")]
    float width = 5f;
    [SerializeField, Min(.01f), Tooltip("世界 Y 方向高度；矩形始终沿世界 XY 轴")]
    float height = 5f;
    [SerializeField, Min(0f), Tooltip("点亮时占用的认知；取消仅释放当次实际占用")]
    float cognitionCost = 1f;
    [SerializeField, Tooltip("可选特殊标记；地图不描绘格内场景")]
    string marker;

    public Vector2 Center => transform.position;
    public float Width => width;
    public float Height => height;
    public float CognitionCost => cognitionCost;
    public string Marker => marker;
    public Rect WorldRect => new Rect(Center - new Vector2(width, height) * .5f, new Vector2(width, height));

    public bool ValidateConfiguration(out string error)
    {
        error = null;
        // 安全：中心、尺寸和费用须能形成有限且有面积的地图矩形。
        Rect rect = WorldRect;
        if (!Finite(width) || !Finite(height) || width <= 0f || height <= 0f
            || !Finite(cognitionCost) || cognitionCost < 0f || !Finite(rect.xMin) || !Finite(rect.xMax)
            || !Finite(rect.yMin) || !Finite(rect.yMax) || rect.xMin >= rect.xMax || rect.yMin >= rect.yMax)
        {
            error = $"地图格子 {name} 的中心、长宽或认知占用配置无效。";
            return false;
        }
        return true;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!ValidateConfiguration(out _)) return;
        Gizmos.color = new Color(.3f, .85f, .85f, .8f);
        Gizmos.DrawWireCube(transform.position, new Vector3(width, height, 0f));
        Gizmos.DrawSphere(transform.position, .06f);
    }
#endif
}
