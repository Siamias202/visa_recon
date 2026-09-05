namespace VISA_RECON.API.Application.DTOs.Maintenance;

public sealed class TestDataResetRequest
{
    public bool Confirm { get; set; }
}

public sealed class TestDataResetResponse
{
    public string Scope { get; set; } = string.Empty;
    public int TotalDeleted { get; set; }
    public Dictionary<string, int> DeletedRows { get; set; } = [];
}
