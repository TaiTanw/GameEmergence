# New2DObject — 2D 运动与接触框架

Unity 2022.3.54f1c1 项目。自研逻辑负责动作、环境效果、预测接触与请求位移；当前仍保留 Unity Dynamic 接触响应。

## 从哪里开始

- **做什么、还有什么问题：** [待办安排](Docs/待办安排.md)
- **玩法循环、道具效果和轻/重刷新规则：** [玩法大纲](Docs/玩法大纲.md)
- **分层与实现边界：** [工程原则](Docs/工程原则.md)
- **为什么改成现在这样：** [历史回顾](Docs/历史回顾.md)
- **文档维护规则：** [文档索引](Docs/README.md)
- **人工操作和名称规则：** [注意事项](注意事项.md)

F5.1–F5.8 已完成：运动快照与环境入口已收敛。F6.1 已整理实体效果数值状态；二维运动公式与多箱上坡仍在后续评估。

当前推箱抖动据用户验证，已通过**角色和箱子的 Rigidbody2D 均启用 Interpolate**暂时解决；后续运动回归以此配置为基线。该结论不代表接触算法或多箱上坡已修复。

## 代码阅读入口

| 模块 | 从这里读 |
|---|---|
| 公共调度 | [Main](Assets/Script/Framworker/Main.cs)、[物理解算器](Assets/Script/Framworker/Function/Physics/Mgr/PhysicsSolverMgr.cs) |
| 输入与角色编排 | [Player](Assets/Script/Framworker/Function/Player/Player.cs)、[InputControlMgr](Assets/Script/Framworker/Function/Player/Mgr/InputControlMgr.cs) |
| 行为策略 | [PlayerStateMachine](Assets/Script/Framworker/Function/Player/FSM/PlayerStateMachine.cs)、[状态类](Assets/Script/Framworker/Function/Player/FSM/BehavioralState.cs) |
| 水与角色生命周期 | [WaterRegion](Assets/Script/Framworker/Function/Physics/Component/SceneObjects/WaterRegion.cs)、[角色能力](Assets/Script/Framworker/Function/Player/PlayerCapabilities.cs)、[死亡/形态/刷新契约](Assets/Script/Framworker/Function/Player/PlayerLifecycleContracts.cs)、[阶段消费者](Assets/Script/Framworker/Function/Player/PlayerLifecycleRules.cs) |
| 运动与提交 | [BasicEntity](Assets/Script/Framworker/Function/Physics/Component/PlayerOBJ/base/BasicEntity.cs)、[EntityEffectState](Assets/Script/Framworker/Function/Physics/Data/EntityEffectState.cs)、[CharacterPhysics](Assets/Script/Framworker/Function/Physics/Component/PlayerOBJ/CharacterPhysics.cs)、[PhysicalBox](Assets/Script/Framworker/Function/Physics/Component/SceneObjects/PhysicalBox.cs) |
| P5.4 框架前置：统一运动参与、接触立即清理与箱子支撑读口 | [实际接口与验证](Docs/待办具体事务文档集中/P_5.4-执行编排.md#9-框架前置整理结果2026-10-07)、[PhysicsSolverMgr](Assets/Script/Framworker/Function/Physics/Mgr/PhysicsSolverMgr.cs)；推拉与收纳玩法尚未开工 |
| 环境来源与关系 | [能力接口](Assets/Script/Framworker/Function/Physics/Interface/IEnvironmentCapabilities.cs)、[EntityEnvironmentContext](Assets/Script/Framworker/Function/Physics/Base/EntityEnvironmentContext.cs)、[EnvironmentCapabilities](Assets/Script/Framworker/Function/Physics/Data/EntityEnvironmentFrame.cs) |
| 数据与表现 | [Struct](Assets/Script/Framworker/Function/Physics/Data/Struct.cs)、[PresentationLayer](Assets/Script/Framworker/Function/Performance/Component/PresentationLayer.cs) |
| Edit Mode 回归 | [EnvironmentIntegrationChecks](Assets/Script/Framworker/Editor/Tool/PhyDataTest/EnvironmentIntegrationChecks.cs) |
| F8 观测与 Play Mode 自动回归 | [维护说明](Docs/待办具体事务文档集中/F_8-物理编辑器观测拆分.md)、[PhysicsEditorObservationChecks](Assets/Script/Framworker/Editor/Tool/PhyDataTest/PhysicsEditorObservationChecks.cs) |
| P5.3 游泳连续运行与输入验证 | [PlayerSwimmingPlayModeChecks](Assets/Script/Framworker/Editor/Tool/PhyDataTest/PlayerSwimmingPlayModeChecks.cs) |
| P5.3 水面泳姿与 V7 请求分量定向检查 | [PlayerSurfaceSwimmingChecks](Assets/Script/Framworker/Editor/Tool/PhyDataTest/PlayerSurfaceSwimmingChecks.cs) |

## 当前物理帧

| 相位 | 职责 |
|---|---|
| 0 | 平台自运动产生 Delta，水按外部配置提交水位 |
| 1 | 实际几何接触与区域重叠查询 |
| 2 | 环境来源核对、脚下与水事实采样、角色左右墙能力解析 |
| 3 | 速度计算，构建 EntityMotionFrame，预测 AABB |
| 4 | 实体接触偏置、环境请求裁剪及有限残余回算；传播阻挡方向后生成后续动态力 |
| 5 | 提交相位 4 已确定的请求位移，一次 MovePosition |

预测和提交共用 `plannedWorldDelta`，但接触偏置、静墙裁剪与引擎修正仍可能改变最终落点。当前接触速度传递主要沿 X 轴，箱链仍是 N=1 基线；不能把 F5 完成理解为多箱解算已完成。

## 运行与验证

- 使用项目指定的 Unity 版本及 New Input System。
- 角色/箱体物理根节点按现有配置冻结旋转，自研重力与 Rigidbody2D 设置配套使用。
- Edit Mode 菜单：`自定义工具/EPhy 验证环境接入（Edit Mode）`；批处理入口：`EnvironmentIntegrationChecks.RunForBatch`。
- 既有完整验证记录：环境接入 148 项及两组共 1080 步单箱 Dynamic 回归通过；不运行输入 FSM 或表现层，也不证明有限箱链位置已闭环。
- 2026-10-05：P_5.1 正常下平台已获用户确认；V_5 密集平台返工已完成，运行时/编辑器脚本离线编译、独立 Unity 项目 14 项定向检查及 12 组连续物理模拟通过。点按落地仍有短暂原生接触纠偏，原场景手感需复验；框架阅读入口与验证边界见 [P_5 专题](Docs/待办具体事务文档集中/P_5-玩家逻辑行为扩展.md)。
- 2026-10-06：P_5.3 当前授权范围及独立水面泳姿已实现，V_6 耗氧周期与 V_7 水流误扣已修复；最新 257 项 Edit Mode、83 项真实 Play Mode（含新增 59 项）、22 项死亡周期、37 项隔离疾跑/改键回归通过。连续游泳使用原预制体 Dynamic/Interpolate、原相位管线和 Unity 2D 模拟；`PlayerSwimmingPlayModeChecks.RunForBatch` 使用临时空场景。水面动画有独立入口，专用素材仍占位；完整阶段、穿墙/认知、关卡/道具重刷新和人工泳感保留后续验收，详见 P_5 专题末节。
