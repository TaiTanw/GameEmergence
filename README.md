# New2DObject

Unity 2022.3.54f1c1 项目。P5.4 操作与 K11 原生物理迁移已实现：角色与箱子同一步模拟，保留六相位和规则时序，删除重复接触解算。自动回归通过；人工手感与已知缺陷原场景仍待核。

## 阅读入口

| 要看什么 | 入口 |
|---|---|
| 当前问题与下一步 | [待办安排](Docs/待办安排.md)，优先看第 11–18 天机关／逃生准备／永久效果闭环 |
| 游戏规则与数据所有权 | [玩法大纲](Docs/玩法大纲.md) |
| 分层、当前时序与收束方向 | [工程原则](Docs/工程原则.md) |
| 推拉／收纳已经实现什么 | [P5.4 摘要](Docs/待办具体事务文档集中/P_5.4-执行编排.md) |
| 玩家能力与后续依赖 | [P5 交接](Docs/待办具体事务文档集中/P_5-玩家逻辑行为扩展.md) |
| 验证记录／全部文档 | [历史回顾](Docs/历史回顾.md)、[文档索引](Docs/README.md) |
| 人工操作与名称约束 | [注意事项](注意事项.md) |

## 代码从哪里读

按要理解的行为读入口，不要求先通读整套物理。

| 要理解什么 | 顺序 |
|---|---|
| 箱子操作为何生效／被拒绝 | [PlayerBoxSelection](Assets/Script/Framworker/Function/Player/PlayerBoxSelection.cs) → [LevelBoxContext](Assets/Script/Framworker/Function/Physics/Component/SceneObjects/LevelBoxContext.cs) → [PhysicalBox](Assets/Script/Framworker/Function/Physics/Component/SceneObjects/PhysicalBox.cs) |
| 每帧输入与规则 | [InputControlMgr](Assets/Script/Framworker/Function/Player/Mgr/InputControlMgr.cs) → [Player](Assets/Script/Framworker/Function/Player/Player.cs) → [PlayerStateMachine](Assets/Script/Framworker/Function/Player/FSM/PlayerStateMachine.cs) |
| 六相位与角色／箱子运动 | [MonoPublicMgr](Assets/Script/Framworker/AOTSystem/Mgr/Scheduling/MonoPublicMgr.cs) → [BasicEntity](Assets/Script/Framworker/Function/Physics/Component/PlayerOBJ/base/BasicEntity.cs) → [CharacterPhysics](Assets/Script/Framworker/Function/Physics/Component/PlayerOBJ/CharacterPhysics.cs) |
| 环境事实与水位 | [EntityEnvironmentContext](Assets/Script/Framworker/Function/Physics/Base/EntityEnvironmentContext.cs)、[WaterRegion](Assets/Script/Framworker/Function/Physics/Component/SceneObjects/WaterRegion.cs) |
| 阶段、认知、逃生与退出 | [SceneChangeMgr](Assets/Script/Framworker/AOTSystem/Mgr/Scheduling/SceneChangeMgr.cs) → [PlayerLifecycleRules](Assets/Script/Framworker/Function/Player/PlayerLifecycleRules.cs) → [LevelCognition](Assets/Script/Framworker/Function/Player/LevelCognition.cs) |
| 逃生准备与最终确认 | [SceneChangeMgr.EscapePreparation](Assets/Script/Framworker/AOTSystem/Mgr/Scheduling/SceneChangeMgr.EscapePreparation.cs) → [EscapeChoiceView](Assets/Script/Framworker/UISystem/UIobject/EscapeChoiceView.cs) → [EscapeHandoff](Assets/Script/Framworker/AOTSystem/Mgr/Scheduling/SceneChangeMgr.EscapeHandoff.cs)；最多三项固定候选、重复购买、独立下一步与负面确认，已实现并通过自动联验 |
| 升级配置、效果确认与进度 | [PlayerUpgradeCatalog](Assets/Script/Framworker/Function/Player/PlayerUpgradeCatalog.cs) → [PlayerEffectLedger](Assets/Script/Framworker/Function/Player/PlayerEffectLedger.cs) → [DataAndInitMgr](Assets/Script/Framworker/AOTSystem/Mgr/MemoryMgr/DataAndInitMgr.cs)；六项等级与普通效果共用记录，关序及已确认永久数据同份保存，本轮自动联验通过 |
| 提示与动画 | [PresentationLayer](Assets/Script/Framworker/Function/Performance/Component/PresentationLayer.cs)、[InteractionHintView](Assets/Script/Framworker/Function/Performance/Component/InteractionHintView.cs) |

K11 顺序为平台／水并同步几何 → 原生接触与探针／区域事实 → 环境／能力／选中 → 合成并写入本步刚体速度 → 统一模拟一次 → 回读实际位置／速度。实际刚体速度是跨帧运动基准；跳跃／墙滑规则仍由行为与执行负责。推拉保留水平贴合与独立竖直运动，收纳和恢复沿用既有事务。

## 运行与验证

使用项目版本与 New Input System；当前角色／箱子开启 Interpolate、物理根节点冻结旋转。冰面与推拉手感未最终定标，只要求可调结构。

玩家基础值沿 `CharacterPhysics.speed`／`upSpeed` 与 `Player.baseAirJumps`；升级使用 [PlayerUpgradeCatalog.asset](Assets/Resources/PlayerUpgradeCatalog.asset)，每项配置开放、上限、固定价格、固定乘法或加减法及每级步长，首批六项合池。小型配置面板已实现并通过 Unity 全工程编译，人工制作入口与独立保存操作见[注意事项](注意事项.md#玩家基础属性与永久升级配置)，窗口操作及原关卡手感仍待人工验收。

环境检查菜单为「自定义工具/EPhy 验证环境接入（Edit Mode）」，批处理入口 EnvironmentIntegrationChecks.RunForBatch；相关行为检查入口见专题。本轮环境、观测、游泳、真实 ICE 场景、箱子与死亡周期检查通过，证据集中在[历史回顾](Docs/历史回顾.md#近期完成与验证)。自动通过不等于人工手感或完整核心循环通过。
