# CLI 后台启动重构计划

> 分支：`hwx/cli-background`
> 目标：CLI（`SourceGit <repo-path>`）打开仓库后立即返回，不再阻塞调用方终端窗口。

---

## 1. 现状分析

### 1.1 涉及文件

| 文件 | 角色 |
|------|------|
| `src/App.axaml.cs` | 入口与所有启动模式分发（`Main` + `OnFrameworkInitializationCompleted`） |
| `src/Models/IpcChannel.cs` | 基于命名管道的进程间通信 + 文件锁单例探测 |
| `src/Native/OS.cs` | 平台无关门面（`IBackend` 抽象 + 静态转发） |
| `src/Native/MacOS.cs` / `Linux.cs` / `Windows.cs` | 平台相关实现 |

### 1.2 当前启动分支

`App.axaml.cs:39-44` 顺序检查：

1. `--rebase-todo-editor` → 纯 CLI，无 GUI，立即 `Environment.Exit`
2. `--rebase-message-editor` → 纯 CLI，无 GUI，立即 `Environment.Exit`
3. `--history <path>` → 启动一个 standalone GUI（FileHistoryViewer），**会阻塞**
4. `--blame <path>` → 启动一个 standalone GUI（BlameViewer），**会阻塞**
5. `--core-editor <path>` → 启动一个 standalone GUI（CommitMessageEditor），**会阻塞**
6. `SOURCEGIT_LAUNCH_AS_ASKPASS=TRUE` → Askpass 模式 GUI，**会阻塞**
7. 其它 → `TryLaunchAsNormal(desktop)`（`App.axaml.cs:454`）

### 1.3 `TryLaunchAsNormal` 行为矩阵

| GUI 是否在运行 | args | 当前行为 | 是否符合需求 |
|---|---|---|---|
| 否（冷启动） | `[<repo>]` | 启动完整 GUI 并 **阻塞 CLI** | ❌ 不符 |
| 否（冷启动） | `[]` | 启动 interactive launcher 并 **阻塞 CLI** | ✅ 可接受（用户就是要 GUI） |
| 是（IPC 命中） | `[<repo>]` | 经 `IpcChannel.SendToFirstInstance` 发给已有 GUI，`Environment.Exit(0)` | ✅ 已经不阻塞 |
| 是（IPC 命中） | `[]` | 同上，发空串让已运行 GUI 提升到前台 | ✅ 已经不阻塞 |

### 1.4 已可行非阻塞链路（暖启动）

`App.axaml.cs:456-470` 已经实现：

```csharp
_ipcChannel = new Models.IpcChannel();
if (!_ipcChannel.IsFirstInstance)
{
    var arg = desktop.Args is { Length: > 0 } ? desktop.Args[0] : string.Empty;
    if (!string.IsNullOrEmpty(arg))
    {
        arg = arg.Replace('\\', '/').TrimEnd('/').Trim('\"').Trim();
        if (arg.Length > 0 && !Path.IsPathFullyQualified(arg))
            arg = Path.GetFullPath(arg);
    }
    _ipcChannel.SendToFirstInstance(arg);
    Environment.Exit(0);   // <— 暖启动已正确退出
    return;
}
```

最大延迟来自 `IpcChannel.SendToFirstInstance` 中 `Thread.Sleep(1000)`（`IpcChannel.cs:55`），在 macOS / Linux 上理论最坏 ~2 秒。这是可接受量级。

### 1.5 ❌ 核心问题：冷启动阻塞

`App.axaml.cs:476-481`：

```csharp
string startupRepo = null;
if (desktop.Args is { Length: 1 })
{
    var arg = desktop.Args[0].Replace('\\', '/').TrimEnd('/').Trim('\"').Trim();
    if (Directory.Exists(arg))
        startupRepo = arg;
}

_launcher = new ViewModels.Launcher(startupRepo);  // 进入 Avalonia 消息循环，CLI 永久阻塞
```

主进程既当 CLI 又当 GUI，无法在保证 GUI 启动同时让 CLI 退出。**必须把 GUI 启动剥离到一个独立子进程**。

---

## 2. 目标行为矩阵（重构后）

