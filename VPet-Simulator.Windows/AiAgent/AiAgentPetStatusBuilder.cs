using System;
using System.Globalization;
using System.Text;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class AiAgentPetStatusBuilder
{
    private readonly IMainWindow mw;

    public AiAgentPetStatusBuilder(IMainWindow mw)
    {
        this.mw = mw;
    }

    public string BuildStatusSummary()
    {
        return mw.Dispatcher.Invoke(() =>
        {
            var save = mw.Core.Save;
            var main = mw.Main;
            var builder = new StringBuilder();
            builder.AppendLine("Current desktop pet status:");
            builder.AppendLine($"time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"pet_name: {save.Name}");
            builder.AppendLine($"owner_name: {save.HostName}");
            builder.AppendLine($"mode: {save.Mode}");
            builder.AppendLine($"state: {main.State}");
            builder.AppendLine($"animation: {main.DisplayType.Name} ({main.DisplayType.Type})");
            builder.AppendLine($"level: {save.Level}");
            builder.AppendLine($"exp: {Format(save.Exp)} / next {save.LevelUpNeed()}");
            builder.AppendLine($"money: {Format(save.Money)}");
            builder.AppendLine($"strength: {Format(save.Strength)} / {Format(save.StrengthMax)}");
            builder.AppendLine($"satiety: {Format(save.StrengthFood)} / {Format(save.StrengthMax)}");
            builder.AppendLine($"hydration: {Format(save.StrengthDrink)} / {Format(save.StrengthMax)}");
            builder.AppendLine($"mood: {Format(save.Feeling)} / {Format(save.FeelingMax)}");
            builder.AppendLine($"health: {Format(save.Health)} / 100");
            builder.AppendLine($"affection: {Format(save.Likability)} / {Format(save.LikabilityMax)}");

            if (main.State == Main.WorkingState.Work && main.NowWork != null)
            {
                var elapsed = DateTime.Now - main.WorkTimer.StartTime;
                var remaining = TimeSpan.FromMinutes(main.NowWork.Time) - elapsed;
                if (remaining < TimeSpan.Zero)
                    remaining = TimeSpan.Zero;

                builder.AppendLine($"current_task: {main.NowWork.NameTrans}");
                builder.AppendLine($"task_type: {main.NowWork.Type}");
                builder.AppendLine($"task_elapsed_minutes: {Format(elapsed.TotalMinutes)}");
                builder.AppendLine($"task_remaining_minutes: {Format(remaining.TotalMinutes)}");
                builder.AppendLine($"task_earned_so_far: {Format(main.WorkTimer.GetCount)}");
            }
            else
            {
                builder.AppendLine("current_task: none");
            }

            return builder.ToString();
        });
    }

    private static string Format(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
