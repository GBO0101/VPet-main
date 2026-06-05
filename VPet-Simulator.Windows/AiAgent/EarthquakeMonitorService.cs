using System;
using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class EarthquakeMonitorService : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);
    private readonly EarthquakeSkillClient client = new();
    private readonly EarthquakeAlertService alertService;
    private Timer? timer;
    private bool seeded;
    private bool checking;

    public EarthquakeMonitorService(IMainWindow mw)
    {
        alertService = new EarthquakeAlertService(mw);
    }

    public void Start()
    {
        timer ??= new Timer(_ => _ = CheckAsync(), null, FirstCheckDelay, CheckInterval);
    }

    public void Dispose()
    {
        timer?.Dispose();
        timer = null;
    }

    private async Task CheckAsync()
    {
        if (checking)
            return;

        checking = true;
        try
        {
            var report = await client.GetLatestReportAsync(CancellationToken.None);
            if (report.IsEmpty)
                return;

            if (!seeded)
            {
                seeded = true;
                alertService.MarkSeen(report);
                return;
            }

            alertService.NotifyIfNew(report);
        }
        finally
        {
            checking = false;
        }
    }
}
