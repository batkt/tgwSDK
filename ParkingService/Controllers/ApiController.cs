using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using ParkingService.Services; // Changed from implied dotnetApi
using Newtonsoft.Json;

namespace ParkingService.Controllers; // Changed from dotnetApi.Controllers

[ApiController]
[Route("[controller]")]
public class apiController : ControllerBase
{
    // Removing callbacks type definition dependency for now until AlprSDK is linked, 
    // but keeping structure as provided.
    // Assuming AlprSDK is reachable or will be added.
    public AlprSDK.ServerFindCallback? findCallback = null;
    public AlprSDK.RecogAllInfoCallback? RecogAllInfoCallback = null;
    public AlprSDK.CarSpaceStateCallback FunCarSpaceStateCallback = null;
    public AlprSDK.DeviceCaptureCallback delegateDeviceCaptureCallback = null;
    private AlprSDK.DEVINFO m_Camera = new AlprSDK.DEVINFO();
    List<string> lisIP = new List<string>();
    Dictionary<string, ushort> dictionary = new Dictionary<string, ushort>();
    private const int AlprSDK_MSG = 0x500;
    
    // Uncommented token as per your code
    private const string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6IjYxMmY0NTdkMTg1MjgwZGI2NzZkMGI1MyIsIm5lciI6IkNBZG1pbiIsImJhaWd1dWxsYWdpaW5JZCI6IjYxMmY0NTdkMTg1MjgwZGI2NzZkMGI1MSIsImlhdCI6MTc2Nzc0NzYyMn0.5vl903FZm4AxNTnJiRuQ6yz86hL2LTngXAmI1tE20Nk";

    private int m_dwLoginID = -1;
    private ushort m_Port = 5000;
    private string m_LoginName = "admin";
    private string m_LoginPassword = "admin";
    private string m_LoginPasswordgz = "123456";
    private IntPtr m_pUserData = IntPtr.Zero;
    private Dictionary<byte, string> colordic = new Dictionary<byte, string>() { { 0, "Black" }, { 20, "Green" }, { 30, "Blue" }, { 50, "Yellow" }, { 255, "White" } };
    int ControlHandle = 0;
    static HttpClient client = new HttpClient()
    {
        Timeout = TimeSpan.FromSeconds(30) 
    };
    
    // Static callbacks
    private static AlprSDK.RecogAllInfoCallback? staticCallback1 = null;
    private static AlprSDK.RecogAllInfoCallback? staticCallback2 = null;
    private static AlprSDK.RecogAllInfoCallback? staticCallback3 = null;
    private static AlprSDK.RecogAllInfoCallback? staticCallback4 = null;
    private static AlprSDK.RecogAllInfoCallback? staticCallback5 = null;
    private static AlprSDK.RecogAllInfoCallback? staticCallback6 = null;
    private static AlprSDK.RecogAllInfoCallback? staticCallback7 = null;
    private static AlprSDK.RecogAllInfoCallback? staticCallback8 = null;
    private static AlprSDK.ServerFindCallback? staticServerFindCallback = null;
    
    public static apiController? staticControllerInstance = null;
    
    private static CriticalErrorLogger? _staticErrorLogger = null;
    private static ILogger<apiController>? _staticLogger = null;
    private readonly ILogger<apiController>? _logger;
    private readonly IConfiguration? _configuration;

    public static IPlateService? PlateService { get; set; }
    public static string CurrentToken => token;
    
    public class MockPlateRequest
    {
        public string Plate { get; set; } = string.Empty;
        public string? CameraIp { get; set; }
    }

    public class DisplayScreenRequest
    {
        public string textOne { get; set; } = string.Empty;
        public string textTwo { get; set; } = string.Empty;
        public string textThree { get; set; } = string.Empty;
        public string textFour { get; set; } = string.Empty;
        public string voiceContent { get; set; } = string.Empty;
        public string ip { get; set; } = string.Empty;
    }

    public class DisplayScreenResponse
    {
        public string respCode { get; set; } = string.Empty;
        public string respMsg { get; set; } = string.Empty;
    }

    public apiController(IPlateService plateService, ILogger<apiController> logger, IConfiguration configuration)
    {
        PlateService = plateService;
        _logger = logger;
        _staticLogger = logger;
        _configuration = configuration;
        
        if (staticCallback1 == null)
        {
            staticControllerInstance = this;
            
            staticCallback1 = RecogResultCallback1;
            staticCallback2 = RecogResultCallback2;
            staticCallback3 = RecogResultCallback3;
            staticCallback4 = RecogResultCallback4;
            staticCallback5 = RecogResultCallback5;
            staticCallback6 = RecogResultCallback6;
            staticCallback7 = RecogResultCallback7;
            staticCallback8 = RecogResultCallback8;
            staticServerFindCallback = ServerFindCallback;
            
            Console.WriteLine("Static callbacks initialized to prevent GC");
        }
    }

    [HttpPost("mock/plate")]
    public async Task<IActionResult> MockPlateAndSend([FromBody] MockPlateRequest request)
    {
        if (string.IsNullOrEmpty(request.Plate))
            return BadRequest("Plate number is required");

        string ip = string.IsNullOrEmpty(request.CameraIp) ? "192.168.1.103" : request.CameraIp;
        
        // Match the exact structure from your working controller - NO Color field
        var product = new
        {
            mashiniiDugaar = request.Plate,
            CAMERA_IP = ip,
            burtgelOgnoo = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
        };

        if (PlateService != null)
        {
             bool success = await PlateService.SendPlateDataAsync(product, token);
             if (success)
             {
                 return Ok(new { success = true, message = "Sent to backend successfully" });
             }
             else
             {
                 // Log the payload for debugging
                 var jsonPayload = JsonConvert.SerializeObject(product);
                 _logger?.LogWarning("Backend API call failed. Payload: {Payload}", jsonPayload);
                 return Ok(new { 
                     success = false, 
                     message = "Backend failed - check logs for details",
                     payload = product
                 });
             }
        }
        return StatusCode(500, "PlateService not initialized");
    }
    
    public static void SetErrorLogger(CriticalErrorLogger logger, ILogger<apiController> controllerLogger)
    {
        _staticErrorLogger = logger;
        _staticLogger = controllerLogger;
        logger.LogCriticalOperation("SYSTEM", "LoggerInitialized", "SUCCESS");
    }
    
    private static CriticalErrorLogger? GetErrorLogger()
    {
        return _staticErrorLogger;
    }

    private class ipObject
    {
        public string ip;
        public int handle;
    }
    private static List<ipObject>? handleList = null;

