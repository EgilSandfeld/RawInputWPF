using System;
using System.Collections.Generic;

namespace RawInputWPF.RawInput
{
    public class GamepadEventArgs : EventArgs
    {
        public GamepadEventArgs(List<ushort> buttons, string deviceName, string oemName)
            : base(deviceName)
        {
            Buttons = buttons;
            OemName = oemName;
        }

        public List<ushort> Buttons { get; private set; }

        public string OemName { get; }
    }
}
