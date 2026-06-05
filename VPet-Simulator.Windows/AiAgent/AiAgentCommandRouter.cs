using System;
using System.Collections.Generic;
using System.Linq;
using LinePutScript;
using Steamworks;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.AiAgent;

internal static class AiAgentCommandRouter
{
    public static bool TryHandle(IMainWindow mw, string text, out string response, PomodoroService? pomodoroService = null)
    {
        response = "";
        var input = Normalize(text);
        if (string.IsNullOrWhiteSpace(input))
            return false;

        if (TryOpenDiyLink(mw, text, out response))
            return true;

        if (AiAgentSettingCommandParser.TryParse(text, out var settingCommand))
        {
            response = mw == null
                ? "桌寵設定服務尚未啟動。"
                : mw.Dispatcher.Invoke(() => ApplySettingCommand(mw, settingCommand, pomodoroService));
            return true;
        }

        if (ContainsAny(input, "\u505c\u6b62\u756a\u8304\u9418", "\u53d6\u6d88\u756a\u8304\u9418", "\u7d50\u675f\u756a\u8304\u9418", "stop pomodoro"))
        {
            response = pomodoroService?.Stop() ?? "\u756a\u8304\u9418\u670d\u52d9\u5c1a\u672a\u555f\u52d5\u3002";
            return true;
        }

        if (ContainsAny(input, "\u66ab\u505c\u756a\u8304\u9418", "\u756a\u8304\u9418\u66ab\u505c", "\u6682\u505c\u756a\u8304\u949f", "\u756a\u8304\u949f\u6682\u505c", "pause pomodoro"))
        {
            response = pomodoroService?.Pause() ?? "\u756a\u8304\u9418\u670d\u52d9\u5c1a\u672a\u555f\u52d5\u3002";
            return true;
        }

        if (ContainsAny(input, "\u7e7c\u7e8c\u756a\u8304\u9418", "\u7ee7\u7eed\u756a\u8304\u949f", "\u6062\u5fa9\u756a\u8304\u9418", "\u6062\u590d\u756a\u8304\u949f", "resume pomodoro", "continue pomodoro"))
        {
            response = pomodoroService?.Resume() ?? "\u756a\u8304\u9418\u670d\u52d9\u5c1a\u672a\u555f\u52d5\u3002";
            return true;
        }

        if ((ContainsAny(input, "\u756a\u8304\u9418", "\u756a\u8304\u949f", "pomodoro")
                && ContainsAny(input, "\u9084\u5269", "\u8fd8\u5269", "\u5269\u591a\u4e45", "\u5269\u9918", "\u5269\u4f59", "\u591a\u4e45", "\u72c0\u614b", "\u72b6\u6001", "status", "remaining"))
            || ContainsAny(input, "\u9084\u8981\u5c08\u6ce8\u591a\u4e45", "\u8fd8\u8981\u4e13\u6ce8\u591a\u4e45", "\u9084\u5269\u591a\u4e45\u8981\u5c08\u6ce8", "\u8fd8\u5269\u591a\u4e45\u8981\u4e13\u6ce8", "\u5269\u591a\u4e45\u8981\u5c08\u6ce8", "\u5269\u591a\u4e45\u8981\u4e13\u6ce8", "\u5c08\u6ce8\u5269\u591a\u4e45", "\u4e13\u6ce8\u5269\u591a\u4e45", "\u9084\u8981\u4f11\u606f\u591a\u4e45", "\u8fd8\u8981\u4f11\u606f\u591a\u4e45", "\u9084\u5269\u591a\u4e45\u8981\u4f11\u606f", "\u8fd8\u5269\u591a\u4e45\u8981\u4f11\u606f", "\u5269\u591a\u4e45\u8981\u4f11\u606f", "\u4f11\u606f\u5269\u591a\u4e45"))
        {
            response = pomodoroService == null
                ? "\u756a\u8304\u9418\u670d\u52d9\u5c1a\u672a\u555f\u52d5\u3002"
                : "\u756a\u8304\u9418\u72c0\u614b\uff1a" + pomodoroService.BuildStatusText();
            return true;
        }

        if (ContainsAny(input, "\u756a\u8304\u9418", "\u756a\u8304\u949f", "pomodoro"))
        {
            response = pomodoroService?.Start() ?? "\u756a\u8304\u9418\u670d\u52d9\u5c1a\u672a\u555f\u52d5\u3002";
            return true;
        }

        if (ContainsAny(input, "\u9192\u4f86", "\u9192\u6765", "\u8d77\u5e8a", "\u4e0d\u8981\u7761", "\u5225\u7761", "\u522b\u7761", "\u505c\u6b62\u7761", "wake up"))
        {
            response = mw.Dispatcher.Invoke(() => WakeOrStop(mw));
            return true;
        }

        if (ContainsAny(input, "\u53bb\u7761", "\u7761\u89ba\u5427", "\u7761\u89c9\u5427", "\u7761\u5427", "\u53bb\u4f11\u606f", "\u4f11\u606f\u4e00\u4e0b", "go sleep", "go to sleep"))
        {
            response = mw.Dispatcher.Invoke(() => Sleep(mw));
            return true;
        }

        if (ContainsAny(input, "\u505c\u6b62\u5de5\u4f5c", "\u505c\u6b62\u5b78\u7fd2", "\u505c\u6b62\u5b66\u4e60", "\u505c\u4e0b", "\u505c\u4e0b\u4f86", "\u505c\u4e0b\u6765", "stop working"))
        {
            response = mw.Dispatcher.Invoke(() => WakeOrStop(mw));
            return true;
        }

        if (ContainsAny(input, "\u6478\u982d", "\u6478\u5934", "\u6478\u6478\u982d", "\u6478\u6478\u5934", "pat head", "touch head"))
        {
            response = mw.Dispatcher.Invoke(() =>
            {
                mw.Main.DisplayTouchHead();
                return "\u6478\u6478\u982d\u6642\u9593\u5230\u3002";
            });
            return true;
        }

        if (ContainsAny(input, "\u6478\u8eab\u9ad4", "\u6478\u8eab\u4f53", "touch body"))
        {
            response = mw.Dispatcher.Invoke(() =>
            {
                mw.Main.DisplayTouchBody();
                return "\u597d\u5566\uff0c\u8f15\u4e00\u9ede\u55b5\u3002";
            });
            return true;
        }

        if (ContainsAny(input, "\u53bb\u5de5\u4f5c", "\u958b\u59cb\u5de5\u4f5c", "\u5f00\u59cb\u5de5\u4f5c", "\u53bb\u6253\u5de5", "\u6253\u5de5", "work now"))
        {
            response = mw.Dispatcher.Invoke(() => StartFirstWork(mw, GraphHelper.Work.WorkType.Work, "\u958b\u59cb\u5de5\u4f5c\u4e86\u55b5\u3002"));
            return true;
        }

        if (ContainsAny(input, "\u53bb\u5b78\u7fd2", "\u53bb\u5b66\u4e60", "\u958b\u59cb\u5b78\u7fd2", "\u5f00\u59cb\u5b66\u4e60", "\u8b80\u66f8", "\u8bfb\u4e66", "study now"))
        {
            response = mw.Dispatcher.Invoke(() => StartFirstWork(mw, GraphHelper.Work.WorkType.Study, "\u958b\u59cb\u5b78\u7fd2\u4e86\u55b5\u3002"));
            return true;
        }

        if (ContainsAny(input, "\u53bb\u73a9", "\u73a9\u800d", "\u73a9\u4e00\u4e0b", "play now"))
        {
            response = mw.Dispatcher.Invoke(() => StartFirstWork(mw, GraphHelper.Work.WorkType.Play, "\u53bb\u73a9\u4e00\u4e0b\u55b5\u3002"));
            return true;
        }

        if (ContainsAny(input, "\u5403\u98ef", "\u5403\u996d", "\u5403\u6771\u897f", "\u5403\u4e1c\u897f", "\u8cb7\u98df\u7269", "\u4e70\u98df\u7269"))
        {
            response = mw.Dispatcher.Invoke(() =>
            {
                mw.ShowBetterBuy(Food.FoodType.Food);
                return "\u6211\u6253\u958b\u98df\u7269\u9801\u4e86\u55b5\u3002";
            });
            return true;
        }

        if (ContainsAny(input, "\u559d\u6c34", "\u559d\u98f2\u6599", "\u559d\u996e\u6599", "\u8cb7\u98f2\u6599", "\u4e70\u996e\u6599", "\u53e3\u6e34"))
        {
            response = mw.Dispatcher.Invoke(() =>
            {
                mw.ShowBetterBuy(Food.FoodType.Drink);
                return "\u6211\u6253\u958b\u98f2\u6599\u9801\u4e86\u55b5\u3002";
            });
            return true;
        }

        return false;
    }

