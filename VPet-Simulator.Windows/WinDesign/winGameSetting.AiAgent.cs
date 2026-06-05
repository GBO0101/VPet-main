using System;
using System.Collections.Generic;
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
        RefreshAiAgentData();
    }

    private void SaveAiAgentSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveAiAgentSettings(true);
    }

    private void SaveAiAgentSettings(bool showMessage)
    {
        AiAgentEnvironment.SetUser(AiAgentEnvironment.AiProvider, GetSelectedAiProvider());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaAutoStart, cbAiOllamaAutoStart.IsChecked == false ? "false" : "true");
        AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaUrl, tbAiOllamaUrl.Text.Trim());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaModel, GetSelectedAiOllamaModel());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.RemoteApiBaseUrl, tbAiRemoteApiBaseUrl.Text.Trim());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.RemoteApiModel, GetSelectedAiRemoteApiModel());

        if (!string.IsNullOrWhiteSpace(pbAiOpenAiKey.Password))
            AiAgentEnvironment.SetUser(AiAgentEnvironment.RemoteApiKey, pbAiOpenAiKey.Password);

        AiAgentEnvironment.SetUser(AiAgentEnvironment.GoogleClientId, tbAiGoogleClientId.Text.Trim());

        if (!string.IsNullOrWhiteSpace(pbAiGoogleClientSecret.Password))
            AiAgentEnvironment.SetUser(AiAgentEnvironment.GoogleClientSecret, pbAiGoogleClientSecret.Password);

        pbAiOpenAiKey.Clear();
        pbAiGoogleClientSecret.Clear();
        UpdateAiAgentStatus();
        if (showMessage)
            MessageBoxX.Show("設定已儲存。", "AI Agent");
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
}
