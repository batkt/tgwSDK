namespace ParkingService.Models;

/// <summary>
/// Request model for testing endpoint to manually send plate data.
/// </summary>
public record TestPlateRequest
{
    public string Plate { get; init; } = string.Empty;
    public string CameraIp { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public string? Color { get; init; }
    public int? Port { get; init; }
    public bool? OpenGate { get; init; }
}
