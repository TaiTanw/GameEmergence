# 夜色平面背景 v1

2026-10-13。使用内置 imagegen 生成，依据用户提供的夜色公园氛围、现有黑发灰白挑染角色与开发者日志 02 的日常／异常意象。三张均为 1024 × 1536、不透明 PNG、2:3 纵向构图。最终完整提示词保存在 [generation-prompts.json](generation-prompts.json)；居室经过一次家具平面化修订。

| 文件 | 用途与视觉判断 |
|---|---|
| [night-park-v1.png](night-park-v1.png) | 最接近所给参考的公园夜色；长椅、草丛、路灯与城市轮廓集中在底部约四分之一 |
| [sleeping-neighborhood-v1.png](sleeping-neighborhood-v1.png) | 日常街区；底部窗灯和围栏稍多，适合较空的关卡 |
| [unwalled-room-v1.png](unwalled-room-v1.png) | 失去墙壁和屋顶的居室远影，呼应日志的熟悉与不安；床仅是背景图案，实际通关道具需保持清楚的轮廓和提示 |

底部比上部细，但整体仍是低对比的平面绘画；中上部只有渐变。已逐张目视检查，并核对尺寸及复制前后 SHA256。尚未在实际关卡完成角色、地形、水体叠加与 Windows 构建观感验收。

## 放入关卡

已配 Single Sprite、Full Rect、Bilinear、Clamp、无 mipmap、无自动物理轮廓，最大尺寸 2048。关闭纹理压缩，优先保留大面积渐变；三张按 RGBA32 估算约 18 MiB 显存。PPU 100，缩放在场景制作时调整。

作为固定在关卡世界中的背景使用，排序低于地形和角色，不附加碰撞器。等比放大到覆盖画面宽度；背景高度不足时从接近纯色的顶部接续天空颜色。不要将整张图上下重复，也不要将整张图跟随镜头移动，否则底部细节会一直留在画面中。本轮只落素材及导入设置，关卡摆放由制作决定。

## 压暗与少量灯光的复杂度评估

工程实查：Unity 2022.3.54f1c1，Built-in 管线，manifest 未引入 URP 或 Post Processing。主游戏镜头常驻；UI 镜头和换关时的 PlayerPortraitCamera 有独立绘制路径。现有 ScenePropLightEffect 可以提亮道具自身并启停柔和光点，现有水体和道具使用自定义 Shader。

以下是基于当前代码的工时估算，包含有限的联验，不是已实施或已测量的耗时。

| 范围 | 复杂度 | 估算 | 主要工作 |
|---|---|---|---|
| 全场景轻微压暗／偏冷 | 低 | 1–3 小时 | 常驻主镜头一个简短颜色处理组件和 Shader；Inspector 调亮度／色调；确认 UI、全黑转场、结局直入与换关角色亮度 |
| 每关少量装饰光晕、光点、自身提亮 | 低 | 1–3 小时 | 使用现有道具效果和少量柔边叠加图形；处理排序、透明水体遮挡、启停与数量 |
| 灯光实际照亮角色或地形，继续 Built-in | 中 | 0.5–1.5 天 | 为需要受光的对象选择／制作受光材质，安排基础照度与灯源位置，核对默认精灵、现有道具和水的差异 |
| 迁移 URP 2D 灯光 | 中高 | 1–3 天或更多 | 管线、材质与自定义 Shader 兼容性、UI／换关镜头、构建回归；当前阶段收益不足以覆盖风险 |

Built-in 支持在相机完成渲染后通过 OnRenderImage 修改最终图像，轻微压暗可只做一次颜色处理，无需引入整套后处理包。见 [Unity 2022.3 OnRenderImage](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MonoBehaviour.OnRenderImage.html)。

默认 Sprites-Default 不响应场景灯光；Built-in 的 Default-Diffuse 可用于实际受光，所以真实照明并不必然要求迁移 URP。现有道具光点、自身提亮与水 Shader 也需要分别判断。见 [Unity 2022.3 Sprite Renderer](https://docs.unity3d.com/2022.3/Documentation/Manual/class-SpriteRenderer.html)。URP 2D 灯光需要相应受光 Shader，已有材质升级范围见 [URP 14 受光材质说明](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/PrepShader.html)。

第 13 天／21 天，按 10 月 18 日之前交基本版本，即 10 月 17 日完成打包候选版来安排。建议只做前两项：镜头亮度先在 0.88–0.95 范围目视试调，每关先放 2–4 处克制的装饰光；实施与联验限定半天，必要时最多一天。当前背景已经较暗，先看原色叠加角色与水的效果，避免黑发、墙缘和危险物一起失去辨识度。后续时间优先留给关卡通路、手感、水体定标、结局内容及 Windows 实包。
