# AI Phase 0 交互原型计划(含计划↔代码绑定机制)

> **分支**:`feat/hwx-s0`(自 `feat/hwx-build` 切出)
> **目标**:用 Wizard-of-Oz 方式(mock agent 回复,无 LLM 调用)验证五条核心交互决策,为嵌入式 Agent 路线(模式 D)定 UI 基调。
> **两阶段方式(2026-08-31 用户拍板)**:**M0 用 H5 技术栈快速实现原型**(动态语言、浏览器直出、AI 可直接自验画面);用户评审通过后,**M1-M3 用项目技术栈(Avalonia)按映射表翻译**。
> **本文档是 Phase 0 进度的唯一源头**(source of truth):任务状态、里程碑 tag 均以本文档及其绑定的 git 历史为准;Notion 侧《SourceGit AI · UI 交互设计与 Phase 0 规划》只是镜像展示。
> **设计背景**:五条交互决策的完整论证与市场分析见 Notion 正典文档《SourceGit · 改造为 AI Coding Agent Control Panel · 2026-08-28》及其同级的 Phase 0 设计文档。

---

## 1. 五条交互决策摘要

| # | 决策 | 要点 |
|---|------|------|
| ① | 对话面板 = 主区底部 dock | 可折叠、可拖拽、高度记忆(`Preferences.Layout`);工具栏开关,默认关;不用模态弹窗、不用右侧栏 |
| ② | 消息流三层分级 | 思考过程(灰暗、默认折叠)/ 动作卡片(📖🔍✏️,可点击跳转)/ 最终回答(正常样式) |
| ③ | 写操作审查回路 | 绝不静默落盘:动作卡片 → diff 视图预览 → Approve/Reject → 批准后进未暂存列表 |
| ④ | 权限分级 | 读静默;写文件首次确认可记住;执行命令永远确认并显示完整命令行 |
| ⑤ | 渐进披露 | 功能默认关闭;关闭时界面与上游版一致 |

## 2. 任务拆分与绑定表

> 状态勾选**必须与该任务的实现代码在同一个 commit 中更新**(见 §3 原子性约定)。

### M0 H5 交互原型(用户评审门)

- [x] **T-s0-0 H5 交互原型页**
  - 交付:`docs/prototype/ai-s0/`(index.html + styles.css + app.js + README.md 翻译映射);零构建、零 npm 依赖,`python3 -m http.server -d docs/prototype/ai-s0 8765` 打开 http://localhost:8765 查看
  - 技术决策(2026-08-31):原型用 H5 快速实现——动态语言迭代快、浏览器直出、AI 可在浏览器中直接自验画面;结构化设计(布局分区对齐 SourceGit、消息流数据驱动、README 含 H5→Avalonia 控件映射表)保证翻译是机械工作
  - 验收:五条交互决策(底部 dock / 三级渲染 / 审查回路 / 权限分级 / 状态横幅)均可在浏览器中完整演示
  - **M0 出口 = 用户评审通过** → 打 `ai-s0/m0`;评审不通过则在本任务内迭代(同一 Task ID 追加 Refs commit)

### M1 对话面板骨架(Avalonia 翻译)

- [x] **T-s0-1 底部 dock 对话面板**
  - 交付:`Views/AIChatPanel.axaml`(+ code-behind)、对应 ViewModel;挂载点:`Repository.axaml`(dock 位置)、工具栏开关按钮、`Models/Preference.cs`(面板高度字段)
  - 验收:面板可开合/拖拽高度并记忆;开关默认关;关闭开关后界面与上游一致
- [x] **T-s0-2 mock 会话数据层**
  - 交付:`IAgentSession` 接口 + `MockAgentSession`(硬编码回复序列:思考/工具调用/回答;含一个 ✏️ 修改提议的假 patch)
  - 验收:面板内跑通一段完整 mock 会话;无任何 LLM/网络调用;接口抽象足以在 Phase 1 换真实现而不动 UI

### M2 审查回路(核心)

- [x] **T-s0-3 消息三级渲染**
  - 交付:折叠思考区控件 / 动作卡片控件 / 回答文本模板
  - 验收:思考默认折叠可展开;动作卡片常显;三者在同一会话流中混排正常
