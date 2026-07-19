# Windows 版性能优化方案（启动速度 + 运行流畅度）

> 分析日期：2026-07-17，基于 v1.0.7（main @ 7c5622b）。
> 约束：**功能零打折**。所有条目均为不改变行为的工程优化；有行为风险的项单独标注并默认不做。

## 0. 结论摘要

代码库已经做过一轮相当到位的性能工作（R2R、无压缩单文件、空闲 MOUSEMOVE 早退、图标双层缓存、拼音记忆化、UI 预热、路径校验隔离线程、防抖写盘）。剩余优化空间集中在四处：

1. **全局鼠标钩子仍挂在 UI 线程**——UI 线程一卡，整机鼠标就卡，而且 Windows 会把超时的 WH_MOUSE_LL 钩子**静默摘除**（手势功能悄悄失效）。这是流畅度上最大的结构性风险。
2. **首次弹窗的图标提取是同步磁盘 I/O**（.exe/.lnk 按真实路径 SHGetFileInfo、自定义图标/favicon 磁盘读），冷盘或网络路径会让第一次手势明显顿挫。
3. **启动路径上有 20–80ms 的可后移工作**：注册表检查、三个外部工具探测（未安装的工具**每次启动都重新探测**，Everything 探测还会枚举全系统进程）、剪贴板历史落盘加载。
4. **没有启动打点**，改了也无法证明快了多少——先加测量。

预期收益（估算，待打点验证）：
- 托盘就绪时间：减 30–100ms（app 层）；冷启动大头仍是 .NET 运行时+WinForms 初始化（约 300–800ms，属发布形态成本，见 §6）。
- 首次手势弹窗：消除冷盘场景下 50–500ms 级的图标提取顿挫。
- 整机鼠标流畅度：从"依赖 UI 线程不卡"变为**结构上有保证**。

## 1. 实测事实（本机）

| 项 | 值 |
|---|---|
| 发布单文件 exe | 约 56.5 MB（R2R，未压缩） |
| config.json / clipboard-history.json | 各约 10 KB（启动读取成本很小） |
| 单文件解压目录 `%TEMP%\.net` | **不存在** → `IncludeNativeLibrariesForSelfExtract` 实际是空操作，运行时原生库直接从 bundle 加载，没有首启解压成本 |
| favicon 磁盘缓存 | 5 个文件 |

## 2. 已经做对的事（不要重复劳动，也不要回退）

- `Quickstart.csproj:16-23`：R2R + 关闭 bundle 压缩 + 自包含裁剪。
- `GlobalMouseHook.cs:71-72`：空闲态 MOUSEMOVE 早退 + blittable 直读（`Unsafe.AsRef`），这是全局钩子延迟的关键，保留。
- `Program.cs:222-331`：手势移动 16ms 合并定时器（只在手势期间运行，无常驻轮询）。
- `MainPopup.cs:29-31`：图标"高分原图 + 按 DPI 预缩放位图"双层缓存，重绘零插值。
- `MainPopup.cs:2220-2273`：路径存在性校验放线程池 + 信号量限流 4，网络盘不再卡 UI（注释里也写明了动机）。
- `PinyinHelper.cs:28`：整串首字母记忆化。
- `ConfigManager.cs:119-126`：LastUsedAt 等高频低价值写入走 1.5s 防抖 + 不可变 JSON 快照。
- `Program.cs:473-485`：主弹窗/动作选择器 Idle 预热，AiPopup 延迟 3s 预热。
- **历史教训**（.workbuddy/memory/2026-07-15）：曾把 `Process.Start` 挪进 `Task.Run`，导致 Windows 判定后台抢焦点、单实例程序（DOpus/Everything）只显示不激活，已回退。`MainPopup.cs:1329-1336` 的注释是结论，**不要再试**。

## 3. P0 —— 结构性修复（运行流畅度核心）

### 3.1 鼠标钩子搬到专用高优先级线程

**问题**：`GlobalMouseHook` 在 `Program.Main`（UI 线程）构造并 `SetWindowsHookEx`（`GlobalMouseHook.cs:41-47`）。WH_MOUSE_LL 回调经由安装线程的消息循环派发，因此：

