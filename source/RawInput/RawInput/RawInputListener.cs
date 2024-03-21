using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Interop;
using RawInput.Helpers;
using Serilog;
using SharpDX.Multimedia;
using SharpDX.RawInput;

namespace RawInput.RawInput
{
    public class RawInputListener
    {
        public event EventHandler<GamepadEventArgs> ButtonsChanged;
        public event EventHandler<MouseEventArgs> MouseButtonsChanged;
        public event EventHandler<KeyboardEventArgs> KeyDown;
        public event EventHandler<KeyboardEventArgs> KeyUp;
        public static bool DebugMode {get; set; }
        public bool PollAll {get; set; }
        public static ILogger Log;
        public static Action<Exception, string> ExceptionLog { get; set; }
        public bool IsInitialized => _hwndSource != null && _hwndSource.Handle != IntPtr.Zero;

        private Dictionary<UsagePage, List<UsageId>> _deviceTypes = new ();
        private HashSet<string> _devicesToPoll = new();
        private HashSet<string> _devicesToIgnore = new();
        private const int WM_INPUT = 0x00FF;
        private HwndSource _hwndSource;
        private IntPtr _hWindow;
        private bool _simucube2ProActualButtonsDown;

        private const string Simucube2Pro = "Simucube 2 Pro"; //"VID_28DE&PID_2300";


        public void Init(IntPtr hWnd, ILogger logger, Action<Exception, string> exceptionLogger)
        {
            if (IsInitialized)
                return;

            Log = logger ?? new LoggerConfiguration().CreateLogger();
            ExceptionLog = exceptionLogger;
            _hWindow = hWnd;
            _hwndSource = HwndSource.FromHwnd(hWnd);
            if (_hwndSource != null)
                _hwndSource.AddHook(WndProc);
            else
            {
                var ex = new Exception("_hwndSource is null");
                ex.Data.Add("StackTraceCustom", Environment.StackTrace);
                ExceptionLog(ex, $"_hwndSource is null, hWnd: {hWnd}");
                return;
            }

            RegisterDeviceTypes();

            Device.RawInput += OnRawInput;
            Device.KeyboardInput += OnKeyboardInput;
            Device.MouseInput += OnMouseInput;
        }

        private void RegisterDeviceTypes()
        {
            //RegisterDeviceType(UsagePage.Generic, UsageId.GenericKeyboard);
            //RegisterDeviceType(UsagePage.Generic, UsageId.GenericMouse);
            RegisterDeviceType(UsagePage.Generic, UsageId.GenericGamepad);
            RegisterDeviceType(UsagePage.Generic, UsageId.GenericJoystick);
            RegisterDeviceType(UsagePage.Generic, UsageId.GenericPointer);
            RegisterDeviceType(UsagePage.Generic, UsageId.SimulationSpaceshipSimulationDevice);

            //FANATEC ClubSport Wheel Base V2.5
            //FANATEC Podium Wheel Base DD1
            //FANATEC Podium Wheel Base DD2
            RegisterDeviceType(UsagePage.Generic, UsageId.LedSelectedIndicator);
            RegisterDeviceType(UsagePage.Generic, UsageId.GenericCountedBuffer);

            //Simucube 2 Pro
            RegisterDeviceType(UsagePage.Generic, UsageId.LedCompose);

            //HID-compliant device with FFB
            RegisterDeviceType(UsagePage.VendorDefinedBegin, UsageId.AlphanumericAlphanumericDisplay);
            
            
            //Logitech G HUB G923 Racing Wheel for XBox One and PC (USB)
            RegisterDeviceType((UsagePage)(-189), (UsageId)(1540));
                
            //Logitech G HUB G920 Driving Force Racing Wheel USB with UsagePage: -189, Usage: 1538
            RegisterDeviceType((UsagePage)(-189), (UsageId)(1538));
                
            //Logitech G HUB PRO Racing Wheel for PlayStation/PC with UsagePage: -189, Usage: 1794
            RegisterDeviceType((UsagePage)(-189), (UsageId)(1794));
                
            //PRO Racing Wheel for PlayStation/PC with UsagePage: -189, Usage: 1796
            RegisterDeviceType((UsagePage)(-189), (UsageId)(1796));
                
            //G923 Racing Wheel for Xbox One and PC with UsagePage: -3, Usage: -767
            RegisterDeviceType((UsagePage)(-3), (UsageId)(-767));
                
            //G29 Driving Force Racing Wheel with UsagePage: VendorDefinedBegin, Usage: GenericMouse
            RegisterDeviceType(UsagePage.VendorDefinedBegin, UsageId.GenericMouse);
        }