| GUI 是否在运行 | args | 重构后行为 |
|---|---|---|
| 否（冷启动） | `[<repo>]` | **新增**：在 CLI 进程里 fork 出 detached GUI 子进程，原 CLI 进程立即 `Environment.Exit(0)` |
| 否（冷启动） | `[]` | 保留原行为：启动 interactive launcher（用户没传路径，CLI 即 GUI） |
| 是（IPC 命中） | `[<repo>]` | 保持：经命名管道发送路径给已运行 GUI，退出 |
| 是（IPC 命中） | `[]` | 保持：把已运行 GUI 提升到前台，退出 |
| 任意 | `--history/--blame/--core-editor` | **保留原行为**（这些模式仍是 standalone GUI，按设计阻塞） |

---

## 3. 架构设计

### 3.1 关键思路

把"判断是否冷启动并启动 GUI"这一步从 GUI 主进程里**剥离**到一个 CLI shim 阶段。`Main` 入口先做 shim，决定后立即退出；只有当真的要进入 GUI 生命周期时，才 `StartWithClassicDesktopLifetime`。

### 3.2 启动顺序（重构后）

```
process Main(string[] args)
├─ SetupDataDir
├─ (existing) TryLaunchAsRebaseTodoEditor    → exit
├─ (existing) TryLaunchAsRebaseMessageEditor → exit
├─ (NEW)      TryLaunchAsCliShim(args)       → exit(0)   ← 新增，只处理 [<repo>] 一种 case
├─ StartWithClassicDesktopLifetime(args)     ← 进入 GUI 消息循环
   └─ OnFrameworkInitializationCompleted
      ├─ TryLaunchAsFileHistoryViewer
      ├─ TryLaunchAsBlameViewer
      ├─ TryLaunchAsCoreEditor
      ├─ TryLaunchAsAskpass
      └─ TryLaunchAsNormal  ← 保留，可接收 IPC
```

### 3.3 `TryLaunchAsCliShim` 决策树

```
if args.Length != 1 → return false   (不是 CLI shim 场景，走老路)
if !Directory.Exists(arg) → return false   (非仓库路径，走老路)
if ProbeLock(进程锁文件) 成功:
    → GUI 未运行
    → Native.OS.LaunchDetachedGui([arg])
    → Environment.Exit(0)
else:
    → GUI 正在运行
    → SendPathToRunningInstance(arg)   (纯命名管道探测，无需持锁)
    → Environment.Exit(0)
```

> 注意：`IpcChannel.SendToFirstInstance` 当前是**已经被构造过 `IpcChannel` 之后**才能调用——持有过锁就会释放不掉。Shim 里我们**不要持锁**，只做探测 + 通过一个独立轻量管道客户端直连首实例。

### 3.4 `Native.OS.LaunchDetachedGui(string[] args)`

新增抽象方法：

```csharp
public interface IBackend
{
    // ...已有方法...
    void LaunchDetachedGui(string[] args);
}

public static void LaunchDetachedGui(string[] args)
{
    _backend.LaunchDetachedGui(args);
}
```

#### macOS（`src/Native/MacOS.cs`）

优先走 `open` 命令，让 macOS 走完整 `.app` 生命周期（Dock 图标、应用菜单、Cmd+Q 终止等）：

```csharp
public void LaunchDetachedGui(string[] args)
{
    // 找 .app 路径：在 MacOS/SourceGit 同级上一级
    var exePath = Environment.ProcessPath
                  ?? Assembly.GetExecutingAssembly().Location;
    var macosDir = Path.GetDirectoryName(exePath);          // .../Contents/MacOS
    var appPath  = Path.GetDirectoryName(macosDir) + ".app";  // .../SourceGit.app

    var psi = new ProcessStartInfo("open");
    psi.ArgumentList.Add("-a");
    psi.ArgumentList.Add(appPath);
    psi.ArgumentList.Add("--args");
    foreach (var a in args) psi.ArgumentList.Add(a);
    psi.UseShellExecute = false;
    psi.RedirectStandardOutput = true;
    psi.RedirectStandardError  = true;
    Process.Start(psi);
}
```

如果跑的是 `dotnet` / 开发态二进制，找不到 `.app` 时回退到直接 spawn 同进程并 `setsid`-like（macOS 上 `posix_spawn` 等价的 .NET 语义下靠不继承 terminal 即可）。

#### Linux（`src/Native/Linux.cs`）