- UI 线程任何一次 >几十 ms 的停顿（首次图标提取、设置窗体构建、同步写盘、模态框布局），全系统鼠标指针跟着卡。
- Windows 7+ 对超时（默认约 300ms，`LowLevelHooksTimeout`）的低级钩子会**静默移除**——表现为"用着用着右拖手势没了"，无异常无日志。当前代码里多处防御性注释（`Program.cs:162-164`）都是在绕这个雷，治标不治本。

**改法**（只动 `GlobalMouseHook`，事件消费端已经是 `BeginInvoke` 投递，无需改动）：

```csharp
public GlobalMouseHook()
{
    _hookReady = new ManualResetEventSlim();
    _hookThread = new Thread(HookThreadMain)
    {
        IsBackground = true,
        Name = "Quickstart.MouseHook",
        Priority = ThreadPriority.Highest   // 钩子回调必须永远最快返回
    };
    _hookThread.Start();
    _hookReady.Wait(TimeSpan.FromSeconds(3));
    if (_hookHandle == IntPtr.Zero) throw ...;
}

private void HookThreadMain()
{
    _hookThreadId = GetCurrentThreadId();
    _hookProc = HookCallback;
    _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
    _hookReady.Set();
    while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
    {
        TranslateMessage(ref msg);
        DispatchMessage(ref msg);
    }
    if (_hookHandle != IntPtr.Zero) UnhookWindowsHookEx(_hookHandle);
}

// Dispose: PostThreadMessage(_hookThreadId, WM_QUIT, 0, 0); _hookThread.Join(500);
```

**配套调整**：
- `UpdateSettings` 写入的 `_enabled/_dragTriggerDxLogical/_dragTolerateDyLogical` 改为 `volatile`（UI 线程写、钩子线程读）。
- `Program.cs:325-331` 的 `GestureMove` 处理程序直接写闭包变量 `latestGesturePoint`（`Point`，两个 int，跨线程有撕裂理论风险）——改为 `Interlocked.Exchange(ref _packedPoint, ((long)pt.Y << 32) | (uint)pt.X)` 打包读写，或加一把小锁。`gestureMovePending` 改 `volatile bool`。
- `SynthesizeRightClick`（SendInput）、`GetForegroundWindow`、`GetDpiForWindow` 在钩子线程调用都是合法的，无需动。

**收益**：整机鼠标延迟与 UI 线程负载**彻底解耦**；钩子被系统摘除的风险从"UI 卡 300ms 就可能发生"降到"几乎不可能"。
**风险**：低。事件时序不变（仍串行经 UI 队列）；需要回归右键单击重放（无拖动时 DOWN+UP 合成）与手势取消。
**验证**：主动在 UI 线程 `Thread.Sleep(2000)`（临时调试代码）期间移动鼠标——改前整机卡顿，改后丝滑；手势照常触发（事件延迟到 UI 恢复，符合预期）。
**工作量**：小（半天含回归）。

### 3.2 首次弹窗图标提取异步化（占位 → 后台补真图）

**问题**：`MainPopup.RefreshList`（`MainPopup.cs:1552-1643`）对未缓存图标同步调用 `IconExtractor.GetIcon`：

- `.exe/.lnk/.ico` 等按真实路径走 `File.Exists` + `SHGetFileInfo`（`IconExtractor.cs:88-108`）——真实磁盘 I/O，冷 NTFS 缓存下每个几 ms～几十 ms，网络路径可达秒级；
- 自定义图标 `CustomIconStore.TryLoad`（`CustomIconStore.cs:20-36`）与 favicon 磁盘缓存 `FaviconService.TryGetCached`（`FaviconService.cs:47-81`）同样是 UI 线程同步 `File.ReadAllBytes`。

首次弹窗（或编辑图标后）会在 UI 线程串行做完全部条目的 I/O。favicon 的网络加载已经是"占位 → 异步 → `ApplyFaviconToItems` 回填"模式（`MainPopup.cs:1698-1744`），但本地图标没有。

