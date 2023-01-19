using System;
using System.Collections.Generic;
using System.Linq;
using SharpDX.RawInput;

namespace RawInput.Helpers
{
    public static class DeviceHelper
    {
        private static readonly Dictionary<IntPtr, DeviceInfo> DevicesFound = new();

        public static DeviceInfo SearchDevice(IntPtr devicePtr)
        {
            if (DevicesFound.ContainsKey(devicePtr))
                return DevicesFound[devicePtr];

            try
            {
                var devs = Device.GetDevices();
                var device = devs.FirstOrDefault(dev => dev != null && dev.Handle == devicePtr);

                if (device == null)
                {
                    device = new DeviceInfo
                    {
                        DeviceName = string.Empty,
                        DeviceType = DeviceType.HumanInputDevice,
                        Handle = new IntPtr(0)
                    };
                }
                else
                    DevicesFound.Add(devicePtr, device);

                return device;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
