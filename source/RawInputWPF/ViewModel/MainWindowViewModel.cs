﻿using System.Text;
using System.Windows;
using System.Windows.Interop;
using GalaSoft.MvvmLight;
using RawInputWPF.RawInput;
using SharpDX.RawInput;


namespace RawInputWPF.ViewModel
{
    public class MainWindowViewModel : ViewModelBase
    {
        private static MainWindowViewModel _instance;
        private readonly RawInputListener _rawInputListener;
        private string _gamepadDeviveName;
        private string _pressedButtons;
        private string _keyboardDeviceName;
        private string _pressedKey;
        private string _mouseDeviceName;
        private string _mouseButtons;
        

        public static MainWindowViewModel Instance
        {
            get { return _instance ?? (_instance = new MainWindowViewModel()); }
        }

        public string GamepadDeviveName
        {
            get { return _gamepadDeviveName; }
            set
            {
                Set(ref _gamepadDeviveName, value);
            }
        }

        public string PressedButtons
        {
            get { return _pressedButtons; }
            set
            {
                Set(ref _pressedButtons, value);
            }
        }

        public string KeyboardDeviceName
        {
            get { return _keyboardDeviceName; }
            set
            {
                Set(ref _keyboardDeviceName, value);
            }
        }

        public string PressedKey
        {
            get { return _pressedKey; }
            set
            {
                Set(ref _pressedKey, value);
            }
        }

        public string MouseDeviceName
        {
            get { return _mouseDeviceName; }
            set
            {
                Set(ref _mouseDeviceName, value);
            }
        }

        public string MouseButtons
        {
            get { return _mouseButtons; }
            set
            {
                Set(ref _mouseButtons, value);
            }
        }

        public MainWindowViewModel()
        {
            _rawInputListener = new RawInputListener();

            Application.Current.MainWindow.Loaded += OnMainWindowLoaded;
        }

        private void OnMainWindowUnloaded(object sender, RoutedEventArgs e)
        {
            Application.Current.MainWindow.Unloaded -= OnMainWindowUnloaded;
            if (_rawInputListener == null)
                return;

            _rawInputListener.ButtonsChanged -= OnButtonsChanged;
            _rawInputListener.MouseButtonsChanged -= OnMouseButtonsChanged;
            _rawInputListener.KeyDown -= OnKeyDown;
            _rawInputListener.KeyDown -= OnKeyUp;
            _rawInputListener.Clear();
        }

        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            Application.Current.MainWindow.Loaded -= OnMainWindowLoaded;

            var wih = new WindowInteropHelper(Application.Current.MainWindow);
            _rawInputListener.Init(wih.Handle, null);

            Application.Current.MainWindow.Unloaded += OnMainWindowUnloaded;
            _rawInputListener.ButtonsChanged += OnButtonsChanged;
            _rawInputListener.MouseButtonsChanged += OnMouseButtonsChanged;
            _rawInputListener.KeyDown += OnKeyDown;
            _rawInputListener.KeyUp += OnKeyUp;
        }

        private void OnKeyDown(object sender, KeyboardEventArgs e)
        {
            KeyboardDeviceName = string.Format(@"{0}", e.DeviceName);
            PressedKey = e.Key.ToString();
        }

        private void OnKeyUp(object sender, KeyboardEventArgs e)
        {
            KeyboardDeviceName = "";
            PressedKey = "";
        }

        private void OnButtonsChanged(object sender, GamepadEventArgs e)
        {
            if (e.Buttons.Count > 0)
            {
                GamepadDeviveName = string.Format(@"{0}: {1}", e.OemName, e.DeviceName);
                var sb = new StringBuilder();
                e.Buttons.ForEach(btn => sb.AppendFormat("{0} ", btn));
                PressedButtons = sb.ToString();
            }
            else
            {
                GamepadDeviveName = "";
                PressedButtons = "";
            }
        }

        private void OnMouseButtonsChanged(object sender, MouseEventArgs e)
        {
            MouseDeviceName = string.Format(@"{0}", e.DeviceName);
            MouseButtons = e.Buttons.ToString();
        }
    }
}