**改法**（复用既有 favicon 模式）：
1. `RefreshList` 里对**未命中 `_iconImages` 的 per-file/自定义图标**：先用按扩展名的通用图标占位（`SHGFI_USEFILEATTRIBUTES` 不触盘，可安全同步），把 `(iconKey, path)` 塞进待取队列。
2. 新增一个**单例后台 STA 工作线程**（`SHGetFileInfo` 取图标建议在 STA/已初始化 COM 的线程上调；参照 `ClipboardHistoryService.cs:394-415` 现成的 STA 线程模式），逐个提取真实图标。
3. 完成后 `BeginInvoke` → `RegisterIcon` + 按 iconKey 刷新对应 `ListViewItem.ImageKey` + `_listView.Invalidate()`——与 `ApplyFaviconToItems` 同构。
4. 通用扩展名图标、`<DIR>` 文件夹图标不触盘，维持同步。

**收益**：首次手势弹窗渲染时间与磁盘状态解耦，冷盘/网络路径条目多时从可感知顿挫降为"先见列表、图标百毫秒内补齐"。
**风险**：低。图标短暂显示通用占位（favicon 已有相同体验先例）；注意窗体销毁时取消队列（复用 `_disposeCts`）。
**验证**：把一个指向断开网络盘的 .lnk 加入收藏 → 改前首次弹窗卡住数秒，改后立即出列表。
**工作量**：中（1 天含回归）。

## 4. P1 —— 启动路径（托盘就绪时间）

> 当前 `Main` 在 `Application.Run()` 前的串行顺序：编码注册 → DPAPI → 单实例 → WinForms 初始化 → 读配置 → **注册表检查/注册** → **三个探测器** → 服务构造 → 托盘图标 → 钩子/热键 → **剪贴板历史落盘加载** → Run。加粗项都可后移。

### 4.0 先加启动打点（改任何东西之前）

`Program.Main` 入口起一个 `Stopwatch`，在下列里程碑处（环境变量 `QUICKSTART_PERF=1` 时）向 `%LOCALAPPDATA%\Quickstart\startup-trace.log` 追加一行：Main 进入、配置加载完、托盘可见、钩子就绪、进入 Run、首个 Idle、预热完成。十几行代码，是所有后续项的验收依据。

### 4.1 Shell 注册表检查/注册后移到后台

`Program.cs:53-61`：每次启动同步做约 6 次注册表键读取（`IsProtocolRegistered` + `IsRegistered`），未注册时再写。结果不被启动路径消费——放 `Task.Run` 在 Run 之后执行即可（首个 Idle 时投递）。约省 1–5ms，更重要的是让托盘更早出现。

### 4.2 外部工具探测后移 + 记住"探测过没找到"

`Program.cs:64-94`：TC/DOpus/Everything 三个探测只在对应路径为空时运行——但**探测失败不落盘任何标记**，于是没装这些工具的用户**每次启动都重新探测**。其中 `EverythingDetector.TryRunningProcess`（`EverythingDetector.cs:48-68`）调用 `Process.GetProcessesByName` 枚举全系统进程（10–50ms），后面还有注册表 + 常见路径 + **整条 PATH 逐目录 `File.Exists`**。

改法（两步都做）：
1. 整块挪到后台（首个 Idle 后 `Task.Run`），探测到再 `configManager.Save()`。消费方都在用户动作路径上（打开文件夹/左滑搜索），后台完成绰绰有余；Everything 路径失效时本就有运行时兜底再探测（`Program.cs:575-590`）。
2. 配置里加 `ToolDetectionAttemptedVersion`（记应用版本号）：本版本探测过且未找到就不再探测；升级版本后重试一次。设置页"检测"按钮仍强制探测，功能不变。

风险点：首次运行后的最初一两秒内打开文件夹，TC 探测可能未完成 → 按现有回退逻辑走资源管理器（`ProcessLauncher.cs:38-45`）。仅首次运行、仅窗口期内、且有合理回退——可接受。

