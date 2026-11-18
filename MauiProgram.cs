using Microsoft.Maui.Hosting;
#if MAUI_DEVFLOW
using Microsoft.Maui.DevFlow.Agent;
#endif

namespace PortableMap;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

#if MAUI_DEVFLOW
        builder.AddMauiDevFlowAgent();
#endif

        return builder.Build();
    }
}
