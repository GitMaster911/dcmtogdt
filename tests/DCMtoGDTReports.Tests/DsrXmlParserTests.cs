using System.Xml.Linq;
using DCMtoGDTReports.Core.Dicom;
using Xunit;

namespace DCMtoGDTReports.Tests;

public class DsrXmlParserTests
{
    /// <summary>
    /// Ausschnitt im Format von dsr2xml, nachgebildet aus einer Vivid-T8-Datei:
    /// Findings-Container mit Finding Site, Measurement Group mit Image Mode und je zwei
    /// identischen NUM-Knoten (mit und ohne Selection Status).
    /// Alle Patienten- und UID-Angaben sind frei erfunden.
    /// </summary>
    private const string SampleXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <report type="Comprehensive3DSR">
          <patient>
            <id>12345</id>
            <name><last>Muster</last><first>Erika</first></name>
            <birthdate><date>1985-03-12</date></birthdate>
            <sex>F</sex>
          </patient>
          <study uid="1.2.826.0.1.3680043.9.9999.1.1">
            <accession><number>1000042</number></accession>
            <date>2024-01-15</date>
            <time>10:30:15</time>
            <description>SONO Vivid T8</description>
          </study>
          <series uid="1.2.826.0.1.3680043.9.9999.1.2"><modality>SR</modality></series>
          <instance uid="1.2.826.0.1.3680043.9.9999.1.3"/>
          <content>
            <container>
              <concept><value>125200</value><scheme>DCM</scheme><meaning>Adult Echocardiography Procedure Report</meaning></concept>
              <container relationship="contains">
                <concept><value>121070</value><scheme>DCM</scheme><meaning>Findings</meaning></concept>
                <code relationship="has concept mod">
                  <concept><value>G-C0E3</value><scheme>SRT</scheme><meaning>Finding Site</meaning></concept>
                  <value>T-32600</value><scheme>SRT</scheme><meaning>Left Ventricle</meaning>
                </code>
                <container relationship="contains">
                  <concept><value>125007</value><scheme>DCM</scheme><meaning>Measurement Group</meaning></concept>
                  <code relationship="has concept mod">
                    <concept><value>G-0373</value><scheme>SRT</scheme><meaning>Image Mode</meaning></concept>
                    <value>G-03A2</value><scheme>SRT</scheme><meaning>2D mode</meaning>
                  </code>
                  <num relationship="contains">
                    <concept><value>29436-3</value><scheme>LN</scheme><meaning>Left Ventricle Internal End Diastolic Dimension</meaning></concept>
                    <value>4.2141821355915</value>
                    <unit><value>cm</value><scheme>UCUM</scheme><meaning>centimeter</meaning></unit>
                    <code relationship="has properties">
                      <concept><value>121404</value><scheme>DCM</scheme><meaning>Selection Status</meaning></concept>
                      <value>121412</value><scheme>DCM</scheme><meaning>Mean value chosen</meaning>
                    </code>
                  </num>
                  <num relationship="contains">
                    <concept><value>29436-3</value><scheme>LN</scheme><meaning>Left Ventricle Internal End Diastolic Dimension</meaning></concept>
                    <value>4.2141821355915</value>
                    <unit><value>cm</value><scheme>UCUM</scheme><meaning>centimeter</meaning></unit>
                  </num>
                  <num relationship="contains">
                    <concept><value>18026-5</value><scheme>LN</scheme><meaning>Left Ventricular End Diastolic Volume</meaning></concept>
                    <value>79.206677328227</value>
                    <unit><value>ml</value><scheme>UCUM</scheme><meaning>milliliter</meaning></unit>
                    <code relationship="has concept mod">
                      <concept><value>G-C036</value><scheme>SRT</scheme><meaning>Measurement Method</meaning></concept>
                      <value>125209</value><scheme>DCM</scheme><meaning>Teichholz</meaning>
                    </code>
                  </num>
                </container>
              </container>
            </container>
          </content>
        </report>
        """;

    private static Core.Models.SrReport ParseSample() =>
        DsrXmlParser.Parse(XDocument.Parse(SampleXml), "Test");

    [Fact]
    public void Parse_LiestKopfdaten()
    {
        var report = ParseSample();

        Assert.Equal("12345", report.Header.PatientId);
        Assert.Equal("Muster", report.Header.LastName);
        Assert.Equal("Erika", report.Header.FirstName);
        Assert.Equal("19850312", report.Header.PatientBirthDate);
        Assert.Equal("F", report.Header.PatientSex);
        Assert.Equal("20240115", report.Header.StudyDate);
        Assert.Equal("1000042", report.Header.AccessionNumber);
        Assert.Equal("1.2.826.0.1.3680043.9.9999.1.3", report.Header.SopInstanceUid);
        Assert.Equal("SR", report.Header.Modality);
    }

    [Fact]
    public void Parse_EntferntDieVomGeraetGeliefertenDubletten()
    {
        var report = ParseSample();

        Assert.Equal(3, report.RawMeasurementCount);
        Assert.Equal(2, report.Measurements.Count);
    }

    [Fact]
    public void Parse_UebernimmtWertEinheitUndKontext()
    {
        var lvidd = ParseSample().Measurements.Single(m => m.SourceCode == "LN:29436-3");

        Assert.Equal("4.2141821355915", lvidd.RawValue);
        Assert.Equal("cm", lvidd.Unit);
        Assert.Equal("Left Ventricle", lvidd.FindingSite);
        Assert.Equal("2D mode", lvidd.ImageMode);
        Assert.Equal("Left Ventricle / 2D mode", lvidd.Group);
        Assert.Equal("Mean value chosen", lvidd.SelectionStatus);
    }

    [Fact]
    public void Parse_UebernimmtMessmethodeAmMesswert()
    {
        var edv = ParseSample().Measurements.Single(m => m.SourceCode == "LN:18026-5");

        Assert.Equal("Teichholz", edv.Method);
        Assert.Equal("Left Ventricular End Diastolic Volume", edv.Name);
    }
}