    private bool ConnectCameraWithRetry(int handle, string ip, ushort port, ushort type, string password, int maxRetries = 5)
    {
        const int baseTimeout = 5000; 
        const int baseDelay = 1000; 
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                int timeout = baseTimeout + (attempt * 500); 
                int timeoutResult = AlprSDK.AlprSDK_SetConnectTimeout(handle, timeout);
                GetErrorLogger()?.LogSdkCall("AlprSDK_SetConnectTimeout", handle, timeoutResult, 
                    new Dictionary<string, object> { ["IP"] = ip, ["Attempt"] = attempt, ["Timeout"] = timeout });

                m_Camera.szIP = ip;
                m_Camera.u16Port = port;
                m_Camera.szUser = m_LoginName;
                m_Camera.szPwd = password;
                m_Camera.uUseP2PConn = 0;
                m_Camera.lprDevType = (ushort)AlprSDK.ELPRDevType.LPR_DEV_GZ;
                
                GetErrorLogger()?.LogCriticalOperation("CAMERA_CONNECTION", $"Connecting_{ip}", 
                    attempt == 1 ? "ATTEMPTING" : $"RETRY_{attempt}", 
                    new Dictionary<string, object> { ["IP"] = ip, ["Port"] = port, ["Handle"] = handle, ["Attempt"] = attempt });
                
                Console.WriteLine($"Connecting to camera {ip} (handle: {handle}) - Attempt {attempt}/{maxRetries}");
                
                m_dwLoginID = AlprSDK.AlprSDK_ConnectDev(handle, ref m_Camera, AlprSDK.EAPIClientType.E_CLIENT_NORMAL);
                GetErrorLogger()?.LogSdkCall("AlprSDK_ConnectDev", handle, m_dwLoginID, 
                    new Dictionary<string, object> { ["IP"] = ip, ["Attempt"] = attempt });
                
                if (m_dwLoginID >= 0)
                {
                    Console.WriteLine($"Successfully connected to camera {ip} on attempt {attempt}");
                    GetErrorLogger()?.LogCriticalOperation("CAMERA_CONNECTION", $"Connected_{ip}", "SUCCESS",
                        new Dictionary<string, object> { ["IP"] = ip, ["Handle"] = handle, ["Attempt"] = attempt });
                    return true;
                }
                else
                {
                    Console.WriteLine($"Connection attempt {attempt} failed for {ip}: {m_dwLoginID}");
                    GetErrorLogger()?.LogCriticalError("CAMERA_CONNECTION", $"ConnectFailed_{ip}", null,
                        new Dictionary<string, object> { ["IP"] = ip, ["Handle"] = handle, ["LoginID"] = m_dwLoginID, ["Attempt"] = attempt });
                    
                    if (attempt < maxRetries)
                    {
                        int delay = baseDelay * attempt; 
                        Console.WriteLine($"Waiting {delay}ms before retry {attempt + 1}...");
                        System.Threading.Thread.Sleep(delay);
                        
                        try
                        {
                            AlprSDK.AlprSDK_DisConnectDev(handle);
                            AlprSDK.AlprSDK_UnInitHandle(handle);
                            int reinitResult = AlprSDK.AlprSDK_InitHandle(handle, IntPtr.Zero);
                            GetErrorLogger()?.LogSdkCall("AlprSDK_InitHandle_Retry", handle, reinitResult, 
                                new Dictionary<string, object> { ["IP"] = ip, ["Attempt"] = attempt + 1 });
                        }
                        catch (Exception cleanupEx)
                        {
                            Console.WriteLine($"Cleanup error before retry: {cleanupEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during connection attempt {attempt} for {ip}: {ex.Message}");
                GetErrorLogger()?.LogCriticalError("CAMERA_CONNECTION", $"ConnectException_{ip}", ex,
                    new Dictionary<string, object> { ["IP"] = ip, ["Handle"] = handle, ["Attempt"] = attempt });
                
                if (attempt < maxRetries)
                {
                    int delay = baseDelay * attempt;
                    System.Threading.Thread.Sleep(delay);
                }
            }
        }
        
        Console.WriteLine($"Failed to connect to camera {ip} after {maxRetries} attempts");
        GetErrorLogger()?.LogCriticalError("CAMERA_CONNECTION", $"ConnectFailed_AllRetries_{ip}", null,
            new Dictionary<string, object> { ["IP"] = ip, ["Handle"] = handle, ["MaxRetries"] = maxRetries });
        
        try
        {
            AlprSDK.AlprSDK_DisConnectDev(handle);
            AlprSDK.AlprSDK_UnInitHandle(handle);
        }
        catch { }
        
        m_dwLoginID = -1;
        return false;
    }

    private void ServerFindCallback(int nDeviceType, string pDeviceName,
                   string pIP, IntPtr macAddr, ushort wPortWeb, ushort wPortListen, string pSubMask,
                   string pGateway, string pMultiAddr, string pDnsAddr, ushort wMultiPort,
                   int nChannelNum, int nFindCount, int dwDeviceID)
    {
        try
        {
            Console.WriteLine("Server Find callback ajillaa");
            Console.WriteLine("oldson niit camera: " + nFindCount);

            if (pIP != null)
            {
                Console.WriteLine("oldson ip: " + pIP);
                lisIP.Add(pIP);
                if (nDeviceType >= 0x60 && nDeviceType <= 0x80)
                    dictionary.Add(pIP, 2);
                else if (nDeviceType >= 0x0 && nDeviceType < 0x60)
                    dictionary.Add(pIP, 1);
                else
                    dictionary.Add(pIP, 0);
            }
            else
            {
                string[] ipArray = {"192.168.1.11", "192.168.1.12", "192.168.1.14"}; 
                int niitCallback = 3;
                
                AlprSDK.RecogAllInfoCallback[] RecogAllInfoCallbacks = new AlprSDK.RecogAllInfoCallback[niitCallback];
                RecogAllInfoCallbacks[0] = staticCallback1 ?? RecogResultCallback1;
                RecogAllInfoCallbacks[1] = staticCallback2 ?? RecogResultCallback2;
                RecogAllInfoCallbacks[2] = staticCallback3 ?? RecogResultCallback3;
                RecogAllInfoCallbacks[3] = staticCallback4 ?? RecogResultCallback4;

                for (int i = 0; i < ipArray.Length; i++)
                {
                    if (handleList == null)
                    {
                        handleList = new List<ipObject>();
                    }

                    ipObject ipObject = new ipObject();
                    ipObject.ip = ipArray[i];
                    ipObject.handle = i;
                    int iRet = AlprSDK.AlprSDK_InitHandle(i, IntPtr.Zero);
                    GetErrorLogger()?.LogSdkCall("AlprSDK_InitHandle", i, iRet, new Dictionary<string, object> { ["IP"] = ipArray[i] });
                    
                    ushort type = 0;
                    dictionary.TryGetValue(ipArray[i], out type);
                    string password = m_LoginPasswordgz;
                    ushort port = m_Port;
                    port = (ushort)(ipArray[i] == "192.168.2.12" ? 80 : 443);
                    m_Camera.Init("IPC", ipArray[i], port, m_LoginName, password, type);
                    handleList.Add(ipObject);
                    if (iRet >= 0)
                    {
                        bool connected = ConnectCameraWithRetry(i, ipArray[i], port, type, password, maxRetries: 5);
                        
                        if (connected && m_dwLoginID >= 0)
                        {
                            Console.WriteLine("Connect func khariu: " + m_dwLoginID);
                            try
                            {
                                if (m_pUserData != IntPtr.Zero)
                                {
                                    Marshal.FreeHGlobal(m_pUserData);
                                }
                                _ = AlprSDK.AlprSDK_StartVideo(i);
                                m_pUserData = Marshal.StringToHGlobalAnsi(m_Camera.szIP);
                                Console.WriteLine("m_pUserData " + m_pUserData);
                                int iReturn = 0;
                                if (i == 0)
                                {
                                    iReturn = AlprSDK.AlprSDK_CreateRecogAllInfoTask(i, staticCallback1 ?? RecogResultCallback1, m_pUserData);
                                }
                                else if (i == 1)
                                {
                                    iReturn = AlprSDK.AlprSDK_CreateRecogAllInfoTask(i, staticCallback2 ?? RecogResultCallback2, m_pUserData);
                                }
                                else if (i == 2)
                                {
                                    iReturn = AlprSDK.AlprSDK_CreateRecogAllInfoTask(i, staticCallback3 ?? RecogResultCallback3, m_pUserData);
                                }
                                else if (i == 3)
                                {
                                    iReturn = AlprSDK.AlprSDK_CreateRecogAllInfoTask(i, staticCallback4 ?? RecogResultCallback4, m_pUserData);
                                }
                                if (iReturn >= 0)
                                {
                                    Console.WriteLine("Callback amjilttai uuslee: " + iReturn);
                                }
                                else
                                {
                                    Console.WriteLine("Callback faildlee");
                                }
                            }
                            catch (Exception error)
                            {
                                Console.WriteLine("Aldaa: " + error.ToString());
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Failed to connect to camera {ipArray[i]} after all retries");
                            handleList.Remove(ipObject);
                        }
                        Console.WriteLine("Burtgegdsen Camera count: " + handleList.Count);

                    }
                }
            }
        }
        catch (Exception error)
        {
            Console.WriteLine("ServerFindCallback Aldaa: " + error.ToString());
        }
    }

    private void RecogResultCallback1(ref AlprSDK.RECOG_ALL_INFO pRecogAllInfo, IntPtr pUserData)
    {
        try
        {
            GetErrorLogger()?.LogCriticalOperation("CALLBACK", "RecogResultCallback1", "EXECUTED",
                new Dictionary<string, object> { ["PlateCount"] = pRecogAllInfo.PlateInfo.nPlateNum });
            
            string tukhainCamera = cameraIpAvya(0);
            Console.WriteLine("Amjilttai tanikh callback ajillaa");
            if (client.BaseAddress != new Uri("http://103.143.40.230:8081/"))
            {
                client.BaseAddress = new Uri("http://103.143.40.230:8081/");
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
            }
            async Task CreateProductAsync(object product, string cameraIp)
            {
                 if (PlateService != null)
                 {
                    bool success = await PlateService.SendPlateDataAsync(product, CurrentToken);
                    if (!success)
                    {
                        int handle = cameraHandleAvya(cameraIp);
                        if (handle > -1)
                        {
                            AlprSDK.AlprSDK_OpenGate(handle);
                            Console.WriteLine($"Offline mode: Gate opened for {cameraIp}");
                        }
                    }
                 }
            }
            try
            {
                for (int i = 0; i < pRecogAllInfo.PlateInfo.nPlateNum; i++)
                {
                    Encoding utf8 = Encoding.UTF8;
                    string CarNO = utf8.GetString(pRecogAllInfo.PlateInfo.pPlate[i].szLicense);
                    byte[] utf8Bytes = utf8.GetBytes(CarNO);
                    Console.WriteLine("RecogResultCallback 111" + CarNO);
                    string Color = "None";
                    colordic.TryGetValue(pRecogAllInfo.PlateInfo.pPlate[i].plateColor, out Color);
                    int time = utf8Bytes.Length;
                    if (string.IsNullOrEmpty(CarNO))
                    {
                        return;
                    }
                    else
                    {
                        var a = new
                        {
                            mashiniiDugaar = CarNO,
                            CAMERA_IP = tukhainCamera,
                            burtgelOgnoo = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                            Color = Color
                        };
                        Console.WriteLine("mashinii Dugaar burtgegdlee:" + a.mashiniiDugaar);
                        
                        // Update display screen with plate information
                        DateTime entranceTime = DateTime.Now;
                        UpdateDisplayScreenForPlate(tukhainCamera, CarNO, entranceTime);
                        
                        _ = CreateProductAsync(a, tukhainCamera); 
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("orj irlee , %d%", ex);
                GetErrorLogger()?.LogCriticalError("CALLBACK", "RecogResultCallback1_PlateProcessing", ex,
                    new Dictionary<string, object> { ["CameraIP"] = tukhainCamera });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("RecogResultCallback1 aldaa , %d%", ex);
            GetErrorLogger()?.LogCriticalError("CALLBACK", "RecogResultCallback1", ex);
        }

    }
    
    // Simplified callbacks 2-8 to avoid duplication in this artifact, 
    // assuming they follow the exact same pattern as RecogResultCallback1
    // but with index changes (0->1, etc)
    
    private void RecogResultCallback2(ref AlprSDK.RECOG_ALL_INFO pRecogAllInfo, IntPtr pUserData)
    {
         RecogResultCallbackGeneric(ref pRecogAllInfo, pUserData, 1, "RecogResultCallback2");
    }

    private void RecogResultCallback3(ref AlprSDK.RECOG_ALL_INFO pRecogAllInfo, IntPtr pUserData)
    {
         RecogResultCallbackGeneric(ref pRecogAllInfo, pUserData, 2, "RecogResultCallback3");
    }

    private void RecogResultCallback4(ref AlprSDK.RECOG_ALL_INFO pRecogAllInfo, IntPtr pUserData)
    {
         RecogResultCallbackGeneric(ref pRecogAllInfo, pUserData, 3, "RecogResultCallback4");
    }
    
    // Placeholders for 5-8 as they were commented/unused in ServerFindCallback but defined
    private void RecogResultCallback5(ref AlprSDK.RECOG_ALL_INFO pRecogAllInfo, IntPtr pUserData) => RecogResultCallbackGeneric(ref pRecogAllInfo, pUserData, 4, "RecogResultCallback5");
    private void RecogResultCallback6(ref AlprSDK.RECOG_ALL_INFO pRecogAllInfo, IntPtr pUserData) => RecogResultCallbackGeneric(ref pRecogAllInfo, pUserData, 5, "RecogResultCallback6");
    private void RecogResultCallback7(ref AlprSDK.RECOG_ALL_INFO pRecogAllInfo, IntPtr pUserData) => RecogResultCallbackGeneric(ref pRecogAllInfo, pUserData, 6, "RecogResultCallback7");
    private void RecogResultCallback8(ref AlprSDK.RECOG_ALL_INFO pRecogAllInfo, IntPtr pUserData) => RecogResultCallbackGeneric(ref pRecogAllInfo, pUserData, 7, "RecogResultCallback8");

    private void RecogResultCallbackGeneric(ref AlprSDK.RECOG_ALL_INFO pRecogAllInfo, IntPtr pUserData, int cameraIndex, string callbackName)
    {
        try
        {
            string tukhainCamera = cameraIpAvya(cameraIndex);
            Console.WriteLine($"Amjilttai tanikh callback ajillaa {callbackName}");
            if (client.BaseAddress != new Uri("http://103.143.40.230:8081/"))
            {
                client.BaseAddress = new Uri("http://103.143.40.230:8081/");
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            async Task CreateProductAsync(object product, string cameraIp)
            {
                 if (PlateService != null)
                 {
                    bool success = await PlateService.SendPlateDataAsync(product, CurrentToken);
                    if (!success)
                    {
                        int handle = cameraHandleAvya(cameraIp);
                        if (handle > -1)
                        {
                            AlprSDK.AlprSDK_OpenGate(handle);
                            Console.WriteLine($"Offline mode: Gate opened for {cameraIp}");
                        }
                    }
                 }
            }
            try
            {
                for (int i = 0; i < pRecogAllInfo.PlateInfo.nPlateNum; i++)
                {
                    Encoding utf8 = Encoding.UTF8;
                    string CarNO = utf8.GetString(pRecogAllInfo.PlateInfo.pPlate[i].szLicense);
                    Console.WriteLine($"{callbackName} " + CarNO);
                    string Color = "None";
                    colordic.TryGetValue(pRecogAllInfo.PlateInfo.pPlate[i].plateColor, out Color);
                    if (string.IsNullOrEmpty(CarNO))
                    {
                        return;
                    }
                    else
                    {
                        var a = new
                        {
                            mashiniiDugaar = CarNO,
                            CAMERA_IP = tukhainCamera,
                            burtgelOgnoo = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                            Color = Color
                        };
                        Console.WriteLine("mashinii Dugaar burtgegdlee:" + a.mashiniiDugaar);
                        
                        // Update display screen with plate information
                        DateTime entranceTime = DateTime.Now;
                        UpdateDisplayScreenForPlate(tukhainCamera, CarNO, entranceTime);
                        
                        _ = CreateProductAsync(a, tukhainCamera);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("orj irlee , %d%", ex);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{callbackName} aldaa , %d%", ex);
        }
    }

    public int cameraHandleAvya(string ip)
    {
        try
        {
            if (handleList == null)
            {
                Console.WriteLine($"handleList is null. SDK may not be initialized. Call /api/kholboy first.");
                return -1;  // Changed to -1 to distinguish from valid handle 0
            }

            ipObject? songogdsonCamera = handleList.Find(i => i.ip == ip);

            if (songogdsonCamera == null)
            {
                Console.WriteLine($"IP address: {ip} camera oldsongui (not found in handleList)");
                return -1;  // Changed to -1 to distinguish from valid handle 0
            }

            int songogdsonHandle = songogdsonCamera.handle;

            return songogdsonHandle;
        }
        catch (Exception error)
        {
            Console.WriteLine("Aldaa: " + error.ToString());
            return -1;  // Changed to -1 to distinguish from valid handle 0
        }
    }
    public string cameraIpAvya(int handle)
    {
        try
        {
            ipObject songogdsonCamera = handleList.Find(i => i.handle == handle);

            if (songogdsonCamera == null)
            {
                Console.WriteLine($"Handle: {handle} camera oldsongui");
            }

            string songogdsonIp = songogdsonCamera.ip;

            return songogdsonIp;
        }
        catch (Exception error)
        {
            Console.WriteLine("Aldaa: " + error.ToString());
            return "0";
        }
    }

    public async Task Khaikh(AlprSDK.ServerFindCallback callback)
    {
        try
        {
            Console.WriteLine("Khaij ekhellee");
            uint interval = 3000;
            dictionary = new Dictionary<string, ushort>();
            AlprSDK.ServerFindCallback callbackToUse = staticServerFindCallback ?? callback;
            if (staticServerFindCallback == null)
            {
                staticServerFindCallback = callbackToUse;
            }
            int iRet = AlprSDK.AlprSDK_SearchAllCameras(interval, callbackToUse);
            if (iRet == 0)
            {
                // Get camera IPs from API at 127.0.0.1:8000/swp-cloud
                List<string> configuredIPs = new List<string>();
                string cameraApiBaseUrl = _configuration?["ParkingService:CameraApiBaseUrl"] ?? "http://127.0.0.1:8000";
                
                try
                {
                    // Try to get camera list from API
                    // Common endpoints: /swp-cloud/api/cameras, /swp-cloud/api/camera/list, /swp-cloud/api/camera/ips
                    string[] possibleEndpoints = {
                        $"{cameraApiBaseUrl}/swp-cloud/api/cameras",
                        $"{cameraApiBaseUrl}/swp-cloud/api/camera/list",
                        $"{cameraApiBaseUrl}/swp-cloud/api/camera/ips"
                    };
                    
                    bool found = false;
                    foreach (var endpoint in possibleEndpoints)
                    {
                        try
                        {
                            var response = await client.GetAsync(endpoint);
                            if (response.IsSuccessStatusCode)
                            {
                                var content = await response.Content.ReadAsStringAsync();
                                Console.WriteLine($"Camera list API response from {endpoint}: {content}");
                                
                                // Try to parse JSON response
                                try
                                {
                                    var jsonResponse = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content);
                                    
                                    // Try different response formats
                                    if (jsonResponse.TryGetProperty("data", out var data))
                                    {
                                        if (data.ValueKind == System.Text.Json.JsonValueKind.Array)
                                        {
                                            foreach (var item in data.EnumerateArray())
                                            {
                                                if (item.TryGetProperty("ip", out var ipProp))
                                                    configuredIPs.Add(ipProp.GetString() ?? "");
                                                else if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                                                    configuredIPs.Add(item.GetString() ?? "");
                                            }
                                        }
                                    }
                                    else if (jsonResponse.TryGetProperty("cameras", out var cameras))
                                    {
                                        if (cameras.ValueKind == System.Text.Json.JsonValueKind.Array)
                                        {
                                            foreach (var item in cameras.EnumerateArray())
                                            {
                                                if (item.TryGetProperty("ip", out var ipProp))
                                                    configuredIPs.Add(ipProp.GetString() ?? "");
                                                else if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                                                    configuredIPs.Add(item.GetString() ?? "");
                                            }
                                        }
                                    }
                                    else if (jsonResponse.ValueKind == System.Text.Json.JsonValueKind.Array)
                                    {
                                        foreach (var item in jsonResponse.EnumerateArray())
                                        {
                                            if (item.TryGetProperty("ip", out var ipProp))
                                                configuredIPs.Add(ipProp.GetString() ?? "");
                                            else if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                                                configuredIPs.Add(item.GetString() ?? "");
                                        }
                                    }
                                    
                                    if (configuredIPs.Count > 0)
                                    {
                                        found = true;
                                        Console.WriteLine($"Successfully retrieved {configuredIPs.Count} camera IPs from API: {string.Join(", ", configuredIPs)}");
                                        break;
                                    }
                                }
                                catch (Exception parseEx)
                                {
                                    Console.WriteLine($"Failed to parse camera list response: {parseEx.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to get camera list from {endpoint}: {ex.Message}");
                        }
                    }
                    
                    // Fallback to configuration if API call failed
                    if (!found && _configuration != null)
                    {
                        var cameraIPsSection = _configuration.GetSection("ParkingService:CameraIPs");
                        configuredIPs = cameraIPsSection.Get<List<string>>() ?? new List<string>();
                        if (configuredIPs.Count > 0)
                        {
                            Console.WriteLine($"Using {configuredIPs.Count} camera IPs from configuration: {string.Join(", ", configuredIPs)}");
                        }
                    }
                    
                    // Final fallback to hardcoded IPs
                    if (configuredIPs.Count == 0)
                    {
                        configuredIPs = new List<string> { "192.168.1.11", "192.168.1.12", "192.168.1.14" };
                        Console.WriteLine("Using fallback camera IPs from code");
                    }
                }
                catch (Exception apiEx)
                {
                    Console.WriteLine($"Error getting camera list from API: {apiEx.Message}");
                    // Fallback to configuration
                    if (_configuration != null)
                    {
                        var cameraIPsSection = _configuration.GetSection("ParkingService:CameraIPs");
                        configuredIPs = cameraIPsSection.Get<List<string>>() ?? new List<string> { "192.168.1.11", "192.168.1.12", "192.168.1.14" };
                        Console.WriteLine($"Using camera IPs from configuration as fallback: {string.Join(", ", configuredIPs)}");
                    }
                    else
                    {
                        configuredIPs = new List<string> { "192.168.1.11", "192.168.1.12", "192.168.1.14" };
                        Console.WriteLine("Using fallback camera IPs from code");
                    }
                }
                
                string[] ipArray = configuredIPs.ToArray();
                for (int i = 0; i < ipArray.Length; i++)
                {
                    if (handleList == null)
                    {
                        handleList = new List<ipObject>();
                    }
                    
                    ipObject ipObject = new ipObject();
                    ipObject.ip = ipArray[i];
                    ipObject.handle = i;
                    
                    // Initialize handle for this camera
                    int iRetInit = AlprSDK.AlprSDK_InitHandle(i, IntPtr.Zero);
                    GetErrorLogger()?.LogSdkCall("AlprSDK_InitHandle", i, iRetInit, new Dictionary<string, object> { ["IP"] = ipArray[i] });
                    
                    ushort type = 0;
                    dictionary.TryGetValue(ipArray[i], out type);
                    string password = m_LoginPasswordgz;
                    ushort port = m_Port;
                    port = (ushort)(ipArray[i] == "192.168.2.12" ? 80 : 443);
                    m_Camera.Init("IPC", ipArray[i], port, m_LoginName, password, type);
                    handleList.Add(ipObject);
                    
                    if (iRetInit >= 0)
                    {
                        bool connected = ConnectCameraWithRetry(i, ipArray[i], port, type, password, maxRetries: 3);
                        if (connected && m_dwLoginID >= 0)
                        {
                            Console.WriteLine($"Camera {ipArray[i]} initialized and connected (handle: {i})");
                        }
                        else
                        {
                            Console.WriteLine($"Camera {ipArray[i]} initialized but connection failed (handle: {i})");
                        }
                    }
                }
                HeartBeat();
                Console.WriteLine($"=== Khail func amjilttai duuslaa - {handleList?.Count ?? 0} camera(s) initialized ===");
                if (handleList != null && handleList.Count > 0)
                {
                    Console.WriteLine($"Cameras in handleList: {string.Join(", ", handleList.Select(h => $"{h.ip}(handle:{h.handle})"))}");
                }
            }
            else
            {
                Console.WriteLine($"WARNING: AlprSDK_SearchAllCameras returned {iRet}, cameras may not be initialized");
            }
        }
        catch (Exception error)
        {
            Console.WriteLine(" Khaikh Aldaa: " + error.ToString());
            Console.WriteLine($"Exception in Khaikh: {error.Message}");
            Console.WriteLine($"Stack trace: {error.StackTrace}");
        }
    }

    [Route("neeye/{ip}")]
    public ActionResult<string> neeye(string ip)
    {
        try
        {
            if (ip == "192.168.1.11" || ip == "192.168.1.12" || ip == "192.168.1.14")
            {
                int handle = cameraHandleAvya(ip);
                if (handle > -1)
                {
                    int iRet = AlprSDK.AlprSDK_OpenGate(handle);
                    Console.WriteLine("Khaalga ongoilgohoos irj bui khariu : " + iRet);
                }
            }
            return "Amjilttai";
        }
        catch (Exception error)
        {
            Console.WriteLine("neeye Aldaa: " + error.ToString());
            return "aldaa";
        }
    }
    
    // Helper method to update display screen when plate is recognized
    // Made static so it can be called from services
    public static void UpdateDisplayScreenForPlate(string cameraIp, string plateNumber, DateTime entranceTime)
    {
        try
        {
            if (staticControllerInstance == null)
            {
                Console.WriteLine($"Cannot update display for {cameraIp}: ApiController instance not available");
                return;
            }
            
            int handle = staticControllerInstance.cameraHandleAvya(cameraIp);
            if (handle < 0)
            {
                Console.WriteLine($"Cannot update display for {cameraIp}: camera handle not found");
                return;
            }

            // Format display text:
            // Line 1: "Zaisan" (hardcoded)
            // Line 2: Plate number (dynamic)
            // Line 3: Entrance time (dynamic)
            // Line 4: "Parkease" (hardcoded)
            string textOne = "Zaisan";
            string textTwo = plateNumber;
            string textThree = entranceTime.ToString("yyyy-MM-dd HH:mm:ss"); // Format: 2026-01-15 14:30:25
            string textFour = "Parkease";

            // Convert text strings to byte arrays (UTF-8 encoding)
            byte[] text1Bytes = System.Text.Encoding.UTF8.GetBytes(textOne);
            byte[] text2Bytes = System.Text.Encoding.UTF8.GetBytes(textTwo);
            byte[] text3Bytes = System.Text.Encoding.UTF8.GetBytes(textThree);
            byte[] text4Bytes = System.Text.Encoding.UTF8.GetBytes(textFour);

            // Call SDK to display text on screen
            int iRet = AlprSDK.AlprSDK_Trans2Screen(
                handle, 
                type: 0,  // Display type (0 = normal text display)
                row1: 1, 
                text1: text1Bytes, 
                row2: 2, 
                text2: text2Bytes, 
                row3: 3, 
                text3: text3Bytes, 
                row4: 4, 
                text4: text4Bytes
            );

            if (iRet == 0)
            {
                Console.WriteLine($"Display screen updated for camera {cameraIp}: Zaisan | {plateNumber} | {textThree} | Parkease");
            }
            else
            {
                Console.WriteLine($"Failed to update display screen for camera {cameraIp}, error code: {iRet}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating display screen for camera {cameraIp}: {ex.Message}");
        }
    }

    // Display Screen Control API - Sambar implementation
    [HttpPost("camera/display")]
    public ActionResult<DisplayScreenResponse> DisplayScreen([FromBody] DisplayScreenRequest request)
    {
        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.textOne) ||
                string.IsNullOrWhiteSpace(request.textTwo) ||
                string.IsNullOrWhiteSpace(request.textThree) ||
                string.IsNullOrWhiteSpace(request.textFour) ||
                string.IsNullOrWhiteSpace(request.ip))
            {
                return BadRequest(new DisplayScreenResponse 
                { 
                    respCode = "10000", 
                    respMsg = "All text fields and ip are required" 
                });
            }

            // Get camera handle from IP
            int handle = cameraHandleAvya(request.ip);
            if (handle < 0)  // Changed from <= 0 to < 0 because handle 0 is valid (first camera)
            {
                Console.WriteLine($"Camera handle not found or invalid for IP: {request.ip}, handle: {handle}");
                
                // Check if handleList is null or empty (SDK not initialized)
                if (handleList == null || handleList.Count == 0)
                {
                    return Ok(new DisplayScreenResponse 
                    { 
                        respCode = "10000", 
                        respMsg = $"SDK not initialized. Please call GET /api/kholboy first to initialize cameras." 
                    });
                }
                
                return Ok(new DisplayScreenResponse 
                { 
                    respCode = "10000", 
                    respMsg = $"Camera not found for IP: {request.ip}. Camera may not be initialized. Call GET /api/kholboy to initialize cameras first." 
                });
            }

            // Convert text strings to byte arrays (UTF-8 encoding)
            byte[] text1Bytes = System.Text.Encoding.UTF8.GetBytes(request.textOne);
            byte[] text2Bytes = System.Text.Encoding.UTF8.GetBytes(request.textTwo);
            byte[] text3Bytes = System.Text.Encoding.UTF8.GetBytes(request.textThree);
            byte[] text4Bytes = System.Text.Encoding.UTF8.GetBytes(request.textFour);

            // Call SDK to display text on screen
            // Parameters: handle, type (0 = normal text), row positions (1-4), text byte arrays
            int iRet = AlprSDK.AlprSDK_Trans2Screen(
                handle, 
                type: 0,  // Display type (0 = normal text display)
                row1: 1, 
                text1: text1Bytes, 
                row2: 2, 
                text2: text2Bytes, 
                row3: 3, 
                text3: text3Bytes, 
                row4: 4, 
                text4: text4Bytes
            );

            GetErrorLogger()?.LogSdkCall("AlprSDK_Trans2Screen", handle, iRet, 
                new Dictionary<string, object> 
                { 
                    ["IP"] = request.ip,
                    ["Text1"] = request.textOne,
                    ["Text2"] = request.textTwo,
                    ["Text3"] = request.textThree,
                    ["Text4"] = request.textFour
                });

            if (iRet == 0)
            {
                Console.WriteLine($"Display screen updated successfully for camera {request.ip}");
                // Note: voiceContent is not handled by Trans2Screen SDK method
                // Voice functionality may need separate SDK call or HTTP API
                if (!string.IsNullOrWhiteSpace(request.voiceContent))
                {
                    Console.WriteLine($"Voice content provided but not sent via SDK: {request.voiceContent}");
                }
                
                return Ok(new DisplayScreenResponse 
                { 
                    respCode = "00000", 
                    respMsg = "success" 
                });
            }
            else
            {
                Console.WriteLine($"Failed to update display screen for camera {request.ip}, error code: {iRet}");
                GetErrorLogger()?.LogCriticalError("DISPLAY_SCREEN", "AlprSDK_Trans2Screen", null,
                    new Dictionary<string, object> 
                    { 
                        ["IP"] = request.ip, 
                        ["Result"] = iRet 
                    });
                
                return Ok(new DisplayScreenResponse 
                { 
                    respCode = "10000", 
                    respMsg = $"Failed to update display screen, SDK error: {iRet}" 
                });
            }
        }
        catch (Exception error)
        {
            Console.WriteLine($"DisplayScreen Aldaa: {error.ToString()}");
            GetErrorLogger()?.LogCriticalError("DISPLAY_SCREEN", "DisplayScreen", error);
            return Ok(new DisplayScreenResponse 
            { 
                respCode = "10000", 
                respMsg = $"Error: {error.Message}" 
            });
        }
    }

    [HttpGet("kholboy")]
    public async Task<ActionResult<string>> Kholboy()
    {
        try
        {
            GetErrorLogger()?.LogCriticalOperation("SDK_INIT", "Kholboy", "STARTED");
            Console.WriteLine("=== Kholbolt func ruu orloo ===");
            Console.WriteLine($"handleList before init: {(handleList == null ? "NULL" : $"Count={handleList.Count}")}");

            findCallback = staticServerFindCallback ?? new AlprSDK.ServerFindCallback(ServerFindCallback);
            if (staticServerFindCallback == null)
            {
                staticServerFindCallback = findCallback;
            }
            
            int iRet = AlprSDK.AlprSDK_Startup(IntPtr.Zero, AlprSDK_MSG);
            GetErrorLogger()?.LogSdkCall("AlprSDK_Startup", -1, iRet);
            Console.WriteLine($"AlprSDK_Startup result: {iRet}");
            
            if (iRet == 0)
            {
                Console.WriteLine("Amjilttai aslaa: " + iRet);
                GetErrorLogger()?.LogCriticalOperation("SDK_INIT", "AlprSDK_Startup", "SUCCESS");
                Console.WriteLine("Calling Khaikh to initialize cameras...");
                await Khaikh(findCallback); // Wait for camera initialization to complete
                Console.WriteLine($"=== Kholboy completed. handleList count: {handleList?.Count ?? 0} ===");
                if (handleList != null && handleList.Count > 0)
                {
                    Console.WriteLine($"Initialized cameras: {string.Join(", ", handleList.Select(h => h.ip))}");
                }
                else
                {
                    Console.WriteLine("WARNING: handleList is still null or empty after Khaikh!");
                }
            }
            else
            {
                Console.WriteLine(" sdk assangui: " + iRet);
                GetErrorLogger()?.LogCriticalError("SDK_INIT", "AlprSDK_Startup", null,
                    new Dictionary<string, object> { ["Result"] = iRet });
            }
            return "Amjilttai";
        }
        catch (Exception error)
        {
            Console.WriteLine("Kholboy Aldaa: " + error.ToString());
            GetErrorLogger()?.LogCriticalError("SDK_INIT", "Kholboy", error);
            return "aldaa";
        }
    }

    public void gantsCamerKholboy(string ip, int handle)
    {
        try
        {
            Console.WriteLine($"Reconnecting camera {ip} (handle: {handle})...");
            
            try { AlprSDK.AlprSDK_ClearRecogAllInfoTask(handle); } catch {}
            try { AlprSDK.AlprSDK_DisConnectDev(handle); } catch {}
            try { AlprSDK.AlprSDK_UnInitHandle(handle); } catch {}
            
            System.Threading.Thread.Sleep(100);
            
            int iRet = -1;
            try
            {
                iRet = AlprSDK.AlprSDK_InitHandle(handle, IntPtr.Zero);
                GetErrorLogger()?.LogSdkCall("AlprSDK_InitHandle_Reconnect", handle, iRet, 
                    new Dictionary<string, object> { ["IP"] = ip });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL: Error initializing handle {handle} for {ip}: {ex.Message}");
                return; 
            }
            
            ushort type = 0;
            dictionary.TryGetValue(ip, out type);
            string password = m_LoginPasswordgz;
            ushort port = m_Port;
            port = (ushort)(ip == "192.168.1.233" ? 80 : 443);
            m_Camera.Init("IPC", ip, port, m_LoginName, password, type);
            if (iRet >= 0)
            {
                bool connected = ConnectCameraWithRetry(handle, ip, port, type, password, maxRetries: 5);
                
                if (connected && m_dwLoginID >= 0)
                {
                    Console.WriteLine("Connect func khariu: " + m_dwLoginID);
                    try
                    {
                        int iReturn = 0;

                        if (handle == 0)
                        {
                            iReturn = AlprSDK.AlprSDK_CreateRecogAllInfoTask(handle, staticCallback1 ?? RecogResultCallback1, Marshal.StringToHGlobalAnsi(m_Camera.szIP));
                        }
                        else if (handle == 1)
                        {
                            iReturn = AlprSDK.AlprSDK_CreateRecogAllInfoTask(handle, staticCallback2 ?? RecogResultCallback2, Marshal.StringToHGlobalAnsi(m_Camera.szIP));
                        }
                         else if (handle == 2)
                        {
                            iReturn = AlprSDK.AlprSDK_CreateRecogAllInfoTask(handle, staticCallback3 ?? RecogResultCallback3, Marshal.StringToHGlobalAnsi(m_Camera.szIP));
                        }
                         else if (handle == 3)
                        {
                            iReturn = AlprSDK.AlprSDK_CreateRecogAllInfoTask(handle, staticCallback4 ?? RecogResultCallback4, Marshal.StringToHGlobalAnsi(m_Camera.szIP));
                        }
                        
                        if (iReturn >= 0)
                        {
                            Console.WriteLine("Callback amjilttai uuslee: " + iReturn);
                        }
                        else
                        {
                            Console.WriteLine("Callback faildlee");
                        }
                    }
                    catch (Exception error)
                    {
                        Console.WriteLine("Aldaa: " + error.ToString());
                    }
                }
                else
                {
                    Console.WriteLine($"Failed to reconnect camera {ip} after all retries");
                    m_dwLoginID = -1;
                }

            }
        }
        catch (Exception error)
        {
            Console.WriteLine("gantsCamerKholboy Aldaa: " + error.ToString());
        }
    }

    public void HeartBeat()
    {
        try
        {
            if (handleList == null)
            {
                GetErrorLogger()?.LogCriticalOperation("HEARTBEAT", "HeartBeat", "NO_CAMERAS",
                    new Dictionary<string, object> { ["HandleList"] = "null" });
                return;
            }
            
            var camerasToCheck = handleList.ToList();
            
            foreach (ipObject ipObject in camerasToCheck)
            {
                try
                {
                    int iRet = AlprSDK.AlprSDK_SendHeartBeat(ipObject.handle);
                    GetErrorLogger()?.LogSdkCall("AlprSDK_SendHeartBeat", ipObject.handle, iRet,
                        new Dictionary<string, object> { ["IP"] = ipObject.ip });
                    
                    if (iRet != 0)
                    {
                        Console.WriteLine("xolbolt baisangui " + iRet);
                        GetErrorLogger()?.LogCriticalError("HEARTBEAT", $"HeartbeatFailed_{ipObject.ip}", null,
                            new Dictionary<string, object> { ["IP"] = ipObject.ip, ["Handle"] = ipObject.handle });
                        
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                System.Threading.Thread.Sleep(200);
                                gantsCamerKholboy(ipObject.ip, ipObject.handle);
                            }
                            catch (Exception reconnectEx)
                            {
                                Console.WriteLine($"Error in background reconnection for {ipObject.ip}: {reconnectEx.Message}");
                            }
                        });
                    }
                    else
                    {
                        Console.WriteLine("xolbolt bainaa " + iRet);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error checking heartbeat for {ipObject.ip}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error in HeartBeat: {ex.Message}");
        }
    }

    public async Task RestartAllConnections()
    {
        try
        {
            Console.WriteLine("=== Starting connection restart process ===");
            
            if (handleList == null || handleList.Count == 0)
            {
                Kholboy();
                return;
            }

            var cameraIPs = handleList.Select(x => x.ip).ToList();
            Console.WriteLine($"Disconnecting {handleList.Count} camera(s)...");
            
            foreach (var ipObj in handleList.ToList())
            {
                try
                {
                    AlprSDK.AlprSDK_ClearRecogAllInfoTask(ipObj.handle);
                    AlprSDK.AlprSDK_DisConnectDev(ipObj.handle);
                    AlprSDK.AlprSDK_UnInitHandle(ipObj.handle);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error disconnecting {ipObj.ip}: {ex.Message}");
                }
            }

            handleList.Clear();
            lisIP.Clear();
            dictionary.Clear();

            await Task.Delay(200);

            Console.WriteLine("Reconnecting all cameras with optimized settings...");

            string[] ipArray = cameraIPs.ToArray();
            for (int i = 0; i < ipArray.Length; i++)
            {
                try
                {
                    if (handleList == null) handleList = new List<ipObject>();

                    ipObject ipObject = new ipObject();
                    ipObject.ip = ipArray[i];
                    ipObject.handle = i;

                    try { AlprSDK.AlprSDK_ClearRecogAllInfoTask(i); } catch { }
                    try { AlprSDK.AlprSDK_DisConnectDev(i); } catch { }
                    try { AlprSDK.AlprSDK_UnInitHandle(i); } catch { }
                    
                    await Task.Delay(100);
                    
                    int iRet = -1;
                    try
                    {
                        iRet = AlprSDK.AlprSDK_InitHandle(i, IntPtr.Zero);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"CRITICAL: Error initializing handle {i} for {ipArray[i]}: {ex.Message}");
                        continue;
                    }
                    
                    ushort type = 0;
                    dictionary.TryGetValue(ipArray[i], out type);
                    string password = m_LoginPasswordgz;
                    ushort port = m_Port;
                    port = (ushort)(ipArray[i] == "192.168.2.12" ? 80 : 443);
                    
                    m_Camera.Init("IPC", ipArray[i], port, m_LoginName, password, type);
                    handleList.Add(ipObject);

                    if (iRet >= 0)
                    {
                        bool connected = ConnectCameraWithRetry(i, ipArray[i], port, type, password, maxRetries: 5);
                        
                        if (connected && m_dwLoginID >= 0)
                        {
                            Console.WriteLine($"Camera {ipArray[i]} connected successfully (handle: {i})");
                            
                            try
                            {
                                if (m_pUserData != IntPtr.Zero)
                                    Marshal.FreeHGlobal(m_pUserData);
                                
                                _ = AlprSDK.AlprSDK_StartVideo(i);
                                m_pUserData = Marshal.StringToHGlobalAnsi(m_Camera.szIP);
                                
                                int iReturn = 0;
                                if (i == 0) iReturn = AlprSDK.AlprSDK_CreateRecogAllInfoTask(i, staticCallback1 ?? RecogResultCallback1, m_pUserData);
                                else if (i == 1) iReturn = AlprSDK.AlprSDK_CreateRecogAllInfoTask(i, staticCallback2 ?? RecogResultCallback2, m_pUserData);
                                else if (i == 2) iReturn = AlprSDK.AlprSDK_CreateRecogAllInfoTask(i, staticCallback3 ?? RecogResultCallback3, m_pUserData);
                                else if (i == 3) iReturn = AlprSDK.AlprSDK_CreateRecogAllInfoTask(i, staticCallback4 ?? RecogResultCallback4, m_pUserData);
                                
                                if (iReturn >= 0) Console.WriteLine($"Recognition task created for {ipArray[i]}");
                                else Console.WriteLine($"Failed to create recognition task for {ipArray[i]}");
                            }
                            catch (Exception error)
                            {
                                Console.WriteLine($"Error setting up recognition for {ipArray[i]}: {error.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Failed to connect camera {ipArray[i]} after all retries");
                            handleList.Remove(ipObject);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reconnecting camera {ipArray[i]}: {ex.Message}");
                }
            }

            Console.WriteLine($"=== Connection restart completed. {handleList.Count} camera(s) connected ===");
        }
        catch (Exception error)
        {
            Console.WriteLine($"RestartAllConnections error: {error.ToString()}");
        }
    }

    [HttpPost("restartConnections")]
    public ActionResult<string> RestartConnections()
    {
        try
        {
            Console.WriteLine("Manual connection restart requested via API");
            RestartAllConnections();
            return Ok($"Connection restart completed. {handleList?.Count ?? 0} camera(s) connected");
        }
        catch (Exception error)
        {
            Console.WriteLine($"RestartConnections API error: {error.ToString()}");
            return StatusCode(500, $"Error: {error.Message}");
        }
    }

    [HttpGet("errorStatistics")]
    public ActionResult<Dictionary<string, object>> GetErrorStatistics()
    {
        try
        {
            var logger = GetErrorLogger();
            if (logger == null)
            {
                return Ok(new Dictionary<string, object> 
                { 
                    ["Status"] = "Logger not initialized",
                    ["Timestamp"] = DateTime.UtcNow
                });
            }
            return Ok(logger.GetErrorStatistics());
        }
        catch (Exception error)
        {
            return StatusCode(500, $"Error: {error.Message}");
        }
    }

    [HttpPost("resetErrorCounters")]
    public ActionResult<string> ResetErrorCounters()
    {
        try
        {
            GetErrorLogger()?.ResetCounters();
            return Ok("Error counters reset successfully");
        }
        catch (Exception error)
        {
            return StatusCode(500, $"Error: {error.Message}");
        }
    }
}
