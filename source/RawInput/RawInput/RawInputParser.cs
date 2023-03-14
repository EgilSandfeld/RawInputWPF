using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Serilog;
using SharpDX.RawInput;

namespace RawInput.RawInput
{
    public static class RawInputParser
    {
        private static Dictionary<string, string> _oemNames = new ();
        private static readonly List<string> RetCodeErrors = new();

        /// <summary>
        /// Logic explained: https://www.codeproject.com/Articles/185522/Using-the-Raw-Input-API-to-Process-Joystick-Input
        /// </summary>
        /// <param name="hidInput"></param>
        /// <param name="pressedButtons"></param>
        /// <param name="hidName"></param>
        /// <param name="oemName"></param>
        /// <param name="isFFB"></param>
        /// <returns></returns>
        public static bool Parse(HidInputEventArgs hidInput, out List<ushort> pressedButtons, string hidName, out string oemName, out bool isFFB)
        {
            var preparsedData = IntPtr.Zero;
            pressedButtons = new List<ushort>();
            oemName = hidName;
            isFFB = false;
            try
            {
                preparsedData = GetPreparsedData(hidInput.Device);
                if (preparsedData == IntPtr.Zero)
                {
                    if (RawInputListener.DebugMode)
                        Log.Verbose("RawInputParser.Parse: preparsedData zero");
                    
                    return false;
                }

                HIDP_CAPS hidCaps;

                var getCapsResult = HidP_GetCaps(preparsedData, out hidCaps);
                if (getCapsResult == HIDP_STATUS_INVALID_PREPARSED_DATA)
                {
                    if (RawInputListener.DebugMode)
                        Log.Verbose("RawInputParser.Parse: HIDP_STATUS_INVALID_PREPARSED_DATA");
                    
                    return false;
                }
                
                if (!CheckError(getCapsResult, -2, 0))
                {
                    if (RawInputListener.DebugMode)
                        Log.Verbose("RawInputParser.Parse: CheckError getCapsResult -2");
                    
                    return false;
                }
                
                oemName = GetDeviceName(hidName);
                (pressedButtons, isFFB) = GetPressedButtons(hidCaps, preparsedData, hidInput.RawData);
            }
            catch (Win32Exception ex)
            {
                var exceptionId = ex.Data.Contains("ExceptionId") ? ex.Data["ExceptionId"].ToString() : "None";
                var errorMsg = $"RawInputParser.Parse: {ex.Message} | ExceptionId: {exceptionId}";
                Log.Verbose(errorMsg);
                RawInputListener.ExceptionLog?.Invoke(ex, errorMsg);
                return false;
            }
            finally
            {
                if (preparsedData != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(preparsedData);
                }
            }

            return true;
        }


        #region InternalMethods

        private static bool CheckError(int retCode, int usagePage, int count)
        {
            //Why to ignore HIDP_STATUS_INCOMPATIBLE_REPORT_ID: https://www.codeproject.com/Articles/185522/Using-the-Raw-Input-API-to-Process-Joystick-Input?msg=3866398#xx3866398xx
            if (retCode is HIDP_STATUS_SUCCESS or HIDP_STATUS_INCOMPATIBLE_REPORT_ID or HIDP_STATUS_USAGE_NOT_FOUND/* or HIDP_STATUS_INVALID_REPORT_LENGTH or HIDP_STATUS_INVALID_PREPARSED_DATA*/) 
                return true;
            
            var error = Marshal.GetLastWin32Error();
            var hResult = Marshal.GetHRForLastWin32Error();
            var exceptionId = $"{error}|{hResult}|{usagePage}|{retCode}|{count}";
                
            if (RetCodeErrors.Contains(exceptionId)) 
                return false;
                
            RetCodeErrors.Add(exceptionId);
            var ex = new Win32Exception();
            ex.Data.Add("ExceptionId", exceptionId);
            throw ex;
        }

        private static IntPtr GetPreparsedData(IntPtr device)
        {
            uint reqDataSize = 0;
            GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, IntPtr.Zero, ref reqDataSize);

            var preparsedData = Marshal.AllocHGlobal((int)reqDataSize);

            GetRawInputDeviceInfo(device, RIDI_PREPARSEDDATA, preparsedData, ref reqDataSize);

            return preparsedData;
        }

