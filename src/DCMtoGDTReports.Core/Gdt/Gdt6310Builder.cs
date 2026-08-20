using System.Globalization;
using System.Text;
using DCMtoGDTReports.Core.Configuration;
using DCMtoGDTReports.Core.Dicom;
using DCMtoGDTReports.Core.Models;

namespace DCMtoGDTReports.Core.Gdt;

/// <summary>
/// Erzeugt eine GDT-2.1-Ruecksendedatei der Satzart 6310 (Ergebnisdatenrueckgabe eines Messgeraets).
/// </summary>
public sealed class Gdt6310Builder(GdtSettings settings)
{
    private const string RecordTypeField = "8000";
    private const string RecordLengthField = "8100";
    private const string ReceiverIdField = "8315";
    private const string SenderIdField = "8316";
    private const string CharsetField = "9206";
    private const string VersionField = "9218";
    private const string PatientIdField = "3000";
    private const string LastNameField = "3101";
    private const string FirstNameField = "3102";
    private const string BirthDateField = "3103";
    private const string SexField = "3110";
    private const string ExamDateField = "6200";
    private const string ExamTimeField = "6201";
    private const string TestTypeField = "8402";
    private const string TestIdField = "8410";
    private const string TestNameField = "8411";
    private const string TestValueField = "8420";
    private const string TestUnitField = "8421";
    private const string ResultTextField = "6220";
    private const string CommentTextField = "6227";

    private const string RecordType6310 = "6310";

    private readonly GdtSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly GdtReportComposer _composer = new(settings);

    /// <summary>Stellt den kompletten Satz als Feldliste zusammen (ohne Feld 8100).</summary>
    public IReadOnlyList<GdtField> BuildFields(SrReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var header = report.Header;
        var fields = new List<GdtField>
        {
            new(RecordTypeField, RecordType6310),
            new(ReceiverIdField, _settings.ReceiverId),
            new(SenderIdField, _settings.SenderId),
            new(CharsetField, _settings.Charset),
            new(VersionField, _settings.Version)
        };

        AddIfPresent(fields, PatientIdField, header.PatientId);
        AddIfPresent(fields, LastNameField, header.LastName);
        AddIfPresent(fields, FirstNameField, header.FirstName);
        AddIfPresent(fields, BirthDateField, DicomValueConverter.ToGdtDate(header.PatientBirthDate));
        AddIfPresent(fields, SexField, DicomValueConverter.ToGdtSex(header.PatientSex));
        AddIfPresent(fields, ExamDateField, DicomValueConverter.ToGdtDate(header.StudyDate));
        AddIfPresent(fields, ExamTimeField, DicomValueConverter.ToGdtTime(header.StudyTime));
        AddIfPresent(fields, TestTypeField, _settings.TestType);
        AddIfPresent(fields, TestIdField, _settings.TestId);
        AddIfPresent(fields, TestNameField, _settings.TestName);

        foreach (var line in _composer.BuildResultLines(report))
            fields.Add(new GdtField(ResultTextField, line));

        if (_settings.EmitStructuredTestValues)
            AddStructuredValues(fields, report);

        foreach (var line in _composer.BuildCommentLines(report))
            fields.Add(new GdtField(CommentTextField, line));

        return fields;
    }

    /// <summary>Serialisiert den Satz inklusive Feld 8100 (Satzlaenge), falls konfiguriert.</summary>
    public string BuildRecord(SrReport report, Encoding encoding)
    {
        var fields = BuildFields(report);
        if (!_settings.IncludeRecordLength)
            return GdtLineFormatter.Format(fields, encoding);

        // Feld 8100 enthaelt die Gesamtlaenge des Satzes einschliesslich der eigenen Zeile.
        var payloadLength = fields.Sum(f => GdtLineFormatter.CalculateLength(f.Content, encoding));
        var recordLengthLine = GdtLineFormatter.CalculateLength("00000", encoding);
        var total = payloadLength + recordLengthLine;

        var builder = new StringBuilder();
        builder.Append(GdtLineFormatter.Format(fields[0], encoding));
        builder.Append(GdtLineFormatter.Format(RecordLengthField, total.ToString("00000", CultureInfo.InvariantCulture), encoding));
        foreach (var field in fields.Skip(1))
            builder.Append(GdtLineFormatter.Format(field, encoding));

        return builder.ToString();
    }

    /// <summary>
    /// Optionale diskrete Wertuebergabe: je Messwert 8410/8411/8420/8421.
    /// Damit kann MEDICAL OFFICE die Werte als einzelne Messgroessen statt nur als Text uebernehmen.
    /// </summary>
    private static void AddStructuredValues(List<GdtField> fields, SrReport report)
    {
        foreach (var measurement in report.Measurements)
        {
            var testId = string.IsNullOrWhiteSpace(measurement.ShortName) ? measurement.Name : measurement.ShortName;
            if (string.IsNullOrWhiteSpace(testId) || string.IsNullOrWhiteSpace(measurement.Value))
                continue;

            fields.Add(new GdtField(TestIdField, Truncate(testId, 8)));
            fields.Add(new GdtField(TestNameField, Truncate($"{testId} ({measurement.Group})", 60)));
            fields.Add(new GdtField(TestValueField, measurement.Value));
            if (!string.IsNullOrWhiteSpace(measurement.Unit))
                fields.Add(new GdtField(TestUnitField, measurement.Unit));
        }
    }

    private static void AddIfPresent(List<GdtField> fields, string fieldId, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields.Add(new GdtField(fieldId, value.Trim()));
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
