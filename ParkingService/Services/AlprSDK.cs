using System;
using System.Runtime.InteropServices;

namespace ParkingService.Services // Or just namespace AlprSDK if preferred, but keeping inside project structure
{
    public static class AlprSDK
    {
        // Enums
        public enum EAPIClientType
        {
            E_CLIENT_NORMAL = 0
        }

        public enum ELPRDevType
        {
            LPR_DEV_GZ = 0
        }

        // Structs
        [StructLayout(LayoutKind.Sequential)]
        public struct DEVINFO
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szIP;
            public ushort u16Port;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szUser;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPwd;
            public int uUseP2PConn;
            public ushort lprDevType;

            public void Init(string type, string ip, ushort port, string user, string pwd, ushort devType)
            {
                szIP = ip;
                u16Port = port;
                szUser = user;
                szPwd = pwd;
                lprDevType = devType;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PLATE_ITEM
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] szLicense;
            public byte plateColor;
            // Add other fields if necessary, padding might be needed
            public int nProcessTime; 
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PLATE_INFO
        {
            public int nPlateNum;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] // Assuming max 8 plates
            public PLATE_ITEM[] pPlate;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECOG_ALL_INFO
        {
            public PLATE_INFO PlateInfo;
        }

        // Delegates
        public delegate void ServerFindCallback(int nDeviceType, string pDeviceName,
                   string pIP, IntPtr macAddr, ushort wPortWeb, ushort wPortListen, string pSubMask,
                   string pGateway, string pMultiAddr, string pDnsAddr, ushort wMultiPort,
                   int nChannelNum, int nFindCount, int dwDeviceID);

        public delegate void RecogAllInfoCallback(ref RECOG_ALL_INFO pRecogAllInfo, IntPtr pUserData);
        public delegate void CarSpaceStateCallback(int handle, int state); // Guessing signature
        public delegate void DeviceCaptureCallback(int handle, IntPtr pData, int len); // Guessing signature

        // Constants
        // Dummies for methods (Replace with DllImport if you have the DLL name, e.g., "AlprSDK.dll")
        // Since I don't have the DLL, I'll return mock values to allow compilation and basic testing.
        
        public static int AlprSDK_SetConnectTimeout(int handle, int timeout) => 0;
        
        public static int AlprSDK_ConnectDev(int handle, ref DEVINFO devInfo, EAPIClientType clientType) => 0; // Return 0 for success handle? or specific ID
        
        public static int AlprSDK_DisConnectDev(int handle) => 0;
        
        public static int AlprSDK_UnInitHandle(int handle) => 0;
        
        public static int AlprSDK_InitHandle(int handle, IntPtr reserved) => 0;
        
        public static int AlprSDK_StartVideo(int handle) => 0;
        
        public static int AlprSDK_CreateRecogAllInfoTask(int handle, RecogAllInfoCallback callback, IntPtr pUserData) => 0;
        
        public static int AlprSDK_ClearRecogAllInfoTask(int handle) => 0;
        
        public static int AlprSDK_SearchAllCameras(uint interval, ServerFindCallback callback) 
        {
             // Mock behavior: invoke callback immediately or return
             return 0; 
        }
        
        public static int AlprSDK_OpenGate(int handle) => 0;
        
        public static int AlprSDK_Trans2Screen(int handle, int type, int row1, byte[] text1, int row2, byte[] text2, int row3, byte[] text3, int row4, byte[] text4) => 0;
        
        public static int AlprSDK_SendHeartBeat(int handle) => 0;

        public static int AlprSDK_Startup(IntPtr reserved, int msg) => 0;
        
        public static int AlprSDK_StartupWithPath(string path) => 0;

    }
}
