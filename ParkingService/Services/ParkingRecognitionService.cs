using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using ParkingService.Controllers;

namespace ParkingService.Services;

public interface IParkingRecognitionService
{
    /// <summary>
    /// Call the camera recognition API and return the plate/timestamp result.
    /// </summary>
    Task<CameraRecognitionResult?> GetRecognitionResultAsync(string cameraIp, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Call the camera gate opening API.
    /// </summary>
    Task<bool> OpenGateAsync(string cameraIp, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Call the camera display screen control API.
    /// </summary>
    Task<bool> DisplayScreenAsync(string cameraIp, int port, string textOne, string textTwo, string textThree, string textFour, string voiceContent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Complete flow: Get recognition result, send to backend API, and open gate if needed.
    /// This follows the pattern from your existing controller.
    /// </summary>
    Task<RecognitionAndSendResult> RecognizeAndSendAsync(string cameraIp, int port, string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all configured static camera IPs.
    /// </summary>
    List<string> GetConfiguredCameraIPs();

    /// <summary>
    /// Poll all configured cameras and process any recognized plates.
    /// </summary>
    Task<List<RecognitionAndSendResult>> PollAllCamerasAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recognition results from all configured cameras one by one.
    /// </summary>
    Task<Dictionary<string, CameraRecognitionResult?>> GetAllRecognitionResultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Connect to all configured cameras and verify connectivity.
    /// </summary>
    Task<Dictionary<string, bool>> ConnectToAllCamerasAsync(CancellationToken cancellationToken = default);
}   

public class ParkingRecognitionService : IParkingRecognitionService
{
    private readonly HttpClient _httpClient;
    private readonly IPlateService _plateService;
    private readonly IPlateTrackingService _plateTrackingService;
    private readonly ILogger<ParkingRecognitionService> _logger;
    private readonly string _cameraApiBaseUrl;
    private readonly List<string> _configuredCameraIPs;
    private readonly string _cameraUsername;
    private readonly string _cameraPassword;

    public ParkingRecognitionService(
        HttpClient httpClient, 
        IPlateService plateService, 
        IPlateTrackingService plateTrackingService,
        IConfiguration configuration,
        ILogger<ParkingRecognitionService> logger)
    {
        _httpClient = httpClient;
        _plateService = plateService;
        _plateTrackingService = plateTrackingService;
        _logger = logger;
        _cameraApiBaseUrl = configuration["ParkingService:CameraApiBaseUrl"] ?? "http://127.0.0.1:8000";
        
        // Load static camera IPs from configuration
        var cameraIPsSection = configuration.GetSection("ParkingService:CameraIPs");
        _configuredCameraIPs = cameraIPsSection.Get<List<string>>() ?? new List<string> { "192.168.1.11", "192.168.1.12", "192.168.1.14" };
        
        _cameraUsername = configuration["ParkingService:CameraUsername"] ?? "admin";
        _cameraPassword = configuration["ParkingService:CameraPassword"] ?? "Tgw123456";
        
        _logger.LogInformation("ParkingRecognitionService initialized with {CameraCount} static camera(s): {CameraIPs}", 
            _configuredCameraIPs.Count, string.Join(", ", _configuredCameraIPs));
    }

    public List<string> GetConfiguredCameraIPs()
    {
        return new List<string>(_configuredCameraIPs);
    }

    public async Task<List<RecognitionAndSendResult>> PollAllCamerasAsync(string token, CancellationToken cancellationToken = default)
    {
        var results = new List<RecognitionAndSendResult>();
        
        foreach (var cameraIp in _configuredCameraIPs)
        {
            try
            {
                // Use default port 8000 (or you can configure per camera)
                var result = await RecognizeAndSendAsync(cameraIp, 8000, token, cancellationToken);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling camera {CameraIp}", cameraIp);
                results.Add(new RecognitionAndSendResult
                {
                    Success = false,
                    Message = $"Error: {ex.Message}",
                    Plate = null,
                    BackendSent = false,
                    GateOpened = false,
                    IsDuplicate = false
                });
            }
        }
        
        return results;
    }

    public async Task<Dictionary<string, bool>> ConnectToAllCamerasAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== Starting connection to {CameraCount} camera(s) ===", _configuredCameraIPs.Count);
        
        var connectionResults = new Dictionary<string, bool>();
        int connectedCount = 0;
        int failedCount = 0;
        
        // Connect to all configured cameras one by one
        foreach (var cameraIp in _configuredCameraIPs)
        {
            try
            {
                _logger.LogInformation("Connecting to camera {CameraIp} (username: {Username})...", cameraIp, _cameraUsername);
                
                // Attempt to connect by calling the recognition API (this verifies connectivity)
                // Base URL is fixed (127.0.0.1:8000), path includes /swp-cloud prefix
                var testUrl = $"{_cameraApiBaseUrl}/swp-cloud/api/camera/scan/result?ip={WebUtility.UrlEncode(cameraIp)}";
                
                var response = await _httpClient.GetAsync(testUrl, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✓ Successfully connected to camera {CameraIp}", cameraIp);
                    connectionResults[cameraIp] = true;
                    connectedCount++;
                }
                else
                {
                    _logger.LogWarning("✗ Failed to connect to camera {CameraIp}: HTTP {StatusCode}", cameraIp, response.StatusCode);
                    connectionResults[cameraIp] = false;
                    failedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "✗ Error connecting to camera {CameraIp}: {ErrorMessage}", cameraIp, ex.Message);
                connectionResults[cameraIp] = false;
                failedCount++;
            }
        }
        
        _logger.LogInformation("=== Connection completed: {ConnectedCount} connected, {FailedCount} failed ===", 
            connectedCount, failedCount);
        
        return connectionResults;
    }

    public async Task<Dictionary<string, CameraRecognitionResult?>> GetAllRecognitionResultsAsync(CancellationToken cancellationToken = default)
    {
        // First, connect to all cameras
        _logger.LogInformation("=== Step 1: Connecting to all cameras ===");
        var connectionResults = await ConnectToAllCamerasAsync(cancellationToken);
        
        // Log connection summary
        var connectedCameras = connectionResults.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
        var failedCameras = connectionResults.Where(kvp => !kvp.Value).Select(kvp => kvp.Key).ToList();
        
        if (connectedCameras.Any())
        {
            _logger.LogInformation("Connected cameras: {ConnectedCameras}", string.Join(", ", connectedCameras));
        }
        if (failedCameras.Any())
        {
            _logger.LogWarning("Failed to connect to cameras: {FailedCameras}", string.Join(", ", failedCameras));
        }
        
        _logger.LogInformation("=== Step 2: Starting to poll {CameraCount} camera(s) for plate recognition ===", _configuredCameraIPs.Count);
        
        var results = new Dictionary<string, CameraRecognitionResult?>();
        int successCount = 0;
        int noPlateCount = 0;
        int errorCount = 0;
        
        // Poll all configured cameras one by one
        foreach (var cameraIp in _configuredCameraIPs)
        {
            try
            {
                _logger.LogInformation("Polling camera {CameraIp} for plate recognition...", cameraIp);
                var result = await GetRecognitionResultAsync(cameraIp, 8000, cancellationToken);
                results[cameraIp] = result;
                
                if (result != null)
                {
                    _logger.LogInformation("✓ Camera {CameraIp} recognized plate: {Plate} (timestamp: {Timestamp})", 
                        cameraIp, result.Plate, result.Timestamp);
                    successCount++;
                }
                else
                {
                    _logger.LogInformation("Camera {CameraIp} returned no recognition result (no plate detected)", cameraIp);
                    noPlateCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "✗ Error getting recognition result from camera {CameraIp}: {ErrorMessage}", cameraIp, ex.Message);
                results[cameraIp] = null;
                errorCount++;
            }
        }
        
        _logger.LogInformation("=== Polling completed: {SuccessCount} with plates, {NoPlateCount} no plates, {ErrorCount} errors ===", 
            successCount, noPlateCount, errorCount);
        
        return results;
    }

    public async Task<CameraRecognitionResult?> GetRecognitionResultAsync(string cameraIp, int port, CancellationToken cancellationToken = default)
    {
        // URL: http://127.0.0.1:8000/swp-cloud/api/camera/scan/result?ip={cameraIp}
        // Method: GET
        // Base URL is fixed (127.0.0.1:8000), path includes /swp-cloud prefix
        var url = $"{_cameraApiBaseUrl}/swp-cloud/api/camera/scan/result?ip={WebUtility.UrlEncode(cameraIp)}";
        
        _logger.LogDebug("Calling camera recognition API: {Url}", url);

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Camera recognition API returned error status {StatusCode} for camera {CameraIp}", 
                response.StatusCode, cameraIp);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<CameraRecognitionApiResponse>(cancellationToken: cancellationToken);
        if (body == null)
        {
            _logger.LogWarning("Camera recognition API returned null response for camera {CameraIp}", cameraIp);
            return null;
        }
        
        if (body.respCode != "00000")
        {
            _logger.LogWarning("Camera recognition API returned error code {RespCode}: {RespMsg} for camera {CameraIp}", 
                body.respCode, body.respMsg, cameraIp);
            return null;
        }
        
        if (body.data == null || string.IsNullOrWhiteSpace(body.data.plate))
        {
            _logger.LogDebug("Camera {CameraIp} returned no plate data (data is null or plate is empty)", cameraIp);
            return null;
        }

        _logger.LogDebug("Camera {CameraIp} recognition successful: plate={Plate}, timestamp={Timestamp}", 
            cameraIp, body.data.plate, body.data.timestamp);

        return new CameraRecognitionResult(body.data.plate, body.data.timestamp);
    }

    public async Task<bool> OpenGateAsync(string cameraIp, int port, CancellationToken cancellationToken = default)
    {
        // URL: http://127.0.0.1:8000/swp-cloud/api/camera/open/gate?ip={cameraIp}
        // Method: GET
        // Base URL is fixed (127.0.0.1:8000), path includes /swp-cloud prefix
        var url = $"{_cameraApiBaseUrl}/swp-cloud/api/camera/open/gate?ip={WebUtility.UrlEncode(cameraIp)}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var body = await response.Content.ReadFromJsonAsync<CameraGateApiResponse>(cancellationToken: cancellationToken);
        return body is { respCode: "00000" };
    }

    public async Task<bool> DisplayScreenAsync(string cameraIp, int port, string textOne, string textTwo, string textThree, string textFour, string voiceContent, CancellationToken cancellationToken = default)
    {
        // URL: http://127.0.0.1:8000/swp-cloud/api/camera/display
        // Method: POST (changed from GET due to 405 MethodNotAllowed error)
        // Base URL is fixed (127.0.0.1:8000), path includes /swp-cloud prefix
        // IP is included in request body, not query parameter
        var url = $"{_cameraApiBaseUrl}/swp-cloud/api/camera/display";

        var requestBody = new CameraDisplayRequest
        {
            textOne = textOne,
            textTwo = textTwo,
            textThree = textThree,
            textFour = textFour,
            voiceContent = voiceContent,
            ip = cameraIp
        };

        // Log the request body for debugging
        var requestJson = System.Text.Json.JsonSerializer.Serialize(requestBody);
        _logger.LogInformation("Calling camera display API: {Url} with request: {RequestJson}", url, requestJson);

        // Use POST method (GET was returning 405 MethodNotAllowed)
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = System.Net.Http.Json.JsonContent.Create(requestBody)
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);
        
        // Read response content for debugging
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("Display API response status: {StatusCode}, body: {ResponseBody}", response.StatusCode, responseContent);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Camera display API returned error status {StatusCode} for camera {CameraIp}, response: {ResponseBody}", 
                response.StatusCode, cameraIp, responseContent);
            return false;
        }

