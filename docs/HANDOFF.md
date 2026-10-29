# HANDOFF · DSH 隔离版启动器

面向接手修改/检查本仓库的 agent。读完你应该能：判断它是否健康、改哪里、怎么验证。

---

## 1. 这是什么

一个 Win32 GUI 小程序（.NET Framework 4.x，`csc` 编译），把 DeepSeek Harness 桌面版
隔离成"自己一份数据"的环境。用户操作只有一个动作：**双击 `DSH-Isolated.exe`**。

它做四件事，然后退出：

1. 读 `launcher.ini`；
2. 准备/检查隔离目录（`home`、`electron-data`）；
3. 用 `DSH_HOME` + `--user-data-dir` 直接 `Process.Start` 主程序；
4. 记日志；若主程序 3 秒内异常退出，弹框报退出码。

**当前状态：已验证可用**（2026-10-29，用户双击启动成功、配置持久化）。

---

## 2. 为什么是"直接启动"，而不是 cmd/wscript 链

历史教训：早期版本以为"Electron 需要控制台上下文"，于是绕成
`lnk → wscript → cmd → exe`，而启动器本身只会生成脚本 + 弹框。

**那个判断是错的。** 真正的变量是 Windows 完整性级别（Integrity Level）：

- 沙箱里编译出来的 exe 带 `Mandatory Label\Low`；
- 双击时 Windows 按镜像标签把进程降到 Low；
- 子进程全部继承 Low，Electron 在 Low IL 下启动即 `0x80000003`（STATUS_BREAKPOINT）；
- 快捷方式之所以"能用"，是因为真正被执行的是系统 `wscript.exe`（由 Medium 的 Explorer 创建），
  与 lnk 自身的标签无关。

结论：**只要 exe 的标签是 Medium，直接启动就完全可行**，所有 cmd/wscript/快捷方式都是多余的。

---

## 3. 代码地图（`src/launcher.cs`，约 250 行）

| 成员 | 作用 | 改动风险 |
| --- | --- | --- |
| `Main` | 解析 ini → 建目录 → 锁检查 → 启动 → 自检 | 高 |
| `FullPath` | 相对路径解析（相对 exe 目录，支持 `%ENV%`） | 低 |
| `RepairHome` | 只在 `home\profiles\desktop` **完全为空**时删除它 | 高（曾因误删吞配置） |
| `CleanStaleLock` | `electron-data\lockfile` 能独占打开才删 | 中 |
| `IsLocked` | 检测主版 profile 是否被占用（仅 `UserData=-` 时用） | 低 |
| `Log` | 追加写 `launcher.log`，失败静默 | 低 |
| `Fail` | `MessageBox` 报错并返回 1 | 低 |

### 启动参数构造（核心，约 20 行）

```csharp
ProcessStartInfo info = new ProcessStartInfo();
info.FileName = exePath;              // ..\DeepSeekHarness桌面版\DeepSeek Harness.exe
info.WorkingDirectory = appDir;
info.UseShellExecute = false;          // 必须 false，否则 EnvironmentVariables 不生效
info.Arguments = "--user-data-dir=\"...\"" [+ ExtraArgs];
info.EnvironmentVariables["DSH_HOME"] = home;
info.EnvironmentVariables.Remove("ELECTRON_RUN_AS_NODE");
info.EnvironmentVariables.Remove("NODE_OPTIONS");
info.EnvironmentVariables.Remove("NODE_PATH");
Process child = Process.Start(info);
```

**三条不变量，改代码时不要破坏：**

1. `UseShellExecute = false`——否则 `EnvironmentVariables` 被忽略，`DSH_HOME` 传不进去。
2. 环境变量只能 `Remove`，**不能赋空字符串**：Electron 用"变量是否存在"判断
   `ELECTRON_RUN_AS_NODE`，空值仍会进入 Node 模式（表现为退出码 9）。
3. `WaitForExit(3000)` 只是自检，不能阻塞用户——主程序正常运行时它会在 3 秒后返回 false 并退出。

### 目录语义

| 路径 | 谁在用 | 说明 |
| --- | --- | --- |
| `home` | `DSH_HOME` | 会话、凭据、插件、storage |
| `electron-data` | `--user-data-dir` | Chromium profile、单实例锁 `lockfile` |
| `launcher.log` | 启动器 | 每次启动一行 |

---

## 4. 已知坑（踩过的）

1. **Low 标签**：只要本目录被沙箱碰过，exe 就会带 `Mandatory Label\Low`，双击必崩。
   - 判据：`icacls DSH-Isolated.exe` 出现 `Mandatory Label` 行。
   - 修复：`icacls "<dir>" /setintegritylevel (OI)(CI)Medium /T /C`，**必须在 Medium 上下文执行**；
     Low 进程改不了标签（`danger-full-access` 提权也不行，已在 2026-10-29 实测失败）。
2. **每次重新编译都会重新带上 Low 标签**，交付前必须提醒用户再跑一次 `icacls`。
3. **relabel 之后，沙箱内无法再写入本目录**（Low 写 Medium 被禁止）。要改文件：
   在可写位置编辑，再由用户在 Medium 上下文复制进来，或让用户执行命令。
4. **不要在沙箱里测试启动**：沙箱禁用命名管道，Electron 会在
   `mojo/platform_channel.cc:108` 处 FATAL（`拒绝访问 0x5`），与产品无关。
   验证只能由用户在资源管理器里双击完成。
5. **`RepairHome` 曾经删掉了非空的 `profiles\desktop`**，导致"每次都是新实例"。现在只在目录
   完全为空时删，改动前先想清楚这个条件。
6. **已有实例时再双击**：主程序会因单实例锁立即以 code 0 退出，启动器只记 `EX exited code=0` 到日志，不弹框（属正常行为）。
7. **全局 `DSH_HOME` 环境变量**：用户机器上可能有全局 `DSH_HOME` 指向主版。启动器显式设置子进程
   环境变量，能覆盖它；不要改成"依赖继承"。

---

## 5. 改动后的验证清单

- [ ] `build.cmd` 编译通过，产物 `dist/DSH-Isolated.exe`
- [ ] `icacls DSH-Isolated.exe` 无 `Mandatory Label` 行（用户 relabel 后）
- [ ] 双击能起窗口，`launcher.log` 出现 `started pid=...`
- [ ] `home\.credentials.yaml`、`home\profiles\desktop\*` 生成
- [ ] 关闭再开，配置还在（不是欢迎页）
- [ ] 主版同时在跑时，两版互不影响
- [ ] 主版 `%APPDATA%\@deepseek-ai\dsh-desktop` 的 `lockfile` 未被本版触碰

---

## 6. 交付前自检（给 agent）

1. 目录里不该出现 `runner.cmd` / `runner.vbs` / `runner-exit.log` / `DSH-Isolated.lnk`（已废弃）。
2. `README.md` 与实际行为一致（单 EXE 直接启动）。
3. 提醒用户：**编译产物带 Low 标签，双击前先 relabel**。
