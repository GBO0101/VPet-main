# AI Agent / 桌寵聊天系統重大變更紀錄

本文整理從本專案導入本地 AI 桌寵 / Agent 功能到目前版本的主要結構變更、改善方法與仍存在的問題點。範圍以 `VPet-Simulator.Windows/AiAgent`、設定頁、測試專案與桌寵互動流程為主。

## 目前架構概覽

目前 AI Agent 已從「TalkBox 直接處理所有聊天與工具邏輯」改成顯式 Skill 鏈：

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

`AiAgentTalkBox` 現在主要負責接收輸入、建立 pipeline、顯示回覆；AI 的人格、語氣、工具規劃、記憶、回覆策略都集中在 `AiAgent/Chat` 模組中。

## 重大改動

| 類別 | 改了哪裡 | 改善方法 | 解決的問題 | 目前問題點 |
| --- | --- | --- | --- | --- |
| ChatPipeline 架構 | `VPet-Simulator.Windows/AiAgent/Chat/AiChatSkills.cs`、`AiChatModels.cs` | 新增 `ChatPipeline` 與各 Skill 介面，讓 emotion、intent、memory、tool、personality、style、response reasoning 分段執行 | 原本聊天流程集中在 `AiAgentTalkBox`，難以替換、測試與擴充 | 第一版 intent/tool planning 仍偏規則式，複雜語意還需要更強的推理層 |
| TalkBox 變薄 | `VPet-Simulator.Windows/AiAgent/AiAgentTalkBox.cs` | 改成呼叫 `ChatPipeline.RunAsync()`，保留 command router 快速指令入口 | TalkBox 不再同時負責聊天、工具、記憶、prompt 組裝 | 目前每輪仍會建立 pipeline，但短期記憶已用 TalkBox 欄位保存 |
| 短期記憶 | `AiChatModels.cs`、`AiChatSkills.cs`、`AiAgentTalkBox.cs` | 新增 `ShortTermMemorySkill`，用固定 10 筆 history list 保存最近對話，只存在記憶體，不寫入長期記憶 | 使用者說「我下禮拜三要去聚餐」後，再說「幫我加上這個行程」時，桌寵能用上一輪內容理解「這個」 | 程式重開後短期記憶會清空；缺少時間時目前會追問，不會擅自新增行事曆 |
| 結構化長期記憶 | `VPet-Simulator.Windows/AiAgent/AiAgentMemoryStore.cs`、`AiStructuredMemory.cs` | 將舊字串記憶升級為 `Profile`、`Preferences`、`Projects`、`ConversationNotes`、`ProactiveState` 等 schema | 舊版只存字串清單，無法區分偏好、專案、備註與狀態 | `RelationshipState`、`EmotionHistory`、`InteractionStats` 目前多為保留欄位，行為邏輯還很輕 |
| 記憶取用降噪 | `MemorySkill.Retrieve`、prompt 組裝 | 只在專案、寫程式、明確相關情境取出長期記憶；短期 history 每次生成前提供給模型，但提示模型只在有關時使用、不主動複述 | 解決每次聊天都把長期記憶拿出來講、回覆變煩的問題，同時避免短接續句忘記上一輪 | 相關性判斷仍是簡單 token / intent 規則，未來可改成 embedding 或本地 rerank |
| 人格 prompt 抽離 | `PersonalitySkill`、`OllamaAgentClient.cs`、`OpenAiAgentClient.cs` | 把 persona 從 provider client 抽到 pipeline skill，client 只負責生成 | 避免 Ollama/OpenAI-compatible client 各自藏一份人格設定，難以統一調整 | 部分舊中文字串在終端顯示會亂碼，後續應統一檢查檔案編碼與 prompt 可讀性 |
| 回覆風格控制 | `StyleSkill`、`ResponseReasoningSkill` | 依 emotion / intent 選 `normal`、`cute`、`teacher`、`coder`、`comfort`、`tsukkomi`，再由 response plan 決定策略 | 桌寵不再只像客服回答；抱怨時先共感，卡程式時先定位問題方向 | 目前最終自然度仍受本地模型能力影響，需要靠 prompt 與模型選擇一起調整 |
| 第三人稱與英文混入修正 | `PersonalitySkill`、`BuildGenerationRequest`、`SanitizePetStatus` | 要求第一人稱自稱、繁中回覆、不主動混英文；桌寵狀態改成中文標籤後再放進 prompt | 解決回覆把自己說成第三人稱、突然插英文、暴露 internal status key 的問題 | 若模型本身偏英文或小模型遵循度低，仍可能偶發，需要模型與 prompt 一起修 |
| ToolSkill 抽象 | `ToolSkill`、`AiAgentSkillExecutor.cs` | `ToolSkill` 負責 plan/execute，既有 `AiAgentSkillExecutor` 保留實際工具 switch | 保留天氣、提醒、Calendar、開程式、搜尋檔案、番茄鐘、桌寵動作，不重寫既有工具 | calendar add 的語意抽取目前只做第一版規則，複雜日期/時間仍需補強 |
| Google Calendar 能力保留 | `GoogleCalendarClient.cs`、`AiAgentSkillExecutor.cs`、`ToolSkill` | 保留列出、新增、搜尋、刪除、摘要等既有功能，由 ToolSkill 呼叫既有 executor | 重構聊天流程時不破壞 Calendar 外部整合 | Calendar integration 需要 OAuth/API，沒有納入快速單元測試 |
| Ollama 預設與模型切換 | `OllamaAgentClient.cs`、`AiModelCatalogService.cs`、`winGameSetting.AiAgent.cs`、`winGameSetting.xaml` | Ollama 預設本地 provider；模型清單改由本地 Ollama `/api/tags` 取得，設定頁不再列推薦模型 | 解決換模型時無法正確切換，以及推薦清單與本機已安裝模型不一致 | 若 Ollama 未啟動或 `/api/tags` 失敗，仍需要 UI 顯示更清楚的錯誤 |
| Remote API 抽象 | `OpenAiAgentClient.cs`、`AiAgentEnvironment.cs`、設定頁 | 保留 OpenAI-compatible API 介面，使用 `VPET_REMOTE_API_BASE_URL`、`VPET_REMOTE_API_KEY`、`VPET_REMOTE_API_MODEL`，模型清單走 `/v1/models` | 不預設公開 API 廠商，也不綁定固定遠端模型 | 各家 OpenAI-compatible API 對 `/v1/responses`、`/v1/chat/completions` 支援不同，仍需依供應商測試 |
| Model Catalog | `AiModelCatalogService.cs`、`AiAgentModelCatalogTests.cs` | 獨立解析 Ollama `/api/tags` 與 remote `/v1/models` JSON | 模型列舉邏輯可測，設定頁可直接刷新可用模型 | 不支援列模型時只能退回手動輸入 remote model id |
| Pomodoro / 提醒 / 指令 | `PomodoroService.cs`、`PomodoroSession.cs`、`AiAgentCommandRouter.cs`、`AiAgentSettingCommandParser.cs` | 保留快速命令與桌寵本地行為，不全部交給 LLM | DIY link、設定指令、番茄鐘、桌寵動作等可低延遲執行 | command router 與 ChatPipeline 並存，未來可再整理成一致入口 |
| 開啟程式與檔案搜尋 | `ProgramShortcutStore.cs`、`FileSearchSkillClient.cs`、`AiAgentSkillExecutor.cs` | 維持白名單程式開啟與檔案搜尋功能，由工具層呼叫 | 避免 LLM 直接執行不安全路徑或任意程式 | 白名單與搜尋範圍仍需要使用者設定與維護 |
| 設定頁重構 | `winGameSetting.xaml`、`winGameSetting.AiAgent.cs`、`winGameSetting.xaml.cs`、`Setting.cs`、`ISetting.cs` | Provider/model/API 設定集中到 AI Agent 設定頁，加入刷新模型清單行為 | 使用者可在 UI 設定 Ollama/remote API，不必改環境變數 | 目前 UI 還偏工程設定頁，錯誤提示與操作流程可再改善 |
| 測試覆蓋 | `VPet-Simulator.Windows.Tests/*` | 新增 intent、response plan、memory、model catalog、pipeline、command router、pomodoro、shortcut 等單元測試 | 重構後能驗證主要行為，不只靠手動測桌寵 | Calendar integration 仍是外部整合測試；快速測試排除 `Category=Calendar` |

