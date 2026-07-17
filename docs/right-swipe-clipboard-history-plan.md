# 右滑「剪贴板历史」规划

> 目标：右滑主弹窗里，在 **「文本」分类下方** 增加 **剪贴板历史**。  
> 手势松手点中某条历史 → **再次写入剪贴板（纯文本）** → 到别处 **Ctrl+V** 粘贴为纯文本。  
> 原则：**越快越好、间接越少** —— 复用现有右滑手势 / 文本复制 / 纯文本剪贴板能力，不做复杂管理器。

---

## 1. 结论：可做，且和现有「文本」天然同轨

| 项 | 建议 |
|----|------|
| 入口位置 | 右滑 `MainPopup` 左侧 Tab：**文本下方** 新增一栏 **「历史」**（或「剪」） |
| 触发 | 与文本条目一致：点击 / **手势松手命中** → 复制纯文本到剪贴板 + Toast「已复制」 |
| 粘贴 | **首期只写剪贴板**，用户到别处 Ctrl+V；结果本身是纯文本，**不必**再走左滑「粘贴为纯文本」 |
| 监听 | 系统剪贴板变化时自动入队（仅文本） |
| 持久化 | 本地轻量 JSON（可加密可选），默认开；敏感可开关「不落盘」 |

现有能力可直接复用：

| 现成能力 | 位置 |
|----------|------|
| 右滑主弹窗 + 手势松手 | `MainPopup.ShowAtGesturePoint` / `TryReleaseAtScreenPoint` |
| 文本复制 + Toast | `CopyToClipboard` → `Clipboard.SetText` +「已复制」 |
| 纯文本写盘式 API | `WebPromptSender` / `ScreenshotOcrService.SetClipboardPlainTextAsync`（STA 重试） |
| 配置持久化 | `ConfigManager` + `%LOCALAPPDATA%\Quickstart\` |

---

## 2. 你要的最短体验

```
日常：任意处 Ctrl+C（或本软件写剪贴板）
    → 后台监听：纯文本入历史队列（去重、截断）

使用：右键右滑 → 切到「历史」Tab
    → 鼠标移到某条文字上松手（或点击）
    → 该条以纯文本再次写入剪贴板
    → 面板关闭 / Toast「已复制」
    → 到目标处 Ctrl+V（粘贴为纯文本）
