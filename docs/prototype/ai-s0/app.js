// SourceGit AI · Phase 0 H5 原型交互脚本(Wizard-of-Oz,无 LLM)
// 数据驱动:SCRIPT 数组即"结构化交互设计",翻译到 Avalonia 时逐条对应 ViewModel 消息类型。
"use strict";

/* ---------------- mock 数据 ---------------- */

// 未暂存文件(初始)。Approve 后由脚本追加带 AI 徽标的条目。
const UNSTAGED = [
  { st: "M", path: "README.md" },
  { st: "U", path: "docs/notes.md" },
];

// 样例 diff(点击文件列表/卡片"查看 Diff"切换)
const DIFFS = {
  "README.md": [
    { k: "hunk", s: "@@ -58,7 +58,9 @@" },
    { k: "ctx", s: " ## Features" },
    { k: "del", s: "- Using AI to generate commit message." },
    { k: "add", s: "+ Using AI to generate commit message." },
    { k: "add", s: "+ Using AI to review the changes made by an agent." },
    { k: "add", s: "+ Using AI as your pair-programming copilot." },
    { k: "ctx", s: " - ..." },
  ],
  "docs/notes.md": [
    { k: "hunk", s: "@@ -1,3 +1,4 @@" },
    { k: "add", s: "+ # Phase 0 验收记录" },
    { k: "add", s: "+ - [ ] 底部 dock 手感" },
    { k: "add", s: "+ - [ ] 审查回路手感" },
  ],
  "src/AI/Agent.cs": [
    { k: "hunk", s: "@@ -17,6 +17,9 @@ public class Agent" },
    { k: "ctx", s: "         public Agent(Service service)" },
    { k: "ctx", s: "         {" },
    { k: "del", s: "-             _service = service;" },
    { k: "add", s: "+             _service = service;" },
    { k: "add", s: "+             _session = new ConversationSession(service);" },
    { k: "add", s: "+             _session.Closed += OnSessionClosed;" },
    { k: "ctx", s: "         }" },
    { k: "hunk", s: "@@ -40,4 +43,18 @@ public class Agent" },
    { k: "ctx", s: "         public async Task GenerateCommitMessageAsync(...)" },
    { k: "del", s: "-             var chatClient = _service.GetChatClient();" },
    { k: "add", s: "+             var chatClient = _session.Client;" },
    { k: "add", s: "+             if (_session.IsCancelled)" },
    { k: "add", s: "+                 throw new OperationCanceledException();" },
    { k: "add", s: "+ " },
    { k: "add", s: "+             // streamed output for long-running generations" },
    { k: "add", s: "+             await foreach (var delta in chatClient.CompleteChatStreamingAsync(messages))" },
    { k: "add", s: "+                 onUpdate?.Invoke(delta.Text);" },
  ],
};

// 演示会话脚本。gate 字段表示需要用户交互(Approve/Reject、权限弹窗)后才继续。
const SCRIPT = [
  { t: "banner", on: true, text: "Agent 正在分析当前改动…" },
  { t: "reasoning", text: "用户希望让对话面板支持流式输出和多轮会话。当前 Agent.cs 是单次调用,没有会话状态。我先读现有实现,再找取消相关的处理。" },
  { t: "read", file: "src/AI/Agent.cs", sub: "123 行" },
  { t: "grep", pattern: "CompleteChatAsync", sub: "4 处匹配" },
  { t: "reasoning", text: "改动点集中在两处:构造函数需要持有会话对象;生成方法需要换成流式接口并尊重取消令牌。生成一个最小改动补丁。" },
  { t: "propose", file: "src/AI/Agent.cs", add: 12, del: 2, gate: "decide" },
  // —— 用户 Approve/Reject 后继续 ——
  { t: "cmd", cmdline: "dotnet build src/SourceGit.csproj -c Debug", gate: "perm" },
  { t: "cmdok", cmdline: "dotnet build", sub: "Build succeeded. 0 Warning(s) · 4.2s" },
  { t: "banner", on: false },
  { t: "answer", text: "已完成对 src/AI/Agent.cs 的改造提议:引入 ConversationSession 承载多轮状态,生成路径切换为流式并接入取消令牌。改动已按你的决定处理(见上方卡片),构建通过。需要我继续把 AIAssistant 弹窗替换为常驻面板吗?" },
  { t: "note", text: "— Mock 会话结束(演示)" },
];

/* ---------------- DOM 工具 ---------------- */
const $ = (id) => document.getElementById(id);
const stream = $("stream");

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/* ---------------- 未暂存列表 / diff 视图 ---------------- */
let selectedFile = "README.md";

