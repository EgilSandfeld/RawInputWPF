using System.Collections.Generic;

namespace RawInput.RawInput
{
    public class GamepadEventArgs : EventArgs
    {
        public GamepadEventArgs(List<ushort> buttons, string deviceName, string oemName, bool isFFB)
            : base(deviceName)
        {
            Buttons = buttons;
            OemName = oemName;
            IsFFB = isFFB;
        }

        public List<ushort> Buttons { get; private set; }

        public string OemName { get; }
        public bool IsFFB { get; }
    }
}
