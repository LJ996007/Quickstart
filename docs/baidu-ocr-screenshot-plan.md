# 左滑「截图 OCR」接入规划（百度免费额度）

> 目标：左滑动作里加一项 **截图 OCR**。触发后框选截图 → 百度 OCR → 结果写入剪贴板（纯文本）→ 你直接 Ctrl+V 粘贴；也可再点「粘贴为纯文本」。  
> 原则：**越快越好、间接越少** —— 最少配置、最少弹窗、复用现有左滑 / 剪贴板 / 密钥能力。

---

## 1. 结论：能接，而且适合个人日用

| 项 | 建议 |
|----|------|
| 是否可接入左滑 | **可以**，与「粘贴为纯文本 / Everything」同级工具项 |
| 接口 | **通用文字识别（标准版）** `general_basic` |
| 免费额度（官方当前） | 实名后自动发免费测试资源：个人约 **1000 次/月**，企业约 **2000 次/月**（成功/失败都计次；当月用不完不结转） |
| 超出后 | 未开通付费时请求会失败 → 托盘提示「额度用尽」；不默认开通扣费 |
| 对个人够用吗 | 一天几十次完全够；重度用可再换高精度版或本地 OCR（二期） |

> 说明：网上「每日 500 次」多为旧文案；以控制台「资源列表」与官方文档为准。  
> 参考：https://ai.baidu.com/ai-doc/OCR/fk3h7xu7h

**为何选标准版而不是高精度：**  
免费够用、延迟更低、实现最简单；首期场景是屏幕文字/网页/微信，标准版足够。

---

## 2. 你要的最终体验（最短路径）

```
右键左滑 → 点「截图 OCR」
    → 立刻进入框选截图（全屏半透明）
    → 松手完成选区
    → 后台调百度 OCR（托盘：识别中…）
    → 成功：纯文本写入剪贴板 + 托盘「已复制，可粘贴」
    → 你在原窗口 Ctrl+V（或再左滑「粘贴为纯文本」）
```

**不做（首期）：**  
- 不弹识别结果窗（少一步）  
- 不自动往目标窗口里 Ctrl+V（截图时焦点/前台窗可能已变；自动粘贴容易贴错）  
- 不强制依赖「粘贴为纯文本」才能用（OCR 结果本身就是纯文本）

**可选开关（设置里，默认关）：**  
`识别后自动粘贴到手势来源窗口` —— 仅在你确认需要时再做，避免贴错。

---

## 3. 操作流程（人 + 系统）

### 3.1 一次性配置（约 5 分钟）