### 4.3 剪贴板历史落盘加载后移

`ClipboardHistoryService.Start`（`ClipboardHistoryService.cs:35-48`）在启动路径同步 `File.ReadAllText` + 反序列化。历史只在弹窗"历史"Tab 或首次去重时被消费。改法：`Start` 只创建监听窗口（需要 UI 线程），`LoadFromDiskIfNeeded` 丢线程池，完成后 `RaiseChanged`。注意与首条剪贴板事件的竞态：加载合并时以磁盘项为底、内存新项置顶（现有去重逻辑已能吸收）。本机文件 10KB 收益小，但该文件上限可到 MB 级（200 条 × 200K 字符），是护栏性优化。

### 4.4 预热时做一次 RefreshList

`PrewarmInitialUi`（`Program.cs:473-485`）只创建了句柄；首次手势仍要现场跑 `RefreshList`（含全部图标提取与布局测量）。在预热处对隐藏窗体调用一次 `popup.RefreshList()`，把这笔成本挪到空闲期。与 §3.2 叠加后，首次手势与后续手势的耗时将基本一致。顺带效果：favicon 后台加载、路径校验也在空闲期提前完成。

### 4.5 二次实例转发路径微清理

`--add`/协议转发路径（Explorer 右键菜单的延迟感）当前已经在 WinForms 初始化前返回，很好。可再把 `Encoding.RegisterProvider` 与 `AiSecretStore.Protector`（`Program.cs:13-16`）移到单实例判定之后，让转发路径零多余工作。收益 ~1ms，属顺手清理。该路径的主成本是 .NET 运行时本身（R2R 后约 100–250ms），见 §6。

## 5. P2 —— 交互细节

### 5.1 主列表开启双缓冲

`MainPopup._listView` 是 OwnerDraw（`MainPopup.cs:160-181`）但未开双缓冲（`AiActionPickerPopup` 的自绘控件开了，主列表漏了）。子类化一个 `BufferedListView : ListView`，构造里 `DoubleBuffered = true;`。消除悬停/选中/滚动重绘闪烁。工作量：10 分钟。

### 5.2 条目增删改的写盘移出 UI 线程

`ConfigManager.AddEntry/UpdateEntry/RemoveEntry/ReorderEntries` 走同步 `Save()`（`ConfigManager.cs:99-116`）：临时文件写 + 备份复制 + 重命名，三次文件 I/O 在 UI 线程。改法：`SaveInternal` 持锁序列化出 JSON 快照后，把 `WriteSnapshot(json)` 投给线程池（`_ioLock` 已保证写盘互斥与顺序）。语义不变（快照仍是修改后的精确状态），只是落盘不再阻塞 UI。慢盘/杀软实时扫描场景下每次编辑省 5–50ms。

### 5.3 搜索防抖 200ms → 120ms

`MainPopup.cs:402-407`。拼音首字母有记忆化缓存、分组签名未变时不重建 UI，实际 `RefreshList` 在常规条目量下是毫秒级，200ms 的防抖此刻偏保守，是搜索"跟手感"的主要延迟来源。降到 120ms 前先用 §4.0 的打点确认单次 RefreshList 耗时 <10ms。

### 5.4 AiPopup 预热按需

`Program.cs:385-389` 无条件在空闲 3 秒后构建最重的 AiPopup。若 `LeftDragAction == EverythingSearch` 且没有任何 API 类 Prompt/Skill 配置，可跳过预热（首次真正用到时现建，仅那一次略慢）。纯内存优化（估计省 10–30MB 工作集），对不用 AI 功能的用户友好。

## 6. P3 —— 发布形态与卫生项

### 6.1 发布参数：现状已接近该形态的最优

冷启动大头是自包含运行时加载 + WinForms 初始化，R2R + 未压缩已把 JIT/解压成本压掉了。可再做的都是边际实验，**改前后各测 5 次冷/热启动取中位数**：

