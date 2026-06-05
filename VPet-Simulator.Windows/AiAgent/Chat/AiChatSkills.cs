using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.AiAgent.Chat;

internal interface IConversationContextBuilder
{
    Task<AiConversationContext> BuildAsync(string userInput, CancellationToken cancellationToken);
}

internal interface IEmotionSkill
{
    AiEmotionResult Analyze(AiConversationContext context);
}

internal interface IIntentReasoningSkill
{
    AiIntentResult Reason(AiConversationContext context, AiEmotionResult emotion);
}

internal interface IStructuredMemoryStore
{
    AiStructuredMemory Load();
    void Save(AiStructuredMemory memory);
}

internal interface IShortTermMemorySkill
{
    AiConversationContext Attach(AiConversationContext context);
    void Update(AiConversationContext context, AiIntentResult intent, string finalResponse);
}

internal interface IMemorySkill
{
    AiStructuredMemory Load();
    IReadOnlyList<AiMemoryItem> Retrieve(AiConversationContext context, AiIntentResult intent);
    AiMemoryUpdateResult Update(AiConversationContext context, AiIntentResult intent, string finalResponse);
}

internal interface IAiAgentToolExecutor
{
    Task<string> ExecuteAsync(OllamaSkillCall skillCall, string userText, CancellationToken cancellationToken);
}

internal interface IToolSkill
{
    AiToolPlan Plan(AiConversationContext context, AiIntentResult intent);
    Task<AiToolExecutionResult> ExecuteAsync(AiToolPlan plan, AiConversationContext context, CancellationToken cancellationToken);
}

internal interface IPersonalitySkill
{
    AiPersonalityProfile GetProfile(AiConversationContext context);
    string BuildSystemPrompt(AiPersonalityProfile profile);
}

internal interface IStyleSkill
{
    AiStyleResult Select(AiConversationContext context, AiEmotionResult emotion, AiIntentResult intent, AiStructuredMemory memory);
}

internal interface IResponseReasoningSkill
{
    AiResponsePlan Plan(AiConversationContext context, AiEmotionResult emotion, AiIntentResult intent, IReadOnlyList<AiMemoryItem> memories, AiToolExecutionResult toolResult, AiStyleResult style);
}

internal interface IProactiveSkill
{
    void UpdateState(AiConversationContext context, AiEmotionResult emotion, AiIntentResult intent, AiResponsePlan responsePlan);
}

internal interface IAiReplyClient
{
    Task<string> GenerateReplyAsync(AiReplyGenerationRequest request, CancellationToken cancellationToken);
}

internal sealed class ConversationContextBuilder : IConversationContextBuilder
{
    private readonly AiAgentPetStatusBuilder petStatusBuilder;
    private readonly CalendarReminderService reminderService;
    private readonly LocationSkillClient locationClient = new();

    public ConversationContextBuilder(AiAgentPetStatusBuilder petStatusBuilder, CalendarReminderService reminderService)
    {
        this.petStatusBuilder = petStatusBuilder;
        this.reminderService = reminderService;
    }

    public async Task<AiConversationContext> BuildAsync(string userInput, CancellationToken cancellationToken)
    {
        var calendarSummary = await reminderService.BuildCalendarSummaryAsync(cancellationToken);
        var currentLocation = await locationClient.GetCurrentLocationAsync(cancellationToken);
        return new AiConversationContext
        {
            UserInput = userInput,
            PetStatus = petStatusBuilder.BuildStatusSummary(),
            CalendarSummary = calendarSummary,
            CurrentLocation = currentLocation,
            Provider = AiAgentEnvironment.Provider,
            Model = AiAgentEnvironment.GetSelectedModel(),
            Now = DateTimeOffset.Now
        };
    }
}

internal sealed class ShortTermMemorySkill : IShortTermMemorySkill
{
    private const int MaxTurns = 10;
    private readonly List<AiShortTermMemoryTurn> history = new(MaxTurns);

    public AiConversationContext Attach(AiConversationContext context)
    {
        return new AiConversationContext
        {
            UserInput = context.UserInput,
            PetStatus = context.PetStatus,
            CalendarSummary = context.CalendarSummary,
            CurrentLocation = context.CurrentLocation,
            Provider = context.Provider,
            Model = context.Model,
            Now = context.Now,
            RecentTurns = history.ToArray()
        };
    }

    public void Update(AiConversationContext context, AiIntentResult intent, string finalResponse)
    {
        if (string.IsNullOrWhiteSpace(context.UserInput))
            return;

        history.Add(new AiShortTermMemoryTurn
        {
            UserInput = context.UserInput,
            AssistantResponse = finalResponse,
            CreatedAt = context.Now,
            PrimaryIntent = intent.PrimaryIntent
        });

        while (history.Count > MaxTurns)
            history.RemoveAt(0);
    }
}

internal sealed class NoOpShortTermMemorySkill : IShortTermMemorySkill
{
    public static NoOpShortTermMemorySkill Instance { get; } = new();

    public AiConversationContext Attach(AiConversationContext context) => context;

    public void Update(AiConversationContext context, AiIntentResult intent, string finalResponse)
    {
    }
}

