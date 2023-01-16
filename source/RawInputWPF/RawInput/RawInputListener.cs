using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;
using RawInputWPF.Helpers;
using SharpDX.Multimedia;
using SharpDX.RawInput;


namespace RawInputWPF.RawInput
{
    public class RawInputListener
    {
        public event EventHandler<GamepadEventArgs> ButtonsChanged;
        public event EventHandler<MouseEventArgs> MouseButtonsChanged;
        public event EventHandler<KeyboardEventArgs> KeyDown;
        public event EventHandler<KeyboardEventArgs> KeyUp;

        private const int WM_INPUT = 0x00FF;
        private HwndSource _hwndSource;
        private IntPtr hWindow;

        public bool IsInitialized => _hwndSource != null;

        public void Init(IntPtr hWnd)
        {
            if (_hwndSource != null)
            {
                return;
            }

            hWindow = hWnd;
            _hwndSource = HwndSource.FromHwnd(hWnd);
            if (_hwndSource != null)
                _hwndSource.AddHook(WndProc);

            //RegisterDeviceType(UsagePage.Generic, UsageId.GenericKeyboard);
            //RegisterDeviceType(UsagePage.Generic, UsageId.GenericMouse);
            RegisterDeviceType(UsagePage.Generic, UsageId.GenericGamepad);
            RegisterDeviceType(UsagePage.Generic, UsageId.GenericJoystick);
            RegisterDeviceType(UsagePage.Generic, UsageId.GenericPointer);

            Device.RawInput += OnRawInput;
            Device.KeyboardInput += OnKeyboardInput;
            Device.MouseInput += OnMouseInput;
        }

        private Dictionary<UsagePage, List<UsageId>> _deviceTypes = new ();
        public bool RegisterDeviceType(UsagePage up, UsageId ui)
        {
            if (_deviceTypes.ContainsKey(up) && _deviceTypes[up].Contains(ui))
                return false;
            
            Device.RegisterDevice(up, ui, DeviceFlags.InputSink, hWindow);
            
            if (!_deviceTypes.ContainsKey(up))
                _deviceTypes.Add(up, new List<UsageId> { ui });
            else    
                _deviceTypes[up].Add(ui);
            
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

            RawInputParser.Parse(hidInput, out var pressedButtons, deviceName, out var oemName, out var isFFB);

            handler(this, new GamepadEventArgs(pressedButtons, deviceName, oemName, isFFB));
        }
    }
}