        private static (List<ushort>, bool) GetPressedButtons(HIDP_CAPS hidCaps, IntPtr preparsedData, byte[] rawInputData)
        {
            var ffbMotorsLength = hidCaps.NumberOutputValueCaps;
            var isFFB = ffbMotorsLength > 0;
            var buttonCapsLength = hidCaps.NumberInputButtonCaps;
            var buttonCaps = new HIDP_BUTTON_CAPS[buttonCapsLength];
            var res = new List<ushort>();
            if (!CheckError(HidP_GetButtonCaps(HIDP_REPORT_TYPE.HidP_Input, buttonCaps, ref buttonCapsLength, preparsedData), -1, buttonCapsLength))
            {
                if (RawInputListener.DebugMode)
                    Log.Verbose("RawInputParser.GetPressedButtons: HidP_GetButtonCaps, buttonCapsLength: {ButtonCapsLength}, preparsedData: {PreparsedData}", buttonCapsLength, preparsedData);
                
                return (res, ffbMotorsLength > 0);
            }

            if (RawInputListener.DebugMode && isFFB)
                Log.Verbose("RawInputParser.GetPressedButtons: HidP_GetButtonCaps, buttonCaps: {ButtonCaps}, buttonCapsLength: {ButtonCapsLength}", string.Join("+", buttonCaps.Select(x => $"{x.UsagePage}:{x.Usage}")), buttonCapsLength);
            
            //var usagePages = new HashSet<ushort>();
            //foreach (var bc in buttonCaps)
            //    usagePages.Add(bc.UsagePage);
            
            
            // foreach (var usagePage in usagePages)
            foreach (var buttonCap in buttonCaps)
            {
                // int usageListLength = hidCaps.NumberInputButtonCaps;
                var buttonsLength = HidP_MaxUsageListLength(HIDP_REPORT_TYPE.HidP_Input, buttonCap.UsagePage, preparsedData);
                
                if (buttonsLength <= 0)
                    buttonsLength = buttonCap.Reserved1;
                
                if (buttonsLength <= 0)
                    continue;
                
                var usageList = new ushort[buttonsLength];

                var result = HidP_GetUsages(HIDP_REPORT_TYPE.HidP_Input, buttonCap.UsagePage, 0/*buttonCap.LinkCollection*/,  usageList, ref buttonsLength, preparsedData, rawInputData, rawInputData.Length);

                switch (result)
                {
                    //The collection does not contain any buttons on the specified usage page in any report of the specified report type.
                    //https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getusages
                    case HIDP_STATUS_USAGE_NOT_FOUND:
                    //The usage page might not contain buttons Why to continue from HIDP_STATUS_INCOMPATIBLE_REPORT_ID
                    //https://www.codeproject.com/Articles/185522/Using-the-Raw-Input-API-to-Process-Joystick-Input?msg=3866398#xx3866398xx
                    case HIDP_STATUS_INCOMPATIBLE_REPORT_ID:
                    //Try continuing from this issue
                    //Indicates that the report length provided in ReportLength is not the expected length of a report of the type specified in ReportType.
                    case HIDP_STATUS_INVALID_REPORT_LENGTH:
                    {
                        if (RawInputListener.DebugMode)
                            Log.Verbose("RawInputParser.GetPressedButtons: HidP_GetUsages ignored error, result: {Result}, UsagePage: {UsagePage}, buttonsLength: {ButtonsLength}, rawInputData.Length: {Length}", result, buttonCap.UsagePage, buttonsLength, rawInputData.Length);

                        continue;
                    }
                }

                if (!CheckError(result, buttonCap.UsagePage, buttonsLength))
                {
                    if (RawInputListener.DebugMode)
                        Log.Verbose("RawInputParser.GetPressedButtons: HidP_GetUsages error, result: {Result}, UsagePage: {UsagePage}, buttonsLength: {ButtonsLength}, rawInputData.Length: {Length}", result, buttonCap.UsagePage, buttonsLength, rawInputData.Length);
                    
                    return (res, isFFB);
                }

                for (var i = 0; i < buttonsLength; ++i)
                    res.Add(usageList[i]);
            }

            if (RawInputListener.DebugMode && isFFB)
                Log.Verbose("RawInputParser.GetPressedButtons: HidP_GetUsages OK, res: ({Res}), FFB: {FfbMotorsLength}", string.Join("+", res), ffbMotorsLength);

            
            return (res, isFFB);
        }

