# DSH 隔离版启动器

一个约 200 KB 的 Windows 启动器（C# / .NET Framework 4.x），把 **DeepSeek Harness 桌面版**隔离成
独立环境：自己的 home、自己的 Electron profile、自己的图标。用户只需双击一个 exe。

- 无黑窗、无 cmd、无 wscript、无快捷方式链
- 不修改主版安装目录，不改注册表，不注册协议
- 两版可同时运行（单实例锁互不冲突）

## 特性

| | |
| --- | --- |
| **双击即启动** | 解析配置 → 准备目录 → 拉起主程序 → 3 秒自检后退出 |
| **数据隔离** | `DSH_HOME` 与 `--user-data-dir` 都指向本目录 |
| **失败可诊断** | 主程序 3 秒内异常退出时弹出退出码，并附修复命令 |
| **零依赖** | 只用系统自带的 .NET Framework 4.x |

## 仓库结构

```
.
├─ src/
│  └─ launcher.cs          启动器源码（单文件，约 250 行）
├─ docs/
│  └─ HANDOFF.md           技术交接文档：原理、坑、改动清单
├─ assets/
│  ├─ app.ico              exe 图标（16–256 多尺寸）
│  ├─ icon.png             源图 512
│  └─ make-icon.py         由源图生成 ico 的脚本
├─ launcher.ini            配置模板
├─ build.cmd               一键编译 → dist/
├─ .gitignore
└─ README.md
```

编译产物在 `dist/`（已在 .gitignore 中忽略）。

## 快速开始

### 1. 编译

```bat
build.cmd
```

产物：`dist\DSH-Isolated.exe`、`dist\launcher.ini`。

### 2. 部署

把 `dist\` 里的两个文件放到你想作为隔离容器的目录，例如：

```
D:\DeepSeek_Harness\dsh-isolated\
├─ DSH-Isolated.exe
├─ launcher.ini
├─ home/                  ← 首次运行自动创建
└─ electron-data/         ← 首次运行自动创建
```

### 3. 去掉 Low 完整性标签（必做）

如果是**从受限环境**（如沙箱）复制或编译出来的 exe，它会带 `Mandatory Label\Low`。
带这个标签的 exe 被双击时，Windows 会把进程降权到 Low，拉起的 Electron 会在启动瞬间退出
（`0x80000003`）。

```powershell
icacls "<容器目录>" /setintegritylevel (OI)(CI)Medium /T /C
```

验证（**不应**再出现 `Mandatory Label` 行）：

```powershell
icacls "<容器目录>\DSH-Isolated.exe"
```

### 4. 双击运行

首次是欢迎页（全新 home），填入 API key 即可。之后每次启动都回到同一份配置。

## 配置

`launcher.ini`（相对路径基于 exe 所在目录，支持 `%ENV%` 变量）：

| 键 | 默认值 | 说明 |
| --- | --- | --- |
| `AppDir` | `..\DeepSeekHarness桌面版` | DeepSeek Harness 安装目录 |
| `Home` | `home` | 隔离的 `DSH_HOME`：会话、凭据、插件、storage |
| `UserData` | `electron-data` | Electron profile 与单实例锁；`-` 表示与主版共用（需先退出主版） |
| `Workspace` | `dsh-workspace` | 默认代码工作目录 |
| `ExtraArgs` | 空 | 追加给主程序的参数，例如 `--disable-gpu` |

## 隔离边界

| 项目 | 主版 | 隔离版 |
| --- | --- | --- |
| Harness 数据 | `%USERPROFILE%\.dsh` 或全局 `DSH_HOME` | `<容器>\home` |
| Electron profile | `%APPDATA%\@deepseek-ai\dsh-desktop` | `<容器>\electron-data` |
| 单实例锁 | 独立 | 独立（可同时运行） |
| 程序本体 | 安装目录 | 与主版共用同一份安装目录（只隔离数据） |

## 常见问题

**弹错误框，退出码 -2147483645（0x80000003）**
完整性标签仍是 Low，重跑 `icacls`，确认输出里没有 `Mandatory Label` 行。

**双击没反应，也没弹框**
看 `launcher.log`：没有新行 = exe 未被执行（拦截或标签）；有 `EX` 行 = 看后面的原因。

**重复双击时程序一闪没了**
已有实例在跑，主程序因单实例锁立即以 code 0 退出，启动器只记日志、不弹框，属正常行为。

**想恢复出厂**
关闭隔离版，删除 `home` 与 `electron-data`。

**主版升级后要做什么**
不需要改动。启动器只调用安装目录里的主程序，主版升级后自动用新版本。

## 构建细节

`build.cmd` 等价于：

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe -optimize+ ^
  -codepage:65001 -win32icon:assets\app.ico -out:dist\DSH-Isolated.exe ^
  src\launcher.cs -r:System.Windows.Forms.dll -r:System.Drawing.dll
```

> 重新编译出的 exe 会重新带上 Low 标签，部署后需再跑一次 `icacls`。

## 开发文档

实现细节、设计取舍、已知坑位与改动检查清单见 [docs/HANDOFF.md](docs/HANDOFF.md)。

## 免责声明

本仓库只包含启动器源码，不包含也不修改 DeepSeek Harness 本体。
使用时请遵守 DeepSeek Harness 的许可条款。
