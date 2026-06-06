using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Panuon.WPF.UI;
using VPet_Simulator.Windows.AiAgent;

namespace VPet_Simulator.Windows;

public partial class winGameSetting
{
    private const int AiAgentSettingsTabIndex = 5;
    private bool isCapturingHotkey = false;
    private string savedHotkeyBeforeCapture = "";
    private readonly OpenAiAgentClient aiOpenAiClient = new();
    private readonly OllamaAgentClient aiOllamaClient = new();
    private readonly GoogleCalendarClient aiCalendarClient = new();
    private readonly LocalReminderStore aiReminderStore = new();
    private readonly ProgramShortcutStore aiShortcutStore = new();
    private readonly AiModelCatalogService aiModelCatalogService = new();

    public void SelectAiAgentSettings()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(SelectAiAgentSettings);
            return;
        }

        MainTab.SelectedIndex = AiAgentSettingsTabIndex;
        RefreshAiAgentData();
        Show();
        Activate();
    }

    private void InitializeAiAgentSettings()
    {
        SelectAiProvider(AiAgentEnvironment.Provider);
        cbAiOllamaAutoStart.IsChecked = AiAgentEnvironment.IsOllamaAutoStartEnabled;

        tbAiOllamaUrl.Text = AiAgentEnvironment.Get(AiAgentEnvironment.OllamaUrl);
        if (string.IsNullOrWhiteSpace(tbAiOllamaUrl.Text))
            tbAiOllamaUrl.Text = "http://localhost:11434";

        var ollamaModel = AiAgentEnvironment.Get(AiAgentEnvironment.OllamaModel);
        SelectAiOllamaModel(ollamaModel);
        _ = RefreshLocalOllamaModelsAsync(saveSelection: false);
        tbAiRemoteApiBaseUrl.Text = AiAgentEnvironment.Get(AiAgentEnvironment.RemoteApiBaseUrl);
        SelectAiRemoteApiModel(AiAgentEnvironment.GetRemoteApiModel());

        tbAiGoogleClientId.Text = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleClientId);
        dpAiReminderDate.SelectedDate = DateTime.Now.Date;
        tbAiReminderTime.Text = DateTime.Now.AddMinutes(5).ToString("HH:mm");

        cbFeatureVoiceInput.IsChecked = FeatureManager.IsVoiceInputEnabled;
        cbFeatureVoiceOutput.IsChecked = FeatureManager.IsVoiceOutputEnabled;
        cbFeatureScreenAware.IsChecked = FeatureManager.IsScreenAwareEnabled;
        cbFeatureWorkflow.IsChecked = FeatureManager.IsWorkflowEnabled;

        var intervalStr = AiAgentEnvironment.Get(AiAgentEnvironment.ScreenAwareInterval);
        slAiScreenInterval.Value = int.TryParse(intervalStr, out var interval) ? interval : 60;

        var visionModel = AiAgentEnvironment.Get(AiAgentEnvironment.VisionModel);
        if (!string.IsNullOrWhiteSpace(visionModel))
            cbAiVisionModel.Text = visionModel;

        var hotkey = AiAgentEnvironment.Get(AiAgentEnvironment.HotkeyPushToTalk);
        tbAiHotkeyPushToTalk.Text = hotkey;
        if (!string.IsNullOrWhiteSpace(hotkey))
            tbAiHotkeyStatus.Text = $"目前註冊：{hotkey}";
        else
            tbAiHotkeyStatus.Text = "無快捷鍵設定";

        RefreshWorkflowList();

        RefreshAiAgentData();
        RefreshDiyHotkeys();
    }

    private void SaveAiAgentSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveAiAgentSettings(true);
    }

    private void SaveAiAgentSettings(bool showMessage)
    {
        var provider = GetSelectedAiProvider();
        var ollamaAutoStart = cbAiOllamaAutoStart?.IsChecked == false ? "false" : "true";

        var voiceInput = cbFeatureVoiceInput?.IsChecked == true;
        var voiceOutput = cbFeatureVoiceOutput?.IsChecked == true;
        var screenAware = cbFeatureScreenAware?.IsChecked == true;
        var workflow = cbFeatureWorkflow?.IsChecked == true;

        var ollamaUrl = tbAiOllamaUrl?.Text.Trim() ?? "";
        var ollamaModel = GetSelectedAiOllamaModel();
        var remoteApiBaseUrl = tbAiRemoteApiBaseUrl?.Text.Trim() ?? "";
        var remoteApiModel = GetSelectedAiRemoteApiModel();
        var remoteApiKey = !string.IsNullOrWhiteSpace(pbAiOpenAiKey?.Password) ? pbAiOpenAiKey.Password : null;
        var googleClientId = tbAiGoogleClientId?.Text.Trim() ?? "";
        var googleClientSecret = !string.IsNullOrWhiteSpace(pbAiGoogleClientSecret?.Password) ? pbAiGoogleClientSecret.Password : null;
        var interval = ((int?)slAiScreenInterval?.Value ?? 60).ToString();
        var visionModel = cbAiVisionModel?.Text.Trim() ?? "";
        var hotkey = tbAiHotkeyPushToTalk?.Text.Trim() ?? "";

        Task.Run(() =>
        {
            AiAgentEnvironment.SetUser(AiAgentEnvironment.AiProvider, provider);
            AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaAutoStart, ollamaAutoStart);
            FeatureManager.SetVoiceInputEnabled(voiceInput);
            FeatureManager.SetVoiceOutputEnabled(voiceOutput);
            FeatureManager.SetScreenAwareEnabled(screenAware);
            FeatureManager.SetWorkflowEnabled(workflow);
            AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaUrl, ollamaUrl);
            AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaModel, ollamaModel);
            AiAgentEnvironment.SetUser(AiAgentEnvironment.RemoteApiBaseUrl, remoteApiBaseUrl);
            AiAgentEnvironment.SetUser(AiAgentEnvironment.RemoteApiModel, remoteApiModel);
            if (remoteApiKey != null)
                AiAgentEnvironment.SetUser(AiAgentEnvironment.RemoteApiKey, remoteApiKey);
            AiAgentEnvironment.SetUser(AiAgentEnvironment.GoogleClientId, googleClientId);
            if (googleClientSecret != null)
                AiAgentEnvironment.SetUser(AiAgentEnvironment.GoogleClientSecret, googleClientSecret);
            AiAgentEnvironment.SetUser(AiAgentEnvironment.ScreenAwareInterval, interval);
            AiAgentEnvironment.SetUser(AiAgentEnvironment.VisionModel, visionModel);
            AiAgentEnvironment.SetUser(AiAgentEnvironment.HotkeyPushToTalk, hotkey);
        }).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                pbAiOpenAiKey?.Clear();
                pbAiGoogleClientSecret?.Clear();
                UpdateAiAgentStatus();
                if (showMessage)
                    MessageBoxX.Show("設定已儲存。", "AI Agent");
            });
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private async void TestAiAgent_Click(object sender, RoutedEventArgs e)
    {
        SaveAiAgentSettings(false);
        await RunAiAgentTaskAsync(async token =>
        {
            var response = GetSelectedAiProvider().Equals("remote_api", StringComparison.OrdinalIgnoreCase)
                ? await aiOpenAiClient.GetReplyAsync("請用繁體中文簡短回答：測試成功。", "", "", token)
                : await aiOllamaClient.GetReplyAsync("請用繁體中文簡短回答：測試成功。", "", "", token);
            MessageBoxX.Show(response, "AI 測試");
        });
    }

    private async void DownloadAiOllamaModel_Click(object sender, RoutedEventArgs e)
    {
        SaveAiAgentSettings(false);
        var model = GetSelectedAiOllamaModel();
        if (MessageBoxX.Show($"要現在下載並部署 Ollama 模型 `{model}` 嗎？", "AI Agent", MessageBoxButton.YesNo, MessageBoxIcon.Info) != MessageBoxResult.Yes)
            return;

        await RunAiAgentTaskAsync(async token =>
        {
            var response = await aiOllamaClient.PullModelAsync(model, token);
            MessageBoxX.Show(response, "Ollama 模型");
        }, TimeSpan.FromHours(1));
    }

    private async void RefreshAiModels_Click(object sender, RoutedEventArgs e)
    {
        SaveAiAgentSettings(false);
        await RunAiAgentTaskAsync(async token =>
        {
            var provider = GetSelectedAiProvider();
            var models = provider.Equals("remote_api", StringComparison.OrdinalIgnoreCase)
                ? await aiModelCatalogService.ListRemoteModelsAsync(tbAiRemoteApiBaseUrl.Text.Trim(), AiAgentEnvironment.GetRemoteApiKey(), token)
                : await aiModelCatalogService.ListOllamaModelsAsync(tbAiOllamaUrl.Text.Trim(), token);

            Dispatcher.Invoke(() =>
            {
                if (provider.Equals("remote_api", StringComparison.OrdinalIgnoreCase))
                {
                    ReplaceModelItems(cbAiRemoteApiModel, models, true);
                }
                else
                {
                    ReplaceModelItems(cbAiOllamaModel, models, false);
                    AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaModel, GetSelectedAiOllamaModel());
                }
            });

            MessageBoxX.Show(models.Count == 0 ? "目前沒有取得模型清單，可手動輸入模型名稱。" : $"已取得 {models.Count} 個模型。", "AI Agent");
        });
    }

    private void AiOllamaModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var model = GetSelectedAiOllamaModel();
        if (!string.IsNullOrWhiteSpace(model))
            AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaModel, model);
    }

    private async Task RefreshLocalOllamaModelsAsync(bool saveSelection)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var models = await aiModelCatalogService.ListOllamaModelsAsync(tbAiOllamaUrl.Text.Trim(), cts.Token);
            Dispatcher.Invoke(() =>
            {
                ReplaceModelItems(cbAiOllamaModel, models, false);
                ReplaceModelItems(cbAiVisionModel, models, true);
                if (saveSelection)
                    AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaModel, GetSelectedAiOllamaModel());
            });
        }
        catch
        {
        }
    }

    private async void ConnectAiGoogle_Click(object sender, RoutedEventArgs e)
    {
        SaveAiAgentSettings(false);
        await RunAiAgentTaskAsync(async token =>
        {
            await aiCalendarClient.ConnectAsync(token);
            UpdateAiAgentStatus();
            MessageBoxX.Show("Google 行事曆已連線。", "AI Agent");
        });
    }

    private void AddAiReminder_Click(object sender, RoutedEventArgs e)
    {
        var date = dpAiReminderDate.SelectedDate ?? DateTime.Now.Date;
        var timeText = string.IsNullOrWhiteSpace(tbAiReminderTime.Text)
            ? DateTime.Now.AddMinutes(5).ToString("HH:mm")
            : tbAiReminderTime.Text.Trim();
        var result = aiReminderStore.Create(
            tbAiReminderTitle.Text,
            date.ToString("yyyy-MM-dd") + " " + timeText,
            tbAiReminderNote.Text);

        MessageBoxX.Show(result, "AI Agent");
        tbAiReminderTitle.Clear();
        tbAiReminderNote.Clear();
        RefreshAiAgentData();
    }

    private void DeleteAiReminder_Click(object sender, RoutedEventArgs e)
    {
        if (dgAiReminders.SelectedItem is not LocalReminderInfo reminder)
        {
            MessageBoxX.Show("請先選擇一筆提醒。", "AI Agent");
            return;
        }

        if (MessageBoxX.Show($"確定刪除提醒「{reminder.Title}」嗎？", "AI Agent", MessageBoxButton.YesNo, MessageBoxIcon.Warning) != MessageBoxResult.Yes)
            return;

        aiReminderStore.Delete(reminder.Id);
        RefreshAiAgentData();
    }

    private void BrowseAiShortcut_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Programs and files|*.exe;*.lnk;*.bat;*.cmd;*.ps1;*.*|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        tbAiShortcutPath.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(tbAiShortcutName.Text))
            tbAiShortcutName.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
    }

    private void BrowseAiShortcutFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "選擇要搜尋程式的資料夾"
        };

        var initialDirectory = GetAiShortcutInitialDirectory();
        if (!string.IsNullOrWhiteSpace(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        if (dialog.ShowDialog(this) != true)
            return;

        var shortcuts = ProgramShortcutStore.FindLaunchableFiles(dialog.FolderName);
        if (shortcuts.Count == 0)
        {
            MessageBoxX.Show("這個資料夾中找不到可開啟的程式檔案。", "AI Agent");
            return;
        }

        var shortcut = shortcuts.Count == 1 ? shortcuts[0] : SelectAiShortcutFromFolder(shortcuts);
        if (shortcut == null)
            return;

        tbAiShortcutPath.Text = shortcut.Path;
        if (string.IsNullOrWhiteSpace(tbAiShortcutName.Text))
            tbAiShortcutName.Text = shortcut.Name;
    }

    private string GetAiShortcutInitialDirectory()
    {
        var path = tbAiShortcutPath.Text.Trim();
        if (Directory.Exists(path))
            return path;

        if (File.Exists(path))
            return Path.GetDirectoryName(path) ?? "";

        return "";
    }

    private ProgramShortcutInfo SelectAiShortcutFromFolder(List<ProgramShortcutInfo> shortcuts)
    {
        ProgramShortcutInfo selectedShortcut = null;
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            ItemsSource = shortcuts,
            Margin = new Thickness(12),
            SelectionMode = DataGridSelectionMode.Single
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "名稱", Binding = new System.Windows.Data.Binding(nameof(ProgramShortcutInfo.Name)), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = "路徑", Binding = new System.Windows.Data.Binding(nameof(ProgramShortcutInfo.Path)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.SelectedIndex = 0;

        var dialog = new Window
        {
            Title = "選擇程式",
            Owner = this,
            Width = 720,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 12, 12)
        };
        var okButton = new Button
        {
            Content = "選擇",
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(16, 6, 16, 6)
        };
        var cancelButton = new Button
        {
            Content = "取消",
            IsCancel = true,
            Padding = new Thickness(16, 6, 16, 6)
        };

        okButton.Click += (_, _) =>
        {
            if (grid.SelectedItem is ProgramShortcutInfo shortcut)
            {
                selectedShortcut = shortcut;
                dialog.DialogResult = true;
            }
        };
        grid.MouseDoubleClick += (_, _) => okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);
        root.Children.Add(grid);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        dialog.Content = root;

        return dialog.ShowDialog() == true ? selectedShortcut : null;
    }

    private void SaveAiShortcut_Click(object sender, RoutedEventArgs e)
    {
        var result = aiShortcutStore.AddOrUpdate(tbAiShortcutName.Text, tbAiShortcutPath.Text);
        MessageBoxX.Show(result, "AI Agent");
        tbAiShortcutName.Clear();
        tbAiShortcutPath.Clear();
        RefreshAiAgentData();
    }

    private void DeleteAiShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (dgAiShortcuts.SelectedItem is not ProgramShortcutInfo shortcut)
        {
            MessageBoxX.Show("請先選擇一筆程式捷徑。", "AI Agent");
            return;
        }

        if (MessageBoxX.Show($"確定刪除程式捷徑「{shortcut.Name}」嗎？", "AI Agent", MessageBoxButton.YesNo, MessageBoxIcon.Warning) != MessageBoxResult.Yes)
            return;

        aiShortcutStore.Delete(shortcut.Name);
        RefreshAiAgentData();
    }

    private async Task RunAiAgentTaskAsync(Func<CancellationToken, Task> action, TimeSpan? timeout = null)
    {
        IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(5));
            await action(cts.Token);
        }
        catch (Exception ex)
        {
            MessageBoxX.Show(ex.Message, "AI Agent");
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void RefreshAiAgentData()
    {
        UpdateAiAgentStatus();
        dgAiReminders.ItemsSource = null;
        dgAiReminders.ItemsSource = aiReminderStore.Load();
        dgAiShortcuts.ItemsSource = null;
        dgAiShortcuts.ItemsSource = aiShortcutStore.Load();
    }

    private void UpdateAiAgentStatus()
    {
        var remoteApi = string.IsNullOrWhiteSpace(AiAgentEnvironment.GetRemoteApiKey()) ? "未設定" : "已設定";
        var google = aiCalendarClient.IsConfigured ? "已連線" : "未連線";
        var ollamaAutoStart = AiAgentEnvironment.IsOllamaAutoStartEnabled ? "已啟用" : "已關閉";
        var windowsStartup = mw.Set.StartUPBoot ? "已啟用" : "已關閉";

        tbAiOverviewStatus.Text =
            $"寵物名稱：{mw.Core.Save.Name}\n" +
            $"主人名稱：{mw.GameSavesData.GameSave.HostName}\n" +
            $"Windows 開機啟動：{windowsStartup}\n" +
            $"AI 來源：{AiAgentEnvironment.Provider}\n" +
            $"Ollama 自動啟動：{ollamaAutoStart}\n" +
            $"Remote API Key：{remoteApi}\n" +
            $"Google 行事曆：{google}";

        tbAiGoogleStatus.Text =
            $"連線狀態：{google}\n" +
            "Google OAuth Redirect URI：http://127.0.0.1:53682/";
    }

    private string GetSelectedAiProvider()
    {
        return cbAiProvider.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : "ollama";
    }

    private string GetSelectedAiOllamaModel()
    {
        if (cbAiOllamaModel.SelectedItem is ComboBoxItem item
            && item.Tag is string tag)
            return tag;

        return cbAiOllamaModel.Text.Trim();
    }

    private string GetSelectedAiRemoteApiModel()
    {
        if (cbAiRemoteApiModel.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && string.Equals(cbAiRemoteApiModel.Text, item.Content?.ToString(), StringComparison.Ordinal))
            return tag;

        return cbAiRemoteApiModel.Text.Trim();
    }

    private void SelectAiProvider(string provider)
    {
        foreach (var item in cbAiProvider.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && comboBoxItem.Tag is string tag
                && tag.Equals(provider, StringComparison.OrdinalIgnoreCase))
            {
                cbAiProvider.SelectedItem = comboBoxItem;
                return;
            }
        }

        cbAiProvider.SelectedIndex = 0;
    }

    private void SelectAiOllamaModel(string model)
    {
        foreach (var item in cbAiOllamaModel.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && comboBoxItem.Tag is string tag
                && tag.Equals(model, StringComparison.OrdinalIgnoreCase))
            {
                cbAiOllamaModel.SelectedItem = comboBoxItem;
                return;
            }
        }

        cbAiOllamaModel.Text = model;
    }

    private void SelectAiRemoteApiModel(string model)
    {
        foreach (var item in cbAiRemoteApiModel.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && comboBoxItem.Tag is string tag
                && tag.Equals(model, StringComparison.OrdinalIgnoreCase))
            {
                cbAiRemoteApiModel.SelectedItem = comboBoxItem;
                return;
            }
        }

        cbAiRemoteApiModel.Text = model;
    }

    private static void ReplaceModelItems(ComboBox comboBox, IReadOnlyList<string> models, bool allowSelectedOutsideList)
    {
        var selected = comboBox.Text;
        comboBox.Items.Clear();
        foreach (var model in models)
            comboBox.Items.Add(new ComboBoxItem { Content = model, Tag = model });

        if (models.Count == 0)
        {
            comboBox.Text = allowSelectedOutsideList ? selected : "";
            return;
        }

        var selectedModel = models.FirstOrDefault(model => model.Equals(selected, StringComparison.OrdinalIgnoreCase))
            ?? (allowSelectedOutsideList && !string.IsNullOrWhiteSpace(selected) ? selected : models[0]);
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && comboBoxItem.Tag is string tag
                && tag.Equals(selectedModel, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = comboBoxItem;
                return;
            }
        }

        comboBox.Text = selectedModel;
    }

    private void SlAiScreenInterval_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
    {
    }

    private void RefreshWorkflowList()
    {
        var workflows = new WorkflowStore().Load();
        dgWorkflows.ItemsSource = workflows.Select(w => new WorkflowDisplayItem
        {
            Name = w.Name,
            TriggerTypeDisplay = w.Trigger.Type switch
            {
                WorkflowTriggerType.Screen => "螢幕",
                WorkflowTriggerType.Schedule => "排程",
                WorkflowTriggerType.Input => "輸入",
                _ => w.Trigger.Type.ToString()
            },
            TriggerSummary = w.Trigger.Type switch
            {
                WorkflowTriggerType.Screen => w.Trigger.ScreenKeyword,
                WorkflowTriggerType.Schedule => w.Trigger.ScheduleCron,
                WorkflowTriggerType.Input => w.Trigger.InputKeyword,
                _ => ""
            },
            Enabled = w.Enabled,
            ActionCount = w.Actions.Count,
            Source = w
        }).ToList();
    }

    private void DgWorkflows_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = dgWorkflows.SelectedItem is WorkflowDisplayItem;
        btnEditWorkflow.IsEnabled = hasSelection;
        btnDeleteWorkflow.IsEnabled = hasSelection;
    }

    private void AddWorkflow_Click(object? sender, RoutedEventArgs e)
    {
        var editor = new winWorkflowEditor();
        if (editor.ShowDialog() == true)
        {
            SaveAiAgentSettings(false);
            RefreshWorkflowList();
            ReloadWorkflowEngine();
        }
    }

    private void EditWorkflow_Click(object? sender, RoutedEventArgs e)
    {
        if (dgWorkflows.SelectedItem is WorkflowDisplayItem item && item.Source != null)
        {
            var editor = new winWorkflowEditor(item.Source);
            if (editor.ShowDialog() == true)
            {
                SaveAiAgentSettings(false);
                RefreshWorkflowList();
                ReloadWorkflowEngine();
            }
        }
    }

    private void DeleteWorkflow_Click(object? sender, RoutedEventArgs e)
    {
        if (dgWorkflows.SelectedItem is WorkflowDisplayItem item && item.Source != null)
        {
            var result = MessageBoxX.Show($"確定刪除流程「{item.Name}」？", "刪除流程", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                var store = new WorkflowStore();
                var workflows = store.Load();
                workflows.RemoveAll(w => w.Name == item.Name);
                store.Save(workflows);
                SaveAiAgentSettings(false);
                RefreshWorkflowList();
                ReloadWorkflowEngine();
            }
        }
    }

    private void ReloadWorkflows_Click(object? sender, RoutedEventArgs e)
    {
        RefreshWorkflowList();
        ReloadWorkflowEngine();
    }

    private static void ReloadWorkflowEngine()
    {
        if (Application.Current.MainWindow is MainWindow mw)
        {
            var talkBox = mw.TalkAPI.Find(x => x.APIName == "AI Agent") as AiAgentTalkBox;
            talkBox?.ReloadWorkflows();
        }
    }

    private void SampleWorkflow_Click(object? sender, RoutedEventArgs e)
    {
        var store = new WorkflowStore();
        var workflows = store.Load();
        var anyAdded = false;

        if (!workflows.Any(w => w.Name.Equals("休息提醒", StringComparison.OrdinalIgnoreCase)))
        {
            workflows.Add(new WorkflowDefinition
            {
                Name = "休息提醒",
                Description = "每2小時提醒休息",
                Enabled = true,
                Trigger = new WorkflowTrigger
                {
                    Type = WorkflowTriggerType.Schedule,
                    ScheduleCron = "0 */2 * * *"
                },
                Actions = new List<WorkflowAction>
                {
                    new() { Type = WorkflowActionType.SendMessage, Message = "已經專心兩小時了，起來走走喝杯水吧！" }
                }
            });
            anyAdded = true;
        }

        if (!workflows.Any(w => w.Name.Equals("開始寫程式", StringComparison.OrdinalIgnoreCase)))
        {
            workflows.Add(new WorkflowDefinition
            {
                Name = "開始寫程式",
                Description = "說「開始寫程式」→ 開 IDE + 啟動番茄鐘 + 桌寵鼓勵",
                Enabled = true,
                Trigger = new WorkflowTrigger
                {
                    Type = WorkflowTriggerType.Input,
                    InputKeyword = "開始寫程式"
                },
                Actions = new List<WorkflowAction>
                {
                    new() { Type = WorkflowActionType.LaunchProgram, ProgramName = "notepad" },
                    new() { Type = WorkflowActionType.StartPomodoro, PomodoroMinutes = 25 },
                    new() { Type = WorkflowActionType.SendMessage, Message = "好喔，一起專心寫程式吧！我幫你開了編輯器也啟動番茄鐘了～" }
                }
            });
            anyAdded = true;
        }

        if (!workflows.Any(w => w.Name.Equals("螢幕偵測瀏覽器", StringComparison.OrdinalIgnoreCase)))
        {
            workflows.Add(new WorkflowDefinition
            {
                Name = "螢幕偵測瀏覽器",
                Description = "螢幕分析出現「瀏覽器」時通知",
                Enabled = true,
                Trigger = new WorkflowTrigger
                {
                    Type = WorkflowTriggerType.Screen,
                    ScreenKeyword = "瀏覽器"
                },
                Actions = new List<WorkflowAction>
                {
                    new() { Type = WorkflowActionType.ShowNotification, NotificationTitle = "螢幕偵測", NotificationBody = "桌寵偵測到您正在使用瀏覽器" }
                }
            });
            anyAdded = true;
        }

        store.Save(workflows);
        RefreshWorkflowList();
        ReloadWorkflowEngine();

        if (anyAdded)
            MessageBoxX.Show("已新增範例流程（休息提醒、開始寫程式、螢幕偵測瀏覽器）。\n可編輯後調整觸發條件與動作。", "新增範例");
        else
            MessageBoxX.Show("所有範例流程已存在，無需重複新增。", "新增範例");
    }

    private async void ImportOllamaModel_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "GGUF 模型 (*.gguf)|*.gguf|所有檔案 (*.*)|*.*",
            Title = "選擇 GGUF 模型檔案"
        };
        if (dialog.ShowDialog() == true)
        {
            var filePath = dialog.FileName;
            var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            var mw = Application.Current.MainWindow as MainWindow;
            if (mw == null) return;
            var inputBox = new winInputBox(mw, "匯入模型", $"將從檔案建立 Ollama 模型：\n{filePath}\n\n請輸入模型名稱：", fileName, false, false, false);
            if (inputBox.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputBox.TextBoxInput.Text))
            {
                var modelName = inputBox.TextBoxInput.Text.Trim();
                await RunAiAgentTaskAsync(async token =>
                {
                    var modelfile = $"FROM {filePath}";
                    var tempFile = System.IO.Path.GetTempFileName();
                    System.IO.File.WriteAllText(tempFile, modelfile);
                    var psi = new ProcessStartInfo
                    {
                        FileName = "ollama",
                        Arguments = $"create {modelName} -f \"{tempFile}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var output = await proc.StandardOutput.ReadToEndAsync();
                        var error = await proc.StandardError.ReadToEndAsync();
                        proc.WaitForExit();
                        System.IO.File.Delete(tempFile);
                        Dispatcher.Invoke(() =>
                        {
                            if (proc.ExitCode == 0)
                            {
                                MessageBoxX.Show($"模型「{modelName}」匯入成功。", "匯入模型");
                                _ = RefreshLocalOllamaModelsAsync(saveSelection: false);
                            }
                            else
                            {
                                MessageBoxX.Show($"匯入失敗：{error}", "匯入模型");
                            }
                        });
                    }
                });
            }
        }
    }

    private async void ImportVisionModel_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "GGUF 模型 (*.gguf)|*.gguf|所有檔案 (*.*)|*.*",
            Title = "選擇 GGUF 模型檔案"
        };
        if (dialog.ShowDialog() == true)
        {
            var filePath = dialog.FileName;
            var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            var mw = Application.Current.MainWindow as MainWindow;
            if (mw == null) return;
            var inputBox = new winInputBox(mw, "匯入模型", $"將從檔案建立 Ollama 模型：\n{filePath}\n\n請輸入模型名稱：", fileName, false, false, false);
            if (inputBox.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputBox.TextBoxInput.Text))
            {
                var modelName = inputBox.TextBoxInput.Text.Trim();
                await RunAiAgentTaskAsync(async token =>
                {
                    var modelfile = $"FROM {filePath}";
                    var tempFile = System.IO.Path.GetTempFileName();
                    System.IO.File.WriteAllText(tempFile, modelfile);
                    var psi = new ProcessStartInfo
                    {
                        FileName = "ollama",
                        Arguments = $"create {modelName} -f \"{tempFile}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var output = await proc.StandardOutput.ReadToEndAsync();
                        var error = await proc.StandardError.ReadToEndAsync();
                        proc.WaitForExit();
                        System.IO.File.Delete(tempFile);
                        Dispatcher.Invoke(() =>
                        {
                            if (proc.ExitCode == 0)
                            {
                                cbAiVisionModel.Text = modelName;
                                MessageBoxX.Show($"模型「{modelName}」匯入成功。", "匯入模型");
                            }
                            else
                            {
                                MessageBoxX.Show($"匯入失敗：{error}", "匯入模型");
                            }
                        });
                    }
                });
            }
        }
    }

    private static bool IsDiyHotkey(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;
        if (content.Contains('\\') || content.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return false;
        if (content.Contains('{') && content.Contains('}'))
            return true;
        if (content.StartsWith('^') || content.StartsWith('%') || content.StartsWith('+'))
            return true;
        return false;
    }

    private void RefreshDiyHotkeys()
    {
        var items = new List<string>();
        foreach (LinePutScript.Sub sub in mw.Set["diy"])
        {
            if (IsDiyHotkey(sub.Info))
                items.Add($"{sub.Name}: {sub.Info}");
        }
        lbDiyHotkeys.ItemsSource = items;
        lbDiyHotkeys.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnSaveHotkey_Click(object? sender, RoutedEventArgs e)
    {
        AiAgentEnvironment.SetUser(AiAgentEnvironment.HotkeyPushToTalk, tbAiHotkeyPushToTalk.Text.Trim());
        var hotkey = tbAiHotkeyPushToTalk.Text.Trim();
        tbAiHotkeyStatus.Text = string.IsNullOrWhiteSpace(hotkey)
            ? "無快捷鍵設定"
            : $"目前註冊：{hotkey}";
        MessageBoxX.Show("快捷鍵設定已儲存。", "快捷鍵");
    }

    private void BtnDetectHotkey_Click(object? sender, RoutedEventArgs e)
    {
        if (isCapturingHotkey)
        {
            isCapturingHotkey = false;
            btnDetectHotkey.Content = "偵測";
            tbAiHotkeyPushToTalk.Text = savedHotkeyBeforeCapture;
            return;
        }

        savedHotkeyBeforeCapture = tbAiHotkeyPushToTalk.Text;
        isCapturingHotkey = true;
        btnDetectHotkey.Content = "取消";
        tbAiHotkeyPushToTalk.Text = "請按下快捷鍵...";
        tbAiHotkeyPushToTalk.Focus();
        e.Handled = true;
    }

    private void TbAiHotkeyPushToTalk_PreviewKeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!isCapturingHotkey) return;

        e.Handled = true;

        if (e.Key == System.Windows.Input.Key.Escape)
        {
            isCapturingHotkey = false;
            btnDetectHotkey.Content = "偵測";
            tbAiHotkeyPushToTalk.Text = savedHotkeyBeforeCapture;
            return;
        }

        var modifiers = AiAgent.HotkeyService.ModifierKeys.None;
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            modifiers |= AiAgent.HotkeyService.ModifierKeys.Control;
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) != 0)
            modifiers |= AiAgent.HotkeyService.ModifierKeys.Alt;
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0)
            modifiers |= AiAgent.HotkeyService.ModifierKeys.Shift;
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Windows) != 0)
            modifiers |= AiAgent.HotkeyService.ModifierKeys.Windows;

        AiAgent.HotkeyService.Keys hotkeyKey;
        if (e.Key >= System.Windows.Input.Key.A && e.Key <= System.Windows.Input.Key.Z)
            hotkeyKey = AiAgent.HotkeyService.Keys.A + (int)(e.Key - System.Windows.Input.Key.A);
        else if (e.Key >= System.Windows.Input.Key.F1 && e.Key <= System.Windows.Input.Key.F12)
            hotkeyKey = (AiAgent.HotkeyService.Keys)((int)e.Key);
        else
            return;

        if (modifiers == AiAgent.HotkeyService.ModifierKeys.None)
        {
            tbAiHotkeyPushToTalk.Text = "請至少按住一個修飾鍵 (Ctrl/Alt/Shift/Win)";
            return;
        }

        tbAiHotkeyPushToTalk.Text = AiAgent.HotkeyService.Format(modifiers, hotkeyKey);
        isCapturingHotkey = false;
        btnDetectHotkey.Content = "偵測";
    }
}