internal sealed class EmotionSkill : IEmotionSkill
{
    public AiEmotionResult Analyze(AiConversationContext context)
    {
        var text = Normalize(context.UserInput);
        if (ContainsAny(text, "好煩", "煩死", "氣死", "爛掉", "搞壞", "崩潰", "不爽"))
            return new AiEmotionResult(AiEmotionType.Angry, 0.86, "使用者語氣帶有煩躁或挫折");
        if (ContainsAny(text, "看不懂", "不懂", "迷路", "混亂", "怎麼辦", "卡住"))
            return new AiEmotionResult(AiEmotionType.Confused, 0.78, "使用者正在卡住或不確定");
        if (ContainsAny(text, "累", "懶", "想睡", "沒力", "疲倦"))
            return new AiEmotionResult(AiEmotionType.Tired, 0.82, "使用者低能量或疲倦");
        if (ContainsAny(text, "開心", "太好了", "成功", "爽", "興奮"))
            return new AiEmotionResult(AiEmotionType.Excited, 0.8, "使用者情緒正向");
        if (ContainsAny(text, "難過", "沮喪", "想哭", "失落"))
            return new AiEmotionResult(AiEmotionType.Sad, 0.82, "使用者情緒低落");

        return new AiEmotionResult(AiEmotionType.Normal, 0.6, "未偵測到明顯特殊情緒");
    }

    private static string Normalize(string text) => (text ?? "").Trim().ToLowerInvariant();

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(text.Contains);
    }
}

internal sealed class IntentReasoningSkill : IIntentReasoningSkill
{
    public AiIntentResult Reason(AiConversationContext context, AiEmotionResult emotion)
    {
        var text = (context.UserInput ?? "").Trim().ToLowerInvariant();
        if (IsCalendarAddRequest(text))
        {
            return new AiIntentResult
            {
                PrimaryIntent = AiPrimaryIntent.Calendar,
                ResponseGoal = "Use recent short-term context to help create a calendar event; if the event time is missing, confirm the event and ask only for the missing time.",
                ShouldUseTool = true,
                CandidateTools = new List<string> { "calendar_add_event" },
                Confidence = 0.88
            };
        }
        if (ContainsAny(text, "行程", "行事曆", "calendar", "會議", "今天有沒有事"))
        {
            return new AiIntentResult
            {
                PrimaryIntent = AiPrimaryIntent.Calendar,
                ResponseGoal = "查詢行程後用自然口語整理結果",
                ShouldUseTool = true,
                CandidateTools = new List<string> { text.Contains("今天") ? "calendar_list_today" : "get_calendar_events" },
                Confidence = 0.9
            };
        }

        if (ContainsAny(text, "提醒我", "提醒", "鬧鐘"))
        {
            return new AiIntentResult
            {
                PrimaryIntent = AiPrimaryIntent.Reminder,
                ResponseGoal = "建立或查詢提醒",
                ShouldUseTool = true,
                CandidateTools = new List<string> { "create_reminder" },
                Confidence = 0.86
            };
        }

        if (ContainsAny(text, "打開", "開啟", "啟動") && ContainsAny(text, "程式", "記事本", "瀏覽器", "vscode", "資料夾"))
        {
            return new AiIntentResult
            {
                PrimaryIntent = AiPrimaryIntent.DesktopAction,
                ResponseGoal = "協助執行桌面動作",
                ShouldUseTool = true,
                CandidateTools = new List<string> { "open_program" },
                Confidence = 0.84
            };
        }

        if (ContainsAny(text, "天氣", "氣溫", "會下雨", "冷不冷", "熱不熱"))
        {
            return new AiIntentResult
            {
                PrimaryIntent = AiPrimaryIntent.AskQuestion,
                ResponseGoal = "查詢天氣並簡短回覆",
                ShouldUseTool = true,
                CandidateTools = new List<string> { "get_weather" },
                Confidence = 0.86
            };
        }

        if (IsFileSearchRequest(text))
        {
            return new AiIntentResult
            {
                PrimaryIntent = AiPrimaryIntent.DesktopAction,
                ResponseGoal = "搜尋本機檔案並整理找到的路徑",
                ShouldUseTool = true,
                CandidateTools = new List<string> { "search_files" },
                Confidence = 0.86
            };
        }

        if (ContainsAny(text, "地震", "震度", "有感地震", "地震快訊", "最近有沒有地震"))
        {
            return new AiIntentResult
            {
                PrimaryIntent = AiPrimaryIntent.AskQuestion,
                ResponseGoal = "查詢中央氣象署最近顯著有感地震並簡短整理",
                ShouldUseTool = true,
                CandidateTools = new List<string> { "get_earthquake_report" },
                Confidence = 0.88
            };
        }

        if (ContainsAny(text, "程式", "code", "bug", "錯誤", "編譯", "搞壞", "壞了"))
        {
            return new AiIntentResult
            {
                PrimaryIntent = AiPrimaryIntent.CodingHelp,
                SecondaryIntent = emotion.Type is AiEmotionType.Angry or AiEmotionType.Sad ? AiPrimaryIntent.EmotionalSupport : null,
                HiddenNeeds = new List<AiHiddenNeed> { AiHiddenNeed.NeedsDebugging, AiHiddenNeed.NeedsEncouragement },
                ResponseGoal = "先安撫，再指出最可能問題與修正方向",
                Confidence = 0.88
            };
        }

        if (ContainsAny(text, "最近都在做", "最近在做", "專案", "project"))
        {
            return new AiIntentResult
            {
                PrimaryIntent = AiPrimaryIntent.ProjectHelp,
                HiddenNeeds = new List<AiHiddenNeed> { AiHiddenNeed.NeedsCompanionship },
                ResponseGoal = "自然接話並記住長期專案背景",
                Confidence = 0.76,
                ShouldUpdateProjectMemory = true
            };
        }

        if (emotion.Type is AiEmotionType.Angry or AiEmotionType.Sad)
        {
            return new AiIntentResult
            {
                PrimaryIntent = AiPrimaryIntent.EmotionalSupport,
                HiddenNeeds = new List<AiHiddenNeed> { AiHiddenNeed.NeedsEncouragement, AiHiddenNeed.NeedsCompanionship },
                ResponseGoal = "先共感，短句陪伴",
                Confidence = 0.75
            };
        }

        return new AiIntentResult
        {
            PrimaryIntent = AiPrimaryIntent.CasualChat,
            HiddenNeeds = emotion.Type == AiEmotionType.Tired
                ? new List<AiHiddenNeed> { AiHiddenNeed.NeedsCompanionship }
                : new List<AiHiddenNeed>(),
            ResponseGoal = "像朋友一樣自然聊天",
            Confidence = 0.66
        };
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(text.Contains);
    }