## 本版新增的短期記憶行為

範例流程：

```text
使用者：我下禮拜三要去聚餐
桌寵：可以自然回應，不把這件事寫入長期記憶

使用者：幫我在行事曆上加上這個行程
pipeline：
  - short_term_memory.attach 取出最近 10 筆 history，其中包含上一輪「我下禮拜三要去聚餐」
  - intent_reasoning_skill 判斷 calendar + calendar_add_event
  - tool_skill.plan 找到 title=聚餐、date=下禮拜三
  - 因缺少具體時間，不執行 Google Calendar API
  - response_reasoning_skill 要求回覆先確認「下禮拜三聚餐」，只追問時間
```

這樣避免兩種壞結果：

- 不會忘記「這個行程」指的是什麼。
- 不會在缺少時間時擅自新增一個可能錯誤的 Calendar 事件。

## 保留的舊功能

以下功能在 ChatPipeline 重構後仍應保留：

- DIY link、設定指令與快速命令。
- 番茄鐘設定、開始、停止與狀態。
- 桌寵睡覺、起床、停止活動、摸頭、摸身體、工作、學習、玩、食物與飲料頁。
- 天氣查詢。
- 本機提醒建立與列表。
- Google Calendar 列表、新增、搜尋、刪除與摘要。
- 長期記憶回想與記住使用者事實。
- 開啟白名單程式。
- 搜尋檔案。
- Ollama 自動啟動與模型預載。
- Provider / model 設定頁基本流程。