```

**首期不做：**

- 不自动往原窗口 Ctrl+V（右滑场景焦点常在弹窗，贴错风险高；需要时可用左滑「粘贴为纯文本」）
- 不存图片 / 文件路径以外的二进制剪贴板
- 不做云同步、不做跨设备

---

## 3. 界面规划（右滑 MainPopup）

### 3.1 Tab 结构

现状 4 Tab：

```
文件夹 | 文件 | 网页 | 文本
```

改为 5 Tab（文本下方增加历史）：

```
文件夹 | 文件 | 网页 | 文本 | 历史
```

| Tab | 竖排文案建议 | 说明 |
|-----|--------------|------|
| 文本 | `文\n本` | 不变：收藏的固定文本 |
| 历史 | `历\n史` 或 `剪` | 剪贴板历史（动态） |

> 若竖排空间紧，可用单字 **「剪」**，Tooltip 写「剪贴板历史」。

### 3.2 列表行

每条历史一行，风格对齐现有 ListView：

| 区域 | 内容 |
|------|------|
| 主文 | 单行预览：首行文字，超长 `…`（约 40～60 字） |
| 副信息（可选） | 时间相对文案：`刚刚` / `3 分钟前`；字符数 |
| Tooltip | 完整文本（过长截断到 2KB 预览） |
| 空态 | 「暂无剪贴板历史」+ 一行小字「复制任意文本后会自动出现」 |

### 3.3 交互

| 操作 | 行为 |
|------|------|
| **手势松手在条目上** | 与文本一致：纯文本写剪贴板 → Hide 弹窗 → Toast |
| **点击条目** | 同上（若当前交互模式已激活弹窗） |
| **右键条目** | 复制 / 删除本条 / 清空全部（P1） |
| **搜索框** | 在历史全文中过滤（子串 + 拼音可选，P1 可先只做子串） |
| **分组栏** | 历史 **无分组**；右侧分组区隐藏或只显示「全部」 |

### 3.4 与「文本」的区别（写进 UI 心智）

| | 文本（收藏） | 历史（剪贴板） |
|--|--------------|----------------|
| 来源 | 用户主动添加 | 系统复制自动记录 |
| 是否长期 | 是 | 默认最多 N 条，可清空 |
| 动作 | 复制到剪贴板 | 再次复制到剪贴板（纯文本） |

---

## 4. 数据与监听

### 4.1 模型

```csharp
public sealed class ClipboardHistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; set; } = string.Empty;   // 纯文本全文
    public DateTime CopiedAt { get; set; } = DateTime.Now;
    public int CharCount => Text?.Length ?? 0;
}
```

### 4.2 配置（`AppConfig`）

```csharp
public sealed class ClipboardHistoryConfig
{
    public bool Enabled { get; set; } = true;
    public int MaxItems { get; set; } = 50;          // 默认 50，可设 20～100
    public int MaxTextLength { get; set; } = 20_000;  // 单条截断，防超大粘贴
    public bool Persist { get; set; } = true;         // false = 仅内存，退出清空
    public bool IgnoreDuplicates { get; set; } = true; // 与队首相同则只刷新时间
}
```

落盘路径建议：

```
%LOCALAPPDATA%\Quickstart\clipboard-history.json
```

与 `config.json` 分离，避免主配置膨胀；`Persist=false` 时不写文件。

### 4.3 监听方式（Windows）

| 方案 | 做法 | 建议 |
|------|------|------|
| **A. Clipboard 监听窗** | 隐藏 `NativeWindow` + `AddClipboardFormatListener` / `WM_CLIPBOARDUPDATE` | **推荐**：省电、及时 |
| B. 定时轮询 | 每 500ms 读 Hash | 实现简单但费电，仅作后备 |

规则：

1. 仅处理 **Unicode 文本**（`CF_UNICODETEXT` / `Clipboard.ContainsText`）  
2. 读剪贴板放 **STA 后台线程**（复用 `PlainTextPasteService` / OCR 写盘同一套路），避免拖死 UI 与鼠标钩子  
3. 本程序自己 `SetText` 也会触发监听 → 用 **忽略窗口 / 序列号 / 短防抖** 避免「自己复制自己」死循环；入队后仍可接受（刷新「最近」到顶）  
4. 空文本、纯空白、超长截断到 `MaxTextLength`  
5. `IgnoreDuplicates`：与当前队首文本相同 → 移到队首并更新时间，不新增  

### 4.4 隐私

- 设置里：**启用剪贴板历史**、**退出后清空 / 不落盘**  
- 不采集密码管理器等特殊格式（只认标准文本）  
- 历史文件权限：仅当前用户可读写（默认 LocalAppData 即可）

---

## 5. 执行链路（松手 / 点击）

```
TryReleaseAtScreenPoint / ItemActivate
  → item.Tag is ClipboardHistoryItem hist
  → ClipboardHistoryService.SetPlainTextToClipboard(hist.Text)  // STA + 重试
  →（可选）Touch：把该项移到历史队首
  → Hide()
  → ShowToast("已复制")
  → 用户在别处 Ctrl+V → 粘贴为纯文本
