using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SharpDX.RawInput;

namespace RawInputWPF.RawInput
{
    public static class RawInputParser
    {
        private static Dictionary<string, string> _oemNames = new ();
        private static readonly List<string> RetCodeErrors = new();

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
                    return false;

                HIDP_CAPS hidCaps;
                if (!CheckError(HidP_GetCaps(preparsedData, out hidCaps), -2, 0))
                    return false;
                
                oemName = GetDeviceName(hidName);
                (pressedButtons, isFFB) = GetPressedButtons(hidCaps, preparsedData, hidInput.RawData);
            }
            catch (Win32Exception ex)
            {
                var exceptionId = ex.Data.Contains("ExceptionId") ? ex.Data["ExceptionId"].ToString() : "None";
                var errorMsg = $"RawInputParser.Parse: {ex.Message} | ExceptionId: {exceptionId}";
                RawInputListener.Log.Verbose(errorMsg);
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
            if (retCode is HIDP_STATUS_SUCCESS or HIDP_STATUS_INCOMPATIBLE_REPORT_ID or HIDP_STATUS_USAGE_NOT_FOUND/* or HIDP_STATUS_INVALID_REPORT_LENGTH*/) 
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
            var buttonCapsLength = hidCaps.NumberInputButtonCaps;
            var buttonCaps = new HIDP_BUTTON_CAPS[buttonCapsLength];
            var res = new List<ushort>();
            if (!CheckError(HidP_GetButtonCaps(HIDP_REPORT_TYPE.HidP_Input, buttonCaps, ref buttonCapsLength, preparsedData), -1, buttonCapsLength))
                return (res, ffbMotorsLength > 0);

            var usagePages = new HashSet<ushort>();
            foreach (var bc in buttonCaps)
                usagePages.Add(bc.UsagePage);
            
            foreach (var usagePage in usagePages)
            {
                int usageListLength = hidCaps.NumberInputButtonCaps;
                var usageList = new ushort[usageListLength];

                if (!CheckError(HidP_GetUsages(HIDP_REPORT_TYPE.HidP_Input, usagePage, 0,  usageList, ref usageListLength, preparsedData, rawInputData, rawInputData.Length), usagePage, usageListLength))
                    return (res, ffbMotorsLength > 0);

                for (var i = 0; i < usageListLength; ++i)
                    res.Add(usageList[i]);
            }

            return (res, ffbMotorsLength > 0);
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
                if (GetDeviceNameByJoystickOEM(vidPid, out var oemName))
                {
                    _oemNames.Add(hidInterfacePath, oemName);
                    return oemName;
                }
                
                if (GetDeviceNameByHIDCLASS(hidInterfacePath, out oemName))
                {
                    _oemNames.Add(hidInterfacePath, oemName);
                    return oemName;
                }
                    
                _oemNames.Add(hidInterfacePath, vidPid);
                return vidPid;
            }
            catch (Exception ex)
            {
                RawInputListener.Log.Verbose("RawInputParser.GetDeviceName: Error: {Message}, vidPid: {VidPid}, hidInterfacePath: {HidInterfacePath}", ex.Message, vidPid,hidInterfacePath);
                _oemNames.Add(hidInterfacePath, vidPid);
                return vidPid;
            }
        }

        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="vidPid"></param>
        /// <param name="oemName"></param>
        /// <returns></returns>
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
            var hidclass = hidInterfacePath.Replace(@"\\?\HID#HIDCLASS&Col02#1&4784345&1&", "").Split('#')[0];
            var oemPath = $@"System\CurrentControlSet\Enum\ROOT\HIDCLASS\{hidclass}";
            RegistryKey key = Registry.LocalMachine.OpenSubKey(oemPath);

            var deviceDescObject = key?.GetValue("DeviceDesc");
            if (deviceDescObject == null)
                return false;

            var deviceDesc = deviceDescObject.ToString();
            var deviceDescSplit = deviceDesc.Split(';');
            if (deviceDescSplit.Length <= 1)
                return false;

            oemName = deviceDescSplit[1];
            return true;
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

        #endregion NativeHidApi
    }
}