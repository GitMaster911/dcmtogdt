using System.Text;

namespace DCMtoGDTReports.Core.Gdt;

/// <summary>
/// Ein einzelnes GDT-Feld. Der Zeilenaufbau ist:
/// &lt;dreistellige Laenge&gt;&lt;vierstellige Feldkennung&gt;&lt;Inhalt&gt;&lt;CRLF&gt;
/// Die Laenge ist die Anzahl der Inhaltszeichen (in Bytes der Zieltkodierung) plus 9.
/// </summary>
public readonly record struct GdtField(string FieldId, string Content)
{
    /// <summary>3 Zeichen Laenge + 4 Zeichen Feldkennung + 2 Zeichen CRLF.</summary>
    public const int Overhead = 9;

    /// <summary>Maximale Inhaltslaenge, damit die dreistellige Laengenangabe nicht ueberlaeuft.</summary>
    public const int MaxContentLength = 999 - Overhead;
}

/// <summary>
/// Serialisiert GDT-Felder in das Satzformat.
/// </summary>
public static class GdtLineFormatter
{
    public const string LineTerminator = "\r\n";

    /// <summary>Berechnet die GDT-Feldlaenge in der Zielkodierung.</summary>
    public static int CalculateLength(string content, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return encoding.GetByteCount(content ?? string.Empty) + GdtField.Overhead;
    }

    /// <summary>
    /// Formatiert eine GDT-Zeile, z. B. "01380006310" fuer Feld 8000 mit Inhalt "6310".
    /// </summary>
    public static string Format(string fieldId, string content, Encoding encoding)
    {
        if (string.IsNullOrEmpty(fieldId) || fieldId.Length != 4 || !fieldId.All(char.IsDigit))
            throw new ArgumentException("Die GDT-Feldkennung muss aus genau vier Ziffern bestehen.", nameof(fieldId));

        content ??= string.Empty;
        var length = CalculateLength(content, encoding);
        if (length > 999)
            throw new ArgumentException($"Der Feldinhalt ist zu lang fuer Feld {fieldId} (Laenge {length}).", nameof(content));

        return $"{length:000}{fieldId}{content}{LineTerminator}";
    }

    public static string Format(GdtField field, Encoding encoding) => Format(field.FieldId, field.Content, encoding);

    /// <summary>Serialisiert einen kompletten Satz.</summary>
    public static string Format(IEnumerable<GdtField> fields, Encoding encoding)
    {
        var builder = new StringBuilder();
        foreach (var field in fields)
            builder.Append(Format(field, encoding));
        return builder.ToString();
    }
}
