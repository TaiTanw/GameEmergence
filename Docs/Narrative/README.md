# 剧情接入：最新五关版本

整合基线：`40d31d2`。保留 P25/P26/P27 的场景、背景视差、操作指南、两页逃生菜单和三个正式结局，不复制旧预览版的菜单覆盖或单一床边结局。

## 文本与入口

- `Assets/Resources/NarrativeSlice.json`：教学 T00–T05；第一关 A00–A02；第二关 B00–B04；第三关 C00–C02；第四关 D00–D03；第五关 F00/F01；首次失败 R00，共 24 个节点。
- `NarrativeFlow`：分页显示、可选书页与行为观察。旁白不显示标签，对话使用中文双引号。空格继续、Esc 跳过当前段；打开页面的按键不直接翻过第一页。
- `SceneChangeMgr.Narrative`：暂停时间、输入与物理，结束或取消后恢复。关卡出口沿原通关演出等待文本结束，再交给原关序和结局选择。
- `DataAndInitMgr`：已读节点随原存档保存；返回菜单中断不会标为已读，清空进度同时清除已读记录。
- 三个结局继续由 `OrimeEnding` 和各自场景中的两句字幕驱动；原结局分流、角色动作、淡出和返回菜单保持不变。旧 E00/E01 不接入新版。

## 适配新版

第三关危险源观察改读 `FixedContactHazard`，支持新版致死液体转换后滑箱的事实采样。压板箱体占用与 `IsPressed` 使用同一采样结果，不修改新版 `BoxesOnly` 判定。取消旧的固定坐标全景镜头，保留新版扩宽关卡与背景视差的镜头构图。

B00、C00、D00、D01 根据新场景修正；F00、F01 为第五关新增。上述六条实际调用本地 Strata `qwen3.8-flash-next-iq3_s`，输出见 `strata-latest-copy.json`。其它兼容的已确认文本复用原稿；三个结局的已有六句字幕未改写。

## 后台验证

在独立 Unity 2022.3.54f1c1 批处理进程中执行：

```text
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod NarrativeIntegrationChecks.RunForBatch -logFile <log>
```

测试使用 `.utmp/NarrativeIntegration` 的隔离存档，不使用玩家存档，不打开游戏窗口，不操作桌面。输出 `progress.txt` 和 `checks.txt`。覆盖教学、五个正式关、剧情暂停/恢复、两种结束探索分支、已读记录、三个结局重播与暂停。通关通过生命周期请求驱动，不能据此宣称实际障碍连续通关或新玩家盲测；本轮无画面或音频人工验收。

2026-10-19：86 项后台流程断言通过，见 `backend-checks.txt`。Windows x64 非开发构建成功（退出码 0）；16 项包体/嵌入文本/资源依赖/上游保留检查通过，见 `build-checks.txt`。第三关滑箱资源锚点与第四关 10 段回廊检查通过。

构建入口为 `NarrativePreviewBuild.Run`，通过 `GAMEEMERGENCE_BUILD_PATH` 指定输出 exe。Windows 上请使用较短工程路径及输出路径；长目录会导致 Unity 的 PackageCache 或 BuildCache 超过路径长度限制。本轮以临时映射盘缩短工程路径完成 `-batchmode -nographics -quit` 构建；映射仅用于本机验证，不是仓库运行依赖。预览构建使用独立的 `GameEmergence Narrative Preview` 产品名，构建后恢复工程产品名。

资源继续复用房间插画、书页图标、字体及最新场景资源。新增文本为手动翻页，无配音、新音乐或新音效依赖；未新增美术资源。原操作指南作为唯一指南保留。
