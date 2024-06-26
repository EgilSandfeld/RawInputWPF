﻿using System.Windows.Input;

namespace RawInput.RawInput
{
    public class KeyboardEventArgs : EventArgs
    {
        public KeyboardEventArgs(string deviceName, Key key)
            : base(deviceName)
        {
            Key = key;
        }

        public Key Key { get; private set; }
    }
}