    private static bool IsFileSearchRequest(string text)
    {
        return ContainsAny(text, "找檔案", "找文件", "搜尋檔案", "搜尋文件", "search file", "search files")
            || ContainsAny(text, "找", "搜尋", "search")
            && ContainsAny(text, "檔案", "文件", "資料夾", ".pdf", ".txt", ".cs", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".png", ".jpg", ".jpeg");
    }

    private static bool IsCalendarAddRequest(string text)
    {
        return ContainsAny(text, "加到行事曆", "加上", "加入", "新增", "記到", "放到")
            && ContainsAny(text, "行事曆", "行程", "calendar")
            || ContainsAny(text, "安排", "預約", "排一下")
            && ContainsAny(text, "今天", "明天", "後天", "下禮拜", "下星期", "下週", "下周", "點", "上午", "下午", "晚上");
    }
}

internal sealed class MemorySkill : IMemorySkill
{
    private readonly IStructuredMemoryStore store;

    public MemorySkill(IStructuredMemoryStore store)
    {
        this.store = store;
    }

    public AiStructuredMemory Load() => store.Load();

    public IReadOnlyList<AiMemoryItem> Retrieve(AiConversationContext context, AiIntentResult intent)
    {
        var memory = store.Load();
        var input = context.UserInput ?? "";
        var shouldUseProjectMemory = intent.PrimaryIntent == AiPrimaryIntent.ProjectHelp
            || intent.PrimaryIntent == AiPrimaryIntent.CodingHelp
            || input.Contains("記得", StringComparison.OrdinalIgnoreCase)
            || input.Contains("記住", StringComparison.OrdinalIgnoreCase)
            || input.Contains("專案", StringComparison.OrdinalIgnoreCase)
            || input.Contains("project", StringComparison.OrdinalIgnoreCase);

        if (!shouldUseProjectMemory)
            return Array.Empty<AiMemoryItem>();

        var items = new List<AiMemoryItem>();
        items.AddRange(memory.Projects
            .Where(project => intent.PrimaryIntent == AiPrimaryIntent.ProjectHelp || IsRelevant(input, project.Name, project.Description))
            .Take(3)
            .Select(project => new AiMemoryItem { Type = "project", Text = $"{project.Name} {project.Description}".Trim() }));
        items.AddRange(memory.ConversationNotes
            .Where(note => IsRelevant(input, note.Text))
            .TakeLast(3)
            .Select(note => new AiMemoryItem { Type = "note", Text = note.Text }));
        items.AddRange(memory.Preferences
            .Where(preference => input.Contains("回答", StringComparison.OrdinalIgnoreCase) || IsRelevant(input, preference.Key, preference.Value))
            .Take(3)
            .Select(preference => new AiMemoryItem { Type = "preference", Text = $"{preference.Key}: {preference.Value}" }));
        return items.Where(item => !string.IsNullOrWhiteSpace(item.Text)).Take(10).ToList();
    }

