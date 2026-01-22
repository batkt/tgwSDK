using Microsoft.Extensions.Hosting;

namespace ParkingService.Services;

/// <summary>
/// Background service that automatically polls all configured cameras every 1 second
/// to get recognition results and process them.
/// </summary>
public class CameraPollingBackgroundService : BackgroundService
{
    private readonly IParkingRecognitionService _parkingRecognitionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CameraPollingBackgroundService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5); // Poll every 5 seconds
    private string? _token;

    public CameraPollingBackgroundService(
        IParkingRecognitionService parkingRecognitionService,
        IConfiguration configuration,
        ILogger<CameraPollingBackgroundService> logger)
    {
        _parkingRecognitionService = parkingRecognitionService;
        _configuration = configuration;
        _logger = logger;
        
        // Get token from configuration or use default
        _token = _configuration["ParkingService:BackendToken"] 
            ?? "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6IjYxMmY0NTdkMTg1MjgwZGI2NzZkMGI1MyIsIm5lciI6IkNBZG1pbiIsImJhaWd1dWxsYWdpaW5JZCI6IjYxMmY0NTdkMTg1MjgwZGI2NzZkMGI1MSIsImlhdCI6MTc2Nzc0NzYyMn0.5vl903FZm4AxNTnJiRuQ6yz86hL2LTngXAmI1tE20Nk";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Camera polling background service started. Polling interval: {Interval} seconds", _pollingInterval.TotalSeconds);
        
        // Wait a bit before starting to ensure all services are initialized
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cameras = _parkingRecognitionService.GetConfiguredCameraIPs();
                
                if (cameras.Count == 0)
                {
                    _logger.LogWarning("No cameras configured. Waiting for configuration...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                // Poll all cameras in parallel for better performance
                var tasks = cameras.Select(cameraIp => 
                    PollCameraAsync(cameraIp, stoppingToken)
                ).ToArray();

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in camera polling loop");
            }

            // Wait for the polling interval before next iteration
            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("Camera polling background service stopped");
    }

    private async Task PollCameraAsync(string cameraIp, CancellationToken cancellationToken)
    {
        try
        {
            // Use default port 8000 (or you can configure per camera)
            var result = await _parkingRecognitionService.RecognizeAndSendAsync(
                cameraIp, 
                8000, 
                _token ?? string.Empty, 
                cancellationToken);

            if (result.Success && result.Plate != null)
            {
                if (result.IsDuplicate)
                {
                    _logger.LogInformation("Duplicate plate ignored: {Plate} from camera {CameraIp} (within debounce period)", 
                        result.Plate, cameraIp);
                }
                else
                {
                    _logger.LogInformation("✓ Processed plate: {Plate} from camera {CameraIp}. Backend sent: {BackendSent}, Gate opened: {GateOpened}", 
                        result.Plate, cameraIp, result.BackendSent, result.GateOpened);
                }
            }
            else if (!result.Success)
            {
                _logger.LogWarning("Failed to process plate from camera {CameraIp}: {Message}", cameraIp, result.Message);
            }
        }
        catch (Exception ex)
        {
            // Log error but don't stop the polling service
            _logger.LogWarning(ex, "Error polling camera {CameraIp}: {ErrorMessage}", cameraIp, ex.Message);
        }
    }
}
