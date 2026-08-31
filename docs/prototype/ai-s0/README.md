# SourceGit AI · Phase 0 H5 原型

Wizard-of-Oz 交互原型:**无 LLM、无网络**,纯 mock 会话脚本驱动,用于在浏览器中验证五条核心交互决策。评审通过后,按本文映射表翻译为 Avalonia 实现(M1-M3)。

## 运行

```bash
python3 -m http.server -d docs/prototype/ai-s0 8765
# 打开 http://localhost:8765
```

操作:「▶ 播放演示」或随意输入发送,即可推进脚本;在 ✏️ 卡片上 Approve/Reject、在权限弹窗中选择,观察审查回路。

## 五条决策的演示点

| 决策 | 原型中的位置 |
|---|---|
| ① 底部 dock | 主区底部面板:折叠按钮、顶部拖拽条调高 |
| ② 三级渲染 | 灰暗折叠的「思考过程」/ 📖🔍✏️ 动作卡片 / 正常回答文本 |
| ③ 审查回路 | ✏️ 卡片 [查看 Diff] → 右侧 diff 视图预览 → Approve 后文件带 `AI` 徽标进入未暂存列表;Reject 无残留 |
| ④ 权限分级 | 🛠 命令卡片弹出确认框,展示完整命令行 + 记住授权选项 |
| ⑤ 渐进披露 | 工具栏 🤖 开关(演示页默认开;关闭后 dock 消失,界面等同无 AI) |

## H5 → Avalonia 翻译映射表

| H5 元素 (index.html / app.js) | Avalonia 对应 | 说明 |
|---|---|---|
| `.app` 整体网格 | `Repository.axaml` 主 Grid | 分区刻意与现有布局对齐 |
| `.sidebar` | 现有左侧栏 | 不动 |
| `.toolbar` + `.tabs` | 现有页签工具栏 | 仅追加 🤖 ToggleButton(挂载点) |
| `.banner-slot` / `setBanner()` | `Repository.axaml` Row 0 `InProgressContext` ContentControl | 新增 `AIAgentTaskContext` VM,参照 `InProgressContexts.cs` 模式;中性蓝背景 |
| `.workarea`(文件列表 + diff) | WorkingCopy 页(`ChangeCollectionView` + `CommitChanges`) | 不新造;AI 徽标 = 行模板加 Badge |
| `.dock-resizer` + `.chat-dock` | 主 Grid 新增 Row + `GridSplitter` | `HistoriesLayout` 模式;高度存 `Preferences.Layout.AIChatPanelHeight` |
| `.stream` 消息流 | `ItemsControl` + DataTemplate Selector | 消息类型枚举 ↔ app.js 中 `t` 字段一一对应 |
| `.msg-reasoning`(details/summary) | `Expander`(Header=「思考过程」,IsExpanded=False)+ 灰色样式 | |
| `.card.read/.propose/.cmd-ok` | 卡片 DataTemplate(边框着色) | |
| `addProposeCard()` 的 Approve/Reject | 卡片内 Button + `PendingChange` 模型 | Approve = 应用 patch 到工作区(D2 接 `git apply`) |
| `#permModal` 权限弹窗 | 轻量 `Popup` 窗口 VM(参照 Askpass 样式) | |
| `$("aiToggle")` | 工具栏 ToggleButton ↔ `Preferences` 布尔项(默认 false) | |
| `SCRIPT` 数组 | `IAgentSession` 的 mock 事件序列 | Phase 1 换 `OpenAIAgentSession` 实现,UI 不动 |

## 与计划的绑定

本原型对应任务 **T-s0-0**(`docs/plan/ai-phase0.md` §2 M0);用户评审通过后打 tag `ai-s0/m0`,随后进入 M1(Avalonia 翻译)。
