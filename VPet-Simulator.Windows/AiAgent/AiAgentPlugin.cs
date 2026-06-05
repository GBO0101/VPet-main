using System;
using System.Threading;
using System.Threading.Tasks;
using LinePutScript;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class AiAgentPlugin : MainPlugin
{
    private readonly OpenAiAgentClient openAiClient = new();
    private readonly OllamaAgentClient ollamaClient = new();
    private readonly GoogleCalendarClient calendarClient = new();
    private AiAgentPetStatusBuilder? petStatusBuilder;
    private CalendarReminderService? reminderService;
    private LocalReminderService? localReminderService;
    private PomodoroService? pomodoroService;
    private EarthquakeMonitorService? earthquakeMonitorService;
    private AiAgentTalkBox? talkBox;

    public AiAgentPlugin(IMainWindow mainwin) : base(mainwin)
    {
    }

    public override string PluginName => "AI Agent";

    public override void LoadPlugin()
    {
        reminderService = new CalendarReminderService(MW, calendarClient);
        petStatusBuilder = new AiAgentPetStatusBuilder(MW);
        reminderService.Start();
        localReminderService = new LocalReminderService(MW, new LocalReminderStore());
        localReminderService.Start();
        pomodoroService = new PomodoroService(MW);
        MW.DynamicResources[PomodoroService.DynamicResourceKey] = pomodoroService;
        earthquakeMonitorService = new EarthquakeMonitorService(MW);
        earthquakeMonitorService.Start();

        talkBox = new AiAgentTalkBox(this, openAiClient, ollamaClient, reminderService, petStatusBuilder, pomodoroService);
        MW.TalkAPI.Add(talkBox);

        // 設為預設聊天介面
        MW.Set["CGPT"][(gstr)"type"] = "DIY";
        MW.Set["CGPT"][(gstr)"DIY"] = "AI Agent";

        if (AiAgentEnvironment.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                await ollamaClient.EnsureReadyAsync(cts.Token);
            });
        }
    }

    public override void EndGame()
    {
        reminderService?.Dispose();
        localReminderService?.Dispose();
        pomodoroService?.Dispose();
        earthquakeMonitorService?.Dispose();
        MW.DynamicResources.Remove(PomodoroService.DynamicResourceKey);
    }

    public override void Setting()
    {
        MW.Dispatcher.Invoke(() =>
        {
            if (MW is MainWindow mainWindow)
                mainWindow.winSetting.SelectAiAgentSettings();
        });
    }

}
