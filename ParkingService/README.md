## ParkingService

This is an ASP.NET Core Web API project that will handle parking-related operations,
such as recognizing vehicles and sending data to your external parking service API.

### Current structure

- **`Program.cs`**: Configures the minimal API and exposes a sample endpoint:
  - `POST /api/parking/recognize` – calls `IParkingRecognitionService.RecognizeAndSendAsync(imagePath)`.
- **`Services/ParkingRecognitionService.cs`**:
  - Contains the `IParkingRecognitionService` interface and a stub implementation.
  - Right now it just returns `true` and does not call any external APIs yet.

### Where to plug in your real parking API

When you provide your API details (URL, authentication, payload format, etc.), we will:

1. Update `ParkingRecognitionService.RecognizeAndSendAsync` to:
   - Perform recognition (e.g. license plate, vehicle info).
   - Call your parking backend API (HTTP client, gRPC, etc.).
2. Optionally add configuration in `appsettings.json` (API base URL, keys, etc.).
3. Extend or add endpoints in `Program.cs` (for check-in, check-out, tariffs, reports, etc.).

You can run the API locally with:

```bash
dotnet run --project ParkingService
```

