using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using VPet_Simulator.Windows.AiAgent.Chat;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class AiAgentTalkBox : TalkBox
{
    private readonly OpenAiAgentClient openAiClient;
    private readonly OllamaAgentClient ollamaClient;
    private readonly CalendarReminderService reminderService;
    private readonly AiAgentPetStatusBuilder petStatusBuilder;
    private readonly AiAgentSkillExecutor skillExecutor;
    private readonly PomodoroService pomodoroService;
    private readonly ShortTermMemorySkill shortTermMemorySkill = new();
    private readonly VoiceService voiceService = new();

    public AiAgentTalkBox(MainPlugin mainPlugin, OpenAiAgentClient openAiClient, OllamaAgentClient ollamaClient, CalendarReminderService reminderService, AiAgentPetStatusBuilder petStatusBuilder, PomodoroService pomodoroService)
        : base(mainPlugin)
    {
        this.openAiClient = openAiClient;
        this.ollamaClient = ollamaClient;
        this.reminderService = reminderService;
        this.petStatusBuilder = petStatusBuilder;
        this.pomodoroService = pomodoroService;
        skillExecutor = new AiAgentSkillExecutor(mainPlugin.MW, reminderService, petStatusBuilder, pomodoroService);

        voiceService.SpeechRecognized += OnSpeechRecognized;
        voiceService.RecordingStateChanged += OnRecordingStateChanged;

        Task.Run(() =>
        {
            VoiceLogger.Log("[Init] 開始背景初始化...");
            try { voiceService.InitializeAsr(); } catch (Exception ex) { VoiceLogger.LogError("[Init] InitializeAsr", ex); }
            try { voiceService.InitializeTts(); } catch (Exception ex) { VoiceLogger.LogError("[Init] InitializeTts", ex); }
            VoiceLogger.Log($"[Init] 初始化完成, ASR就緒={voiceService.IsAsrReady}, TTS就緒={voiceService.IsTtsReady}");
            MainPlugin.MW.Dispatcher.Invoke(() =>
            {
                btnMic.Content = voiceService.IsAsrReady ? "\uD83C\uDFA4" : "\u274C";
                btnMic.ToolTip = voiceService.IsAsrReady ? "\u6309\u4F4F\u8AAA\u8A71" : "\u8A9E\u97F3\u6A21\u7D44\u8F09\u5165\u5931\u6557";
                VoiceLogger.Log("[Init] UI 按鈕已更新");
            });
        });
    }

    public override string APIName => "AI Agent";

    public override void OnMicButtonDown()
    {
        if (voiceService.IsAsrReady)
            Task.Run(() => voiceService.StartRecording());
    }

    public override void OnMicButtonUp()
    {
        Task.Run(() => voiceService.StopRecording());
    }

    private void OnSpeechRecognized(object? sender, string text)
    {
        Task.Run(() => Responded(text));
    }

    private void OnRecordingStateChanged(object? sender, bool isRecording)
    {
        MainPlugin.MW.Dispatcher.InvokeAsync(() =>
        {
            btnMic.Content = isRecording ? "\uD83D\uDD34" : "\uD83C\uDFA4";
            btnMic.Background = isRecording
                ? System.Windows.Media.Brushes.Red
                : (System.Windows.Media.Brush)System.Windows.Application.Current.TryFindResource("SecondaryLight") ?? System.Windows.Media.Brushes.LightGray;
        });
    }

    public override void Responded(string text)
    {
        DisplayThink();
        try
        {
            if (AiAgentCommandRouter.TryHandle(MainPlugin.MW, text, out var commandResponse, pomodoroService))
            {
                DisplayThinkToSayRnd(commandResponse, APIName);
                voiceService.Speak(commandResponse);
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var result = CreatePipeline().RunAsync(text, cts.Token).GetAwaiter().GetResult();
            var response = string.IsNullOrWhiteSpace(result.FinalResponse) ? "\u6211\u73fe\u5728\u9084\u60f3\u4e0d\u5230\u600e\u9ebc\u56de\u7b54\u55b5\u3002" : result.FinalResponse;
            DisplayThinkToSayRnd(response, APIName);
            voiceService.Speak(response);
        }
        catch (Exception ex)
        {
            var errorMsg = "\u6211\u7684 AI \u52a9\u624b\u51fa\u932f\u4e86\uff1a" + ex.Message;
            DisplayThinkToSayRnd(errorMsg, APIName);
            voiceService.Speak(errorMsg);
        }
    }

    private ChatPipeline CreatePipeline()
    {
        IAiReplyClient replyClient = AiAgentEnvironment.Provider.Equals("remote_api", StringComparison.OrdinalIgnoreCase)
            || AiAgentEnvironment.Provider.Equals("openai", StringComparison.OrdinalIgnoreCase)
                ? openAiClient
                : ollamaClient;
        var memoryStore = new AiAgentMemoryStore();
        return new ChatPipeline(
            new ConversationContextBuilder(petStatusBuilder, reminderService),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(memoryStore),
            new ToolSkill(skillExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(memoryStore),
            replyClient,
            shortTermMemorySkill);
    }

    public override void Setting()
    {
        MainPlugin.MW.Dispatcher.Invoke(() =>
        {
            if (MainPlugin.MW is MainWindow mainWindow)
                mainWindow.winSetting.SelectAiAgentSettings();
        });
    }
}