    private static string Sleep(IMainWindow mw)
    {
        var main = mw.Main;
        if (main.State == Main.WorkingState.Sleep)
            return "\u6211\u5df2\u7d93\u5728\u7761\u89ba\u4e86\u55b5\u3002";

        if (main.State == Main.WorkingState.Work && main.NowWork != null)
            main.WorkTimer.Stop(() => main.DisplaySleep(true), WorkTimer.FinishWorkInfo.StopReason.MenualStop);
        else
            main.DisplaySleep(true);

        return "\u597d\u5594\uff0c\u6211\u53bb\u7761\u89ba\u4e86\u55b5\u3002";
    }

    private static string WakeOrStop(IMainWindow mw)
    {
        var main = mw.Main;
        if (main.State == Main.WorkingState.Sleep)
        {
            if (main.Core.Save.Mode == IGameSave.ModeType.Ill)
                return "\u6211\u73fe\u5728\u4e0d\u592a\u8212\u670d\uff0c\u5148\u8b93\u6211\u8eba\u4e00\u4e0b\u55b5\u3002";

            main.State = Main.WorkingState.Nomal;
            main.Display(GraphInfo.GraphType.Sleep, GraphInfo.AnimatType.C_End, main.DisplayNomal);
            return "\u9192\u4f86\u4e86\u55b5\u3002";
        }

        if (main.State == Main.WorkingState.Work && main.NowWork != null)
        {
            main.WorkTimer.Stop(reason: WorkTimer.FinishWorkInfo.StopReason.MenualStop);
            return "\u6211\u5148\u505c\u4e0b\u4f86\u4e86\u55b5\u3002";
        }

        main.DisplayToNomal();
        return "\u6211\u73fe\u5728\u6c92\u6709\u5728\u5fd9\u55b5\u3002";
    }