- [x] **T-s0-4 动作卡片与 diff 联动**
  - 交付:✏️ 卡片点击 → 在现有 diff 视图打开假 patch 预览
  - 验收:从卡片到看到 diff ≤ 2 次点击
- [x] **T-s0-5 Approve/Reject 回路**
  - 交付:卡片上的 Approve/Reject;批准后 mock 改动进入 WorkingCopy 未暂存列表,拒绝后卡片标记 Rejected 且无残留
  - 验收:Reject 后工作区确无残留;Approve 后可走正常暂存/提交流程

### M3 权限与状态

- [ ] **T-s0-6 权限确认弹窗**
  - 交付:执行命令确认弹窗(完整命令行展示,样式参照 Askpass);写文件首次确认 + 记住选择
  - 验收:mock 会话触发两类确认,交互符合决策④
- [ ] **T-s0-7 AI 任务状态横幅**
  - 交付:`InProgressContext` 槽位新增 AI 任务横幅(中性蓝),mock 状态流转
  - 验收:横幅随 mock 会话阶段变化;不与 rebase/merge 等现有横幅冲突;面板折叠/展开不打断横幅显示

## 3. 计划↔代码绑定约定

1. **commit 尾注(代码 → 计划)**
   - 所有 Phase 0 实现类 commit 使用 scope `ai-s0`,并在正文末尾加尾注:
     ```
     feat(ai-s0): add bottom chat dock panel

     ...正文...

     Refs: AI-S0-T1
     ```
   - 一个 commit 覆盖多个任务时写多行 `Refs:`。
   - 仅文档/格式类 commit(不对应任务)可不带尾注。
2. **原子性(计划 → 代码)**:任务勾选与实现代码同 commit 提交。因此 `git log -- docs/plan/ai-phase0.md` 的每次变更都能对应到带 `Refs:` 的实现 commit。
3. **任务级追溯**(抗 rebase,不记录裸 SHA):
   ```
   git log --grep='Refs: AI-S0-T3'
   ```
4. **里程碑锚点**:每个 milestone 全部任务完成时打 annotated tag 并推送:
   ```
   git tag -a ai-s0/m1 -m "M1: dock panel + mock session"
   git push ghx ai-s0/m1
   ```
   - tag 名固定为 `ai-s0/m1`、`ai-s0/m2`、`ai-s0/m3`。
   - **rebase/amend 后允许重指 tag**(`git tag -f` + force push):tag 是"当前指向该里程碑的提交"的稳定句柄,不是不可变历史;Notion 与文档链接使用 tag 名而非 SHA,正是为了在重写历史后只需重指 tag 即可修复全部外链。
5. **Notion 镜像维护**:每个 milestone tag 推送后,由 ZCode 更新 Notion Phase 0 文档的「进度与绑定」节(回填 tag 链接与一句话总结)。镜像不承载状态,仅作人读视图。
6. **一致性自检**(任何时候可跑):
   ```
   # 每个已勾选任务都应有对应 Refs commit
   git log --grep='Refs: AI-S0-' --format='%H %s' | sort
   # 已打的里程碑 tag
   git tag -l 'ai-s0/*'
   ```

## 4. Phase 0 总体验收

> 验收方式(2026-08-31 调整):交互逻辑类条目在 M0 H5 原型上先行验证;与现有视图共存/布局记忆类条目在 M1-M3 Avalonia 翻译后验证。

- [ ] 不看终端,只看 GUI 能说出 agent 正在做什么、进行到哪一步
- [ ] 从动作卡片到看到 diff ≤ 2 次点击
- [ ] Reject 一个修改后,工作区确无残留
- [ ] 面板折叠/展开不打断正在看的 diff/graph(Avalonia 阶段验证)
- [ ] 关闭功能开关后,界面与上游一致(Avalonia 阶段验证)
- [ ] 全程无 LLM/网络依赖(mock 数据驱动)

## 5. 工程约束(上游合并缓解)

- 以**新增文件**为主(新文件几乎不与上游冲突),命名前缀 `AIChat` / `AIAgent`。
- 对现有文件的改动仅限最小挂载点:`Repository.axaml`(横幅槽位/dock 挂载)、工具栏按钮、`Preferences` 布局字段。
- mock 数据层与未来真 agent 层共用 `IAgentSession` 接口,Phase 1 换实现不动 UI。
- 不改 `src/AI/` 现有文件(commit message 生成功能保持独立)。