        var responseBody = await response.Content.ReadFromJsonAsync<CameraDisplayApiResponse>(cancellationToken: cancellationToken);
        var success = responseBody is { respCode: "00000" };
        
        if (success)
        {
            _logger.LogInformation("Display screen updated successfully for camera {CameraIp}: {Text1} | {Text2} | {Text3} | {Text4}", 
                cameraIp, textOne, textTwo, textThree, textFour);
        }
        else
        {
            _logger.LogWarning("Display screen update failed for camera {CameraIp}: {RespCode} - {RespMsg}. Request was: {RequestJson}", 
                cameraIp, responseBody?.respCode, responseBody?.respMsg, requestJson);
        }
        
        return success;
    }

    public async Task<RecognitionAndSendResult> RecognizeAndSendAsync(string cameraIp, int port, string token, CancellationToken cancellationToken = default)
    {
        try
        {
            // Step 1: Get recognition result from camera
            var recognitionResult = await GetRecognitionResultAsync(cameraIp, port, cancellationToken);
            
            if (recognitionResult == null || string.IsNullOrWhiteSpace(recognitionResult.Plate))
            {
                _logger.LogWarning("No recognition result found for camera {CameraIp}", cameraIp);
                return new RecognitionAndSendResult
                {
                    Success = false,
                    Message = "No plate recognized",
                    Plate = null,
                    BackendSent = false,
                    GateOpened = false,
                    IsDuplicate = false
                };
            }

            // Step 2: Check for duplicates using debounce logic
            // This prevents processing the same plate multiple times within the debounce period
            if (!_plateTrackingService.ShouldProcessPlate(cameraIp, recognitionResult.Plate, recognitionResult.Timestamp))
            {
                _logger.LogInformation("Duplicate plate detected and ignored: {Plate} from camera {CameraIp} (within debounce period)", 
                    recognitionResult.Plate, cameraIp);
                return new RecognitionAndSendResult
                {
                    Success = true,
                    Message = "Duplicate plate ignored (within debounce period)",
                    Plate = recognitionResult.Plate,
                    Timestamp = recognitionResult.Timestamp,
                    BackendSent = false,
                    GateOpened = false,
                    IsDuplicate = true
                };
            }

            _logger.LogInformation("Processing new plate: {Plate} from camera {CameraIp}", recognitionResult.Plate, cameraIp);

            // Step 3: Update display screen with plate information (Sambar)
            DateTime entranceTime = DateTime.Now;
            string textOne = "Zaisan";
            string textTwo = recognitionResult.Plate ?? string.Empty;
            string textThree = entranceTime.ToString("yyyy-MM-dd HH:mm:ss");
            string textFour = "Parkease";
            string voiceContent = $"Welcome {recognitionResult.Plate}, have a good day"; // Add voice content
            
            // Update display screen via camera's HTTP API at http://127.0.0.1:8000/swp-cloud/api/camera/display
            try
            {
                bool displaySuccess = await DisplayScreenAsync(cameraIp, port, textOne, textTwo, textThree, textFour, voiceContent, cancellationToken);
                if (displaySuccess)
                {
                    _logger.LogInformation("Display screen updated for camera {CameraIp}: {Text1} | {Text2} | {Text3} | {Text4}", 
                        cameraIp, textOne, textTwo, textThree, textFour);
                }
                else
                {
                    _logger.LogWarning("Failed to update display screen for camera {CameraIp}", cameraIp);
                }
            }
            catch (Exception displayEx)
            {
                _logger.LogWarning(displayEx, "Error updating display screen for camera {CameraIp}: {ErrorMessage}", 
                    cameraIp, displayEx.Message);
                // Don't fail the whole process if display update fails
            }

            // Step 4: Prepare plate data following your working controller EXACTLY
            // Match the exact structure from your working CreateProductAsync - NO Color field
            var plateData = new
            {
                mashiniiDugaar = recognitionResult.Plate ?? string.Empty,
                CAMERA_IP = cameraIp ?? string.Empty,
                burtgelOgnoo = entranceTime.ToString("yyyy-MM-ddTHH:mm:ss")
            };

            _logger.LogInformation("Prepared plate data: {PlateData}. Sending to backend API...", 
                System.Text.Json.JsonSerializer.Serialize(plateData));

            // Step 5: Send to backend API
            bool backendSuccess = await _plateService.SendPlateDataAsync(plateData, token);
            
            _logger.LogInformation("Backend API response for plate {Plate}: Success={Success}", 
                recognitionResult.Plate, backendSuccess);

            // Step 6: Record that this plate has been processed (only if we attempted to send)
            _plateTrackingService.RecordProcessedPlate(cameraIp, recognitionResult.Plate, recognitionResult.Timestamp);

            // Step 7: If backend fails, open gate locally (offline mode)
            bool gateOpened = false;
            if (!backendSuccess)
            {
                _logger.LogWarning("Backend API failed, opening gate locally for camera {CameraIp}", cameraIp);
                gateOpened = await OpenGateAsync(cameraIp, port, cancellationToken);
                
                if (gateOpened)
                {
                    _logger.LogInformation("Gate opened successfully in offline mode for camera {CameraIp}", cameraIp);
                }
            }

            return new RecognitionAndSendResult
            {
                Success = true,
                Message = backendSuccess ? "Plate sent to backend successfully" : "Backend failed, gate opened locally",
                Plate = recognitionResult.Plate,
                Timestamp = recognitionResult.Timestamp,
                BackendSent = backendSuccess,
                GateOpened = gateOpened,
                IsDuplicate = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RecognizeAndSendAsync for camera {CameraIp}", cameraIp);
            return new RecognitionAndSendResult
            {
                Success = false,
                Message = $"Error: {ex.Message}",
                Plate = null,
                BackendSent = false,
                GateOpened = false,
                IsDuplicate = false
            };
        }
    }
}

