# P24 环境光照预览与验证

2026-10-17。Built-in 管线，参考工程封面及用户提供的公园夜景封面 2。基础照度保持蓝灰／暮紫，局部冷暖灯照亮人物及场景物体。四普通关和三正式结局已布光，教学、水体与远景原图保持原表现；制作入口见[注意事项](../../../注意事项.md#环境光照制作p24)。

| 视图 | 预览 |
|---|---|
| L01 暖色公园，灯下人物 | [受光](L01_PropertyLabyrinth_Lamp_Lit.png)／[关闭受光对照](L01_PropertyLabyrinth_Lamp_Original.png) |
| L02 冷白停车场 | [灯下预览](L02_SequentialGates_Lamp_Lit.png) |
| L03 蓝色配电层 | [灯下预览](L03_ElectricTraverse_Lamp_Lit.png) |
| L04 暮紫高厅 | [入口预览](L04_FoldedWells_Entry_Lit.png) |
| L01 实际资源包运行 | [实体与精神体](L01_PropertyLabyrinth_Runtime_Lit.png) |
| 三结局实际运行 | [仍在下雨](E01_StillRaining_Runtime_Lit.png)／[另一侧](E02_OtherSide_Runtime_Lit.png)／[未展开](E03_Unfolded_Runtime_Lit.png) |

灯下与入口视图是 Unity 相机实际渲染，人物临时放到平台上观察受光，不代表全路线试玩。Runtime 图片来自实际 Play Mode，保留当前画幅和镜头位置；并非新生成的概念图。关闭受光对照保留已有装饰光晕，用于比较人物／地形材质的实际变化。

- [20项 GPU 与生命周期检查](gpu-checks.txt)：原生点光、颜色与距离衰减、聚光锥、透明度、翻转、无设置恢复、退出清理，以及全黑期间撤掉旧灯不改变前景亮度。
- [160项普通流程](level-flow-checks.txt)：真实菜单、教学、探索、模拟、保留／重置、角色复用及转场；[附加运行观察](runtime-observer.txt)确认 Use Existing Build 的实体已用受光材质。
- [51项结局运行](ending-flow-checks.txt)（另有一行 RESULT）：三场进入、移动、跳跃、雨／微粒、暂停、短句、结束返回和保存重播；[受光状态观察](runtime-observer-ending.txt)确认进入与返回清理。
- [装配检查](authoring-checks.txt)及[磁盘资产核对](asset-audit.json)：7,971条原序列化记录保留，已有对象只改变精灵材质，另追加设置和灯光根对象；无新增碰撞、玩法配置修改、缺失引用或重复 GUID。四普通关分别14／16／18／9盏、结局各2盏，灯分散在较高地图的不同区域，非同屏数量。
- [最终定向回归](refine-checks.txt)：另通过既有16项道具自身光效、39项道具交互及像素化 GPU 检查。主工程原 P66 夹具写死旧教学场景，本次仅在隔离夹具中改读当前关序首关；结局夹具也只保留验证部分，去掉旧制作写回步骤。
- [Addressables 构建](addressables-build.txt)成功，打包角色材质已实际加载；[7个缓存文件 SHA256](packed-cache-verification.txt)与主工程一致。旧缓存备份在 `.utmp/P24Lighting/OriginalPackedContent`，主工程 Play Mode 选择未改。已有播放实例仍须退出后重新进入。

本轮未制作实时阴影、法线贴图或水反射；墙体不会挡光，制作时以范围和方向控制穿墙照明。人工仍需核对四关完整通路可读性、最终明暗强度、结局节奏与 Windows 实包性能。专项检查及截图不替代这些签收。
