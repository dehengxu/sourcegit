---
name: prototype-ui-verify
description: Verify and iterate HTML/H5 UI prototypes by actually rendering them — serve locally, open in the in-app browser, drive the scripted interactions, and self-verify via screenshots. Use whenever building or editing an HTML prototype (e.g. docs/prototype/**), whenever the user asks to check/review/截图 a prototype's look or behavior, or reports a prototype looks broken — even if they only mention "页面"/"原型"/"prototype" in passing. Never declare an H5 prototype "done" from reading the code alone.
---

# Prototype UI Verification (H5)

Core principle: the model **cannot see** native GUI rendering (Avalonia/WPF), but **can see browser pages**. An HTML prototype is verifiable end-to-end without a human: render it, interact with it, screenshot it, judge the pixels, fix, repeat. Use this loop instead of claiming "should work".

## Workflow

### 1. Serve the prototype over HTTP (IAB cannot open `file://`)

```bash
python3 -m http.server 8765 --directory <prototype-dir>
```

Run with `run_in_background: true`. Pick a port that isn't obviously in use (8765 is the convention for `docs/prototype/ai-s0`). The URL is `http://localhost:<port>/`.

### 2. Open and inspect structure

Use the `browser-use:control-browser` skill (bootstrap + IAB). Then:

```js
const tab = await browser.tabs.new();
await tab.goto("http://localhost:8765/");
await tab.playwright.waitForLoadState({ state: "domcontentloaded" });
const snap = await tab.playwright.domSnapshot(); // structure/presence check, no pixels
```

Check the DOM snapshot first: are the expected regions, buttons, and list items present? Are modals that should be hidden absent from the tree?

### 3. Visual self-verification (the step that catches real bugs)

Screenshots are for judging layout, colors, overlap — things the DOM tree cannot tell you. Load the screenshot guidance once, then emit:

```js
nodeRepl.write(await agent.documentation.get("screenshots"));
nodeRepl.emitImage(await tab.screenshot());
```

Judge against the intended design (e.g. the five interaction decisions for the ai-s0 prototype: bottom dock, three-level messages, propose card with stats + buttons, colored diff, banner slot, permission modal hidden until triggered). If the image needs close reading, `Read` the artifact PNG path returned by the tool.

### 4. Drive interactions, screenshot at gates

Build locators only from snapshot facts; one state-changing action per observation cycle. For scripted/mock prototypes, wait for timers before shooting (mock scripts typically advance ~750 ms/step):

```js
await tab.playwright.getByRole("button", { name: "▶ 播放演示" }).click();
await tab.playwright.waitForTimeout(4200);            // play up to the user-gate card
nodeRepl.emitImage(await tab.screenshot());
```

Exercise every interactive path the design claims: play button, Approve/Reject on propose cards, permission-modal allow/deny, collapse/resize, toggle switches. After each action, verify the *effect* (file list gains a badge, card shows verdict, banner clears), not just absence of errors.

### 5. Fix loop

Found an issue → edit the source file → `tab.reload()` → re-verify the specific fix (cheap targeted check first, full screenshot only when pixels matter again). Commit fixes with the plan-binding trailers when working inside a bound plan (see `docs/plan/ai-phase0.md` §3).

## Gotchas (real bugs this loop has caught)

- **`[hidden]` beaten by CSS**: a class rule like `.modal-mask { display:flex }` overrides the browser's `[hidden] { display:none }`, so the modal renders on first paint. Fix with an explicit `.modal-mask[hidden] { display:none }`. Always check "should-be-hidden" elements in the snapshot/screenshot.
- `nodeRepl.emitImage(...)` must be in the **same JS cell** as `tab.screenshot()`; never return raw bytes.
- Auto-scrolling chat streams: screenshot mid-animation can show half-clipped cards — wait for the script to settle first.
- `tab.goto()` rejects `file://`; always serve via HTTP.
- The localhost tab doubles as the user's review surface — leave it open and hand them the URL (`http://localhost:<port>/`) when asking for approval; don't close it at turn end.

## Boundaries

- This skill is read-render-fix for prototypes. Committing, tagging milestone tags (`ai-s0/m0`…), and Notion mirror updates follow the plan-binding workflow in `docs/plan/ai-phase0.md`, not this skill.
- Applies to any HTML prototype under `docs/prototype/**` (or elsewhere), not only ai-s0.