        public async Task<bool> RegisterDevice(UsagePage up, UsageId usageId, string interfacePath, bool ignore)
        {
            while (_hWindow == IntPtr.Zero)
                await Task.Delay(75);
            
            return RegisterDeviceType(up, usageId, interfacePath, ignore);
        }

        public void ChangePollingForDevice(string interfacePath, bool enable)
        {
            var interfacePathLowered = interfacePath.ToLower();
            if (enable && _devicesToPoll.Contains(interfacePathLowered) || !enable && !_devicesToPoll.Contains(interfacePathLowered))
                return;
                    
            if (enable)
                _devicesToPoll.Add(interfacePathLowered);
            else
                _devicesToPoll.Remove(interfacePathLowered);
        }

        private bool RegisterDeviceType(UsagePage up, UsageId usageId, string interfacePath = null, bool ignore = false)
        {
            if (interfacePath != null)
            {
                var interfacePathLowered = interfacePath.ToLower();
                if (ignore)
                {
                    /*if (_devicesToPoll.Contains(interfacePathLowered))
                        _devicesToPoll.Remove(interfacePathLowered);
                    else*/
                    _devicesToIgnore.Add(interfacePathLowered);
                    Log.ForContext("Context", "IO").Verbose("RawInputListener.RegisterDeviceType: Device ignored from polling (has no buttons): {UsagePage}:{UsageId} {InterfacePath}", up, usageId, interfacePath);
                    return false;
                }
                
                _devicesToPoll.Add(interfacePathLowered);
            }

            if (up == UsagePage.Undefined)
                return false;
            
            if (_deviceTypes.ContainsKey(up) && _deviceTypes[up].Contains(usageId))
            {
                Log.ForContext("Context", "IO").Verbose("RawInputListener.RegisterDeviceType: Device already added: {UsagePage}:{UsageId} {InterfacePath}", up, usageId, interfacePath);
                return false;
            }

            //var usageId = (UsageId)Enum.Parse(typeof(UsageId), usageIdString);
            Device.RegisterDevice(up, usageId, DeviceFlags.InputSink, _hWindow);
            
            if (!_deviceTypes.ContainsKey(up))
                _deviceTypes.Add(up, new List<UsageId> { usageId });
            else    
                _deviceTypes[up].Add(usageId);
            
            Log.ForContext("Context", "IO").Verbose("RawInputListener.RegisterDeviceType: Device type added: {UsagePage}:{UsageId}", up, usageId);
            return true;
        }

        public void Clear()
        {
            Device.RawInput -= OnRawInput;
            Device.MouseInput -= OnMouseInput;
            Device.KeyboardInput -= OnKeyboardInput;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_INPUT)
            {
                Device.HandleMessage(lParam, hwnd);
            }

            return IntPtr.Zero;
        }

        private void OnMouseInput(object sender, MouseInputEventArgs e)
        {
            var handler = MouseButtonsChanged;
            if (handler == null)
                return;

            var deviceName = "";
            if (e.Device != IntPtr.Zero)
                deviceName = DeviceHelper.SearchDevice(e.Device)?.DeviceName;

            if (string.IsNullOrEmpty(deviceName))
                return;
            
            var evt = new MouseEventArgs(deviceName, e.ButtonFlags);

            if (evt.Buttons == MouseButtonFlags.None)
                return;
            
            handler(this, evt);
        }