    private static string StartFirstWork(IMainWindow mw, GraphHelper.Work.WorkType type, string okText)
    {
        mw.Main.WorkList(out var works, out var studies, out var plays);
        var work = type switch
        {
            GraphHelper.Work.WorkType.Study => studies.FirstOrDefault(),
            GraphHelper.Work.WorkType.Play => plays.FirstOrDefault(),
            _ => works.FirstOrDefault()
        };

        if (work == null)
            return "\u76ee\u524d\u627e\u4e0d\u5230\u53ef\u4ee5\u57f7\u884c\u7684\u9805\u76ee\u55b5\u3002";

        return mw.Main.StartWork(work) ? okText : "\u9019\u500b\u9805\u76ee\u73fe\u5728\u9084\u4e0d\u80fd\u958b\u59cb\u55b5\u3002";
    }

    private static string ApplySettingCommand(IMainWindow mw, AiAgentSettingCommand command, PomodoroService? pomodoroService)
    {
        return command.Type switch
        {
            AiAgentSettingCommandType.PomodoroFocusMinutes => SetPomodoroMinutes(mw, command.FocusMinutes, null, pomodoroService),
            AiAgentSettingCommandType.PomodoroBreakMinutes => SetPomodoroMinutes(mw, null, command.BreakMinutes, pomodoroService),
            AiAgentSettingCommandType.PomodoroDurations => SetPomodoroMinutes(mw, command.FocusMinutes, command.BreakMinutes, pomodoroService),
            AiAgentSettingCommandType.PetName => SetPetName(mw, command.Name),
            AiAgentSettingCommandType.OwnerName => SetOwnerName(mw, command.Name),
            _ => "我還不會設定這個項目。"
        };
    }

    private static string SetPomodoroMinutes(IMainWindow mw, int? focusMinutes, int? breakMinutes, PomodoroService? pomodoroService)
    {
        if (focusMinutes is < 1 or > 120)
            return "專注時間請設定在 1 到 120 分鐘之間。";
        if (breakMinutes is < 1 or > 60)
            return "休息時間請設定在 1 到 60 分鐘之間。";

        if (focusMinutes.HasValue)
            mw.Set.PomodoroFocusMinutes = focusMinutes.Value;
        if (breakMinutes.HasValue)
            mw.Set.PomodoroBreakMinutes = breakMinutes.Value;

        var suffix = pomodoroService?.IsRunning == true || pomodoroService?.IsPaused == true
            ? "目前這輪不會重算，下一次番茄鐘會使用新的時間。"
            : "";
        return $"番茄鐘已設定：專注 {mw.Set.PomodoroFocusMinutes} 分鐘，休息 {mw.Set.PomodoroBreakMinutes} 分鐘。{suffix}";
    }

    private static string SetPetName(IMainWindow mw, string name)
    {
        mw.Core.Save.Name = name;
        mw.GameSavesData.GameSave.Name = name;
        if (mw.IsSteamUser)
            SteamFriends.SetRichPresence("username", mw.Core.Save.Name);

        return $"桌寵名稱已改成「{name}」。";
    }

    private static string SetOwnerName(IMainWindow mw, string name)
    {
        mw.Core.Save.HostName = name;
        mw.GameSavesData.GameSave.HostName = name;
        return $"主人稱呼已改成「{name}」。";
    }

    private static bool TryOpenDiyLink(IMainWindow mw, string text, out string response)
    {
        response = "";
        if (mw is not MainWindow mainWindow)
            return false;

        var links = new List<(string Name, string Content)>();
        foreach (ISub sub in mainWindow.Set["diy"])
            links.Add((sub.Name, sub.Info));

        if (!AiAgentDiyLinkMatcher.TryFindTarget(text, links, out var name, out var content))
            return false;

        response = mw.Dispatcher.Invoke(() =>
        {
            mainWindow.RunDIY(content);
            return "已開啟自訂連結：" + name;
        });
        return true;
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(Normalize(value), StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value)
    {
        return (value ?? "").Trim().ToLowerInvariant();
    }
}
