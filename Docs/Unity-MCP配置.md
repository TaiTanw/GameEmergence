# Unity MCP 本机配置

配置日期：2026-10-18。

## 已安装

- Unity：2022.3.54f1c1，编辑器位于 `D:\unityHub\2022.3.54f1c1\Editor\Unity.exe`。
- Unity 插件：`com.coplaydev.unity-mcp` v10.3.0，通过 `Packages/manifest.json` 固定 Git 标签；锁定提交为 `aa5fc638d623f56178d50329d9ad8c541a57fe66`。
- Python 服务端：`mcpforunityserver==10.3.0`，使用本机 `uvx` 和 Python 3.12。
- Codex：本项目 `.codex/config.toml` 中的 `unityMCP` 连接 `http://127.0.0.1:8080/mcp`；保留原有 Serena 设置。此文件被 Git 忽略，不随仓库同步。
- Unity 偏好：HTTP Local、基础地址 `http://127.0.0.1:8080`、Auto-Start on Editor Load 已开启；`uvx` 路径为 `C:\Users\16028\.local\bin\uvx.exe`。这些是当前 Windows 用户的 Unity EditorPrefs，其他电脑需重新配置。

## 已完成的验证

- Codex 实际发现 Unity MCP 工具，并连接到本项目的 Unity 2022 编辑器。
- 读取项目资料、编辑器就绪状态和 BeginScenes 场景层级。
- 在独立临时场景创建物体，添加 Rigidbody，将质量改为 2.5、关闭重力，并调整位置为 (4, 5, 6)。
- 保存并重新打开临时场景，读取并确认上述属性保留。
- 删除测试物体，确认搜索结果为空；恢复原 BeginScenes 场景。临时场景和一次性安装脚本随后清理。
- 清理安装脚本后重新编译并完成域重载，MCP 自动恢复连接；再次读取场景成功，Unity Console 错误条目为 0。

## 人工使用注意

1. 使用 MCP 时保持本项目的 Unity 编辑器打开。等待导入、编译和域重载完成；出现模态弹窗时先处理弹窗。
2. 在 Unity 的 `Window > MCP for Unity` 查看连接。若未自动连接，使用 `Start Server`（或已有服务时的 `Connect`）。正常状态应显示已连接。
3. 本次 Codex 已自动载入新增工具，无需立即重启。以后若工具未出现，在 Codex 的 MCP 设置重新连接服务，必要时重启应用后继续本聊天。
4. 自动启动开关在 MCP 窗口的 Advanced 设置中。它属于当前用户的编辑器偏好；若不希望随编辑器启动，可关闭 Auto-Start on Editor Load。
5. 同时打开多个 Unity 项目时，先确认目标实例名称和项目路径。不要仅凭端口判断项目。
6. 正式编辑场景前保存手工改动；编辑器正在运行 Play Mode 时，许多场景修改不会在退出后保留。
7. 不需要点击 Configure All Detected Clients；本次仅配置了此项目的 Codex 连接。服务绑定回环地址，不需要开放公网或更改防火墙。
8. 插件和服务端应保持相同版本。升级时一起升级并重新验证，避免长期跟随 main/beta 浮动分支。

## 本次观察到的提示

- MCP 面板的绿色 `Session Active (GameEmergence)` 表示 Unity 会话已连接。下方客户端下拉框若默认选中其他软件，红色 `Not Configured` 指该客户端的配置；本项目 Codex 已通过实际调用验证，无需为此点击 Configure All。
- 插件自动检查更新时出现网络超时提示；固定版本服务和场景操作正常，不需要为此重新安装。
- 编辑器空闲时，状态资源曾报告 `stale_status`，但实际场景读取和控制台查询仍正常。后续遇到该提示，应结合编译状态、绿色会话状态和实际只读请求判断连接，避免仅凭缓存时间戳断定断线。

## 排查与移除

- MCP 地址：`http://127.0.0.1:8080/mcp`；健康检查：`http://127.0.0.1:8080/health`。
- 本次服务日志：`Logs/unity-mcp-server.stderr.log`；本次编辑器启动日志：`Logs/unity-mcp-editor.log`。以后由 Unity 自动启动的服务日志以 MCP 窗口和 Unity Console 为准。
- 8080 被其他程序占用时，先确认占用者；如需换端口，Unity HTTP URL 和 Codex 配置必须一起修改。
- 移除时：在 Unity MCP 窗口关闭自动启动并停止服务，通过 Package Manager 移除插件，再删除本项目 `.codex/config.toml` 中的 `[mcp_servers.unityMCP]` 段。保留 Serena 配置。
- 原 Codex 项目配置备份：`.utmp/codex-config-before-unity-mcp.toml`。

上游文档：<https://github.com/CoplayDev/unity-mcp>；Codex MCP 文档：<https://learn.chatgpt.com/docs/extend/mcp?surface=cli>。
