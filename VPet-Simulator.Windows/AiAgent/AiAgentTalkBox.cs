using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using VPet_Simulator.Windows.AiAgent.Chat;
using VPet_Simulator.Windows.Interface;
using System.Windows;

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
    private readonly ScreenCaptureService screenCapture = new();
    private readonly ScreenAnalysisService screenAnalysis = new();
    private readonly WorkflowEngine workflowEngine;
    private readonly SemaphoreSlim _busySemaphore = new(1, 1);
    private string lastScreenDescription = "";
    private DateTime lastProactiveAt = DateTime.MinValue;
    private bool isInConversation;
    private HotkeyService? hotkeyService;
    private int hotkeyRegistrationId;
    private volatile bool _isHotkeyRecording;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private bool IsHotkeyDown(HotkeyService.ModifierKeys modifiers, HotkeyService.Keys key)
    {
        if ((GetAsyncKeyState((int)key) & 0x8000) == 0) return false;
        if ((modifiers & HotkeyService.ModifierKeys.Control) != 0 && (GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0) return false;
        if ((modifiers & HotkeyService.ModifierKeys.Shift) != 0 && (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0) return false;
        if ((modifiers & HotkeyService.ModifierKeys.Alt) != 0 && (GetAsyncKeyState(VK_MENU) & 0x8000) == 0) return false;
        if ((modifiers & HotkeyService.ModifierKeys.Windows) != 0)
        {
            if ((GetAsyncKeyState(VK_LWIN) & 0x8000) == 0 && (GetAsyncKeyState(VK_RWIN) & 0x8000) == 0) return false;
        }
        return true;
    }

    public AiAgentTalkBox(MainPlugin mainPlugin, OpenAiAgentClient openAiClient, OllamaAgentClient ollamaClient, CalendarReminderService reminderService, AiAgentPetStatusBuilder petStatusBuilder, PomodoroService pomodoroService)
        : base(mainPlugin)
    {
        this.openAiClient = openAiClient;
        this.ollamaClient = ollamaClient;
        this.reminderService = reminderService;
        this.petStatusBuilder = petStatusBuilder;
        this.pomodoroService = pomodoroService;
        skillExecutor = new AiAgentSkillExecutor(mainPlugin.MW, reminderService, petStatusBuilder, pomodoroService);
        workflowEngine = new WorkflowEngine(mainPlugin.MW, reminderService, petStatusBuilder, pomodoroService, _busySemaphore);
        workflowEngine.WorkflowExecuted += (name, trigger) =>
            WorkflowLogger.Log($"[Integrate] Workflow 執行完成: {name}, 觸發源: {trigger}");

        Task.Run(() =>
        {
            VoiceLogger.Log("[Init] 開始背景初始化...");
            VoiceLogger.Log($"[Init] 功能開關: VoiceInput={FeatureManager.IsVoiceInputEnabled}, VoiceOutput={FeatureManager.IsVoiceOutputEnabled}, ScreenAware={FeatureManager.IsScreenAwareEnabled}, Workflow={FeatureManager.IsWorkflowEnabled}");

            if (FeatureManager.IsVoiceInputEnabled)
            {
                voiceService.SpeechRecognized += OnSpeechRecognized;
                voiceService.RecordingStateChanged += OnRecordingStateChanged;
                try { voiceService.InitializeAsr(); } catch (Exception ex) { VoiceLogger.LogError("[Init] InitializeAsr", ex); }
            }

            if (FeatureManager.IsVoiceOutputEnabled)
            {
                try { voiceService.InitializeTts(); } catch (Exception ex) { VoiceLogger.LogError("[Init] InitializeTts", ex); }
            }

            if (FeatureManager.IsVoiceInputEnabled || FeatureManager.IsVoiceOutputEnabled)
                VoiceLogger.Log($"[Init] ASR就緒={voiceService.IsAsrReady}, TTS就緒={voiceService.IsTtsReady}");
            else
                VoiceLogger.Log("[Init] 語音功能已禁用，跳過 ASR/TTS 初始化");

            MainPlugin.MW.Dispatcher.Invoke(() =>
            {
                btnMic.Content = voiceService.IsAsrReady ? "\uD83C\uDFA4" : "\u274C";
                btnMic.ToolTip = voiceService.IsAsrReady ? "\u6309\u4F4F\u8AAA\u8A71" : "\u8A9E\u97F3\u6A21\u7D44\u8F09\u5165\u5931\u6557";
                VoiceLogger.Log("[Init] UI 按鈕已更新");

                if (MainPlugin.MW is MainWindow mw)
                {
                    try
                    {
                        hotkeyService = new HotkeyService(mw);
                        var hotkeyStr = AiAgentEnvironment.Get(AiAgentEnvironment.HotkeyPushToTalk);
                        if (!string.IsNullOrWhiteSpace(hotkeyStr) && HotkeyService.TryParse(hotkeyStr, out var mods, out var key))
                        {
                            hotkeyRegistrationId = hotkeyService.Register(mods, key, () =>
                            {
                                if (FeatureManager.IsVoiceInputEnabled && voiceService.IsAsrReady && !_isHotkeyRecording)
                                {
                                    var capturedMods = mods;
                                    var capturedKey = key;
                                    _isHotkeyRecording = true;
                                    voiceService.StartRecording();
                                    Task.Run(() =>
                                    {
                                        try
                                        {
                                            while (IsHotkeyDown(capturedMods, capturedKey))
                                                Thread.Sleep(50);
                                        }
                                        finally
                                        {
                                            voiceService.StopRecording();
                                            _isHotkeyRecording = false;
                                        }
                                    });
                                }
                            });
                            VoiceLogger.Log($"[Init] 快捷鍵已註冊: {HotkeyService.Format(mods, key)}");
                        }
                        else
                        {
                            VoiceLogger.Log("[Init] 無快捷鍵設定或格式錯誤");
                        }
                    }
                    catch (Exception ex)
                    {
                        VoiceLogger.LogError("[Init] 註冊快捷鍵", ex);
                    }
                }
            });

            if (FeatureManager.IsScreenAwareEnabled && screenAnalysis.IsConfigured)
            {
                VoiceLogger.Log("[Init] 螢幕感知功能已啟用，開始啟動");
                StartScreenAwareness();
            }
            else
            {
                VoiceLogger.Log($"[Init] 螢幕感知未啟用: Enabled={FeatureManager.IsScreenAwareEnabled}, Configured={screenAnalysis.IsConfigured}");
            }
        });
    }

    public override string APIName => "AI Agent";

    public void ReloadWorkflows() => workflowEngine.Reload();

    private void StartScreenAwareness()
    {
        Task.Run(async () =>
        {
            ScreenAwareLogger.Log("[ScreenAware] 開始背景螢幕感知");
            while (FeatureManager.IsScreenAwareEnabled)
            {
                var intervalStr = AiAgentEnvironment.Get(AiAgentEnvironment.ScreenAwareInterval);
                var intervalSec = int.TryParse(intervalStr, out var parsed) && parsed >= 5 ? parsed : 60;
                await Task.Delay(TimeSpan.FromSeconds(intervalSec));

                try
                {
                    await _busySemaphore.WaitAsync();

                    try
                    {
                        var t0 = DateTime.Now;
                        var base64 = screenCapture.CaptureAsBase64();
                        ScreenAwareLogger.Log($"[Capture] 截圖完成, size={base64.Length} bytes, 耗時={(DateTime.Now - t0).TotalMilliseconds:F0}ms");

                        var description = await screenAnalysis.AnalyzeAsync(base64, CancellationToken.None);
                        ScreenAwareLogger.Log($"[Analysis] 分析結果: \"{description}\"");

                        if (!string.IsNullOrWhiteSpace(description))
                            workflowEngine.TryMatchScreen(description);

                        if (FeatureManager.IsWorkflowEnabled)
                            workflowEngine.CheckSchedule();

                        if (string.IsNullOrWhiteSpace(description))
                        {
                            ScreenAwareLogger.Log("[Skip] 分析結果為空");
                            continue;
                        }
                        if (description == "\u770b\u8d77\u4f86\u6c92\u4ec0\u9ebc\u7279\u5225\u7684")
                        {
                            ScreenAwareLogger.Log("[Skip] 畫面無重要活動");
                            continue;
                        }

                        var isSame = description.Equals(lastScreenDescription, StringComparison.Ordinal);
                        lastScreenDescription = description;

                        if (isSame)
                        {
                            ScreenAwareLogger.Log("[Skip] 畫面描述與上次相同");
                            continue;
                        }

                        var timeSinceLastProactive = DateTime.Now - lastProactiveAt;
                        if (timeSinceLastProactive.TotalMinutes < 5)
                        {
                            ScreenAwareLogger.Log($"[Skip] 距上次主動回應僅 {timeSinceLastProactive.TotalMinutes:F1} 分鐘");
                            continue;
                        }
                        if (isInConversation)
                        {
                            ScreenAwareLogger.Log("[Skip] 正在對話中");
                            continue;
                        }

                        ScreenAwareLogger.Log($"[Change] 畫面變化: {description}");
                        var proactiveMsg = await screenAnalysis.GenerateProactiveMessageAsync(description, CancellationToken.None);
                        ScreenAwareLogger.Log($"[Proactive] 生成回應: \"{proactiveMsg}\"");

                        if (!string.IsNullOrWhiteSpace(proactiveMsg))
                        {
                            lastProactiveAt = DateTime.Now;
                            var _ = MainPlugin.MW.Dispatcher.InvokeAsync(() =>
                            {
                                DisplayThinkToSayRnd(proactiveMsg, "AI Agent");
                            });
                            if (FeatureManager.IsVoiceOutputEnabled)
                                voiceService.Speak(proactiveMsg);
                            ScreenAwareLogger.Log($"[Proactive] 已顯示, TTS={FeatureManager.IsVoiceOutputEnabled}: {proactiveMsg}");
                        }
                    }
                    finally
                    {
                        _busySemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    ScreenAwareLogger.LogError("[ScreenAware]", ex);
                }
            }
        });
    }

    public override void OnMicButtonDown()
    {
        if (FeatureManager.IsVoiceInputEnabled && voiceService.IsAsrReady)
            Task.Run(() => voiceService.StartRecording());
    }

    public override void OnMicButtonUp()
    {
        if (FeatureManager.IsVoiceInputEnabled)
            Task.Run(() => voiceService.StopRecording());
    }

    private void OnSpeechRecognized(object? sender, string text)
    {
        WorkflowLogger.Log($"[Integrate] 收到語音: \"{text}\"");
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
        _busySemaphore.Wait();
        try
        {
            isInConversation = true;

            if (FeatureManager.IsWorkflowEnabled && workflowEngine.TryMatchInput(text))
            {
                WorkflowLogger.Log("[Integrate] 輸入被 workflow 攔截，不送 LLM");
                isInConversation = false;
                return;
            }

            DisplayThink();
            try
            {
                if (AiAgentCommandRouter.TryHandle(MainPlugin.MW, text, out var commandResponse, pomodoroService))
                {
                    DisplayThinkToSayRnd(commandResponse, APIName);
                    if (FeatureManager.IsVoiceOutputEnabled)
                        voiceService.Speak(commandResponse);
                    return;
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var result = CreatePipeline().RunAsync(text, cts.Token).GetAwaiter().GetResult();
                var response = string.IsNullOrWhiteSpace(result.FinalResponse) ? "\u6211\u73fe\u5728\u9084\u60f3\u4e0d\u5230\u600e\u9ebc\u56de\u7b54\u55b5\u3002" : result.FinalResponse;
                DisplayThinkToSayRnd(response, APIName);
                if (FeatureManager.IsVoiceOutputEnabled)
                    voiceService.Speak(response);
            }
            catch (Exception ex)
            {
                var errorMsg = "\u6211\u7684 AI \u52a9\u624b\u51fa\u932f\u4e86\uff1a" + ex.Message;
                DisplayThinkToSayRnd(errorMsg, APIName);
                if (FeatureManager.IsVoiceOutputEnabled)
                    voiceService.Speak(errorMsg);
            }
            finally
            {
                isInConversation = false;
            }
        }
        finally
        {
            _busySemaphore.Release();
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
