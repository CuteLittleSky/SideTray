# 副屏任务栏音频控制

[![Build](https://github.com/CuteLittleSky/SideTray/actions/workflows/build.yml/badge.svg)](https://github.com/CuteLittleSky/SideTray/actions/workflows/build.yml)

把「切换音频输出设备 / 调音量」做成 Windows **副屏任务栏里的一个伪托盘单元**：
嵌在任务栏右侧、时钟左边，不占桌面空间，不遮挡工作区。

![在副屏任务栏里的样子](docs/taskbar.png)

Windows 10/11 的副屏任务栏（`Shell_SecondaryTrayWnd`）里**没有通知区域**——`TrayNotifyWnd`
只存在于主任务栏。所以第三方托盘图标没法真的搬到副屏。这个程序换了个思路：
把自己 `SetParent` 进副屏任务栏的 `WorkerW`，位置让给时钟和已有的小工具（比如 TrafficMonitor），
再截一张任务栏背景当底图画上去，看起来就是任务栏的一部分。

> **命名**：仓库名 / 可执行文件名用 ASCII 的 `SideTray`（GitHub 仓库名和下载链接都省事），
> 界面上显示的名字是中文的「副屏任务栏音频控制」（窗口标题、托盘提示、文件属性、Release 标题）。

## 特性

- 嵌进副屏任务栏（也可切成浮动面板），鼠标移上去就是普通控件的手感
- 点设备名直接切音频输出设备，三个 role（Console / Multimedia / Communications）一起切
- **切输出时连带把麦克风切到同一台硬件**（按型号自动配对：Realtek↔Realtek、HyperX↔HyperX；也能在配置里手动指定）
- 音量条拖动 / 滚轮调音量 / 一键静音
- 右键是自己的设置菜单（不是任务栏那个「层叠窗口 / 任务管理器」）
- 任务栏布局是活的：TrafficMonitor 晚到会自己让位；资源管理器重启后进程自动重启并重新嵌入
- 零依赖：只用 .NET Framework 4.x 自带的 WinForms + Win32 P/Invoke，单文件 exe

浮动模式长这样（`Ctrl+Alt+B` 切换）：

![浮动模式](docs/panel.png)

## 用法

| 操作 | 效果 |
|---|---|
| 点最左边的喇叭图标 | 静音 / 取消静音 |
| 点设备名 | 弹出全部输出设备，点一下就切（开着联动时麦克风跟着切） |
| 在音量条上拖 / 在条上滚轮 | 调音量 |
| 右键 | 自己的设置菜单 |
| 双击 | 打开 Windows 声音设置 |
| `Ctrl+Alt+A` | 显示 / 隐藏 |
| `Ctrl+Alt+V` | 切到下一台输出设备 |
| `Ctrl+Alt+B` | 嵌入任务栏 ⇄ 浮动面板 |
| 点系统托盘图标 | 显示 / 隐藏，右键菜单同上 |

右键菜单：

```
切换输出设备 ▸   当前设备打勾
切换输入设备 ▸   麦克风列表，当前设备打勾，附「与当前输出配对：…」
切输出时同时切麦克风  ☑ 默认开启
静音
显示方式 ▸       嵌进副屏任务栏 / 浮动面板
隐藏面板         Ctrl+Alt+A
──────────
总在最前         （嵌入任务栏时不可用）
开机自启         （勾选即写 HKCU\Software\Microsoft\Windows\CurrentVersion\Run）
Windows 声音设置...
打开配置文件...
──────────
退出
```

### 麦克风联动怎么配

切输出设备时，程序按下面的顺序给这台输出找「同一台硬件」的麦克风：

1. 配置文件里的 `pair=输出名|输入名` 手动指定（优先级最高）
2. 括号里的型号完全一致，例如 `扬声器 (Realtek(R) Audio)` ↔ `麦克风阵列 (Realtek(R) Audio)`
3. 型号里有两个以上共同的实词（排除 audio / usb / digital 这类通用词）

找不到就**不动麦克风**（比如 HDMI 显示器根本没有麦克风）。本机实测：

| 输出设备 | 联动到的麦克风 |
|---|---|
| 扬声器 (Realtek(R) Audio) | 麦克风阵列 (Realtek(R) Audio) |
| 耳机 (HyperX Cloud III) | 麦克风 (HyperX Cloud III) |
| H27T22C (NVIDIA HDMI) | 无配对，输入保持不变 |

## 配置

`%LOCALAPPDATA%\SideTray\config.txt`（改完重启程序生效；目录名沿用 SideTray，避免升级时丢配置）：

```ini
# mode: taskbar = 嵌进副屏任务栏, float = 浮动面板
mode=taskbar
topmost=1
# shrink: 1 = 嵌入时缩窄任务按钮区给它让位（会跟 TrafficMonitor 抢布局，默认 0）
shrink=0
# switchinput: 1 = 切输出设备时把麦克风也切到配对的设备（0 = 只切输出）
switchinput=1
# pair: 手动指定配对，可多行；名字写一部分就行，也可以用设备 ID
# pair=耳机|麦克风
x=-448      # 浮动模式的位置
y=1324
```

日志在同一个目录的 `log.txt`（超过 300KB 自动清空）。

## 实现要点

下面这几处是踩过的坑，改代码时留意：

1. **嵌入**：`FindWindow("Shell_SecondaryTrayWnd")` → 子窗口 `WorkerW` 当宿主 →
   `SetWindowLongPtr(WS_POPUP → WS_CHILD)` + `SetParent`。
2. **定位**：`min(时钟左边缘, 已有外挂窗口左边缘) − 自身宽度 − 8`。TrafficMonitor 可能比我们
   晚回到任务栏，所以每 4 个轮询周期（约 6 秒）复查一次位置。
3. **背景**：子窗口用不了分层透明，嵌入前先把那块任务栏截一张图当底图绘制；
   换主题/改透明度后按 `Ctrl+Alt+B` 切一圈会重新采样。
4. **右键菜单**：嵌入后本窗口是 explorer 的子窗口，`menu.Show(this, …)` 会把菜单甩到任务栏坐标，
   所以一律用屏幕坐标 `menu.Show(point)` 自己算位置（优先向上弹）。另外 `WM_CONTEXTMENU`
   必须吞掉，否则 `DefWindowProc` 会转发给父窗口，任务栏弹出自己的原生菜单。
5. **自愈**：资源管理器重启会把嵌在任务栏里的窗口一起带走。同一个进程里重建窗体会导致
   鼠标和热键全部失灵，所以看门狗直接**重启进程**（10 秒内不重复，防崩溃循环）。
6. **切设备**：未公开接口 `IPolicyConfig::SetDefaultEndpoint`，对三个 role 各调一次；
   切完 Windows 自己通过 `IMMNotificationClient` 通知各程序，不需要手动广播。

## 构建

需要 Windows + .NET Framework 4.x（系统自带即可，**不需要装 SDK 或 Visual Studio**）。

```powershell
# 最快：直接调系统自带 csc，产物 bin\SideTray.exe
.\build.ps1

# 和 CI 一致：MSBuild，产物 bin\Release\SideTray.exe
msbuild SideTray.csproj /p:Configuration=Release
```

## 下载

到 [Releases](https://github.com/CuteLittleSky/SideTray/releases)
下载 `SideTray-win-x64.zip`，解压运行即可。

CI 行为（`.github/workflows/build.yml`）：

- push 到 `main` 或提 PR：只编译 + 上传 artifact
- push 形如 `v1.0.0` 的 tag：编译 + 建 Release 并附上 zip
- 也可以在 Actions 页面手动触发
- MSBuild 不可用时自动退回系统自带 csc

发版：

```bash
git tag v1.0.0
git push origin v1.0.0
```

## 已知限制

- 只切**输出设备**，没有「按应用分设备」的路由
- 不镜像系统托盘的其它图标（WiFi / 音量 / QQ 那些）——那需要跨进程读 explorer 的
  `TRAYDATA` / `TB_GETBUTTON`，目前没有免费且活跃的成品
- 嵌入时窗口浮在任务按钮区之上，任务按钮特别多时会从它下面穿过去
- 只有 x64 构建

## 许可

本项目以 **GNU 宽通用公共许可证第 3 版（LGPL-3.0-or-later）** 发布。

- [`LICENSE`](LICENSE) —— LGPL v3 全文
- [`LICENSE.GPL-3.0`](LICENSE.GPL-3.0) —— LGPL v3 引用的 GPL v3 全文（两者要一起提供）

简单说：你可以自由使用、修改、再分发；**修改后分发需要同样开源**；
把它当库链接进自己的软件里也可以（LGPL 的「宽」就在这里），但要保留许可声明、
提供可替换该库的方式。软件按「现状」提供，不附带任何担保。

Copyright (C) 2026 CuteLittleSky
