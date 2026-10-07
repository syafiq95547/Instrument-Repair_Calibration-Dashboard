using InstrumentHub.Shared;
namespace InstrumentHub.Api.Data;
public sealed class DemoData
{
    public readonly SemaphoreSlim Gate = new(1, 1);
    public List<Instrument> Instruments { get; } = [];
    public List<RepairLog> Repairs { get; } = [];
    public List<Certificate> Certificates { get; } = [];
    public DemoData()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        string[][] rows = [
            ["EQ-00124", "Analytical balance", "Mettler Toledo · XPR205", "Quality control", "Lab A · Bench 04", "Sarah Kim"],
            ["EQ-00087", "Gas chromatograph", "Agilent · 8890 GC", "Analytical chemistry", "Lab B · Station 02", "James Wilson"],
            ["EQ-00156", "UV-Vis spectrophotometer", "Shimadzu · UV-1900i", "Quality control", "Lab A · Bench 07", "Emily Chen"],
            ["EQ-00043", "Digital pressure gauge", "Fluke · 700G29", "Engineering", "Workshop · Bay 03", "Michael Lee"],
            ["EQ-00201", "HPLC system", "Waters · Alliance e2695", "Research & development", "Lab C · Station 01", "Sarah Kim"],
            ["EQ-00092", "Digital multimeter", "Keysight · 34461A", "Engineering", "Workshop · Bay 01", "David Park"],
            ["EQ-00178", "Temperature chamber", "Memmert · HPP110", "Environmental testing", "Lab B · Room 12", "Emily Chen"],
            ["EQ-00218", "pH meter", "Hanna · HI5221", "Quality control", "Lab A · Bench 02", "David Park"],
            ["EQ-00112", "Centrifuge", "Eppendorf · 5804 R", "Research & development", "Lab C · Station 03", "Michael Lee"],
            ["EQ-00235", "Oscilloscope", "Tektronix · TBS1202", "Engineering", "Workshop · Bay 02", "James Wilson"],
            ["EQ-00241", "Torque wrench", "Norbar · Pro 200", "Engineering", "Workshop · Bay 04", "Sarah Kim"],
            ["EQ-00248", "Humidity sensor", "Vaisala · HMP110", "Environmental testing", "Lab B · Room 12", "Emily Chen"]];
        int?[] offsets = [-5, 160, -9, 125, 60, 180, 150, 12, 70, 0, null, 29];
        for (var i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            Instruments.Add(new Instrument { Id = i + 1, AssetId = r[0], Name = r[1], Model = r[2], Department = r[3], Location = r[4], Owner = r[5],
                CurrentStatus = i is 4 or 8 ? "In Repair" : "Active", LastCalibrationDate = offsets[i] is null ? null : today.AddDays(-180),
                NextDueDate = offsets[i] is int d ? today.AddDays(d) : null, TaskStatus = i is 0 or 2 ? "Out of Spec" : i is 4 or 7 or 8 or 9 or 10 or 11 ? "In Progress" : "Calibrated" });
        }
        Repairs.Add(new RepairLog { Id = 1, InstrumentId = 5, AssetId = "EQ-00201", InstrumentName = "HPLC system", Issue = "Pump pressure fluctuation", Technician = "Sarah Kim", OpenedDate = today.AddDays(-4) });
        Repairs.Add(new RepairLog { Id = 2, InstrumentId = 9, AssetId = "EQ-00112", InstrumentName = "Centrifuge", Issue = "Rotor alignment & vibration", Technician = "Michael Lee", Status = "Awaiting Parts", OpenedDate = today.AddDays(-2) });
    }
}