```csharp
public void LaunchDetachedGui(string[] args)
{
    var exePath = Environment.ProcessPath
                  ?? Assembly.GetExecutingAssembly().Location;
    var psi = new ProcessStartInfo(exePath);
    foreach (var a in args) psi.ArgumentList.Add(a);
    psi.UseShellExecute = false;
    psi.RedirectStandardInput  = true;
    psi.RedirectStandardOutput = true;
    psi.RedirectStandardError  = true;
    Process.Start(psi);   // 默认 detach，不共享 controlling terminal
}
```

对 Linux 而言，`Process.Start` 不带 `UseShellExecute` 时子进程已经是独立进程组；额外做 `setsid` 优化可以但非必须。

#### Windows（`src/Native/Windows.cs`）

```csharp
public void LaunchDetachedGui(string[] args)
{
    var exePath = Environment.ProcessPath
                  ?? Assembly.GetExecutingAssembly().Location;
    var psi = new ProcessStartInfo(exePath);
    foreach (var a in args) psi.ArgumentList.Add(a);
    psi.UseShellExecute = true;          // 关键：不继承父 console
    Process.Start(psi);
}
```

### 3.5 `SendPathToRunningInstance(string path)`

从 `IpcChannel` 提炼一个不带文件锁的纯发送函数，新文件 `src/Models/IpcClient.cs`（或并入 `IpcChannel`）：