| 实验 | 预期 | 说明 |
|---|---|---|
| `PublishReadyToRunComposite=true` | 启动 -0~30ms，体积 +5~10% | 复合 R2R 减少跨程序集 stub 开销；收益不确定，测了再说 |
| 移除 `IncludeNativeLibrariesForSelfExtract` | 无变化 | 本机已证实无解压发生，该开关是空操作，属清理 |
| `InvariantGlobalization=true` | 启动 -0~10ms，工作集 -几 MB | csproj 注释称 GB2312 需要 false，**此前提大概率不成立**：GB2312 由 `CodePagesEncodingProvider` 提供，不依赖 ICU。但必须实测拼音搜索、日期显示、中文排序后再上——存疑就保持现状 |

### 6.2 NativeAOT：明确不做（功能风险）

csproj 已注明放弃。补充理由固化下来：WinForms 在 .NET 10 仍不受 NativeAOT 官方支持；`BuiltInComInteropSupport` 与 NativeAOT 不兼容，而剪贴板、拖放、Shell 图标都踩在 COM 上。与"功能不打折"冲突，除非未来官方支持，否则不碰。

### 6.3 无界缓存加上限（长期驻留卫生）

托盘应用一跑数周，三个字典只增不减：
- `MainPopup._truncateCache`（`MainPopup.cs:61`）：key 是 `(文本, 宽度)`，DPI/改名会积累 → 超过 ~2000 条时 `Clear()`。
- `PinyinHelper.InitialsCache`（`PinyinHelper.cs:28`）：剪贴板历史预览、最近条目路径都会进来 → 超过 ~4096 条时 `Clear()`（重算成本低）。
- `IconExtractor._cache` 实际受条目数约束，可不动。

### 6.4 小型 GDI/线程卫生

- `BuildItemContextMenu` 每次弹菜单 `new Font(..., Bold)`（`MainPopup.cs:953、988、1008` 等）：`ToolStripItem.Dispose` 不负责释放 Font，虽有 finalizer 兜底，仍建议提为静态复用。
- `ClipboardHistoryService` 每次读/写剪贴板都新建 STA 线程（`ClipboardHistoryService.cs:394-488`）：频率低可接受；若做 §3.2 的共享 STA 工作线程，可顺路合并。

## 7. 明确不建议做的（负优化清单）

| 想法 | 为什么不 |
|---|---|
| `Process.Start` 挪后台线程 | 已踩过坑：前台激活权丢失，单实例程序不前置（2026-07-15 记录，`MainPopup.cs:1329` 注释） |
| ListView 虚拟模式 | 收藏条目量级用不上，会显著复杂化 OwnerDraw/拖拽重排 |
| GC 参数调优（Server GC/堆上限） | 托盘小应用，默认 Workstation 并发 GC 就是正确答案 |
| 砍图标/拼音/路径缓存换启动内存 | 这些缓存正是当前流畅度的来源 |
| 开机自启改计划任务延迟启动 | 手势工具的价值就是登录后立刻可用，延迟启动伤功能体验 |

## 8. 建议实施顺序与验收

```
第 1 步  §4.0 启动打点                       （半小时，出基线）
第 2 步  §3.1 钩子专用线程                   （半天 + 回归右键/手势）
第 3 步  §3.2 图标异步管线                   （1 天 + 回归图标显示/编辑图标）
第 4 步  §4.1/4.2/4.3 启动项后移             （半天，打点对比）
第 5 步  §4.4 预热 RefreshList + §5.1 双缓冲 （1 小时）
第 6 步  §5.2/5.3/5.4 + §6.3/6.4             （按需，零散小改）
第 7 步  §6.1 发布参数实验                   （可选，测不出收益就回退）
```

每步验收：
- 打点日志：`Main→托盘可见`、`Main→钩子就绪`、首次/二次手势 `RefreshList` 耗时，改前后对比。
- 回归清单：右键单击重放、右拖/左拖手势全流程、微信选区捕获、Everything 搜索、设置窗保存、图标（exe/lnk/自定义/favicon）显示、剪贴板历史、拖拽重排、多显示器 DPI 切换。
- 长稳：连续运行 48h 后 GDI 对象数（任务管理器加列）与工作集无单调增长。
