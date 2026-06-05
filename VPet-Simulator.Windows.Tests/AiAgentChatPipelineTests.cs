using System;
using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.AiAgent;
using VPet_Simulator.Windows.AiAgent.Chat;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class AiAgentChatPipelineTests
{
    [Fact]
    public async Task CalendarRequestExecutesToolBeforeGeneratingFinalResponse()
    {
        var toolExecutor = new FakeToolExecutor();
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var pipeline = new ChatPipeline(
            new FakeContextBuilder("幫我看今天有沒有行程"),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(toolExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient);

        var result = await pipeline.RunAsync("幫我看今天有沒有行程", CancellationToken.None);

        Assert.True(result.ToolResult.Executed);
        Assert.Equal("calendar_list_today", toolExecutor.LastTool);
        Assert.Contains("今天 15:00 開會", replyClient.LastRequest.ContextPrompt);
        Assert.Equal("final", result.FinalResponse);
    }

    [Fact]
    public async Task PromptUsesFirstPersonAndDoesNotExposeInternalEnglishStatusLabels()
    {
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var pipeline = new ChatPipeline(
            new FakeContextBuilder("今天有點懶", "Current desktop pet status:\npet_name: 小可愛\nowner_name: KAO\ncurrent_task: none"),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(new FakeToolExecutor()),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient);

        await pipeline.RunAsync("今天有點懶", CancellationToken.None);

        Assert.Contains("第一人稱", replyClient.LastRequest.SystemPrompt);
        Assert.Contains("不要用桌寵名字第三人稱自稱", replyClient.LastRequest.SystemPrompt);
        Assert.Contains("不要在回覆中輸出英文", replyClient.LastRequest.SystemPrompt);
        Assert.DoesNotContain("pet_name", replyClient.LastRequest.ContextPrompt);
        Assert.DoesNotContain("current_task", replyClient.LastRequest.ContextPrompt);
    }

    [Fact]
    public async Task FollowUpCalendarAddUsesRecentShortTermContext()
    {
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var shortTermMemory = new ShortTermMemorySkill();
        var pipeline = new ChatPipeline(
            new PassThroughContextBuilder(),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(new FakeToolExecutor()),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient,
            shortTermMemory);

        await pipeline.RunAsync("我下禮拜三要去聚餐", CancellationToken.None);
        var result = await pipeline.RunAsync("幫我在行事曆上加上這個行程", CancellationToken.None);

        Assert.Equal(AiPrimaryIntent.Calendar, result.Intent.PrimaryIntent);
        Assert.False(result.ToolPlan.ShouldExecute);
        Assert.True(result.ResponsePlan.ShouldAskFollowUp);
        Assert.Contains("我下禮拜三要去聚餐", replyClient.LastRequest.ContextPrompt);
    }

    [Fact]
    public async Task CalendarAddWithDateTimeExecutesExistingExecutor()
    {
        var toolExecutor = new FakeToolExecutor();
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var pipeline = new ChatPipeline(
            new PassThroughContextBuilder(),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(toolExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient);

        var result = await pipeline.RunAsync("幫我把明天下午3點打球加到行事曆", CancellationToken.None);

        Assert.True(result.ToolPlan.ShouldExecute);
        Assert.True(result.ToolResult.Executed);
        Assert.Equal("calendar_add_event", toolExecutor.LastTool);
        Assert.Equal("打球", toolExecutor.LastCall.Title);
        Assert.Contains("T15:00:00", toolExecutor.LastCall.StartDatetime);
        Assert.Contains("T16:00:00", toolExecutor.LastCall.EndDatetime);
    }

    [Fact]
    public async Task CalendarAddWithChineseRangeExecutesExistingExecutor()
    {
        var toolExecutor = new FakeToolExecutor();
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var pipeline = new ChatPipeline(
            new FixedNowContextBuilder(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.FromHours(8))),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(toolExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient);

        var result = await pipeline.RunAsync("幫我把下周三兩點到三點打球加到行事曆", CancellationToken.None);

        Assert.True(result.ToolPlan.ShouldExecute);
        Assert.True(result.ToolResult.Executed);
        Assert.Equal("calendar_add_event", toolExecutor.LastTool);
        Assert.Equal("打球", toolExecutor.LastCall.Title);
        Assert.Contains("2026-06-10T14:00:00", toolExecutor.LastCall.StartDatetime);
        Assert.Contains("2026-06-10T15:00:00", toolExecutor.LastCall.EndDatetime);
    }

    [Fact]
    public async Task CalendarArrangeWithoutCalendarKeywordExecutesExistingExecutor()
    {
        var toolExecutor = new FakeToolExecutor();
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var pipeline = new ChatPipeline(
            new FixedNowContextBuilder(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.FromHours(8))),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(toolExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient);

        var result = await pipeline.RunAsync("幫我安排明天下午兩點到三點打籃球", CancellationToken.None);

        Assert.True(result.ToolPlan.ShouldExecute);
        Assert.True(result.ToolResult.Executed);
        Assert.Equal("calendar_add_event", toolExecutor.LastTool);
        Assert.Equal("打籃球", toolExecutor.LastCall.Title);
        Assert.Contains("T14:00:00", toolExecutor.LastCall.StartDatetime);
        Assert.Contains("T15:00:00", toolExecutor.LastCall.EndDatetime);
    }

    [Fact]
    public async Task CalendarSuccessClaimIsBlockedWithoutToolResult()
    {
        var replyClient = new FakeReplyClient("好！已經幫你安排好了，明天下午兩點到三點打籃球。");
        var store = new InMemoryStructuredMemoryStore();
        var pipeline = new ChatPipeline(
            new PassThroughContextBuilder(),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(new FakeToolExecutor()),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient);

        var result = await pipeline.RunAsync("幫我安排打籃球", CancellationToken.None);

        Assert.Contains("還沒真的新增", result.FinalResponse);
        Assert.DoesNotContain("已經幫你安排好了", result.FinalResponse);
    }

    [Fact]
    public async Task CalendarAddFinalResponseDoesNotExposeEventId()
    {
        var toolExecutor = new FakeToolExecutor("Skill `calendar_add_event` 執行結果：\n已新增 Google Calendar 事件：打籃球\n時間：2026-06-06 14:00 - 15:00\nevent_id：abc123");
        var replyClient = new FakeReplyClient("好！已經幫你安排好了，event_id 是 abc123。");
        var store = new InMemoryStructuredMemoryStore();
        var pipeline = new ChatPipeline(
            new FixedNowContextBuilder(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.FromHours(8))),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(toolExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient);

        var result = await pipeline.RunAsync("幫我安排明天下午兩點到三點打籃球", CancellationToken.None);

        Assert.DoesNotContain("event_id", result.FinalResponse);
        Assert.DoesNotContain("abc123", result.FinalResponse);
    }

    [Fact]
    public async Task CalendarAddMissingDetailsAsksInsteadOfHallucinatingSuccess()
    {
        var toolExecutor = new FakeToolExecutor();
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var pipeline = new ChatPipeline(
            new PassThroughContextBuilder(),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(toolExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient);

        var result = await pipeline.RunAsync("幫我把打球加到行事曆", CancellationToken.None);

        Assert.False(result.ToolPlan.ShouldExecute);
        Assert.False(result.ToolResult.Executed);
        Assert.True(result.ResponsePlan.ShouldAskFollowUp);
        Assert.Contains("Do not claim", result.ResponsePlan.FinalInstruction);
    }

    [Fact]
    public async Task CalendarAddFollowUpWithMissingTimeAsksInsteadOfExecuting()
    {
        var toolExecutor = new FakeToolExecutor();
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var shortTermMemory = new ShortTermMemorySkill();
        var pipeline = new ChatPipeline(
            new PassThroughContextBuilder(),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(toolExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient,
            shortTermMemory);

        await pipeline.RunAsync("我下禮拜三要去聚餐", CancellationToken.None);
        var result = await pipeline.RunAsync("幫我在行事曆上加上這個行程", CancellationToken.None);

        Assert.False(result.ToolPlan.ShouldExecute);
        Assert.False(result.ToolResult.Executed);
        Assert.True(result.ResponsePlan.ShouldAskFollowUp);
    }

    [Fact]
    public async Task CalendarAddUsesRecentDesiredActivityAsTitle()
    {
        var toolExecutor = new FakeToolExecutor();
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var shortTermMemory = new ShortTermMemorySkill();
        var pipeline = new ChatPipeline(
            new FixedNowContextBuilder(new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.FromHours(8))),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(toolExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient,
            shortTermMemory);

        await pipeline.RunAsync("我好想打球喔", CancellationToken.None);
        var result = await pipeline.RunAsync("請你把我剛說想做的那件事情加到行事曆，下周三八點到九點", CancellationToken.None);

        Assert.True(result.ToolPlan.ShouldExecute);
        Assert.True(result.ToolResult.Executed);
        Assert.Equal("calendar_add_event", toolExecutor.LastTool);
        Assert.Equal("打球", toolExecutor.LastCall.Title);
        Assert.Contains("2026-06-10T08:00:00", toolExecutor.LastCall.StartDatetime);
        Assert.Contains("2026-06-10T09:00:00", toolExecutor.LastCall.EndDatetime);
    }

    [Fact]
    public async Task RecentHistoryIsAvailableForEveryReply()
    {
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var shortTermMemory = new ShortTermMemorySkill();
        var pipeline = new ChatPipeline(
            new PassThroughContextBuilder(),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(new FakeToolExecutor()),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient,
            shortTermMemory);

        await pipeline.RunAsync("我想去打球", CancellationToken.None);
        await pipeline.RunAsync("你覺得呢", CancellationToken.None);

        Assert.Contains("我想去打球", replyClient.LastRequest.ContextPrompt);
    }

    [Fact]
    public async Task FinalResponseIsTrimmedBeforeDisplay()
    {
        var replyClient = new FakeReplyClient("  哈哈，可以啊  ");
        var store = new InMemoryStructuredMemoryStore();
        var pipeline = new ChatPipeline(
            new PassThroughContextBuilder(),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(new FakeToolExecutor()),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient);

        var result = await pipeline.RunAsync("我想去打球", CancellationToken.None);

        Assert.Equal("哈哈，可以啊", result.FinalResponse);
    }

    [Fact]
    public async Task EarthquakeQuestionPlansEarthquakeReportTool()
    {
        var toolExecutor = new FakeToolExecutor("中央氣象署地震快訊：目前沒有新的顯著有感地震。");
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var pipeline = new ChatPipeline(
            new PassThroughContextBuilder(),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(toolExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient);

        var result = await pipeline.RunAsync("最近有沒有地震快訊？", CancellationToken.None);

        Assert.True(result.ToolPlan.ShouldExecute);
        Assert.True(result.ToolResult.Executed);
        Assert.Equal("get_earthquake_report", toolExecutor.LastTool);
        Assert.Contains("中央氣象署地震快訊", replyClient.LastRequest.ContextPrompt);
    }

    [Fact]
    public async Task WeatherQuestionWithoutExplicitLocationUsesCurrentLocation()
    {
        var toolExecutor = new FakeToolExecutor("中央氣象署 臺中市 預報：多雲。");
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var pipeline = new ChatPipeline(
            new FixedLocationContextBuilder("臺中市"),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(toolExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient);

        var result = await pipeline.RunAsync("明天天氣如何？", CancellationToken.None);

        Assert.True(result.ToolResult.Executed);
        Assert.Equal("get_weather", toolExecutor.LastTool);
        Assert.Equal("臺中市", toolExecutor.LastCall.Location);
    }

    [Fact]
    public async Task FileSearchQuestionPlansSearchFilesTool()
    {
        var toolExecutor = new FakeToolExecutor("找到 1 筆檔案：小考.pdf");
        var replyClient = new FakeReplyClient();
        var store = new InMemoryStructuredMemoryStore();
        var pipeline = new ChatPipeline(
            new PassThroughContextBuilder(),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(store),
            new ToolSkill(toolExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(store),
            replyClient);

        var result = await pipeline.RunAsync("幫我找小考.pdf", CancellationToken.None);

        Assert.True(result.ToolResult.Executed);
        Assert.Equal("search_files", toolExecutor.LastTool);
        Assert.Equal("小考.pdf", toolExecutor.LastCall.Query);
    }

    private sealed class FakeContextBuilder : IConversationContextBuilder
    {
        private readonly string userInput;
        private readonly string petStatus;

        public FakeContextBuilder(string userInput, string petStatus = "")
        {
            this.userInput = userInput;
            this.petStatus = petStatus;
        }

        public Task<AiConversationContext> BuildAsync(string userInput, CancellationToken cancellationToken)
        {
            var context = AiConversationContext.ForTest(this.userInput);
            return Task.FromResult(new AiConversationContext
            {
                UserInput = context.UserInput,
                PetStatus = petStatus,
                CalendarSummary = context.CalendarSummary,
                Provider = context.Provider,
                Model = context.Model,
                Now = context.Now
            });
        }
    }

    private sealed class PassThroughContextBuilder : IConversationContextBuilder
    {
        public Task<AiConversationContext> BuildAsync(string userInput, CancellationToken cancellationToken)
        {
            return Task.FromResult(AiConversationContext.ForTest(userInput));
        }
    }

    private sealed class FixedNowContextBuilder : IConversationContextBuilder
    {
        private readonly DateTimeOffset now;

        public FixedNowContextBuilder(DateTimeOffset now)
        {
            this.now = now;
        }

        public Task<AiConversationContext> BuildAsync(string userInput, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AiConversationContext
            {
                UserInput = userInput,
                Provider = "ollama",
                Model = "test",
                Now = now
            });
        }
    }

    private sealed class FixedLocationContextBuilder : IConversationContextBuilder
    {
        private readonly string currentLocation;

        public FixedLocationContextBuilder(string currentLocation)
        {
            this.currentLocation = currentLocation;
        }

        public Task<AiConversationContext> BuildAsync(string userInput, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AiConversationContext
            {
                UserInput = userInput,
                Provider = "ollama",
                Model = "test",
                Now = DateTimeOffset.Now,
                CurrentLocation = currentLocation
            });
        }
    }

    private sealed class FakeToolExecutor : IAiAgentToolExecutor
    {
        private readonly string result;

        public FakeToolExecutor(string result = "今天 15:00 開會")
        {
            this.result = result;
        }

        public string LastTool { get; private set; } = "";
        public OllamaSkillCall LastCall { get; private set; } = OllamaSkillCall.None;

        public Task<string> ExecuteAsync(OllamaSkillCall skillCall, string userText, CancellationToken cancellationToken)
        {
            LastTool = skillCall.SkillName;
            LastCall = skillCall;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeReplyClient : IAiReplyClient
    {
        private readonly string response;

        public FakeReplyClient(string response = "final")
        {
            this.response = response;
        }

        public AiReplyGenerationRequest LastRequest { get; private set; } = new();

        public Task<string> GenerateReplyAsync(AiReplyGenerationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }
}
