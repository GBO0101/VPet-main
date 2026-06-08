# VPet AI Pet Edition — Agent Guide

## Project structure

- Source code lives in `VPet-main-main/`. Root also has loose files (`.env`, `ask.py`), archive zip/rar, and this file.
- The project extends the upstream [VPet-Simulator](https://github.com/LorisYounger/VPet) with a local-first AI agent (.NET 8 WPF, win-x64 only).

## Key directories

| Path | Purpose |
|------|---------|
| `VPet-main-main/` | Solution root; open `VPet.sln` |
| `VPet-Simulator.Windows/AiAgent/` | AI agent: talk box, chat pipeline, tool executor, model clients, skills |
| `VPet-Simulator.Windows/AiAgent/Chat/` | `AiChatSkills.cs` (pipeline + skill interfaces/impls), `AiChatModels.cs`, `AiStructuredMemory.cs` |
| `VPet-Simulator.Windows/WinDesign/` | UI windows: `winGameSetting*.cs` (settings), `winWorkflowEditor*`, `winActionEditor*` |
| `VPet-Simulator.Windows.Tests/` | xUnit tests (fake/stub impls, no real API) |
| `VPet-Simulator.Core/` | Upstream core simulator logic |

## Build & run

```powershell
# Build (x64 required — no x86 support):
dotnet build .\VPet-Simulator.Windows\VPet-Simulator.Windows.csproj -p:Platform=x64

# Run (builds + launches):
.\Start-VPet.cmd

# First run: run mklink.bat as admin to link mod files.
```

Build output: `bin\x64\Debug\net8.0-windows\win-x64\VPet-Simulator.Windows.exe`

## Test

```powershell
# Fast (excludes Calendar):
dotnet test .\VPet-Simulator.Windows.Tests\VPet-Simulator.Windows.Tests.csproj --no-restore -p:Platform=x64 -p:UseAppHost=false -p:OutDir=test-out\ --filter "Category!=Calendar"

# All:
dotnet test .\VPet-Simulator.Windows.Tests\VPet-Simulator.Windows.Tests.csproj --no-restore -p:Platform=x64
```

- xUnit v2, no mocking lib — inline `Fake*` / `InMemory*` stubs.
- `AiConversationContext.ForTest(userInput)` creates test context quickly.
- Calendar tests tagged `[Category("Calendar")]` for exclusion.
- All tests self-contained; no external API or Ollama required.

## Key architecture

### Chat pipeline (sequential skill chain)

`ChatPipeline.RunAsync` (`AiChatSkills.cs`):
```
user → context_builder → short_term_memory.attach → emotion_skill
→ intent_reasoning_skill → memory_skill.retrieve → tool_skill.plan
→ tool_skill.execute → personality_skill → style_skill
→ response_reasoning_skill → final_response
→ short_term_memory.update → memory_skill.update → proactive_skill.update_state
```

Key interfaces (all `internal` in `AiChatSkills.cs`):
`IConversationContextBuilder`, `IEmotionSkill`, `IIntentReasoningSkill`, `IMemorySkill`, `IToolSkill`, `IPersonalitySkill`, `IStyleSkill`, `IResponseReasoningSkill`, `IProactiveSkill`, `IAiReplyClient`, `IAiAgentToolExecutor`

### Concurrency model (SemaphoreSlim)

Three execution paths share a single `SemaphoreSlim(1,1)` (`_busySemaphore` in `AiAgentTalkBox`):

| Path | Location | Acquire | Release |
|------|----------|---------|---------|
| LLM response (`Responded`) | `AiAgentTalkBox.cs` | `Wait()` at start | `finally` |
| Screen awareness analysis | `StartScreenAwareness()` | `WaitAsync()` before capture→analyze→proactive | inner `finally` |
| Workflow execution | `WorkflowEngine.ExecuteAsync()` | `Wait(0)` non-blocking try | `finally` if acquired |

`Wait(0)` returns `false` when already held by caller (Responded or screen awareness), so nested workflow calls skip re-acquisition. Prevents GPU contention between Ollama text & vision models.

### Voice (ASR / TTS)

Requires sherpa-onnx models at `C:\model\`:
- ASR: `sherpa-onnx-paraformer-zh-2023-09-14`
- TTS: `vits-zh-hf-fanchen-C` (Speaker ID = 20)
- Audio output: `DirectSoundOut` (not `WaveOutEvent`; fixes `BadDeviceId`)
- TTS `RuleFars` = `rule.far`; `RuleFsts` = `phone.fst,date.fst,number.fst` (fixes init crash)

Feature split: `IsVoiceInputEnabled` / `IsVoiceOutputEnabled` (replaced old single `IsVoiceEnabled`). Change takes effect immediately (writes to both User + Process scopes).

### Hotkey (Push-to-Talk)

`HotkeyService.cs`: wraps Win32 `RegisterHotKey`. Parses `VPET_HOTKEY_PUSH_TO_TALK` (e.g. `Ctrl+Shift+V`).
- Detection: settings tab uses WPF `PreviewKeyDown` → maps `Key.A..Z` (44-69) to `HotkeyService.Keys.A..Z` (65-90).
- Behavior: long-press to record, `GetAsyncKeyState` polling (50ms), release stops → ASR triggers.
- Modifiers only: Ctrl, Alt, Shift, Win. Key: A-Z or F1-F12.

### Workflow automation

Trigger types (merged from old Voice/Text → single `Input`):
- `Screen` — screen description contains keyword
- `Schedule` — Cron expression
- `Input` — user input contains keyword (case-insensitive `Contains`)

Action types:
- `LaunchProgram` — if param contains `\` or `/`, ShellExecute directly; otherwise OpenFileDialog
- `StartPomodoro` — start timer
- `SendMessage` — pet speaks
- `Wait` — delay (seconds)
- `ShowNotification` — Windows balloon tip

Storage: `%APPDATA%\VPet\AiAgentWorkflows.json` (JSON, `WorkflowStore.cs`).

**Important**: After adding/editing/deleting workflows via settings UI, call `talkBox.ReloadWorkflows()` to sync the running engine. Use `mw.TalkAPI.Find(x => x.APIName == "AI Agent") as AiAgentTalkBox` to find the instance (NOT `mw.TalkBox` — that's the `TalkSelect` UI element).

### Screen awareness

- `ScreenCaptureService.cs`: cursor-centered 1000×800 → base64 JPEG
- `ScreenAnalysisService.cs`: Ollama vision API (`VPET_VISION_MODEL`, default `llava:7b`)
- Interval: env var `VPET_SCREEN_AWARE_INTERVAL`, adjustable 5-300s via settings slider
- Skips: same description as last, <5 min since last proactive, `isInConversation == true`

### Feature toggles

`FeatureManager.cs` (static) reads `VPET_FEATURE_*` env vars. All default `true`.
- `SetUser()` writes both `EnvironmentVariableTarget.User` (persist) + `Process` (immediate).
- Env writes run on `Task.Run` background thread to avoid `WM_SETTINGCHANGE` broadcast blocking UI.

### Memory system

`AiStructuredMemory`: Profile, Preferences, Projects, ConversationNotes, ProactiveState.
Storage: `InMemoryStructuredMemoryStore` (tests) / `AiAgentMemoryStore` (runtime JSON).

## Environment variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `VPET_AI_PROVIDER` | `ollama` | `ollama` or `remote` |
| `VPET_OLLAMA_MODEL` | — | Local LLM model name |
| `VPET_OLLAMA_URL` | — | Custom Ollama URL |
| `VPET_VISION_MODEL` | `llava:7b` | Multimodal model for screen awareness |
| `VPET_CWA_API_KEY` | — | Taiwan CWA weather/seismic API |
| `VPET_DEFAULT_LOCATION` | — | Default city (e.g. `臺中市`) |
| `VPET_HOTKEY_PUSH_TO_TALK` | — | Hotkey string (e.g. `Ctrl+Shift+V`) |
| `VPET_SCREEN_AWARE_INTERVAL` | `60` | Screen analysis interval (seconds) |
| `VPET_FEATURE_VOICE_INPUT` | `true` | Voice input (ASR) toggle |
| `VPET_FEATURE_VOICE_OUTPUT` | `true` | Voice output (TTS) toggle |
| `VPET_FEATURE_SCREEN_AWARE` | `true` | Screen awareness toggle |
| `VPET_FEATURE_WORKFLOW` | `true` | Workflow automation toggle |
| `VPET_REMOTE_API_BASE_URL` | — | OpenAI-compatible base URL |
| `VPET_REMOTE_API_KEY` / `_MODEL` | — | Remote API credentials |
| `VPET_GOOGLE_CLIENT_ID`/`_SECRET`/`_REFRESH_TOKEN` | — | Google Calendar OAuth |

Env lookup: `Environment.GetEnvironmentVariable()` (process) → `...(User)` → `""`.

## Debug logs

All in exe directory:
- `voice_debug.log` — ASR/TTS
- `screen_aware.log` — Screen capture & analysis
- `workflow_debug.log` — Workflow engine
- `hotkey_debug.log` — Hotkey registration

## Known pre-existing issues

- Steamworks NuGet errors (CS0246, missing `Facepunch.Steamworks`) — 80+ occurrences, do not affect our code.
- `qwen2.5vl:7b` pull fails with `file does not exist` — stay on `llava:7b`.

## Conventions

- **No comments in code** unless asked.
- `Platform=x64` mandatory for build & test.
- PascalCase for types/methods/properties; interfaces prefixed `I`; file-scoped namespaces.
- `.editorconfig` disables CS1591, CS1573, CS1570, CS8632, CA1416, CA1707.
- Do not commit `.env`, `credentials.json`, `token.json`, or API keys.
- New test stubs: copy pattern from existing `Fake*` / `InMemory*` classes.
