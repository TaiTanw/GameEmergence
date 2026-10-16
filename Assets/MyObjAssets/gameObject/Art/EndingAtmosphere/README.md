# orime 结局第一版素材

2026-10-16。四张 PNG 通过内置 imagegen 生成，色调与物件参考工程 `Art/UI/封面.png`。三处场景分别为「仍在下雨」「另一侧」「未展开」；保留蓝灰、暮紫、湿地反光、极少量烛火，以及熟悉房间失去墙顶后的空旷感。没有借用网络游戏图片。

| 文件 | 用途 | 生成源文件 |
|---|---|---|
| ending-rain-room-v1.png | 无墙顶卧室，床、屏幕、烛火与远处重复门框 | exec-c148edbf-536a-46c3-b4b1-3b6de5cbe7eb.png |
| ending-other-side-v1.png | 隔着浅水的白色柱廊，对岸有床 | exec-fcd16242-2fa3-453c-8475-1f408fe5c87f.png |
| ending-unfolded-v1.png | 开敞房间，床、湿书、植物与一道轻微地面折线 | exec-c6d10a62-49c4-43d6-b2cc-75c45243785f.png |
| ending-props-v1.png | 真透明背景的床、湿书、提灯图集，供普通关通关物使用 | exec-f607ad93-58e6-402c-a581-453f11b2dfcd.png |

生成原图均为 1536×1024，原始像素和透明通道保留。内置工具原始输出位于 `C:/Users/16028/.codex/generated_images/01a13e4b-42a4-7d32-888b-2dd222e88b02/`，工程引用只指向本目录。生成用规格整理见 [generation-prompts.json](generation-prompts.json)。

背景按最大边512、Point采样、无 mipmap、无压缩、Single Sprite、Full Rect 导入，固定在场景中；低清来自导入尺度和画面本身。道具按最大边1024、Multiple Sprite 导入，Bed／WetBook／Lantern 三个导入矩形按原图分区里的实际不透明轮廓裁定，仅调整 Unity 精灵元数据，没有重绘图片。三件物品分别使用2.5／1.6／0.7世界宽度，并沿用原通关物的亮度材质与探索光效。

动态雨／缓慢漂浮微粒和微弱灯晕由场景对象叠加，暂停时冻结。角色另用现有动画，新增不透明冷色 `EndingSpiritActor.prefab` 与独立状态机。关卡配置、物件向量及原生预览统一见 [P22 关卡设计](../../../../../Docs/待办具体事务文档集中/P_22-首批关卡设计.md)。
