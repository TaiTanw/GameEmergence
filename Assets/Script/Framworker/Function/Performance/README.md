# 角色动画表现层说明

## 范围

角色动画包括待机、地面行走、疾跑、上升、下落、贴墙以及水底/水面待机和移动。专用推拉动画尚未制作，推拉期间继续使用已有移动切片并面向逻辑层指定的箱子。

Animator Controller 不需要建立 Transition 或参数连线。`PresentationLayer` 通过 `Animator.Play` 直接选择已经关联好切片的 Base Layer 状态：

| 表现语义 | Controller 状态 | 切片类型 |
|---|---|---|
| 待机 | `On` | 循环 |
| 地面移动 | `move` | 循环 |
| 地面疾跑 | `Sprint` | 循环，8 帧 |
| 起跳/重新起跳 | `Jump_Up` | 触发动作，单次 |
| 持续上升 | `OnAir` | 循环 |
| 持续下落 | `OnAir_Down` | 循环 |
| 落地收势 | `Jump_Down` | 触发动作，单次 |
| 贴墙下滑 | `Wall` | 循环 |
| 水底悬浮待机 | `SwimIdle` | 循环，4 帧 |
| 水底移动（上下左右共用） | `Swim` | 循环，6 帧 |
| 水面待机 | `SurfaceSwimIdle` | 循环，4 帧 |
| 水面移动 | `SurfaceSwim` | 循环，6 帧 |
| 飞行 | `Flight` | 既有占位 |

其中 `On` 与 `OnAir` 分别承载 `idle` 与 `OnAir_Up` 切片。五组新序列作为新资源导入，原有 `Swim`、`SurfaceSwim` 占位状态挂上移动片段，再新增疾跑及两种泳待机状态；新增状态没有连线，已有行走起步到行走循环的连线保留。

## 数据流

```text
PlayerStateMachine
  └─ ReadOnly_ActionData
       ├─ NowState / isSwimming / isSurfaceSwimming ► 贴墙、水底与水面行为
       ├─ onMove / verticalInput ─► 地面行走与泳待机/移动、左右朝向
       └─ speedScale ─────────────► 状态机已结算的疾跑加速

CharacterPhysics / BasicEntity
  ├─ ReadOnly_GeometryPhysicsData
  │    └─ isGrounded ─────────────► 地面/空中边界
  └─ ReadOnly_PlayerPhysicsData
       ├─ verticalSpeed ──────────► 主动跳跃冲量检测
       └─ verticalSpeed+phyVSpeed ► 实际上升/下落阶段

PresentationLayer
  └─ Animator.Play / SpriteRenderer.flipX
       （只消费，不回写逻辑或物理）
```

表现层在 `Player.Update` 之后执行，以读取同一渲染帧内已经更新的逻辑输出；物理数据则始终读取最近一次完成的 FixedUpdate 快照。

## 触发动作与持续动作

- 触发动作只在状态边沿开始：物理层确认一次正向主动跳跃冲量时播放 `Jump_Up`，空中/贴墙转为着地时播放 `Jump_Down`。
- 持续动作表达当前行为：地面待机/行走/疾跑、持续上升/下落、贴墙以及水底/水面待机和移动。同一动画保持时不重新调用 `Animator.Play`，让循环正常推进。
- `Jump_Up` 是独立触发动作；完成后若仍在上升则交给 `OnAir`，开始下落或贴墙会提前中断它并立即切换持续状态。
- `Jump_Down` 只在着地后播放，完成后交给 `On`；再次离地、贴墙、跳跃或出现移动意图都会中断落地动作。
- 着地时物理层会把负的主动竖直速度重置为零；代码额外要求跳跃冲量的结果必须为正，避免把该归零误判成跳跃。

## 切换规则

1. 普通地面行为按水平移动意图选择 `On` 或 `move`；有移动意图且逻辑层 `speedScale > 1` 时选择 `Sprint`。体力和疾跑许可由 FSM 结算，表现层不读取原始疾跑键或重新推导许可。
2. FSM 确认贴墙时，播放 `Wall`。
3. 主动跳跃冲量成立时播放一次 `Jump_Up`；起跳动作结束且仍上升时进入 `OnAir`，开始下降时可提前打断。
4. 总竖直速度转为向下时直接进入持续状态 `OnAir_Down`，不再错误播放落地动作。
5. 从空中或贴墙状态进入地面时播放一次 `Jump_Down`；移动或再次跳跃可提前打断，随后进入相应持续状态。
6. 空中跳或墙跳会使主动竖直速度出现正向离散增量，据此从头重播 `Jump_Up`。
7. 顶点附近使用小速度死区并保持上一升降阶段，避免在零速附近反复切换。
8. 墙跳输入与物理结算分属 Update/FixedUpdate；离墙后会等待主动竖直速度产生下一次物理变化，再决定上升或下落，避免闪过错误动画。
9. 飞行、水面游泳、水底游泳优先于几何着地。泳姿由逻辑层状态区分；`onMove` 或 `verticalInput` 超过死区即播放移动，两者为零则播放待机。水流或水面追随造成的被动速度不触发主动划水。
10. 水面向上输入由逻辑层过滤为零；下潜输出负竖直意图，使用同一移动片段。纯上下移动保留已有左右朝向。跃出水面后的跳跃继续使用原有空中与跳跃动作。

## 新资源规格

五张新序列图共 28 个 512×512 切片，以原待机的头部尺度为基准，PPU 为 118，显示尺寸为原 PPU=100 时的 84.75%。原图像素和各帧固定 pivot 保留，不按姿态外框逐帧调整。疾跑每帧 0.08 秒，两种待机每帧 0.24 秒，两种移动每帧 0.16 秒，全部循环。

原生 Unity 资源导入与核对工具为 `CatgirlSprintSwimAssets`；表现层验收入口为 `CatgirlPresentationAnimationChecks.RunForBatch`。导入和验收记录写入 `output/animation-integration-20261008`。物理与逻辑层生产代码保持不变。

## 运行时校验

初始化时会检查全部必需 Animator 状态。任一状态缺失时，组件会输出明确错误并停用自身，避免状态名变更后静默播放错误动画。