    private static bool IsRelevant(string input, params string[] values)
    {
        var tokens = SplitTokens(input);
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(SplitTokens)
            .Any(token => token.Length >= 2 && tokens.Contains(token, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> SplitTokens(string text)
    {
        return (text ?? "")
            .Split(new[] { ' ', '\t', '\r', '\n', '，', '。', ',', '.', '：', ':', '/', '\\', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
    }

    public AiMemoryUpdateResult Update(AiConversationContext context, AiIntentResult intent, string finalResponse)
    {
        var memory = store.Load();
        var input = (context.UserInput ?? "").Trim();
        var updated = false;

        if (intent.ShouldUpdateProjectMemory || input.Contains("最近都在做", StringComparison.OrdinalIgnoreCase))
        {
            var projectName = ExtractProjectName(input);
            if (!string.IsNullOrWhiteSpace(projectName)
                && !memory.Projects.Any(project => project.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase)))
            {
                memory.Projects.Add(new AiMemoryProject
                {
                    Name = projectName,
                    Description = "使用者提到最近在做的長期專案",
                    UpdatedAt = context.Now
                });
                updated = true;
            }
        }

        if (input.StartsWith("記住", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("請記住", StringComparison.OrdinalIgnoreCase))
        {
            memory.ConversationNotes.Add(new AiMemoryNote { Text = input, CreatedAt = context.Now });
            updated = true;
        }

        memory.InteractionStats.TotalInteractions++;
        memory.InteractionStats.LastInteractionAt = context.Now;
        memory.ProactiveState.LastInteractionAt = context.Now;
        memory.ProactiveState.LastContext = intent.PrimaryIntent.ToString();
        store.Save(memory);

        return new AiMemoryUpdateResult(updated, updated ? "memory_updated" : "state_updated");
    }

    private static string ExtractProjectName(string input)
    {
        var marker = "最近都在做";
        var index = input.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return "";

        return input[(index + marker.Length)..].Trim(' ', '。', '，', ',', '.');
    }
}

internal sealed class InMemoryStructuredMemoryStore : IStructuredMemoryStore
{
    private AiStructuredMemory memory = AiStructuredMemory.CreateDefault();

    public AiStructuredMemory Load() => memory;

    public void Save(AiStructuredMemory memory)
    {
        this.memory = memory;
    }
}

internal sealed class ToolSkill : IToolSkill
{
    private readonly IAiAgentToolExecutor executor;

    public ToolSkill(IAiAgentToolExecutor executor)
    {
        this.executor = executor;
    }

    public AiToolPlan Plan(AiConversationContext context, AiIntentResult intent)
    {
        if (!intent.ShouldUseTool || intent.CandidateTools.Count == 0)
            return AiToolPlan.None;

        var tool = intent.CandidateTools[0];
        var call = tool switch
        {
            "calendar_list_today" => new OllamaSkillCall("calendar_list_today"),
            "get_calendar_events" => new OllamaSkillCall("get_calendar_events"),
            "calendar_add_event" => BuildCalendarAddCall(context),
            "create_reminder" => new OllamaSkillCall("create_reminder", title: context.UserInput, time: "", note: ""),
            "open_program" => new OllamaSkillCall("open_program", target: context.UserInput),
            "get_weather" => new OllamaSkillCall("get_weather", location: ExtractWeatherLocation(context), query: context.UserInput),
            "get_earthquake_report" => new OllamaSkillCall("get_earthquake_report"),
            "search_files" => new OllamaSkillCall("search_files", query: ExtractFileSearchQuery(context.UserInput)),
            _ => new OllamaSkillCall(tool)
        };
        if (call.SkillName == "calendar_add_event"
            && (string.IsNullOrWhiteSpace(call.Title)
                || string.IsNullOrWhiteSpace(call.Date)
                || string.IsNullOrWhiteSpace(call.StartDatetime)
                || string.IsNullOrWhiteSpace(call.EndDatetime)))
        {
            return new AiToolPlan(false, call, "missing_calendar_event_details");
        }

        return new AiToolPlan(true, call, intent.ResponseGoal);
    }

    public async Task<AiToolExecutionResult> ExecuteAsync(AiToolPlan plan, AiConversationContext context, CancellationToken cancellationToken)
    {
        if (!plan.ShouldExecute)
            return AiToolExecutionResult.None;

        var result = await executor.ExecuteAsync(plan.ToolCall, context.UserInput, cancellationToken);
        return new AiToolExecutionResult(true, plan.ToolCall.SkillName, result);
    }

    private static OllamaSkillCall BuildCalendarAddCall(AiConversationContext context)
    {
        var source = FindCalendarEventSource(context);
        var currentInput = context.UserInput ?? "";
        var titleSource = ShouldResolveTitleFromHistory(currentInput) ? source : currentInput;
        var title = ExtractCalendarTitle(titleSource);
        if (string.IsNullOrWhiteSpace(title) || IsGenericCalendarReferenceTitle(title))
            title = ExtractCalendarTitle(source);

        var date = ExtractCalendarDate(currentInput, context.Now);
        if (string.IsNullOrWhiteSpace(date))
            date = ExtractCalendarDate(source, context.Now);

        var timeRange = ExtractCalendarTimeRange(currentInput);
        if (timeRange.Start is null)
            timeRange = ExtractCalendarTimeRange(source);

        if (string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(date)
            || timeRange.Start is null)
        {
            return new OllamaSkillCall("calendar_add_event", title: title, date: date);
        }

        var start = new DateTimeOffset(DateOnly.Parse(date).ToDateTime(TimeOnly.MinValue).Add(timeRange.Start.Value), context.Now.Offset);
        var end = new DateTimeOffset(DateOnly.Parse(date).ToDateTime(TimeOnly.MinValue).Add(timeRange.End ?? timeRange.Start.Value.Add(TimeSpan.FromHours(1))), context.Now.Offset);
        if (end <= start)
            end = start.AddHours(1);

        return new OllamaSkillCall(
            "calendar_add_event",
            title: title,
            date: date,
            startDatetime: start.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            endDatetime: end.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            description: "Created from AI pet short-term conversation context.");
    }

    private static string ExtractWeatherLocation(AiConversationContext context)
    {
        var input = context.UserInput ?? "";
        foreach (var location in WeatherLocationAliases)
        {
            if (input.Contains(location, StringComparison.OrdinalIgnoreCase))
                return location;
        }

        return string.IsNullOrWhiteSpace(context.CurrentLocation) ? input : context.CurrentLocation;
    }

    private static readonly string[] WeatherLocationAliases =
    {
        "臺北市", "台北市", "臺北", "台北", "新北市", "新北", "桃園市", "桃園",
        "臺中市", "台中市", "臺中", "台中", "臺南市", "台南市", "臺南", "台南",
        "高雄市", "高雄", "基隆市", "基隆", "新竹市", "新竹縣", "新竹", "苗栗縣", "苗栗",
        "彰化縣", "彰化", "南投縣", "南投", "雲林縣", "雲林", "嘉義市", "嘉義縣", "嘉義",
        "屏東縣", "屏東", "宜蘭縣", "宜蘭", "花蓮縣", "花蓮", "臺東縣", "台東縣", "臺東", "台東",
        "澎湖縣", "澎湖", "金門縣", "金門", "連江縣", "連江", "馬祖"
    };

    private static string ExtractFileSearchQuery(string input)
    {
        var query = input ?? "";
        foreach (var value in new[]
        {
            "幫我找一下", "幫我找", "找一下", "搜尋一下", "搜尋", "查找", "尋找",
            "找檔案", "找文件", "搜尋檔案", "搜尋文件", "檔案", "文件", "在哪裡", "在哪", "在哪兒",
            "search files", "search file", "search"
        })
        {
            query = query.Replace(value, "", StringComparison.OrdinalIgnoreCase);
        }

        return query.Trim(' ', '\t', '\r', '\n', '，', ',', '。', '.', '？', '?', '：', ':', '「', '」', '"');
    }

    private static string FindCalendarEventSource(AiConversationContext context)
    {
        var candidates = context.RecentTurns
            .Reverse()
            .Select(turn => turn.UserInput)
            .Prepend(context.UserInput);
        return candidates.FirstOrDefault(IsLikelyCalendarEventText) ?? context.UserInput;
    }

    private static bool IsLikelyCalendarEventText(string text)
    {
        return ContainsAny(text, "聚餐", "會議", "開會", "上課", "考試", "報告", "約", "打球", "讀書", "看電影")
            || ContainsAny(text, "下禮拜", "下週", "下星期", "明天", "後天");
    }

    private static bool ShouldResolveTitleFromHistory(string text)
    {
        return ContainsAny(text, "剛說", "剛剛", "剛才", "那件事", "這件事", "那件事情", "這件事情", "想做的");
    }

    private static bool IsGenericCalendarReferenceTitle(string title)
    {
        return ContainsAny(title, "那件事", "這件事", "那件事情", "這件事情", "想做");
    }

    private static string ExtractCalendarTitle(string text)
    {
        if (ContainsAny(text, "聚餐"))
            return "聚餐";
        if (ContainsAny(text, "會議", "開會"))
            return "會議";
        if (ContainsAny(text, "上課"))
            return "上課";
        if (ContainsAny(text, "考試"))
            return "考試";
        if (ContainsAny(text, "打球"))
            return "打球";
        if (ContainsAny(text, "打籃球"))
            return "打籃球";
        if (ContainsAny(text, "籃球"))
            return "打籃球";
        if (ContainsAny(text, "讀書"))
            return "讀書";
        if (ContainsAny(text, "看電影"))
            return "看電影";

        return ExtractTitleFromCalendarCommand(text);
    }

    private static string ExtractCalendarDate(string text, DateTimeOffset now)
    {
        if (ContainsAny(text, "下禮拜三", "下星期三", "下週三", "下周三"))
            return GetNextWeekday(now, DayOfWeek.Wednesday).ToString("yyyy-MM-dd");
        if (ContainsAny(text, "明天"))
            return now.Date.AddDays(1).ToString("yyyy-MM-dd");
        if (ContainsAny(text, "後天"))
            return now.Date.AddDays(2).ToString("yyyy-MM-dd");
        return "";
    }

    private static DateTime GetNextWeekday(DateTimeOffset now, DayOfWeek target)
    {
        var currentIsoDay = now.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)now.DayOfWeek;
        var nextWeekMonday = now.Date.AddDays(8 - currentIsoDay);
        var targetIsoDay = target == DayOfWeek.Sunday ? 7 : (int)target;
        return nextWeekMonday.AddDays(targetIsoDay - 1);
    }

    private static (TimeSpan? Start, TimeSpan? End) ExtractCalendarTimeRange(string text)
    {
        var marker = text.IndexOf("到", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            marker = text.IndexOf("-", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            marker = text.IndexOf("~", StringComparison.OrdinalIgnoreCase);

        if (marker >= 0)
        {
            var start = ExtractCalendarTime(text[..marker]);
            var end = ExtractCalendarTime(text[(marker + 1)..]);
            if (start is not null && end is not null)
                return (start, end);
        }

        return (ExtractCalendarTime(text), null);
    }

    private static TimeSpan? ExtractCalendarTime(string text)
    {
        for (var hour = 0; hour <= 23; hour++)
        {
            if (text.Contains($"{hour}點", StringComparison.OrdinalIgnoreCase)
                || text.Contains($"{hour}:00", StringComparison.OrdinalIgnoreCase))
            {
                return TimeSpan.FromHours(NormalizeCalendarHour(text, hour));
            }
        }

        foreach (var (textHour, hour) in ChineseHours)
        {
            if (text.Contains($"{textHour}點", StringComparison.OrdinalIgnoreCase))
                return TimeSpan.FromHours(NormalizeCalendarHour(text, hour));
        }

        return null;
    }

    private static readonly (string Text, int Hour)[] ChineseHours =
    {
        ("零", 0),
        ("一", 1),
        ("二", 2),
        ("兩", 2),
        ("三", 3),
        ("四", 4),
        ("五", 5),
        ("六", 6),
        ("七", 7),
        ("八", 8),
        ("九", 9),
        ("十", 10),
        ("十一", 11),
        ("十二", 12)
    };

    private static int NormalizeCalendarHour(string text, int hour)
    {
        if (ContainsAny(text, "凌晨", "早上", "上午") && hour == 12)
            return 0;
        if (ContainsAny(text, "凌晨", "早上", "上午"))
            return hour;
        if (ContainsAny(text, "下午", "晚上", "傍晚") && hour is > 0 and < 12)
            return hour + 12;
        if (ContainsAny(text, "中午") && hour == 12)
            return 12;
        if (hour is >= 1 and <= 7)
            return hour + 12;
        return hour;
    }

    private static string ExtractTitleFromCalendarCommand(string text)
    {
        var title = text;
        foreach (var value in new[]
        {
            "幫我", "請", "把", "安排", "預約", "新增", "加入", "加上", "加到行事曆", "加到google行事曆", "加到 google 行事曆",
            "放到行事曆", "記到行事曆", "行事曆", "行程", "google calendar", "calendar",
            "明天", "後天", "下禮拜三", "下星期三", "下週三", "下周三", "上午", "早上", "中午", "下午", "晚上", "傍晚",
            "點", ":00"
        })
        {
            title = title.Replace(value, "", StringComparison.OrdinalIgnoreCase);
        }

        for (var hour = 0; hour <= 23; hour++)
            title = title.Replace(hour.ToString(), "", StringComparison.OrdinalIgnoreCase);

        return title.Trim(' ', '\t', '\r', '\n', '，', ',', '。', '.', '：', ':');
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class PersonalitySkill : IPersonalitySkill
{
    public AiPersonalityProfile GetProfile(AiConversationContext context)
    {
        return new AiPersonalityProfile
        {
            Name = "AI 桌寵",
            Instruction = "你就是桌面寵物本體，不是另一個聊天機器人。"
        };
    }

    public string BuildSystemPrompt(AiPersonalityProfile profile)
    {
        return string.Join(Environment.NewLine, new[]
        {
            profile.Instruction,
            "一律使用繁體中文回答，口語化，像朋友聊天。",
            "你必須用第一人稱「我」代表桌寵自己，不要用桌寵名字第三人稱自稱。",
            "不要說「小可愛覺得」「桌寵覺得」這種第三人稱句型；要說「我覺得」。",
            "不要在回覆中輸出英文，除非使用者明確要求、程式碼、錯誤訊息或專有名詞需要保留。",
            "內部狀態和記憶只是參考，不要逐條背出來，也不要主動說「我記得你...」，除非使用者問到或這次話題真的相關。",
            "個性：可愛、自然、會吐槽、會關心使用者，但不要太吵。",
            "可以偶爾有桌寵感，但不要每句都硬裝可愛或每句都加語助詞。",
            "如果使用者在抱怨，先共感，不要立刻丟一大串解法。",
            "如果使用者在卡程式，先指出最可能問題，再給修正方向。",
            "如果有工具結果，根據結果自然整理，不要說你沒有執行工具。"
        });
    }
}

internal sealed class StyleSkill : IStyleSkill
{
    public AiStyleResult Select(AiConversationContext context, AiEmotionResult emotion, AiIntentResult intent, AiStructuredMemory memory)
    {
        if (intent.PrimaryIntent == AiPrimaryIntent.CodingHelp)
            return new AiStyleResult(AiStyleMode.Coder, "短、直接，先抓最可能問題，再給下一步。");
        if (emotion.Type is AiEmotionType.Angry or AiEmotionType.Sad)
            return new AiStyleResult(AiStyleMode.Comfort, "先安撫，語氣放軟，不要說教。");
        if (intent.PrimaryIntent is AiPrimaryIntent.StudyHelp)
            return new AiStyleResult(AiStyleMode.Teacher, "用簡單步驟解釋。");
        if (intent.PrimaryIntent == AiPrimaryIntent.CasualChat)
            return new AiStyleResult(AiStyleMode.Cute, "自然陪聊，可以輕微吐槽或撒嬌，但不要過度。");

        return new AiStyleResult(AiStyleMode.Normal, "自然、簡短、清楚。");
    }
}

internal sealed class ResponseReasoningSkill : IResponseReasoningSkill
{
    public AiResponsePlan Plan(AiConversationContext context, AiEmotionResult emotion, AiIntentResult intent, IReadOnlyList<AiMemoryItem> memories, AiToolExecutionResult toolResult, AiStyleResult style)
    {
        if (toolResult.Executed)
        {
            return new AiResponsePlan
            {
                ReplyStrategy = "tool_result_summary",
                Tone = style.Mode.ToString(),
                Length = AiResponseLength.Short,
                MustMentionToolResult = true,
                FinalInstruction = "請根據工具結果自然口語整理給使用者，避免像報表。"
            };
        }

        if (intent.PrimaryIntent == AiPrimaryIntent.Calendar
            && intent.CandidateTools.Contains("calendar_add_event"))
        {
            return new AiResponsePlan
            {
                ReplyStrategy = "confirm_recent_context_then_ask_missing_calendar_time",
                Tone = style.Mode.ToString(),
                Length = AiResponseLength.Short,
                ShouldAskFollowUp = true,
                FinalInstruction = "Use the current input and short-term context to identify the calendar event. Some required calendar details are missing, so ask only for the missing date or time. Do not claim the event was created. Do not say you already added it unless a tool result explicitly says it was created."
            };
        }

        if (intent.PrimaryIntent == AiPrimaryIntent.CodingHelp && emotion.Type is AiEmotionType.Angry or AiEmotionType.Sad)
        {
            return new AiResponsePlan
            {
                ReplyStrategy = "empathy_then_fix_direction",
                Tone = "comfort_coder",
                Length = AiResponseLength.Short,
                ShouldBePlayful = false,
                FinalInstruction = "先共感使用者卡程式的煩躁，再給最可能問題與修正方向，不要一次塞太多解法。"
            };
        }

        if (intent.PrimaryIntent == AiPrimaryIntent.CasualChat)
        {
            return new AiResponsePlan
            {
                ReplyStrategy = "natural_companion_chat",
                Tone = style.Mode.ToString(),
                Length = AiResponseLength.Short,
                ShouldBePlayful = true,
                FinalInstruction = "像朋友自然接話，有一點桌寵感，但不要像客服或報告。"
            };
        }

        return new AiResponsePlan
        {
            ReplyStrategy = "direct_answer",
            Tone = style.Mode.ToString(),
            Length = AiResponseLength.Short,
            ShouldBePlayful = style.Mode == AiStyleMode.Tsukkomi,
            FinalInstruction = "直接回答，保持繁體中文與自然語氣。"
        };
    }
}

internal sealed class ProactiveSkill : IProactiveSkill
{
    private readonly IStructuredMemoryStore store;

    public ProactiveSkill(IStructuredMemoryStore store)
    {
        this.store = store;
    }

    public void UpdateState(AiConversationContext context, AiEmotionResult emotion, AiIntentResult intent, AiResponsePlan responsePlan)
    {
        var memory = store.Load();
        memory.ProactiveState.LastInteractionAt = context.Now;
        memory.ProactiveState.LastContext = $"{emotion.Type}:{intent.PrimaryIntent}";
        store.Save(memory);
    }
}

internal sealed class ChatPipeline
{
    private readonly IConversationContextBuilder contextBuilder;
    private readonly IEmotionSkill emotionSkill;
    private readonly IIntentReasoningSkill intentSkill;
    private readonly IMemorySkill memorySkill;
    private readonly IToolSkill toolSkill;
    private readonly IPersonalitySkill personalitySkill;
    private readonly IStyleSkill styleSkill;
    private readonly IResponseReasoningSkill responseSkill;
    private readonly IProactiveSkill proactiveSkill;
    private readonly IAiReplyClient replyClient;
    private readonly IShortTermMemorySkill shortTermMemorySkill;

    public ChatPipeline(
        IConversationContextBuilder contextBuilder,
        IEmotionSkill emotionSkill,
        IIntentReasoningSkill intentSkill,
        IMemorySkill memorySkill,
        IToolSkill toolSkill,
        IPersonalitySkill personalitySkill,
        IStyleSkill styleSkill,
        IResponseReasoningSkill responseSkill,
        IProactiveSkill proactiveSkill,
        IAiReplyClient replyClient,
        IShortTermMemorySkill? shortTermMemorySkill = null)
    {
        this.contextBuilder = contextBuilder;
        this.emotionSkill = emotionSkill;
        this.intentSkill = intentSkill;
        this.memorySkill = memorySkill;
        this.toolSkill = toolSkill;
        this.personalitySkill = personalitySkill;
        this.styleSkill = styleSkill;
        this.responseSkill = responseSkill;
        this.proactiveSkill = proactiveSkill;
        this.replyClient = replyClient;
        this.shortTermMemorySkill = shortTermMemorySkill ?? NoOpShortTermMemorySkill.Instance;
    }

    public async Task<ChatPipelineResult> RunAsync(string userInput, CancellationToken cancellationToken)
    {
        var context = shortTermMemorySkill.Attach(await contextBuilder.BuildAsync(userInput, cancellationToken));
        var emotion = emotionSkill.Analyze(context);
        var intent = intentSkill.Reason(context, emotion);
        var memories = memorySkill.Retrieve(context, intent);
        var toolPlan = toolSkill.Plan(context, intent);
        var toolResult = await toolSkill.ExecuteAsync(toolPlan, context, cancellationToken);
        var memory = memorySkill.Load();
        var profile = personalitySkill.GetProfile(context);
        var style = styleSkill.Select(context, emotion, intent, memory);
        var responsePlan = responseSkill.Plan(context, emotion, intent, memories, toolResult, style);
        var finalResponse = await replyClient.GenerateReplyAsync(
            BuildGenerationRequest(context, memories, toolResult, profile, style, responsePlan),
            cancellationToken);
        finalResponse = (finalResponse ?? "").Trim();
        finalResponse = GuardCalendarSuccessClaim(finalResponse, intent, toolResult);
        finalResponse = SanitizeCalendarEventId(finalResponse, toolResult);

        if (string.IsNullOrWhiteSpace(finalResponse))
            finalResponse = "我剛剛腦袋小當機了一下，主人再說一次好嗎？";

        shortTermMemorySkill.Update(context, intent, finalResponse);
        var memoryUpdate = memorySkill.Update(context, intent, finalResponse);
        proactiveSkill.UpdateState(context, emotion, intent, responsePlan);

        return new ChatPipelineResult
        {
            FinalResponse = finalResponse,
            Context = context,
            Emotion = emotion,
            Intent = intent,
            ToolPlan = toolPlan,
            ToolResult = toolResult,
            ResponsePlan = responsePlan,
            MemoryUpdate = memoryUpdate
        };
    }

    private AiReplyGenerationRequest BuildGenerationRequest(
        AiConversationContext context,
        IReadOnlyList<AiMemoryItem> memories,
        AiToolExecutionResult toolResult,
        AiPersonalityProfile profile,
        AiStyleResult style,
        AiResponsePlan responsePlan)
    {
        var contextPrompt = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context.PetStatus))
            contextPrompt.AppendLine(SanitizePetStatus(context.PetStatus));
        if (!string.IsNullOrWhiteSpace(context.CalendarSummary))
            contextPrompt.AppendLine("Google 行事曆摘要：").AppendLine(context.CalendarSummary);
        if (ShouldIncludeShortTermContext(context, responsePlan))
        {
            contextPrompt.AppendLine("最近 10 輪短期對話紀錄：只用來判斷這輪是否承接前文；有關才自然使用，不要主動說「我記得」或逐條複述。");
            foreach (var turn in context.RecentTurns.TakeLast(10))
            {
                contextPrompt.AppendLine($"- 使用者：{turn.UserInput}");
                if (!string.IsNullOrWhiteSpace(turn.AssistantResponse))
                    contextPrompt.AppendLine($"  回覆：{turn.AssistantResponse}");
            }
        }
        if (memories.Count > 0)
        {
            contextPrompt.AppendLine("內部記憶參考（只有話題相關時才自然使用，不要主動逐條提起）：");
            foreach (var memory in memories)
                contextPrompt.AppendLine($"- [{memory.Type}] {memory.Text}");
        }
        if (toolResult.Executed)
            contextPrompt.AppendLine($"工具 `{toolResult.ToolName}` 執行結果：").AppendLine(toolResult.Result);
        contextPrompt.AppendLine("語氣模式：").AppendLine(style.Instruction);
        contextPrompt.AppendLine("回覆策略：").AppendLine(responsePlan.FinalInstruction);

        return new AiReplyGenerationRequest
        {
            SystemPrompt = personalitySkill.BuildSystemPrompt(profile),
            ContextPrompt = contextPrompt.ToString(),
            UserInput = context.UserInput
        };
    }

    private static string GuardCalendarSuccessClaim(string finalResponse, AiIntentResult intent, AiToolExecutionResult toolResult)
    {
        if (string.IsNullOrWhiteSpace(finalResponse) || !LooksLikeCalendarSuccessClaim(finalResponse))
            return finalResponse;
        if (toolResult.Executed
            && toolResult.ToolName.Equals("calendar_add_event", StringComparison.OrdinalIgnoreCase)
            && toolResult.Result.Contains("event_id", StringComparison.OrdinalIgnoreCase))
        {
            return finalResponse;
        }

        if (intent.CandidateTools.Contains("calendar_add_event") || LooksLikeCalendarSuccessClaim(finalResponse))
            return "我還沒真的新增到 Google 行事曆，因為剛剛沒有拿到行事曆工具的成功結果。請再給我完整的日期和時間，我會重新幫你加。";

        return finalResponse;
    }

    private static bool LooksLikeCalendarSuccessClaim(string text)
    {
        return ContainsAnyText(text, "已經幫你", "幫你安排好了", "安排好了", "新增好了", "加到行事曆", "排好了")
            && ContainsAnyText(text, "安排", "新增", "行事曆", "行程", "預約", "明天", "今天", "下週", "下周");
    }

    private static bool ContainsAnyText(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitizeCalendarEventId(string finalResponse, AiToolExecutionResult toolResult)
    {
        if (!toolResult.ToolName.Equals("calendar_add_event", StringComparison.OrdinalIgnoreCase))
            return finalResponse;

        var sanitized = Regex.Replace(finalResponse, @"[（(]?\s*event_id\s*(是|:|：)?\s*[A-Za-z0-9_\-\.]+[）)]?", "", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"\s{2,}", " ");
        return sanitized.Trim();
    }

    private static bool ShouldIncludeShortTermContext(AiConversationContext context, AiResponsePlan responsePlan)
    {
        return context.RecentTurns.Count > 0;
    }

    private static string SanitizePetStatus(string petStatus)
    {
        var builder = new StringBuilder();
        builder.AppendLine("桌寵狀態（內部參考，不要照抄欄位名，不要用第三人稱描述自己）：");
        foreach (var rawLine in petStatus.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Equals("Current desktop pet status:", StringComparison.OrdinalIgnoreCase))
                continue;

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                builder.AppendLine(line);
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            var label = key switch
            {
                "time" => "時間",
                "pet_name" => "你的名字",
                "owner_name" => "使用者稱呼",
                "mode" => "狀態模式",
                "state" => "目前狀態",
                "animation" => "動畫",
                "level" => "等級",
                "exp" => "經驗值",
                "money" => "金錢",
                "strength" => "體力",
                "satiety" => "飽食度",
                "hydration" => "口渴度",
                "mood" => "心情",
                "health" => "健康",
                "affection" => "親密度",
                "current_task" => "目前活動",
                "task_type" => "活動類型",
                "task_elapsed_minutes" => "已進行分鐘",
                "task_remaining_minutes" => "剩餘分鐘",
                "task_earned_so_far" => "目前收益",
                _ => key
            };
            builder.AppendLine($"{label}：{value}");
        }

        return builder.ToString();
    }
}