        private static string GetDeviceName(string hidInterfacePath)
        {
            if (_oemNames.ContainsKey(hidInterfacePath))
                return _oemNames[hidInterfacePath];

            //\\?\HID#VID_044F&PID_B10A#8&27a93c19&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}
            //\\?\HID#VID_044F&PID_B10A&MI_00#8&27a93c19&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}
            var pathSplit = hidInterfacePath.Replace(@"\\?\HID#", "").Split('#')[0].Split('&');
            var vid = pathSplit.FirstOrDefault(x => x.StartsWith("VID"));
            var pid = pathSplit.FirstOrDefault(x => x.StartsWith("PID"));
            var vidPid = $"{vid}&{pid}";
            try
            {
                if (RawInputListener.DebugMode)
                    Log.Verbose("RawInputParser.GetDeviceName: vidPid: {VidPid}, hidInterfacePath: {HidInterfacePath}", vidPid, hidInterfacePath);
                
                if (GetDeviceNameByJoystickOEM(vidPid, out var oemName))
                {
                    if (RawInputListener.DebugMode)
                        Log.Verbose("RawInputParser.GetDeviceName: GetDeviceNameByJoystickOEM found: {OemName}", oemName);
                    
                    _oemNames.Add(hidInterfacePath, oemName);
                    return oemName;
                }
                
                if (GetDeviceNameByHIDCLASS(hidInterfacePath, out oemName))
                {
                    if (RawInputListener.DebugMode)
                        Log.Verbose("RawInputParser.GetDeviceName: GetDeviceNameByHIDCLASS found: {OemName}", oemName);
                    
                    _oemNames.Add(hidInterfacePath, oemName);
                    return oemName;
                }
                    
                if (RawInputListener.DebugMode)
                    Log.Verbose("RawInputParser.GetDeviceName: Defaulting to: {VidPid}", vidPid);
                
                _oemNames.Add(hidInterfacePath, vidPid);
                return vidPid;
            }
            catch (Exception ex)
            {
                Log.Verbose("RawInputParser.GetDeviceName: Error: {Message}, vidPid: {VidPid}, hidInterfacePath: {HidInterfacePath}", ex.Message, vidPid,hidInterfacePath);
                _oemNames.Add(hidInterfacePath, vidPid);
                return vidPid;
            }
        }
        
        private static bool GetDeviceNameByJoystickOEM(string vidPid, out string oemName)
        {
            oemName = string.Empty;
            var oemPath = @"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\" + vidPid;
            var key = Registry.CurrentUser.OpenSubKey(oemPath);

            var oemNameObject = key?.GetValue("OEMName");
            if (oemNameObject == null)
                return false;
                
            oemName = oemNameObject.ToString();
            return true;
        }