public record RecognitionAndSendResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Plate { get; init; }
    public long? Timestamp { get; init; }
    public bool BackendSent { get; init; }
    public bool GateOpened { get; init; }
    public bool IsDuplicate { get; init; }
}

public record CameraRecognitionResult(string Plate, long Timestamp);

// DTOs that match the camera recognition result API
public class CameraRecognitionApiResponse
{
    public CameraRecognitionData? data { get; set; }
    public string? respCode { get; set; }
    public string? respMsg { get; set; }
}

public class CameraRecognitionData
{
    public string? plate { get; set; }
    public long timestamp { get; set; }
}

// DTO for gate opening API
public class CameraGateApiResponse
{
    public string? respCode { get; set; }
    public string? respMsg { get; set; }
}

// DTO for display screen control API request
public class CameraDisplayRequest
{
    public string textOne { get; set; } = string.Empty;
    public string textTwo { get; set; } = string.Empty;
    public string textThree { get; set; } = string.Empty;
    public string textFour { get; set; } = string.Empty;
    public string voiceContent { get; set; } = string.Empty;
    public string ip { get; set; } = string.Empty;
}

// DTO for display screen control API response
public class CameraDisplayApiResponse
{
    public string? respCode { get; set; }
    public string? respMsg { get; set; }
}
