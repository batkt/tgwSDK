using System.Collections.Concurrent;

namespace ParkingService.Services;

/// <summary>
/// Tracks processed plates to prevent duplicate processing.
/// Uses debounce logic: same plate within debounce period is ignored.
/// </summary>
public interface IPlateTrackingService
{
    /// <summary>
    /// Checks if a plate should be processed (not a duplicate within debounce period).
    /// </summary>
    /// <param name="cameraIp">Camera IP address</param>
    /// <param name="plate">License plate number</param>
    /// <param name="timestamp">Recognition timestamp from camera</param>
    /// <returns>True if plate should be processed, false if it's a duplicate</returns>
    bool ShouldProcessPlate(string cameraIp, string plate, long timestamp);

    /// <summary>
    /// Records that a plate has been processed.
    /// </summary>
    void RecordProcessedPlate(string cameraIp, string plate, long timestamp);

    /// <summary>
    /// Clears tracking for a specific camera (useful for testing or reset).
    /// </summary>
    void ClearTrackingForCamera(string cameraIp);
}

public class PlateTrackingService : IPlateTrackingService
{
    private readonly ConcurrentDictionary<string, PlateTrackingInfo> _trackedPlates;
    private readonly ILogger<PlateTrackingService> _logger;
    private readonly int _debounceSeconds;

    public PlateTrackingService(IConfiguration configuration, ILogger<PlateTrackingService> logger)
    {
        _trackedPlates = new ConcurrentDictionary<string, PlateTrackingInfo>();
        _logger = logger;
        _debounceSeconds = configuration.GetValue<int>("ParkingService:DebounceSeconds", 30); // Default 30 seconds
    }

    public bool ShouldProcessPlate(string cameraIp, string plate, long timestamp)
    {
        if (string.IsNullOrWhiteSpace(plate) || string.IsNullOrWhiteSpace(cameraIp))
        {
            return false;
        }

        var key = $"{cameraIp}:{plate}";
        
        if (!_trackedPlates.TryGetValue(key, out var trackingInfo))
        {
            // New plate, should process
            _logger.LogDebug("New plate detected: {Plate} from camera {CameraIp}", plate, cameraIp);
            return true;
        }

        // Check if timestamp is newer (camera might refresh timestamp even for same plate)
        if (timestamp <= trackingInfo.LastTimestamp)
        {
            _logger.LogDebug("Ignoring duplicate plate with older/equal timestamp: {Plate} from camera {CameraIp}", plate, cameraIp);
            return false;
        }

        // Check debounce period
        var timeSinceLastProcess = DateTime.UtcNow - trackingInfo.LastProcessedTime;
        if (timeSinceLastProcess.TotalSeconds < _debounceSeconds)
        {
            _logger.LogDebug("Ignoring duplicate plate within debounce period: {Plate} from camera {CameraIp} (last processed {Seconds} seconds ago)", 
                plate, cameraIp, (int)timeSinceLastProcess.TotalSeconds);
            return false;
        }

        // Same plate but debounce period expired - treat as new event
        _logger.LogInformation("Plate returned after debounce period expired: {Plate} from camera {CameraIp} (last processed {Seconds} seconds ago)", 
            plate, cameraIp, (int)timeSinceLastProcess.TotalSeconds);
        return true;
    }

    public void RecordProcessedPlate(string cameraIp, string plate, long timestamp)
    {
        if (string.IsNullOrWhiteSpace(plate) || string.IsNullOrWhiteSpace(cameraIp))
        {
            return;
        }

        var key = $"{cameraIp}:{plate}";
        var trackingInfo = new PlateTrackingInfo
        {
            Plate = plate,
            CameraIp = cameraIp,
            LastTimestamp = timestamp,
            LastProcessedTime = DateTime.UtcNow
        };

        _trackedPlates.AddOrUpdate(key, trackingInfo, (k, existing) => trackingInfo);
        _logger.LogDebug("Recorded processed plate: {Plate} from camera {CameraIp} at timestamp {Timestamp}", 
            plate, cameraIp, timestamp);
    }

    public void ClearTrackingForCamera(string cameraIp)
    {
        var keysToRemove = _trackedPlates.Keys
            .Where(k => k.StartsWith($"{cameraIp}:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _trackedPlates.TryRemove(key, out _);
        }

        _logger.LogInformation("Cleared tracking for camera {CameraIp} ({Count} entries removed)", 
            cameraIp, keysToRemove.Count);
    }

    private class PlateTrackingInfo
    {
        public string Plate { get; set; } = string.Empty;
        public string CameraIp { get; set; } = string.Empty;
        public long LastTimestamp { get; set; }
        public DateTime LastProcessedTime { get; set; }
    }
}