```

与现有文本条目的差异仅在于：**数据源是历史服务，不是 `QuickEntry.Path`**。

> 用户原话「再次复制，在别的地方粘贴，粘贴为纯文本」→ **写入时只用 Unicode 纯文本**，不要带 HTML/RTF。这样别处粘贴自然是纯文本。

---

## 6. 模块拆分（实现清单）

| 模块 | 职责 | 文件建议 |
|------|------|----------|
| 配置 | 开关、条数、是否落盘 | `AppConfig.ClipboardHistory` |
| 服务 | 监听、入队、去重、持久化、写剪贴板 | `Quickstart/Core/ClipboardHistoryService.cs` |
| 模型 | 历史项 | `Quickstart.Core` 或 `Models/ClipboardHistoryItem.cs` |
| UI | 第 5 个 Tab + 列表绑定 + 空态 | `MainPopup.cs` |
| 设置 | 启用 / 条数 / 清空 / 不落盘 | `SettingsForm` 一小段 |
| 生命周期 | 启动注册监听、退出保存/释放 | `Program.cs` |

> **状态（2026-07-16）：P0 已落地** —— 监听 + 历史 Tab + 纯文本再复制 + 落盘 + 设置开关/清空。

**工期粗估（P0）：约 0.5～1 天。**

| 阶段 | 内容 | 约时 |
|------|------|------|
| P0 | 监听 + 内存队列 + 历史 Tab + 复制纯文本 + Toast | 3～4h |
| P0 | 落盘 + 设置开关 + 清空 | 1～2h |
| P1 | 右键删除单条、搜索过滤、相对时间 | 1～2h |
| P2 | 图片缩略图、固定钉住、快捷键呼出历史 | 另议 |

---

## 7. MainPopup 改动要点（避免踩坑）

1. **`TabKind` 增加 `ClipboardHistory`**，排在 `Texts` 之后。  
2. **`GetEntriesForActiveTab` 旁路**：历史 Tab 不走 `QuickEntry`，走 `ClipboardHistoryService.GetItems()`。  
3. **ListView 绑定**：`item.Tag = ClipboardHistoryItem`；`ExecuteEntry` 分支识别 Tag 类型。  
4. **手势松手**：`TryReleaseAtScreenPoint` 里除 `QuickEntry` 外处理历史项。  
5. **分组栏**：历史 Tab 隐藏分组或固定「全部」。  
6. **刷新时机**：弹窗 `ShowAtGesturePoint` 时 `RefreshList()`，保证最新历史在顶。  
7. **宽度**：长预览行可与现网文本一致；不必为历史单独加宽（可 P1 再调）。

---

## 8. 设置页文案（建议）

**剪贴板历史**

- ☑ 启用剪贴板历史（默认开）  
- 最多保留：`[50]` 条  
- ☑ 退出后保存到本地（取消则仅本次运行）  
- 按钮：`清空历史`

---

## 9. 验收清单

1. 外部复制一段 Word 富文本 → 历史出现，**内容为纯文本预览**  
2. 右滑 → 历史 Tab → 松手在条目上 → Toast「已复制」→ 记事本 Ctrl+V 为纯文本  
3. 连续复制相同内容 → 只有一条在顶（去重）  
4. 复制超长文本 → 截断，不卡 UI  
5. 关闭「启用」→ 不再入队；历史 Tab 可显示空态或隐藏  
6. 重启后：`Persist=true` 时历史还在；`false` 时为空  
7. 本软件「文本」收藏复制 / OCR 写剪贴板 → 也会进历史（合理）；无死循环  

---

## 10. 风险与对策

| 风险 | 对策 |
|------|------|
| 剪贴板被占用读失败 | STA 重试 3～5 次 + 短延迟（现有模式） |
| 监听拖死鼠标钩子 | 读/写均离 UI 线程；UI 只收「已更新」事件刷新 |
| 密码被记入历史 | 设置可关 / 不落盘；文档提示；P2 可做「忽略含 password 的窗」 |
| 历史 Tab 挤占竖向 | 单字「剪」或略缩 Tab 字号 |
| 与「文本」收藏混淆 | 空态与标题区分；历史不可「编辑收藏」 |

---

## 11. 建议落地顺序

1. **配置 + `ClipboardHistoryService`（内存）**  
2. **Program 启动注册监听**  
3. **MainPopup 第 5 Tab + 列表 + 松手/点击复制纯文本**  
4. **落盘 + 设置开关 + 清空**  
5. 自测验收清单  

---

## 一句话

**右滑 →「历史」→ 松手某条 → 纯文本再进剪贴板 → 别处 Ctrl+V。**  
监听自动记、最多约 50 条、可选不落盘；首期不做自动粘贴、不存图。
