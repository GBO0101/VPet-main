using Panuon.WPF.UI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VPet_Simulator.Windows.AiAgent;

namespace VPet_Simulator.Windows;

public partial class winWorkflowEditor : WindowX
{
    private readonly WorkflowDefinition? existing;
    private readonly ObservableCollection<ActionDisplayItem> actions = new();

    internal class ActionDisplayItem
    {
        public string Type { get; set; } = "";
        public string Summary { get; set; } = "";
        public WorkflowAction? Source { get; set; }
    }

    public winWorkflowEditor(WorkflowDefinition? existing = null)
    {
        InitializeComponent();
        this.existing = existing;
        cbEditorTriggerType.SelectedIndex = 0;

        if (existing != null)
        {
            Title = "編輯流程";
            tbEditorName.Text = existing.Name;
            cbEditorEnabled.IsChecked = existing.Enabled;

            switch (existing.Trigger.Type)
            {
                case WorkflowTriggerType.Screen:
                    cbEditorTriggerType.SelectedIndex = 0;
                    tbEditorTriggerParam.Text = existing.Trigger.ScreenKeyword;
                    break;
                case WorkflowTriggerType.Schedule:
                    cbEditorTriggerType.SelectedIndex = 1;
                    tbEditorTriggerParam.Text = existing.Trigger.ScheduleCron;
                    break;
                case WorkflowTriggerType.Input:
                    cbEditorTriggerType.SelectedIndex = 2;
                    tbEditorTriggerParam.Text = existing.Trigger.InputKeyword;
                    break;
            }

            foreach (var a in existing.Actions)
                actions.Add(new ActionDisplayItem
                {
                    Type = ActionTypeLabel(a.Type),
                    Summary = ActionSummary(a),
                    Source = a
                });
        }
        else
        {
            Title = "新增流程";
        }

        dgEditorActions.ItemsSource = actions;
        UpdateTriggerParamHint();
    }

    private void UpdateTriggerParamHint()
    {
        tbEditorTriggerParam.ToolTip = cbEditorTriggerType.SelectedIndex switch
        {
            0 => "螢幕分析關鍵字，例如：瀏覽器",
            1 => "Cron 表達式，例如：*/30 * * * *（每30分鐘）",
            2 => "輸入關鍵字，例如：天氣",
            _ => ""
        };
    }

    private void EditorTriggerType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateTriggerParamHint();
    }

    private void DgEditorActions_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = dgEditorActions.SelectedItem is ActionDisplayItem;
        btnEditAction.IsEnabled = hasSelection;
        btnDeleteAction.IsEnabled = hasSelection;
    }

    private void AddAction_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new winActionEditor();
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            actions.Add(new ActionDisplayItem
            {
                Type = ActionTypeLabel(dialog.Result.Type),
                Summary = ActionSummary(dialog.Result),
                Source = dialog.Result
            });
        }
    }

    private void EditAction_Click(object? sender, RoutedEventArgs e)
    {
        if (dgEditorActions.SelectedItem is ActionDisplayItem item && item.Source != null)
        {
            var dialog = new winActionEditor(item.Source);
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                item.Type = ActionTypeLabel(dialog.Result.Type);
                item.Summary = ActionSummary(dialog.Result);
                item.Source = dialog.Result;
                dgEditorActions.Items.Refresh();
            }
        }
    }

    private void DeleteAction_Click(object? sender, RoutedEventArgs e)
    {
        if (dgEditorActions.SelectedItem is ActionDisplayItem item)
        {
            actions.Remove(item);
        }
    }

    private void EditorOK_Click(object? sender, RoutedEventArgs e)
    {
        var name = tbEditorName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBoxX.Show("請輸入流程名稱。", "提示");
            return;
        }

        var trigger = new WorkflowTrigger();
        switch (cbEditorTriggerType.SelectedIndex)
        {
            case 0:
                trigger.Type = WorkflowTriggerType.Screen;
                trigger.ScreenKeyword = tbEditorTriggerParam.Text.Trim();
                break;
            case 1:
                trigger.Type = WorkflowTriggerType.Schedule;
                trigger.ScheduleCron = tbEditorTriggerParam.Text.Trim();
                break;
            case 2:
                trigger.Type = WorkflowTriggerType.Input;
                trigger.InputKeyword = tbEditorTriggerParam.Text.Trim();
                break;
        }

        var workflow = new WorkflowDefinition
        {
            Name = name,
            Description = "",
            Enabled = cbEditorEnabled.IsChecked == true,
            Trigger = trigger,
            Actions = actions.Select(a => a.Source!).ToList()
        };

        var store = new WorkflowStore();
        var workflows = store.Load();

        if (existing != null)
        {
            var idx = workflows.FindIndex(w => w.Name == existing.Name);
            if (idx >= 0)
                workflows[idx] = workflow;
            else
                workflows.Add(workflow);
        }
        else
        {
            if (workflows.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBoxX.Show("已有相同名稱的流程。", "提示");
                return;
            }
            workflows.Add(workflow);
        }

        store.Save(workflows);
        DialogResult = true;
        Close();
    }

    private void EditorCancel_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string ActionTypeLabel(WorkflowActionType type) => type switch
    {
        WorkflowActionType.LaunchProgram => "啟動程式",
        WorkflowActionType.StartPomodoro => "番茄鐘",
        WorkflowActionType.SendMessage => "發送訊息",
        WorkflowActionType.Wait => "等待",
        WorkflowActionType.ShowNotification => "系統通知",
        _ => type.ToString()
    };

    private static string ActionSummary(WorkflowAction a) => a.Type switch
    {
        WorkflowActionType.LaunchProgram => a.ProgramName,
        WorkflowActionType.StartPomodoro => $"{a.PomodoroMinutes} 分鐘",
        WorkflowActionType.SendMessage => a.Message,
        WorkflowActionType.Wait => $"{a.DelaySeconds} 秒",
        WorkflowActionType.ShowNotification => $"{a.NotificationTitle} - {a.NotificationBody}",
        _ => ""
    };
}
