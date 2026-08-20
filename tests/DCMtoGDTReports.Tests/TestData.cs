using DCMtoGDTReports.Core.Models;

namespace DCMtoGDTReports.Tests;

/// <summary>
/// Testdaten, die den Aufbau eines Vivid-T8-Berichts nachbilden.
/// Alle Patienten-, Geraete- und UID-Angaben sind frei erfunden.
/// </summary>
internal static class TestData
{
    public static SrReport CreateReport() => new()
    {
        Engine = "Test",
        RawMeasurementCount = 4,
        Header = new SrHeader
        {
            PatientId = "12345",
            PatientName = "Muster^Erika",
            LastName = "Muster",
            FirstName = "Erika",
            PatientBirthDate = "19850312",
            PatientSex = "F",
            StudyDate = "20240115",
            StudyTime = "103015",
            AccessionNumber = "1000042",
            StudyInstanceUid = "1.2.826.0.1.3680043.9.9999.1.1",
            SeriesInstanceUid = "1.2.826.0.1.3680043.9.9999.1.2",
            SopInstanceUid = "1.2.826.0.1.3680043.9.9999.1.3",
            Modality = "SR",
            Manufacturer = "GE Vingmed Ultrasound",
            ManufacturerModelName = "Vivid T8",
            StationName = "VIVIDT8-DEMO",
            DocumentTitle = "Adult Echocardiography Procedure Report"
        },
        Measurements =
        [
            Measurement("Left Ventricle Internal End Diastolic Dimension", "LVIDd", "4.21", "cm", "LN:29436-3"),
            Measurement("Interventricular Septum Diastolic Thickness", "IVSd", "0.64", "cm", "LN:18154-5"),
            Measurement("Left Ventricular End Diastolic Volume", "EDV", "79.21", "ml", "LN:18026-5", "Teichholz"),
            Measurement("Left Ventricular Ejection Fraction", "EF", "67.28", "%", "LN:18043-0")
        ]
    };

    private static MeasurementResult Measurement(
        string name, string shortName, string value, string unit, string code, string method = "") => new()
    {
        Name = name,
        ShortName = shortName,
        Value = value,
        RawValue = value,
        Unit = unit,
        SourceCode = code,
        Method = method,
        FindingSite = "Left Ventricle",
        ImageMode = "2D mode",
        Group = "Left Ventricle / 2D mode",
        RawPath = "/0/0"
    };
}