function renderUnstaged() {
  const ul = $("unstagedList");
  ul.innerHTML = "";
  for (const f of UNSTAGED) {
    const li = el("li", f.path === selectedFile ? "sel" : "");
    const st = el("span", "st " + f.st, f.st);
    const name = el("span", "", f.path);
    li.append(st, name);
    if (f.ai) li.append(el("span", "badge-ai", "AI"));
    li.onclick = () => { selectedFile = f.path; showDiff(f.path); renderUnstaged(); };
    ul.appendChild(li);
  }
  $("unstagedCount").textContent = UNSTAGED.length;
}

function showDiff(path, meta) {
  selectedFile = path;
  $("diffTitle").textContent = path;
  $("diffMeta").textContent = meta || "";
  const pane = $("diffPane");
  pane.innerHTML = "";
  const lines = DIFFS[path] || [{ k: "ctx", s: "(二进制或空 diff — mock)" }];
  for (const l of lines) pane.appendChild(el("span", "ln " + l.k, l.s));
}

/* ---------------- 状态横幅(InProgressContext 槽位) ---------------- */
function setBanner(on, text) {
  const slot = $("bannerSlot");
  slot.innerHTML = "";
  if (!on) return;
  const b = el("div", "ai-banner");
  const spin = el("span", "spin");
  const label = el("span", "grow", text);
  const pause = el("button", "mini", "暂停");
  const cancel = el("button", "mini", "取消");
  cancel.onclick = () => { setBanner(false); addNote("— 已请求取消(mock)"); };
  b.append(spin, label, pause, cancel);
  slot.appendChild(b);
}

/* ---------------- 消息渲染(决策② 三级) ---------------- */
function addReasoning(text) {
  const d = el("details", "msg-reasoning");
  const s = el("summary", null, "思考过程(已折叠)");
  d.appendChild(s);
  d.appendChild(el("div", null, text));
  stream.appendChild(d);
  scrollStream();
}
function addUser(text) {
  stream.appendChild(el("div", "msg-user", text));
  scrollStream();
}
function addAnswer(text) {
  const w = el("div", "msg-answer");
  w.appendChild(el("div", "who", "🤖 Assistant"));
  w.appendChild(el("div", null, text));
  stream.appendChild(w);
  scrollStream();
}
function addNote(text) {
  stream.appendChild(el("div", "msg-note", text));
  scrollStream();
}

function addReadCard(file, sub) {
  const c = el("div", "card read");
  const row = el("div", "row1");
  row.append(el("span", null, "📖"), el("span", "fname", file), el("span", "grow"));
  const link = el("button", "linkish", "查看文件");
  link.onclick = () => showDiff(file, "只读 · " + (sub || ""));
  row.appendChild(link);
  c.appendChild(row);
  if (sub) c.appendChild(el("div", "sub", sub));
  stream.appendChild(c);
  scrollStream();
}

function addGrepCard(pattern, sub) {
  const c = el("div", "card read");
  const row = el("div", "row1");
  row.append(el("span", null, "🔍"), el("span", "fname", "grep \"" + pattern + "\""));
  c.appendChild(row);
  if (sub) c.appendChild(el("div", "sub", sub));
  stream.appendChild(c);
  scrollStream();
}

/* ✏️ 修改提议卡(决策③ 审查回路) */
function addProposeCard(step, onDone) {
  const c = el("div", "card propose");
  const row = el("div", "row1");
  row.append(
    el("span", null, "✏️"),
    el("span", "fname", step.file),
    el("span", "stat-add", "+" + step.add),
    el("span", "stat-del", "−" + step.del),
    el("span", "grow")
  );
  const actions = el("span", "card-actions");
  const view = el("button", "linkish", "查看 Diff");
  view.onclick = () => showDiff(step.file, "AI 提议 · 未应用");
  const ok = el("button", "btn primary", "Approve");
  const no = el("button", "btn danger", "Reject");
  actions.append(view, ok, no);
  row.appendChild(actions);
  c.appendChild(row);
  c.appendChild(el("div", "sub", "该修改尚未写入工作区,批准后进入未暂存列表"));
  stream.appendChild(c);
  scrollStream();

  const settle = (approved) => {
    ok.disabled = no.disabled = view.disabled = true;
    c.classList.add("applied");
    c.querySelector(".sub").textContent = "";
    const v = el("div", "sub verdict " + (approved ? "ok" : "no"), approved ? "✓ 已应用 — 进入未暂存列表,可正常暂存/提交/丢弃" : "✗ 已拒绝 — 未写入任何内容");
    c.appendChild(v);
    if (approved) {
      UNSTAGED.push({ st: "M", path: step.file, ai: true });
      renderUnstaged();
      showDiff(step.file, "AI 提议 · 已应用");
    }
    onDone(approved);
  };
  ok.onclick = () => settle(true);
  no.onclick = () => settle(false);
}

