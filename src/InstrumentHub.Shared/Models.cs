namespace InstrumentHub.Shared;

public static class CalibrationStatus
{
    public static string Calculate(string? currentStatus, DateOnly? nextDueDate, DateOnly today)
    {
        if (currentStatus is "In Repair" or "Out of Service") return currentStatus;
        if (nextDueDate is null) return "Pending Calibration";
        var days = nextDueDate.Value.DayNumber - today.DayNumber;
        return days < 0 ? "Overdue" : days <= 30 ? "Due Soon" : "Calibrated";
    }
    public static readonly string[] ManualStatuses = ["Active", "In Repair", "Out of Service"];
    public static readonly string[] TaskStatuses = ["Calibrated", "Out of Spec", "In Progress"];
}
public sealed class Instrument
{
    public int Id { get; set; }
    public string AssetId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string Department { get; set; } = "";
    public string Location { get; set; } = "";
    public string Owner { get; set; } = "";
    public string CurrentStatus { get; set; } = "Active";
    public DateOnly? LastCalibrationDate { get; set; }
    public DateOnly? NextDueDate { get; set; }
    public string TaskStatus { get; set; } = "In Progress";
}
public sealed record InstrumentView(int Id, string AssetId, string Name, string Model, string Department,
    string Location, string Owner, string CurrentStatus, DateOnly? LastCalibrationDate, DateOnly? NextDueDate,
    string Status, string TaskStatus);
public sealed class RepairLog
{
    public int Id { get; set; }
    public int InstrumentId { get; set; }
    public string AssetId { get; set; } = "";
    public string InstrumentName { get; set; } = "";
    public string Issue { get; set; } = "";
    public string Technician { get; set; } = "";
    public string Status { get; set; } = "In Progress";
    public DateOnly OpenedDate { get; set; }
}
public sealed class Certificate
{
    public int Id { get; set; }
    public int InstrumentId { get; set; }
    public string AssetId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string StorageName { get; set; } = "";
    public string ContentType { get; set; } = "application/pdf";
    public DateTime UploadedAt { get; set; }
    public long Size { get; set; }
}
public sealed record DashboardStats(int Total, int Overdue, int DueSoon, int UnderRepair, int Calibrated,
    int Pending, int OutOfService, decimal ComplianceRate, decimal AvailabilityRate, int OpenRepairs, bool DemoMode);
public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string Token, string Name, DateTime ExpiresAt);
public sealed record FormSubmission(string AssetId, string InstrumentName, string Department, DateOnly? NextDueDate, string? Owner);
public sealed record ImportResult(int Imported);
