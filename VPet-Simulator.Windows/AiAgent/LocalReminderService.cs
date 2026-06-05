using System;
using System.Threading.Tasks;
using System.Timers;
using VPet_Simulator.Windows.Interface;
using Timer = System.Timers.Timer;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class LocalReminderService : IDisposable
{
    private readonly IMainWindow mw;
    private readonly LocalReminderStore store;
    private readonly Timer timer;
    private bool isChecking;

    public LocalReminderService(IMainWindow mw, LocalReminderStore store)
    {
        this.mw = mw;
        this.store = store;
        timer = new Timer(TimeSpan.FromSeconds(30).TotalMilliseconds)
        {
            AutoReset = true
        };
        timer.Elapsed += Timer_Elapsed;
    }

    public void Start()
    {
        timer.Start();
        _ = CheckAsync();
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Elapsed -= Timer_Elapsed;
        timer.Dispose();
    }

    private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        _ = CheckAsync();
    }

    private Task CheckAsync()
    {
        if (isChecking)
            return Task.CompletedTask;

        isChecking = true;
        try
        {
            var dueReminders = store.MarkDueRemindersTriggered(DateTime.Now);
            foreach (var reminder in dueReminders)
            {
                var text = AiAgentReminderSpeech.BuildLocalReminderText(reminder.Title, reminder.Note);
                mw.Dispatcher.Invoke(() => mw.Main.SayRnd(text, true, "AI Agent"));
            }
        }
        catch
        {
            // Local reminders must not interrupt the pet main flow.
        }
        finally
        {
            isChecking = false;
        }

        return Task.CompletedTask;
    }
}
