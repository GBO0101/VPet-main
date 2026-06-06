using Panuon.WPF.UI;
using System.Windows;
using System.Windows.Controls;
using VPet_Simulator.Windows.AiAgent;

namespace VPet_Simulator.Windows;

public partial class winActionEditor : WindowX
{
    private readonly WorkflowAction? existing;

    public WorkflowAction? Result { get; private set; }

    public winActionEditor(WorkflowAction? existing = null)
    {
        InitializeComponent();
        this.existing = existing;
        cbActionType.SelectedIndex = 0;

        if (existing != null)
        {
            Title = "編輯動作";
            switch (existing.Type)
            {
                case WorkflowActionType.LaunchProgram:
                    cbActionType.SelectedIndex = 0;
                    tbActionProgram.Text = existing.ProgramName;
                    break;
                case WorkflowActionType.StartPomodoro:
                    cbActionType.SelectedIndex = 1;
                    numActionPomodoro.Value = existing.PomodoroMinutes;
                    break;
                case WorkflowActionType.SendMessage:
                    cbActionType.SelectedIndex = 2;
                    tbActionMessage.Text = existing.Message;
                    break;
                case WorkflowActionType.Wait:
                    cbActionType.SelectedIndex = 3;
                    numActionWait.Value = existing.DelaySeconds;
                    break;
                case WorkflowActionType.ShowNotification:
                    cbActionType.SelectedIndex = 4;
                    tbActionNotifTitle.Text = existing.NotificationTitle;
                    tbActionNotifBody.Text = existing.NotificationBody;
                    break;
            }
        }
        else
        {
            Title = "新增動作";
        }

        UpdatePanels();
    }

    private void UpdatePanels()
    {
        panelProgram.Visibility = Visibility.Collapsed;
        panelPomodoro.Visibility = Visibility.Collapsed;
        panelMessage.Visibility = Visibility.Collapsed;
        panelWait.Visibility = Visibility.Collapsed;
        panelNotification.Visibility = Visibility.Collapsed;

        switch (cbActionType.SelectedIndex)
        {
            case 0: panelProgram.Visibility = Visibility.Visible; break;
            case 1: panelPomodoro.Visibility = Visibility.Visible; break;
            case 2: panelMessage.Visibility = Visibility.Visible; break;
            case 3: panelWait.Visibility = Visibility.Visible; break;
            case 4: panelNotification.Visibility = Visibility.Visible; break;
        }
    }

    private void ActionType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdatePanels();
    }

    private void ActionOK_Click(object? sender, RoutedEventArgs e)
    {
        var action = new WorkflowAction();
        switch (cbActionType.SelectedIndex)
        {
            case 0:
                action.Type = WorkflowActionType.LaunchProgram;
                action.ProgramName = tbActionProgram.Text.Trim();
                if (string.IsNullOrWhiteSpace(action.ProgramName))
                {
                    MessageBoxX.Show("請輸入程式名稱。", "提示");
                    return;
                }
                break;
            case 1:
                action.Type = WorkflowActionType.StartPomodoro;
                action.PomodoroMinutes = (int)numActionPomodoro.Value;
                break;
            case 2:
                action.Type = WorkflowActionType.SendMessage;
                action.Message = tbActionMessage.Text.Trim();
                if (string.IsNullOrWhiteSpace(action.Message))
                {
                    MessageBoxX.Show("請輸入訊息內容。", "提示");
                    return;
                }
                break;
            case 3:
                action.Type = WorkflowActionType.Wait;
                action.DelaySeconds = (int)numActionWait.Value;
                break;
            case 4:
                action.Type = WorkflowActionType.ShowNotification;
                action.NotificationTitle = tbActionNotifTitle.Text.Trim();
                action.NotificationBody = tbActionNotifBody.Text.Trim();
                if (string.IsNullOrWhiteSpace(action.NotificationBody))
                {
                    MessageBoxX.Show("請輸入通知內容。", "提示");
                    return;
                }
                break;
        }

        Result = action;
        DialogResult = true;
        Close();
    }

    private void ActionCancel_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BrowseFile_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇要執行的程式或檔案",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
            tbActionProgram.Text = dialog.FileName;
    }
}
