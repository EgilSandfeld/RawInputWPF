using System;
using System.Collections.Generic;
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
        public static ILogger Log;
        public static Action<Exception, string> ExceptionLog { get; set; }


        private const int WM_INPUT = 0x00FF;
        private HwndSource _hwndSource;
        private IntPtr _hWindow;

        public bool IsInitialized => _hwndSource != null && _hwndSource.Handle != IntPtr.Zero;

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
                ex.Data.Add("StackTraceCustom", Environment.StackTrace.ToString());
                ExceptionLog(ex, $"_hwndSource is null, hWnd: {hWnd}");
                return;
            }

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

            Device.RawInput += OnRawInput;
            Device.KeyboardInput += OnKeyboardInput;
            Device.MouseInput += OnMouseInput;
        }


        private Dictionary<UsagePage, List<UsageId>> _deviceTypes = new ();
        public bool RegisterDeviceType(UsagePage up, UsageId usageId)
        {
            if (_deviceTypes.ContainsKey(up) && _deviceTypes[up].Contains(usageId))
            {
                Log.Verbose("RawInputListener.RegisterDeviceType: Device type already added: {UsagePage}:{UsageId}", up, usageId);
                return false;
            }

            //var usageId = (UsageId)Enum.Parse(typeof(UsageId), usageIdString);
            Device.RegisterDevice(up, usageId, DeviceFlags.InputSink, _hWindow);
            
            if (!_deviceTypes.ContainsKey(up))
                _deviceTypes.Add(up, new List<UsageId> { usageId });
            else    
                _deviceTypes[up].Add(usageId);
            
            Log.Verbose("RawInputListener.RegisterDeviceType: Device type added: {UsagePage}:{UsageId}", up, usageId);
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
            if (DebugMode)
                Log.Verbose("RawInputListener: OnRawInput: Device: {Device}, WindowHandle: {Handle}", e?.Device, e?.WindowHandle);
            
            var handler = ButtonsChanged;
            if (handler == null)
            {
                if (DebugMode)
                    Log.Verbose("RawInputListener: handler is null, while e: {E}", e?.Device);
                
                return;
            }

            var hidInput = e as HidInputEventArgs;
            if (hidInput == null)
            {
                if (DebugMode)
                    Log.Verbose("RawInputListener: hidInput is null, while e: {E}", e?.Device);
                
                return;
            }

            if (e.Device == IntPtr.Zero)
            {
                if (DebugMode)
                    Log.Verbose("RawInputListener: e.Device is null");
                
                return;
            }

            var deviceName = DeviceHelper.SearchDevice(e.Device)?.DeviceName;

            if (string.IsNullOrEmpty(deviceName))
            {
                if (DebugMode)
                    Log.Verbose("RawInputListener: deviceName is null, while e: {E}", e.Device);
                
                return;
            }

            if (!RawInputParser.Parse(hidInput, out var pressedButtons, deviceName, out var oemName, out var isFFB))
            {
                if (DebugMode && isFFB)
                    Log.Verbose("RawInputListener: RawInputParser.Parse failed, while e: {E}, deviceName: {DeviceName}", e.Device, deviceName);
                
                return;
            }

            if (DebugMode && isFFB)
                Log.Verbose("RawInputListener: Returning: {DeviceName}, isFFB: {FFB}, buttons: {ButtonsCount}", oemName, isFFB, pressedButtons.Count);
            
            handler(this, new GamepadEventArgs(pressedButtons, deviceName, oemName, isFFB));
        }
    }
}