```csharp
public static class IpcClient
{
    public static bool TrySendPath(string path, int timeoutMs = 1500)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                "SourceGitIPCChannel" + Environment.UserName,
                PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMs);
            if (!client.IsConnected) return false;
            using var writer = new StreamWriter(client);
            writer.WriteLine(path);
            writer.Flush();
            if (OperatingSystem.IsWindows())
                client.WaitForPipeDrain();
            else
                Thread.Sleep(200);  // 比原版 1000ms 短，shim 退出更快
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

被 `TryLaunchAsCliShim` 和现有 `IpcChannel.SendToFirstInstance`（暖启动路径）共用。

### 3.6 文件锁探测

冷启动探测用现成的 lock 文件存在性 + 进程扫描组合（避免 lock 持有者崩溃后锁变孤儿）：

```csharp
private static bool ProbeLock()
{
    var lockPath = Path.Combine(Native.OS.DataDir, "process.lock");
    try
    {
        using var probe = File.Open(
            lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        return true;   // 拿到了 → 没人在用
    }
    catch
    {
        return false;  // 持有冲突 → 已有 GUI
    }
}
```

> 与 `IpcChannel` 现有逻辑一致（`IpcChannel.cs:19-33`），只是**用完即释放**，不调 `Task.Run(StartServer)`，确保 shim 退出后子进程能正常拿到锁。

### 3.7 时序图

```
终端                       CLI shim                 detached GUI           已运行的 GUI
 │                             │                          │                       │
 ├─ exec SourceGit /repo ─────►│                          │                       │
 │                             ├─ ProbeLock ──────────────┼── 拿到 ─► 决定 spawn  │
 │                             ├─ LaunchDetachedGui(args)►│                       │
 │                             ├─ Environment.Exit(0)     │                       │
 │◄───── prompt 返回 ──────────┤                          │                       │
 │                                                            │                       │
 │                                                       ├─ IPC lock probe(空)   │
 │                                                       ├─ IpcChannel ctor (首实例)
 │                                                       ├─ StartWithClassic...  │
 │                                                       ├─ Launcher.Open(/repo) │
 │                                                            ├─ GUI 显示仓库
```

---

## 4. 任务拆分

按独立可提交的粒度排列；每完成一个跑一次 `dotnet build` + 手动 smoke test。

### T1 · 添加 `LaunchDetachedGui` 平台实现

- `src/Native/OS.cs`：扩展 `IBackend`、加静态转发
- `src/Native/MacOS.cs`：实现 `open -a <App>.app --args ...` 回退到直接 spawn
- `src/Native/Linux.cs`：实现 `Process.Start` + 重定向 stdio
- `src/Native/Windows.cs`：实现 `Process.Start` + `UseShellExecute=true`
- 验收：三平台 `dotnet build` 通过，独立 unit 调用能起新进程

### T2 · 抽出 `IpcClient.TrySendPath`

- 新建 `src/Models/IpcClient.cs`
- `IpcChannel.SendToFirstInstance` 改为内部调用 `IpcClient.TrySendPath`，消重
- 验收：暖启动场景不退化，CLI 退出耗时下降

### T3 · 在 `App.Main` 加入 `TryLaunchAsCliShim`

- `src/App.axaml.cs:23-50` 加入第三个 `else if (TryLaunchAsCliShim(args))`
- `src/App.axaml.cs` 新增私有 `TryLaunchAsCliShim(string[] args)`：
  - 复用现有路径规范化（提取为 `NormalizeRepoArg` 静态方法）
  - `ProbeLock()` + 分支
- 验收：冷启动 `SourceGit /repo` 返回耗时 < 1s；GUI 窗口出现并打开该仓库

### T4 · README / 文档

- `README.md` 添加 "Command Line Usage" 小节，列出行为矩阵
- 翻译辅助脚本（`translate_helper.py`）保持不动

### T5 · 回归测试矩阵

| 场景 | 期望 | 检查手段 |
|---|---|---|
| 冷启动 + 路径 | CLI 退出 < 1s，新 GUI 打开该仓库 | `time SourceGit /tmp/repo` |
| 冷启动 + 无路径 | CLI 阻塞，启动 interactive launcher | manual |
| 暖启动 + 路径 | CLI 退出 < 2s，已运行 GUI 多 tab + 提升 | manual |
| 暖启动 + 无路径 | CLI 退出 < 2s，已运行 GUI 提升 | manual |
| `SourceGit /repo /another`（多参） | 走老路（`TryLaunchAsNormal`，阻塞打开首参） | 兼容性 |
| `SourceGit --history /repo/file` | 走老路（standalone） | 兼容性 |
| macOS `open SourceGit.app` | 走 `TryLaunchAsNormal`（args 全空），无回归 | 兼容性 |
| Linux 双击 `.desktop` | 同上 | 兼容性 |

### T6 · 性能基线（可选，仅供 PR 描述）

- 冷启动退出现状：永久阻塞 → 重构后：<800ms（具体数字写进 PR）
- 暖启动退出：从 ~2s → ~700ms（缩短 `Thread.Sleep`）

---

## 5. 风险与开放问题

| 风险 | 缓解 |
|---|---|
| macOS `.app` 路径解析在开发态 dotnet run 下失败 | T1 实现里走 fallback：找不到 `.app` 时直接 spawn 二进制 |
| detached 进程拿不到 IPC 锁（shim 已 Exit 但句柄未立即释放） | shim 中 `probe.Dispose()` 显式关闭、`GC.Collect()` 兜底；首进程 `ProbeLock` 到 `LaunchDetachedGui` 间不持有任何共享句柄 |
| macOS 上 `open -a` 受 LaunchServices 缓存影响 | 给 `.app` 加 `--new` 强制新实例，或直接 `open -n -a <App> --args ...` |
| 用户传的是文件而非目录 | shim `!Directory.Exists(arg)` 走老路——保持文件路径场景仍由原 `TryLaunchAsNormal` 接管 |
| AOT 模式下 `Environment.ProcessPath` / `Assembly.Location` 行为差异 | 在 macOS / Linux / Windows 三平台各跑一遍 build-mac / build-win 验证 |
| Shim 内 `Environment.Exit` 后命名管道的客户端/服务端握手失败 | shim 不创建 `IpcChannel`，只读 lock 文件做探测，无管道竞争 |

---

## 6. 验收标准

1. `dotnet build src/SourceGit.csproj` 三平台 0 warning / 0 error
2. `time SourceGit /path/to/repo`（冷启动） wall time < 1s
3. 暖启动 CLI 退出耗时 < 2s（保持现有水准）
4. 行为矩阵 §2 中 8 个 case 全部通过
5. PR 描述含：动机、行为矩阵、风险、回退方案（`git revert` 该 commit 即可）

---

## 7. 提交粒度建议

```
T1 feat(native): add cross-platform LaunchDetachedGui backend
T2 refactor(ipc): extract IpcClient.TrySendPath from IpcChannel.SendToFirstInstance
T3 feat(cli): add TryLaunchAsCliShim to detach GUI on cold start
T4 docs(readme): document CLI behavior matrix
```

每个 commit 单独可编译运行；T2 单独 cherry-pick 也能给暖启动提速。
