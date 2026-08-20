using System.ServiceProcess;

namespace DCMtoGDTReports.App.ViewModels;

public enum ServiceState
{
    NotInstalled,
    Running,
    Stopped,
    Other
}

/// <summary>
/// Fragt den Zustand des Windows-Dienstes ab. Die GUI kann so anzeigen, ob die
/// Verarbeitung im Hintergrund laeuft - unabhaengig von der GUI-eigenen Ueberwachung.
/// </summary>
public static class WindowsServiceStatus
{
    public const string DefaultServiceName = "DCMtoGDTReports";

    public static ServiceState Query(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) serviceName = DefaultServiceName;

        try
        {
            using var controller = new ServiceController(serviceName);
            return controller.Status switch
            {
                ServiceControllerStatus.Running => ServiceState.Running,
                ServiceControllerStatus.Stopped => ServiceState.Stopped,
                _ => ServiceState.Other
            };
        }
        catch (InvalidOperationException)
        {
            // Wird geworfen, wenn kein Dienst dieses Namens existiert.
            return ServiceState.NotInstalled;
        }
    }

    public static string Describe(ServiceState state) => state switch
    {
        ServiceState.Running => "laeuft",
        ServiceState.Stopped => "gestoppt",
        ServiceState.NotInstalled => "nicht installiert",
        _ => "wird gestartet/beendet"
    };
}