1. 打开 [百度智能云](https://console.bce.baidu.com/) → 实名认证  
2. 进入 **文字识别** 控制台 → 创建应用 → 勾选 **通用文字识别**  
3. 拿到 **API Key / Secret Key**  
4. Quickstart **设置 → OCR（百度）**  
   - 粘贴 API Key / Secret Key（用现有 `AiSecretStore`/DPAPI 加密存）  
   - 开关：启用截图 OCR  
5. 左滑工具列出现 **截图 OCR**

### 3.2 日常使用

| 步骤 | 你 | 软件 |
|------|----|------|
| 1 | 右键左滑 | 弹出动作面板 |
| 2 | 点「截图 OCR」 | 关面板，全屏进入框选 |
| 3 | 拖拽框选区域，松手 | 截图 → 压 JPEG/PNG → 调百度 |
| 4 | 等 1～2 秒 | 托盘「识别中…」 |
| 5 | 成功后 Ctrl+V | 剪贴板已是纯文本 |
| 6 | （可选）再左滑「粘贴为纯文本」 | 与现网逻辑一致，冗余保险 |

### 3.3 失败怎么提示

| 情况 | 提示 |
|------|------|
| 未配置 Key | 「请先在设置中填写百度 OCR 密钥」并打开设置页 |
| 框选太小 / 取消（Esc / 右键） | 静默退出，不计次 |
| 网络 / Token 失败 | 「OCR 请求失败：…」 |
| 额度用尽 | 「百度 OCR 免费额度已用尽」 |
| 图中无字 | 「未识别到文字」 |

---

## 4. 实现方式（技术规划）

### 4.1 挂接点（复用现有链路）

| 层 | 改动 | 说明 |
|----|------|------|
| `AiActionKind` | 新增 `ScreenshotOcr` | 与 `PlainTextPaste` 并列 |
| `AiActionPickerPopup` | 工具列固定一项「截图 OCR」 | 未配置 Key 也显示，点了引导配置 |
| `Program.ExecuteLeftAction` | `if (selection.IsScreenshotOcr) RunScreenshotOcr(...)` | 与纯文本粘贴同级分支 |
| 新服务 `ScreenshotOcrService` | 截图 + OCR + 写剪贴板 | 单文件即可 |
| 新 UI `RegionCaptureOverlay` | 全屏半透明框选 | 无边框 TopMost，Esc 取消 |
| 新客户端 `BaiduOcrClient` | token + general_basic | `HttpClient`，无 NuGet 也可 |
| `AppConfig` / 密钥 | `OcrConfig` + Secret 存 Key | 对齐现有 AI 密钥模式 |
| `SettingsForm` | 一小节 OCR 配置 | 两个文本框 + 测试按钮 |

**不碰：** Prompt / Skill / AiPopup 主流程。OCR 是工具，不是模型对话。

### 4.2 核心流水线

```
1. 记录手势来源窗（可选，仅自动粘贴时用）
2. RegionCaptureOverlay.ShowModal() → Bitmap? / null
3. Bitmap → JPEG 质量 85 或 PNG，Base64；边长限制约 4096，过大则缩小
4. BaiduOcrClient.GetAccessToken(apiKey, secret)  // 缓存 ~25 天，本地记忆过期时间
5. POST general_basic  image=base64&language_type=CHN_ENG
6. 解析 words_result[].words，用 \n 拼接
7. Clipboard.SetText(纯文本 Unicode)
8. TrayBalloon「已复制 N 字，可直接粘贴」
```

### 4.3 百度 API（最小实现）

**Token**

```
GET https://aip.baidubce.com/oauth/2.0/token
  ?grant_type=client_credentials
  &client_id={API_KEY}
  &client_secret={SECRET_KEY}
```

**OCR 标准版**

```
POST https://aip.baidubce.com/rest/2.0/ocr/v1/general_basic?access_token=...
Content-Type: application/x-www-form-urlencoded
body: image={urlencode(base64)}&language_type=CHN_ENG
```

**解析**

```json
{ "words_result": [ { "words": "第一行" }, { "words": "第二行" } ] }
→ "第一行\n第二行"
```

Token 缓存：内存 + 配置里 `TokenExpireAt`，到期前 1 天刷新。

### 4.4 截图框选（Windows 最快实现）

- 全屏 `Form`：`FormBorderStyle.None`、`TopMost`、`Opacity≈0.3` 暗罩  
- 鼠标拖矩形，半透明选区 + 细边框  
- 松手：`Graphics.CopyFromScreen` 裁切选区  
- Esc / 右键：取消  
- 多显示器：覆盖手势所在 `Screen` 或全部 `VirtualScreen`（建议首期 **当前屏**，实现更短）

不必上 Windows Graphics Capture API；`CopyFromScreen` 对日用截屏 OCR 足够快。

### 4.5 与「粘贴为纯文本」的关系

| 能力 | 截图 OCR | 粘贴为纯文本 |
|------|----------|--------------|
| 输入 | 屏幕区域图像 | 剪贴板已有内容 |
| 输出 | 写入纯文本到剪贴板 | 把剪贴板洗成纯文本并 Ctrl+V |
| 是否必须串联 | **否** | OCR 后你直接 Ctrl+V 即可 |

OCR 结果本身是纯文本，**不需要**再走一次「粘贴为纯文本」才能贴；后者是富文本剪贴板场景的保险。

---

## 5. 配置模型（建议字段）

```csharp
// AppConfig
public OcrConfig Ocr { get; set; } = new();

public sealed class OcrConfig
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "baidu"; // 预留本地/其他
    public string ApiKeyId { get; set; } = "baidu-ocr"; // SecretStore 的 key 槽
    // Secret / API Key 走 AiSecretStore，不进明文 config.json
    public bool AutoPasteAfterOcr { get; set; } = false; // 二期
    public string LanguageType { get; set; } = "CHN_ENG";
}
```

密钥：`AiSecretStore.SaveApiKey("baidu-ocr", secretOrApiKey)` 或拆两个 id：`baidu-ocr-ak` / `baidu-ocr-sk`。

---

## 6. 文件与工期（最短路径）

| 优先级 | 工作 | 预估 |
|--------|------|------|
| P0 | 百度控制台拿 Key + 配置 UI | 0.5h（你操作）+ 0.5h 代码 |
| P0 | `RegionCaptureOverlay` 框选截图 | 0.5～1h |
| P0 | `BaiduOcrClient` + token 缓存 | 0.5h |
| P0 | 左滑项 + `ExecuteLeftAction` + 写剪贴板 + 托盘 | 0.5h |
| P0 | 联调：无字/取消/额度/网络 | 0.5h |
| **合计首期** | **可上线可用** | **约 0.5～1 天** |
| P1 | 自动粘贴回手势窗、最近使用记 OCR | 0.5h |
| P2 | 高精度版开关、多显示器整桌、本地 Paddle 兜底 | 按需 |

**依赖：** 无新 NuGet 也可（`HttpClient` + `System.Text.Json` + 现有 WinForms）。

---

## 7. 风险与规避

| 风险 | 规避 |
|------|------|
| 免费额度用完突然失败 | 托盘明确文案；设置页链到控制台资源列表 |
| 密钥泄露 | DPAPI 存本地，config 不写明文 |
| 截图含隐私上传云端 | 设置页文案声明「图片会上传百度 OCR」；取消即不上传 |
| 大图超时/超限 | 最长边缩到 1600～2000，JPEG 压缩 |
| 手势 NOACTIVATE 与截图层焦点 | 选 OCR 后先 Hide 动作面板，再 Show 截图层并抢焦点 |
| 自动粘贴贴错窗 | 首期默认只写剪贴板 |

---

## 8. 验收清单

1. 未配 Key：点「截图 OCR」→ 提示并打开设置  
2. 已配 Key：框选有字区域 → 1～3 秒内托盘成功 → 记事本 Ctrl+V 为纯文本  
3. Esc 取消：不请求 API、不计次  
4. 空白图：提示未识别到文字，不覆盖剪贴板（或可选清空——建议 **不覆盖**）  
5. 左滑「最近」可出现「截图 OCR」  
6. 与「粘贴为纯文本」互不干扰  

---

## 9. 推荐落地顺序（直接开干用）

1. **你**：百度控制台创建应用，准备 AK/SK  
2. **代码 P0**：配置字段 + 设置页 + Secret 存储  
3. **代码 P0**：框选截图  
4. **代码 P0**：OCR 客户端 + 左滑入口 + 剪贴板  
5. 自测 5 分钟 → 日常用  

**一句话产品定义：**  
左滑「截图 OCR」= **框一下 → 字进剪贴板 → 你随便贴**；百度免费额度扛日常，密钥本地加密，首期不做自动粘贴。

---

## 10. 落地状态（2026-07-16）

**P0 已实现（Windows）：**

| 文件 | 作用 |
|------|------|
| `Quickstart.Core/AppConfig.cs` | `OcrConfig` |
| `Quickstart.Core/BaiduOcrClient.cs` | token + `general_basic` |
| `Quickstart.Core/AiSecretStore.cs` | `GetApiKeyById` / 存 AK·SK |
| `Quickstart/UI/RegionCaptureOverlay.cs` | 全屏框选截图 |
| `Quickstart/Core/ScreenshotOcrService.cs` | 截图→OCR→剪贴板 |
| `Quickstart/UI/AiActionPickerPopup.cs` | 工具列「截图 OCR」 |
| `Quickstart/UI/SettingsForm.cs` | OCR 开关 + AK/SK |
| `Quickstart/Program.cs` | `RunScreenshotOcrAsync` |

**使用：** 设置里填百度 AK/SK → 保存 → 左滑点「截图 OCR」→ 框选 → 托盘提示后 Ctrl+V。
