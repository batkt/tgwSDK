using ParkingService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers(); // Enable Controllers
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Plate tracking service for debounce logic
builder.Services.AddSingleton<IPlateTrackingService, PlateTrackingService>();

// Parking recognition service registration with HttpClient to call camera APIs.
builder.Services.AddHttpClient<IParkingRecognitionService, ParkingRecognitionService>();

// Plate service for sending data to backend API.
builder.Services.AddHttpClient<IPlateService, PlateService>();

// Background service to automatically poll cameras every 5 seconds
builder.Services.AddHostedService<CameraPollingBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers(); // Map Controller Endpoints

// 1) Obtain recognition result from camera
//    Wraps: GET http://{ip}:{port}/api/camera/scan/result?ip={ip}
app.MapGet("/api/camera/scan/result", async (string ip, int port, IParkingRecognitionService service, CancellationToken ct) =>
{
    var result = await service.GetRecognitionResultAsync(ip, port, ct);
    return result is null
        ? Results.StatusCode(StatusCodes.Status502BadGateway)
        : Results.Ok(result);
})
.WithName("GetCameraRecognitionResult")
.WithOpenApi();

// 2) Open gate via camera
//    Wraps: GET http://{ip}:{port}/api/camera/open/gate?ip={ip}
app.MapGet("/api/camera/open/gate", async (string ip, int port, IParkingRecognitionService service, CancellationToken ct) =>
{
    var success = await service.OpenGateAsync(ip, port, ct);
    return success
        ? Results.Ok(new { success = true })
        : Results.StatusCode(StatusCodes.Status502BadGateway);
})
.WithName("OpenCameraGate")
.WithOpenApi();

// 3) Complete flow: Recognize plate, send to backend, and open gate if backend fails
//    This follows the pattern from your existing controller
//    POST /api/parking/recognize-and-send?ip={cameraIp}&port={port}&token={bearerToken}
app.MapPost("/api/parking/recognize-and-send", async (
    string ip, 
    int port, 
    string token, 
    IParkingRecognitionService service, 
    CancellationToken ct) =>
{
    var result = await service.RecognizeAndSendAsync(ip, port, token, ct);
    
    if (!result.Success)
    {
        return Results.BadRequest(result);
    }
    
    return Results.Ok(result);
})
.WithName("RecognizeAndSendParkingData")
.WithOpenApi();

// 4) Poll all static configured cameras and process any recognized plates
//    GET /api/parking/poll-all?token={bearerToken}
app.MapGet("/api/parking/poll-all", async (
    string token, 
    IParkingRecognitionService service, 
    CancellationToken ct) =>
{
    var results = await service.PollAllCamerasAsync(token, ct);
    return Results.Ok(new { 
        cameraCount = results.Count,
        results = results 
    });
})
.WithName("PollAllCameras")
.WithOpenApi();

// 5) Get list of configured static camera IPs
//    GET /api/parking/cameras
app.MapGet("/api/parking/cameras", (IParkingRecognitionService service) =>
{
    var cameras = service.GetConfiguredCameraIPs();
    return Results.Ok(new { 
        count = cameras.Count,
        cameras = cameras 
    });
})
.WithName("GetConfiguredCameras")
.WithOpenApi();

// 6) Display screen control API
//    NOTE: This endpoint is now implemented in ApiController.cs using SDK directly
//    POST /api/camera/display - Updates physical LED screen via AlprSDK_Trans2Screen
//    The SDK implementation in ApiController provides direct hardware control

app.Run();