## 已知問題與後續建議

| 問題 | 影響 | 建議 |
| --- | --- | --- |
| Intent / tool planning 第一版仍偏規則式 | 複雜語意、複合指令、模糊日期可能判斷不準 | 保留規則作快速路徑，再加入本地 LLM structured reasoning 或可測的 parser |
| Calendar add 缺少 all-day 支援 | 使用者只說日期、不說時間時不能直接新增 | Google Calendar client 可新增 all-day event 支援，或 UI/回覆固定追問時間 |
| 部分 prompt/測試字串在終端顯示亂碼 | 維護與 code review 可讀性下降 | 統一檢查檔案編碼，將核心 prompt 改成乾淨 UTF-8 或 resource 檔 |
| 短期記憶只在 TalkBox 實例內 | App 重開或 TalkBox 重建後會消失 | 第一版合理；若要跨重啟，可加入短 TTL 的 session store |
| 長期記憶相關性仍簡單 | 可能漏拿真正相關記憶，或偶爾拿到弱相關內容 | 後續可加 embedding、關鍵詞權重或使用者明確 pin 的記憶 |
| 遠端 API 相容性不一 | 不同供應商 endpoint 行為不同 | 設定頁顯示 endpoint 檢查結果，保留手動 model id |
| 快速單測不跑 Calendar integration | 無法每次驗證 OAuth/API 實際成功 | 保留 `GoogleCalendarIntegrationTests` 作手動或 CI secret-enabled 測試 |

## 目前測試重點

快速單元測試主要覆蓋：

- 抱怨情境：煩躁 + coding help + emotional support。
- 查行程情境：calendar intent + tool plan + final prompt。
- 長期專案：更新 `Projects` 或 `ConversationNotes`。
- 閒聊：不呼叫工具，不變客服式回覆。
- 記憶相容：舊字串清單轉入新版結構。
- ToolSkill：使用 fake executor 驗證仍走既有工具入口。
- Model catalog：Ollama 與 remote model list JSON 解析。
- ChatPipeline：完整順序與 final response generation input。
- 短期記憶：固定 10 筆 history 會在每次生成前提供給模型，下一輪「這個行程」能引用上一輪「下禮拜三聚餐」。

