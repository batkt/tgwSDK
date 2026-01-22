using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ParkingService.Services;

/// <summary>
/// Service to send plate recognition data to the backend parking API.
/// </summary>
public interface IPlateService
{
    /// <summary>
    /// Sends plate recognition data to the backend API with Bearer token authentication.
    /// </summary>
    /// <param name="plateData">The plate recognition data object</param>
    /// <param name="token">Bearer token for authentication</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> SendPlateDataAsync(object plateData, string token);
}

public class PlateService : IPlateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PlateService> _logger;
    private readonly string _backendBaseUrl;
    private readonly string _endpointPath;

    public PlateService(HttpClient httpClient, IConfiguration configuration, ILogger<PlateService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _backendBaseUrl = configuration["ParkingBackend:BaseUrl"] ?? "http://103.143.40.230:8081/";
        _endpointPath = configuration["ParkingBackend:EndpointPath"] ?? "zogsoolSdkService"; // Default endpoint to match your implementation
        
        // Set base address if not already set
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(_backendBaseUrl);
        }
        
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<bool> SendPlateDataAsync(object plateData, string token)
    {
        try
        {
            // Follow EXACT pattern from your controller: check BaseAddress with new Uri() comparison
            if (_httpClient.BaseAddress != new Uri(_backendBaseUrl))
            {
                _httpClient.BaseAddress = new Uri(_backendBaseUrl);
                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                // Update authorization header with the provided token (BaseAddress already set)
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }

            // Use endpoint path from configuration - default to "zogsoolSdkService" to match your implementation
            string endpoint = _endpointPath ?? "zogsoolSdkService";

            _logger.LogInformation("Sending plate data to backend: {BackendUrl}{EndpointPath}", _backendBaseUrl, endpoint);

            // Follow EXACT pattern from your working controller: use PostAsJsonAsync (System.Text.Json)
            // This matches the working CreateProductAsync method exactly
            var response = await _httpClient.PostAsJsonAsync(endpoint, plateData);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Plate data sent successfully to {Endpoint}", endpoint);
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to send plate data to {Endpoint}. Status: {Status}, Response: {Response}", 
                    endpoint, response.StatusCode, errorContent);
                
                // If endpoint was empty and got 404, suggest configuring endpoint path
                if (string.IsNullOrEmpty(endpoint) && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogError("Backend rejected POST to root path. Please configure 'ParkingBackend:EndpointPath' in appsettings.json with the correct endpoint (e.g., 'api/plates', 'api/parking/plates', etc.)");
                }
                
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending plate data to backend");
            return false;
        }
    }
}