        private void OnKeyboardInput(object sender, KeyboardInputEventArgs e)
        {
            if (e.State != KeyState.KeyDown && e.State != KeyState.KeyUp)
                return;

            var handler = e.State == KeyState.KeyDown ? KeyDown : KeyUp;
            if (handler == null)
                return;

            var deviceName = "";
            if (e.Device != IntPtr.Zero)
                deviceName = DeviceHelper.SearchDevice(e.Device)?.DeviceName;

            if (string.IsNullOrEmpty(deviceName))
                return;

            var key = KeyInterop.KeyFromVirtualKey((int)e.Key);
            var evt = new KeyboardEventArgs(deviceName, key);
            handler(this, evt);
        }

        private void OnRawInput(object sender, RawInputEventArgs e)
        {
            string deviceName = null;
            try
            {
                if (DebugMode)
                    Log.ForContext("Context", "IO").Verbose("RawInputListener: OnRawInput: Device: {Device}, WindowHandle: {Handle}", e?.Device, e?.WindowHandle);
            
                var handler = ButtonsChanged;
                if (handler == null)
                {
                    if (DebugMode)
                        Log.ForContext("Context", "IO").Verbose("RawInputListener: handler is null, while e: {E}", e?.Device);
                
                    return;
                }

                var hidInput = e as HidInputEventArgs;
                if (hidInput == null)
                {
                    if (DebugMode)
                        Log.ForContext("Context", "IO").Verbose("RawInputListener: hidInput is null, while e: {E}", e?.Device);
                
                    return;
                }

                if (e.Device == IntPtr.Zero)
                {
                    if (DebugMode)
                        Log.ForContext("Context", "IO").Verbose("RawInputListener: e.Device is null");
                
                    return;
                }

                deviceName = DeviceHelper.SearchDevice(e.Device)?.DeviceName;

                if (string.IsNullOrEmpty(deviceName))
                {
                    if (DebugMode)
                        Log.ForContext("Context", "IO").Verbose("RawInputListener: deviceName is null, while e: {E}", e.Device);
                
                    return;
                }

                var deviceNameLowered = deviceName.ToLower();

                foreach (var toIgnore in _devicesToIgnore)
                {
                    if (deviceNameLowered.Contains(toIgnore))
                    {
                        if (DebugMode)
                            Log.ForContext("Context", "IO").Verbose("RawInputListener: deviceName ignored, is in _devicesToIgnore: {E}", e.Device);
                        
                        return;
                    }
                }
                
                if (!PollAll)
                {
                    var found = false;
                    foreach (var toPoll in _devicesToPoll)
                    {
                        if (!deviceNameLowered.Contains(toPoll))
                            continue;

                        found = true;
                        break;
                    }
                    
                    if (!found)
                    {
                        if (DebugMode)
                            Log.ForContext("Context", "IO").Verbose("RawInputListener: Ignoring deviceName not in _deviceTypesToPoll: {E} {DeviceName}", e.Device, deviceName);
                
                        return;
                    }
                }

                if (!RawInputParser.Parse(hidInput, out var pressedButtons, deviceName, out var oemName, out var isFFB))
                {
                    if (DebugMode && isFFB)
                        Log.ForContext("Context", "IO").Verbose("RawInputListener: RawInputParser.Parse failed, while e: {E}, deviceName: {DeviceName}", e.Device, deviceName);
                
                    return;
                }
                
                if (FilterSimucube2Pro(oemName, pressedButtons)) 
                    return;

                if (DebugMode && isFFB)
                    Log.ForContext("Context", "IO").Verbose("RawInputListener: Returning: {DeviceName}, isFFB: {FFB}, ButtonsCount: {ButtonsCount}", oemName, isFFB, pressedButtons.Count);
            
                handler(this, new GamepadEventArgs(pressedButtons, deviceName, oemName, isFFB));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "OnRawInput: deviceName: {DeviceName}", deviceName);
            }
        }

        private bool FilterSimucube2Pro(string oemName, List<ushort> pressedButtons)
        {
            if (oemName != Simucube2Pro) 
                return false;
            
            if (pressedButtons.All(x => x == 1))
                return true;
                    
            if (pressedButtons.Count == 0 && !_simucube2ProActualButtonsDown)
            {
                _simucube2ProActualButtonsDown = false;
                return true;
            }

            _simucube2ProActualButtonsDown = true;

            return false;
        }
    }
}