/* 🛠 命令执行卡 + 权限弹窗(决策④) */
function addCommandWithPermission(step, onDone) {
  const modal = $("permModal");
  $("permCmd").textContent = "$ " + step.cmdline;
  modal.hidden = false;
  const finish = (allowed) => {
    modal.hidden = true;
    const c = el("div", "card cmd-ok");
    const row = el("div", "row1");
    row.append(el("span", null, allowed ? "🛠" : "⛔"), el("span", "fname", step.cmdline));
    c.appendChild(row);
    c.appendChild(el("div", "sub", allowed ? "已执行" : "已拒绝执行"));
    stream.appendChild(c);
    scrollStream();
    onDone(allowed);
  };
  $("permAllow").onclick = () => finish(true);
  $("permDeny").onclick = () => finish(false);
}

function addCmdOkCard(sub) {
  const c = el("div", "card cmd-ok");
  const row = el("div", "row1");
  row.append(el("span", null, "✅"), el("span", "fname", "构建通过"));
  c.appendChild(row);
  c.appendChild(el("div", "sub", sub));
  stream.appendChild(c);
  scrollStream();
}

function scrollStream() { stream.scrollTop = stream.scrollHeight; }

/* ---------------- 播放器 ---------------- */
let idx = 0;
let playing = false;
let timer = null;

function runStep(step) {
  switch (step.t) {
    case "banner": setBanner(step.on, step.text); return true;
    case "reasoning": addReasoning(step.text); return true;
    case "read": addReadCard(step.file, step.sub); return true;
    case "grep": addGrepCard(step.pattern, step.sub); return true;
    case "answer": addAnswer(step.text); return true;
    case "note": addNote(step.text); return true;
    case "cmdok": addCmdOkCard(step.sub); return true;
    case "propose":
      addProposeCard(step, () => setTimeout(next, 500));
      return false; // 暂停等待用户决定
    case "cmd":
      addCommandWithPermission(step, () => setTimeout(next, 500));
      return false; // 暂停等待权限确认
    default: return true;
  }
}

function next() {
  if (idx >= SCRIPT.length) { playing = false; $("btnPlay").textContent = "▶ 播放演示"; return; }
  const step = SCRIPT[idx++];
  const goOn = runStep(step);
  if (goOn) timer = setTimeout(next, 750);
}

function play() {
  if (playing) return;
  playing = true;
  $("btnPlay").textContent = "⏳ 播放中…";
  next();
}

/* ---------------- 初始化与全局交互 ---------------- */
$("btnPlay").onclick = play;
$("btnSend").onclick = send;
$("composerInput").addEventListener("keydown", (e) => { if (e.key === "Enter") send(); });

function send() {
  const input = $("composerInput");
  const text = input.value.trim();
  if (!text) return;
  input.value = "";
  addUser(text);
  if (!playing) { playing = true; setTimeout(next, 300); }
}

/* 决策①:折叠/展开 + 拖拽调高(正式版记忆到 Preferences.Layout) */
$("btnCollapse").onclick = () => {
  const dock = $("chatDock");
  dock.classList.toggle("collapsed");
  $("btnCollapse").textContent = dock.classList.contains("collapsed") ? "▴ 展开" : "▾ 折叠";
};

(function initResizer() {
  const resizer = $("dockResizer");
  const dock = $("chatDock");
  let startY = 0, startH = 0;
  resizer.addEventListener("mousedown", (e) => {
    startY = e.clientY; startH = dock.getBoundingClientRect().height;
    const move = (ev) => { dock.style.height = Math.min(Math.max(startH + (startY - ev.clientY), 120), window.innerHeight * 0.7) + "px"; };
    const up = () => { document.removeEventListener("mousemove", move); document.removeEventListener("mouseup", up); };
    document.addEventListener("mousemove", move);
    document.addEventListener("mouseup", up);
    e.preventDefault();
  });
})();

/* 决策⑤:AI 开关(演示页默认开;关闭 = 界面回到"上游无 AI"状态) */
$("aiToggle").addEventListener("change", (e) => {
  const dock = $("chatDock");
  const resizer = $("dockResizer");
  const on = e.target.checked;
  dock.style.display = on ? "" : "none";
  resizer.style.display = on ? "" : "none";
});

renderUnstaged();
showDiff("README.md");
setBanner(false);
