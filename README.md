# VPet AI Pet Edition

這是一個基於開源 `VPet-Simulator` 改造的桌寵模擬器版本，重點不是單純把 LLM 接進聊天框，而是把桌寵行為、工具能力、記憶與對話流程整合成一套可擴充的本地 AI Agent 架構。

目前版本預設以本地 `Ollama` 為主要模型提供者，遠端模型只保留 OpenAI-compatible API 介面，方便之後替換不同供應商或模型。

## 專案定位

- 桌面寵物模擬器
- LLM 聊天助手
- 本地優先的 AI Agent
- 可接 UI / Skill / Tool 的桌寵改造版

這不是原版 VPet 的純鏡像，而是針對「桌寵 + 本地 AI 助手」方向持續改造的版本。

## 來源與致謝

本專案是基於開源專案 `VPet-Simulator` 進行改造。

- 原始專案：[`LorisYounger/VPet`](https://github.com/LorisYounger/VPet)
- 本專案保留上游授權，並在此基礎上新增 AI Agent、聊天管線、工具整合與設定頁能力

如果你要分發、商用或再改造，請先確認原始授權與上游資源使用限制。

## 目前功能

### 1. 桌寵聊天與人格

- 支援繁體中文聊天
- 桌寵人格已抽離為獨立 `PersonalitySkill`
- 回覆風格會依情緒、意圖、模式調整
- 避免每句都硬裝可愛，改成偏自然、口語、朋友感

### 2. 顯式 ChatPipeline

聊天流程已重構為固定 Skill 鏈：

```text
user_input
-> conversation_context_builder
-> short_term_memory.attach
-> emotion_skill
-> intent_reasoning_skill
-> memory_skill.retrieve
-> tool_skill.plan
-> tool_skill.execute
-> personality_skill
-> style_skill
-> response_reasoning_skill
-> final_response
-> short_term_memory.update
-> memory_skill.update
-> proactive_skill.update_state
```

第一版重點：

- `AiAgentTalkBox` 變薄，只負責接收輸入、呼叫 pipeline、顯示結果
- 各 Skill 有明確介面，方便後續替換或擴充
- 原本散在 `TalkBox`、provider client 裡的邏輯已拆出

### 3. 記憶系統

- 結構化長期記憶
- 短期對話記憶
- 最近 10 句 history 會進入聊天上下文
- 可保存使用者偏好、專案、對話筆記、主動互動狀態

目前實際使用的記憶欄位：

- `Profile`
- `Preferences`
- `Projects`
- `ConversationNotes`
- `ProactiveState`

已保留但暫未深入行為化的欄位：

- `RelationshipState`
- `EmotionHistory`
- `InteractionStats`

### 4. 意圖推理與回覆策略

- `IntentReasoningSkill` 會推理主要意圖、次要意圖、隱含需求、是否需要工具
- `ResponseReasoningSkill` 會決定先共感、先解題、是否追問、是否整理工具結果
- 抱怨、卡程式、閒聊、查行程、下指令，走的回覆策略不同

### 5. 工具整合

既有工具能力保留，並包成 `ToolSkill`：

- 天氣查詢
- 地震快訊 / 地震查詢
- Google Calendar 列出 / 新增 / 搜尋 / 刪除 / 摘要
- 本機提醒
- 開啟白名單程式
- 搜尋檔案
- 番茄鐘
- 桌寵動作控制

### 6. 天氣與地震

- 天氣改接中央氣象署開放資料 API
- 支援依目前位置查詢明日天氣
- 若未明確指定地點，會優先使用 `VPET_DEFAULT_LOCATION`
- 若未設定預設位置，會退回以 IP 估算目前城市
- 支援地震資料查詢
- 支援背景地震監看、桌寵緊張動作與 Windows 通知

### 7. 模型與 Provider 設定

- 預設 provider：`Ollama`
- Ollama 模型清單直接從本機 `/api/tags` 取得
- 不再提供寫死的推薦模型清單
- 保留 remote OpenAI-compatible API 介面
- 可由 API base URL 讀取 `/v1/models`

### 8. 本地優先

- 預設走本地模型
- 遠端 API 只保留介面，不綁定單一服務商
- 適合想把桌寵當成個人桌面助手，而不是單純雲端聊天視窗的人

## 主要改造重點

相較於原版 VPet，這個版本新增或明顯調整了以下方向：

- 將聊天流程從 UI 元件內抽離成 `ChatPipeline`
- 將人格 prompt 從模型 client 中抽離
- 將記憶升級為結構化 schema
- 新增短期記憶 history
- 新增意圖推理與回覆策略層
- 工具從隱式呼叫改成顯式 plan / execute
- 將 Ollama 模型選擇改成讀本機安裝清單
- 將天氣來源改接中央氣象署
- 新增地震快訊、桌寵警示動作與系統通知

## 專案結構

核心 AI 相關程式主要集中在：

- `VPet-Simulator.Windows/AiAgent`
- `VPet-Simulator.Windows/AiAgent/Chat`
- `VPet-Simulator.Windows.Tests`
- `docs/ai-agent-major-changes.md`

大致分工如下：

- `AiAgentTalkBox.cs`：聊天 UI 入口
- `Chat/AiChatSkills.cs`：ChatPipeline 與各 Skill 實作
- `Chat/AiChatModels.cs`：pipeline 使用的資料模型
- `AiAgentSkillExecutor.cs`：工具執行入口
- `AiAgentMemoryStore.cs`：結構化記憶存取
- `OllamaAgentClient.cs`：本地模型生成
- `OpenAiAgentClient.cs`：OpenAI-compatible remote API 生成
- `WeatherSkillClient.cs`：天氣查詢
- `EarthquakeSkillClient.cs`：地震查詢
- `LocationSkillClient.cs`：位置解析

## 執行需求

- Windows
- .NET SDK
- `Ollama`（建議，作為本地模型 provider）

如果要使用完整外部能力，還需要自行準備：

- Google Calendar OAuth 憑證
- 中央氣象署 API Key

## 重要設定

常用環境變數：

```text
VPET_CWA_API_KEY
VPET_DEFAULT_LOCATION
VPET_REMOTE_API_BASE_URL
VPET_REMOTE_API_KEY
VPET_REMOTE_API_MODEL
```

說明：

- `VPET_CWA_API_KEY`：中央氣象署天氣 / 地震 API
- `VPET_DEFAULT_LOCATION`：固定預設地點，例如 `臺中市`
- `VPET_REMOTE_API_BASE_URL`：遠端 OpenAI-compatible API 位址
- `VPET_REMOTE_API_KEY`：遠端 API 金鑰
- `VPET_REMOTE_API_MODEL`：遠端模型 id

## 建置與啟動

在 Visual Studio 或命令列開啟 `VPet.sln` 後建置 `x64` 即可。

常用命令：

```powershell
dotnet test .\VPet-Simulator.Windows.Tests\VPet-Simulator.Windows.Tests.csproj --no-restore -p:Platform=x64 -p:UseAppHost=false -p:OutDir=test-out\ --filter "Category!=Calendar"
dotnet build .\VPet-Simulator.Windows\VPet-Simulator.Windows.csproj --no-restore -p:Platform=x64
```

若要直接啟動桌寵，可使用：

```powershell
.\Start-VPet.cmd
```

## 測試覆蓋

目前已補上的測試重點包含：

- ChatPipeline 順序與工具呼叫
- IntentReasoningSkill
- ResponseReasoningSkill
- 記憶更新與相容
- Ollama / Remote model catalog 解析
- 番茄鐘
- 指令路由
- 程式捷徑
- 地震與天氣解析

`Google Calendar` 相關仍偏整合測試，不是每次快速測試都會跑。

## 已知限制

- 意圖推理第一版仍以規則式為主，不是完整語意代理
- Google Calendar 若資訊不足，仍需要補時間或目標
- 位置推定預設不是 GPS，而是 `VPET_DEFAULT_LOCATION` 或 IP 地理位置
- Remote API 目前只支援 OpenAI-compatible 格式

## 文件

- 重大改動整理：[docs/ai-agent-major-changes.md](docs/ai-agent-major-changes.md)

## 授權

本專案沿用原始開源專案的授權基礎，新增部分也在相同脈絡下維護。實際使用前請先閱讀：

- [LICENSE](LICENSE)
- 原始專案授權與資源說明

