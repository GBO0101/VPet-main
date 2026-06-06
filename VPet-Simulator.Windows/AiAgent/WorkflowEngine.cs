using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class WorkflowEngine
{
    private readonly IMainWindow mw;
    private readonly WorkflowStore store = new();
    private readonly ProgramShortcutStore shortcutStore = new();
    private readonly CalendarReminderService reminderService;
    private readonly AiAgentPetStatusBuilder petStatusBuilder;
    private readonly PomodoroService pomodoroService;
    private readonly SemaphoreSlim? _busySemaphore;
    private List<WorkflowDefinition> workflows = new();
    private DateTime lastScheduleCheck = DateTime.MinValue;
    private CancellationTokenSource? workflowCts;
    private readonly object syncLock = new();

    public WorkflowEngine(IMainWindow mw, CalendarReminderService reminderService, AiAgentPetStatusBuilder petStatusBuilder, PomodoroService pomodoroService, SemaphoreSlim? busySemaphore = null)
    {
        this.mw = mw;
        this.reminderService = reminderService;
        this.petStatusBuilder = petStatusBuilder;
        this.pomodoroService = pomodoroService;
        _busySemaphore = busySemaphore;
        Reload();
    }

    public void Reload()
    {
        lock (syncLock)
        {
            workflows = store.Load();
        }
    }

    public event Action<string, string>? WorkflowExecuted;

    public bool TryMatchInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            WorkflowLogger.Log("[TryMatchInput] 輸入為空，跳過");
            return false;
        }

        WorkflowLogger.Log($"[TryMatchInput] 檢查輸入觸發: \"{text}\"");

        lock (syncLock)
        {
            foreach (var w in workflows)
            {
                if (!w.Enabled || w.Trigger.Type != WorkflowTriggerType.Input)
                    continue;
                if (string.IsNullOrWhiteSpace(w.Trigger.InputKeyword))
                    continue;

                var matched = text.Contains(w.Trigger.InputKeyword, StringComparison.OrdinalIgnoreCase);
                WorkflowLogger.LogTrigger(w.Name, $"[Input] \"{text}\" contains \"{w.Trigger.InputKeyword}\"", matched);

                if (matched)
                {
                    WorkflowLogger.Log($"[TryMatchInput] ✓ 命中 workflow: {w.Name}");
                    RunWorkflow(w, $"[Input] {text}");
                    return true;
                }
            }
        }
        WorkflowLogger.Log("[TryMatchInput] 未命中任何輸入 workflow");
        return false;
    }

    public bool TryMatchScreen(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            WorkflowLogger.Log("[TryMatchScreen] 畫面描述為空，跳過");
            return false;
        }

        WorkflowLogger.Log($"[TryMatchScreen] 檢查螢幕觸發: \"{description}\"");

        lock (syncLock)
        {
            foreach (var w in workflows)
            {
                if (!w.Enabled || w.Trigger.Type != WorkflowTriggerType.Screen)
                    continue;
                if (string.IsNullOrWhiteSpace(w.Trigger.ScreenKeyword))
                    continue;

                var matched = description.Contains(w.Trigger.ScreenKeyword, StringComparison.OrdinalIgnoreCase);
                WorkflowLogger.LogTrigger(w.Name, $"[Screen] \"{description}\" contains \"{w.Trigger.ScreenKeyword}\"", matched);

                if (matched)
                {
                    WorkflowLogger.Log($"[TryMatchScreen] ✓ 命中 workflow: {w.Name}");
                    RunWorkflow(w, $"[Screen] {description}");
                    return true;
                }
            }
        }
        WorkflowLogger.Log("[TryMatchScreen] 未命中任何螢幕 workflow");
        return false;
    }

    public void CheckSchedule()
    {
        var now = DateTime.Now;
        if ((now - lastScheduleCheck).TotalSeconds < 30)
            return;
        lastScheduleCheck = now;

        WorkflowLogger.Log($"[CheckSchedule] 檢查排程觸發 ({now:HH:mm})");

        lock (syncLock)
        {
            foreach (var w in workflows)
            {
                if (!w.Enabled || w.Trigger.Type != WorkflowTriggerType.Schedule)
                    continue;
                if (string.IsNullOrWhiteSpace(w.Trigger.ScheduleCron))
                    continue;

                var matched = MatchCron(w.Trigger.ScheduleCron, now);
                WorkflowLogger.LogTrigger(w.Name, $"[Schedule] cron=\"{w.Trigger.ScheduleCron}\" now={now:HH:mm}", matched);

                if (matched)
                {
                    WorkflowLogger.Log($"[CheckSchedule] ✓ 命中 workflow: {w.Name}");
                    RunWorkflow(w, $"[Schedule] {w.Trigger.ScheduleCron}");
                }
            }
        }
    }

    private async void RunWorkflow(WorkflowDefinition w, string triggerInfo)
    {
        try
        {
            workflowCts?.Cancel();
            workflowCts = new CancellationTokenSource();
            await ExecuteAsync(w, triggerInfo, workflowCts.Token);
        }
        catch (OperationCanceledException)
        {
            WorkflowLogger.Log($"[RunWorkflow] 取消: {w.Name}");
        }
        catch (Exception ex)
        {
            WorkflowLogger.LogError($"[RunWorkflow] 未預期錯誤: {w.Name}", ex);
        }
    }

    private async Task ExecuteAsync(WorkflowDefinition w, string triggerInfo, CancellationToken cancellationToken = default)
    {
        bool acquired = false;
        if (_busySemaphore != null)
            acquired = _busySemaphore.Wait(0);

        try
        {
            WorkflowLogger.Log($"[Execute] 開始執行 workflow: {w.Name} ({triggerInfo}), 動作數={w.Actions.Count}");

            for (int i = 0; i < w.Actions.Count; i++)
            {
                var action = w.Actions[i];
                WorkflowLogger.LogAction(w.Name, i, $"{action.Type} (ProgramName={action.ProgramName}, Msg={action.Message}, Delay={action.DelaySeconds}s, Pomodoro={action.PomodoroMinutes}min)");

                try
                {
                    switch (action.Type)
                    {
                        case WorkflowActionType.LaunchProgram:
                            ExecuteLaunch(action.ProgramName);
                            break;
                        case WorkflowActionType.StartPomodoro:
                            ExecutePomodoro(action.PomodoroMinutes);
                            break;
                        case WorkflowActionType.SendMessage:
                            ExecuteMessage(action.Message);
                            break;
                        case WorkflowActionType.Wait:
                            var delay = Math.Max(1, action.DelaySeconds);
                            WorkflowLogger.Log($"[Execute] 等待 {delay} 秒...");
                            await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                            break;
                        case WorkflowActionType.ShowNotification:
                            ExecuteNotification(action.NotificationTitle, action.NotificationBody);
                            break;
                    }
                    WorkflowLogger.LogAction(w.Name, i, $"{action.Type} ✓ 完成");
                }
                catch (Exception ex)
                {
                    WorkflowLogger.LogError($"[Execute] 動作#{i} 失敗: {action.Type}", ex);
                }
            }

            WorkflowExecuted?.Invoke(w.Name, triggerInfo);
            WorkflowLogger.Log($"[Execute] ✓ workflow 完成: {w.Name}");
        }
        finally
        {
            if (acquired) _busySemaphore?.Release();
        }
    }

    private void ExecuteLaunch(string programName)
    {
        programName = (programName ?? "").Trim();

        if (programName.Contains('\\') || programName.Contains('/'))
        {
            WorkflowLogger.Log($"[ExecuteLaunch] 嘗試開啟檔案路徑: {programName}");
            try
            {
                Process.Start(new ProcessStartInfo { FileName = programName, UseShellExecute = true });
                WorkflowLogger.Log($"[ExecuteLaunch] ✓ 已開啟: {programName}");
            }
            catch (Exception ex)
            {
                WorkflowLogger.LogError($"[ExecuteLaunch] 開啟失敗: {programName}", ex);
            }
            return;
        }

        WorkflowLogger.Log("[ExecuteLaunch] 參數不是檔案路徑，開啟檔案選擇對話框...");
        Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "選擇要開啟的檔案",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = dialog.FileName, UseShellExecute = true });
                    WorkflowLogger.Log($"[ExecuteLaunch] ✓ 已開啟選擇的檔案: {dialog.FileName}");
                }
                catch (Exception ex)
                {
                    WorkflowLogger.LogError($"[ExecuteLaunch] 開啟選擇的檔案失敗: {dialog.FileName}", ex);
                }
            }
            else
            {
                WorkflowLogger.Log("[ExecuteLaunch] 使用者取消選擇檔案");
            }
        });
    }

    private void ExecutePomodoro(int minutes)
    {
        if (minutes < 1) minutes = 25;
        WorkflowLogger.Log($"[ExecutePomodoro] 啟動番茄鐘: {minutes}分鐘");
        var result = pomodoroService.Start();
        WorkflowLogger.Log($"[ExecutePomodoro] ✓ {result}");
    }

    private void ExecuteMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            WorkflowLogger.Log("[ExecuteMessage] 訊息為空，跳過");
            return;
        }

        WorkflowLogger.Log($"[ExecuteMessage] 桌寵說話: \"{message}\"");
        mw.Dispatcher.InvokeAsync(() =>
        {
            if (mw is MainWindow mainWindow)
            {
                var box = mainWindow.TalkBox;
                if (box != null)
                {
                    var type = box.GetType();
                    var method = type.GetMethod("DisplayThinkToSayRnd",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    method?.Invoke(box, new object[] { message, "AI Agent" });
                    WorkflowLogger.Log("[ExecuteMessage] ✓ 訊息已顯示");
                }
                else
                {
                    WorkflowLogger.Log("[ExecuteMessage] TalkBox 為 null");
                }
            }
            else
            {
                WorkflowLogger.Log("[ExecuteMessage] MainWindow 型別不符");
            }
        });
    }

    private void ExecuteNotification(string title, string body)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
        {
            WorkflowLogger.Log("[ExecuteNotification] 標題與內容皆為空，跳過");
            return;
        }

        WorkflowLogger.Log($"[ExecuteNotification] 顯示通知: \"{title}\" - \"{body}\"");
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Information,
                Visible = true,
                BalloonTipTitle = string.IsNullOrWhiteSpace(title) ? "VPet Workflow" : title,
                BalloonTipText = body,
                BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info
            };
            notifyIcon.ShowBalloonTip(5000);
            notifyIcon.Dispose();
        });
        WorkflowLogger.Log("[ExecuteNotification] ✓ 通知已顯示");
    }

    private static bool MatchCron(string cron, DateTime time)
    {
        var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return false;

        return MatchCronPart(parts[0], time.Minute)
            && MatchCronPart(parts[1], time.Hour)
            && MatchCronPart(parts[2], time.Day)
            && MatchCronPart(parts[3], time.Month)
            && MatchCronPart(parts[4], (int)time.DayOfWeek);
    }

    private static bool MatchCronPart(string pattern, int value)
    {
        if (pattern == "*")
            return true;

        if (pattern.StartsWith("*/"))
        {
            if (int.TryParse(pattern[2..], out var step) && step > 0)
                return value % step == 0;
            return false;
        }

        if (int.TryParse(pattern, out var exact))
            return exact == value;

        return false;
    }
}