        private static bool GetDeviceNameByHIDCLASS(string hidInterfacePath, out string oemName)
        {
            oemName = string.Empty;
            if (!hidInterfacePath.Contains("HIDCLASS"))
                return false;
            
            //\\?\HID#HIDCLASS&Col02#1&4784345&1&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}
            var hidId = hidInterfacePath.Replace(@"\\?\HID#", "");
            var hidIdSplit = hidId.Split('#');

            if (hidIdSplit.Length < 2)
                return false;
            
            var hidClass = hidIdSplit[0];
            var hidClass2 = hidIdSplit[1];
            
            //Computer\HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\HID\HIDCLASS&Col02\1&2d595ca7&2&0001
            // var oemPath = $@"System\CurrentControlSet\Enum\ROOT\HIDCLASS\{hidclass}";
            var oemPath = $@"System\CurrentControlSet\Enum\HID\{hidClass}\{hidClass2}";
            RegistryKey key = Registry.LocalMachine.OpenSubKey(oemPath);

            var hardwareIdObject = key?.GetValue("HardwareID");
            if (hardwareIdObject == null)
                return false;
            
            //HID\VID_1234&PID_BEAD&REV_0219&Col02
            var pathSplit = (hardwareIdObject as string[])?[0].Replace(@"HID\", "").Split('&');
            
            if (pathSplit == null)
                return false;
            
            var vid = pathSplit.FirstOrDefault(x => x.StartsWith("VID"));
            var pid = pathSplit.FirstOrDefault(x => x.StartsWith("PID"));
            var vidPid = $"{vid}&{pid}";
            
            return GetDeviceNameByJoystickOEM(vidPid, out oemName);
        }

        #endregion InternalMethods


        #region NativeHidApi

        private const int ERROR_INVALID_DATA = 13;

        private enum HIDP_REPORT_TYPE
        {
            HidP_Input,
            HidP_Output,
            HidP_Feature,
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;

            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct HIDP_BUTTON_CAPS
        {
            [FieldOffset(0)] public ushort UsagePage;
            [FieldOffset(2)] public byte ReportID;
            [FieldOffset(3)] public byte IsAlias;
            [FieldOffset(4)] public ushort BitField;
            [FieldOffset(6)] public ushort LinkCollection;
            [FieldOffset(8)] public ushort LinkUsage;
            [FieldOffset(10)] public ushort LinkUsagePage;
            [FieldOffset(12)] public byte IsRange;
            [FieldOffset(13)] public byte IsStringRange;
            [FieldOffset(14)] public byte IsDesignatorRange;
            [FieldOffset(15)] public byte IsAbsolute;
            [FieldOffset(56)] public ushort Usage;
            [FieldOffset(58)] public ushort Reserved1;
            [FieldOffset(60)] public ushort StringIndex;
            [FieldOffset(62)] public ushort Reserved2;
            [FieldOffset(64)] public ushort DesignatorIndex;
            [FieldOffset(66)] public ushort Reserved3;
            [FieldOffset(68)] public ushort DataIndex;
            [FieldOffset(70)] public ushort Reserved4;
        }

        // HID status codes (all codes see in <hidpi.h>).
        private const int HIDP_STATUS_SUCCESS = (0x0 << 28) | (0x11 << 16) | 0;

        #region HIDP Error Codes

        //USB HID usage table http://www.freebsddiary.org/APC/usb_hid_usages.php
        //Descriptions from https://www.freepatentsonline.com/6311228.html
        
        /// <summary>
        /// Indicates that the buttons states specified by the parameter UsagePage is known, but cannot be found in the data provided at Report.
        /// </summary>
        private const int HIDP_STATUS_INCOMPATIBLE_REPORT_ID = -1072627702;
        
        /// <summary>
        /// Indicates that button states specified by the parameter UsagePage cannot be found in any data report for the HiD device.
        /// </summary>
        private const int HIDP_STATUS_USAGE_NOT_FOUND = -1072627708;
        
        /// <summary>
        /// Indicates that the report length provided in ReportLength is not the expected length of a report of the type specified in ReportType.
        /// </summary>
        private const int HIDP_STATUS_INVALID_REPORT_LENGTH  = -1072627709;
        
        /// <summary>
        /// Indicates the preparsed HID device data provided at PreparsedData is malformed.
        /// </summary>
        private const int HIDP_STATUS_INVALID_PREPARSED_DATA  = -1072627711;

        #endregion

        
        // https://msdn.microsoft.com/ru-ru/library/windows/desktop/ms645597(v=vs.85).aspx
        // Commands for GetRawInputDeviceInfo
        private const uint RIDI_PREPARSEDDATA = 0x20000005;

        // https://msdn.microsoft.com/ru-ru/library/windows/desktop/ms645597(v=vs.85).aspx
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr outData, ref uint dataSize);

        // http://msdn.microsoft.com/en-us/library/windows/hardware/ff539715%28v=vs.85%29.aspx
        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        // http://msdn.microsoft.com/en-us/library/windows/hardware/ff539707%28v=vs.85%29.aspx
        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetButtonCaps(HIDP_REPORT_TYPE reportType, [In, Out] HIDP_BUTTON_CAPS[] buttonCaps,
            ref ushort buttonCapsLength, IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetUsages(HIDP_REPORT_TYPE reportType, ushort usagePage, ushort linkCollection,
            [In, Out] ushort[] usageList, ref int usageLength, IntPtr preparsedData,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 7)]
            byte[] report, int reportLength);
        
        
        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_MaxUsageListLength(HIDP_REPORT_TYPE reportType, ushort usagePage, IntPtr preparsedData);

        #endregion NativeHidApi
    }
}