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
            RegisterDeviceType(UsagePage.Generic, nameof(UsageId.GenericGamepad));
            RegisterDeviceType(UsagePage.Generic, nameof(UsageId.GenericJoystick));
            RegisterDeviceType(UsagePage.Generic, nameof(UsageId.GenericPointer));
            
            //FANATEC ClubSport Wheel Base V2.5
            //FANATEC Podium Wheel Base DD1
            //FANATEC Podium Wheel Base DD2
            RegisterDeviceType(UsagePage.Generic, nameof(UsageId.LedSelectedIndicator));
            
            //Simucube 2 Pro
            RegisterDeviceType(UsagePage.Generic, nameof(UsageId.LedCompose));

            Device.RawInput += OnRawInput;
            Device.KeyboardInput += OnKeyboardInput;
            Device.MouseInput += OnMouseInput;
        }


        private Dictionary<UsagePage, List<string>> _deviceTypes = new ();
        public bool RegisterDeviceType(UsagePage up, string usageIdString)
        {
            if (_deviceTypes.ContainsKey(up) && _deviceTypes[up].Contains(usageIdString))
            {
                Log.Verbose("RawInputListener.RegisterDeviceType: Device type already added: {UsagePage}:{UsageId}", up, usageIdString);
                return false;
            }

            var usageId = (UsageId)Enum.Parse(typeof(UsageId), usageIdString);
            Device.RegisterDevice(up, usageId, DeviceFlags.InputSink, _hWindow);
            
            if (!_deviceTypes.ContainsKey(up))
                _deviceTypes.Add(up, new List<string> { usageIdString });
            else    
                _deviceTypes[up].Add(usageIdString);
            
            Log.Verbose("RawInputListener.RegisterDeviceType: Device type added: {UsagePage}:{UsageId}", up, usageIdString);
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
            var handler = ButtonsChanged;
            if (handler == null)
                return;

            var hidInput = e as HidInputEventArgs;
            if (hidInput == null)
                return;

            if (e.Device == IntPtr.Zero)
                return;

            var deviceName = DeviceHelper.SearchDevice(e.Device)?.DeviceName;

            if (string.IsNullOrEmpty(deviceName))
                return;

            if (!RawInputParser.Parse(hidInput, out var pressedButtons, deviceName, out var oemName, out var isFFB))
                return;

            handler(this, new GamepadEventArgs(pressedButtons, deviceName, oemName, isFFB));
        }
    }
}
