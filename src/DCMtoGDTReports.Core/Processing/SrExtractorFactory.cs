using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Dicom;
using DCMtoGDTReports.Tools;

namespace DCMtoGDTReports.Core.Processing;

/// <summary>
/// Waehlt die Auswertungs-Engine. Standard ist das eingebaute Toolkit; DCMTK wird nur genutzt,
/// wenn es konfiguriert ist und tatsaechlich gefunden wird.
/// </summary>
public static class SrExtractorFactory
{
    public const string EngineBuiltin = "Builtin";
    public const string EngineDcmtk = "Dcmtk";

    public static ISrExtractor Create(AppSettings settings, out DcmtkInstallation? dcmtk)
    {
        dcmtk = DcmtkLocator.Locate(settings.DcmtkPath);

        var wantsDcmtk = string.Equals(settings.Processing.PreferredEngine, EngineDcmtk, StringComparison.OrdinalIgnoreCase);
        if (wantsDcmtk && dcmtk is not null)
            return new DcmtkSrExtractor(dcmtk);

        return new FoDicomSrExtractor();
    }
}
