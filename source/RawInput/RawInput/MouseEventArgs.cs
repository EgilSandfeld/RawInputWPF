﻿using SharpDX.RawInput;

namespace RawInput.RawInput
{
    public class MouseEventArgs : EventArgs
    {
        public MouseEventArgs(string deviceName, MouseButtonFlags mouseButtons)
            : base(deviceName)
        {
            Buttons = mouseButtons;
        }

        public MouseButtonFlags Buttons { get; private set; }
    }
}
