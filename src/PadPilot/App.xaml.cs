using PadPilot.Services;

namespace PadPilot;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (UpdateService.HandleStartupArguments(e.Args))
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
}

