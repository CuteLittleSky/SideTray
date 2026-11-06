// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (C) 2026 CuteLittleSky
//
// Windows CoreAudio / IPolicyConfig 互操作：枚举输出设备、切换默认设备、读写音量。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AudioSw
{
    public enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    public enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }
    [Flags]
    public enum DEVICE_STATE : uint { ACTIVE = 0x1, DISABLED = 0x2, NOTPRESENT = 0x4, UNPLUGGED = 0x8, ALL = 0xF }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPVARIANT
    {
        public short vt;
        public short r1;
        public short r2;
        public short r3;
        public IntPtr p1;
        public IntPtr p2;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, DEVICE_STATE stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float min, out float max, out float inc);
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool isDefault, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool isDefault, out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref long period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool visible);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    public class CPolicyConfigClient { }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumeratorComObject { }

    public class AudioDeviceInfo
    {
        public string Id;
        public string Name;
        public string Desc;
        public bool IsDefault;
        public int VolumePercent = -1;
        public bool Muted;
        public override string ToString() { return Name; }
    }

    public static class AudioCore
    {
        public static IMMDeviceEnumerator CreateEnumerator()
        {
            return (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }

        static PROPERTYKEY PKEY_Device_FriendlyName = new PROPERTYKEY
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 14
        };

        static PROPERTYKEY PKEY_Device_DeviceDesc = new PROPERTYKEY
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 2
        };

        static string ReadStringProp(IMMDevice device, PROPERTYKEY key)
        {
            IPropertyStore store;
            if (device.OpenPropertyStore(0, out store) != 0) return null;
            try
            {
                PROPVARIANT pv;
                if (store.GetValue(ref key, out pv) != 0) return null;
                try
                {
                    if (pv.vt == 31 && pv.p1 != IntPtr.Zero) return Marshal.PtrToStringUni(pv.p1);
                    return null;
                }
                finally { PropVariantClear(ref pv); }
            }
            finally { Marshal.ReleaseComObject(store); }
        }

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PROPVARIANT pv);

        public static string GetFriendlyName(IMMDevice device)
        {
            string n = ReadStringProp(device, PKEY_Device_FriendlyName);
            if (string.IsNullOrWhiteSpace(n)) n = ReadStringProp(device, PKEY_Device_DeviceDesc);
            return n ?? "(未知设备)";
        }

        public static string GetDeviceDesc(IMMDevice device)
        {
            return ReadStringProp(device, PKEY_Device_DeviceDesc);
        }

        public static string GetDefaultId(ERole role)
        {
            return GetDefaultId(EDataFlow.eRender, role);
        }

        public static string GetDefaultId(EDataFlow flow, ERole role)
        {
            IMMDeviceEnumerator en = CreateEnumerator();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(flow, role, out dev) != 0) return null;
            string id;
            dev.GetId(out id);
            Marshal.ReleaseComObject(dev);
            Marshal.ReleaseComObject(en);
            return id;
        }

        public static List<AudioDeviceInfo> ListRenderDevices(bool includeInactive)
        {
            return ListDevices(EDataFlow.eRender, includeInactive);
        }

        public static List<AudioDeviceInfo> ListCaptureDevices(bool includeInactive)
        {
            return ListDevices(EDataFlow.eCapture, includeInactive);
        }

        public static List<AudioDeviceInfo> ListDevices(EDataFlow flow, bool includeInactive)
        {
            var result = new List<AudioDeviceInfo>();
            string defId = GetDefaultId(flow, ERole.eMultimedia) ?? GetDefaultId(flow, ERole.eConsole);
            IMMDeviceEnumerator en = CreateEnumerator();
            IMMDeviceCollection col;
            DEVICE_STATE mask = includeInactive ? DEVICE_STATE.ALL : DEVICE_STATE.ACTIVE;
            if (en.EnumAudioEndpoints(flow, mask, out col) != 0) return result;
            int count;
            col.GetCount(out count);
            for (int i = 0; i < count; i++)
            {
                IMMDevice dev;
                if (col.Item(i, out dev) != 0) continue;
                try
                {
                    uint state;
                    dev.GetState(out state);
                    if (!includeInactive && state != 1) continue;
                    string id;
                    dev.GetId(out id);
                    var info = new AudioDeviceInfo
                    {
                        Id = id,
                        Name = GetFriendlyName(dev),
                        Desc = GetDeviceDesc(dev),
                        IsDefault = (id == defId)
                    };
                    if (state != 1) info.Name = info.Name + " (已断开)";
                    result.Add(info);
                }
                finally { Marshal.ReleaseComObject(dev); }
            }
            Marshal.ReleaseComObject(col);
            Marshal.ReleaseComObject(en);
            return result;
        }

        public static IAudioEndpointVolume GetVolumeInterface(IMMDevice device)
        {
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            object o;
            if (device.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out o) != 0) return null;
            return (IAudioEndpointVolume)o;
        }

        public static bool SetDefault(string deviceId, bool allRoles)
        {
            var pc = (IPolicyConfig)new CPolicyConfigClient();
            int hr = pc.SetDefaultEndpoint(deviceId, ERole.eConsole);
            if (allRoles)
            {
                pc.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
                pc.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            }
            Marshal.ReleaseComObject(pc);
            return hr == 0;
        }

        public static bool GetVolume(out int percent, out bool muted)
        {
            percent = -1; muted = false;
            IMMDeviceEnumerator en = CreateEnumerator();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out dev) != 0)
            {
                Marshal.ReleaseComObject(en);
                return false;
            }
            try
            {
                var vol = GetVolumeInterface(dev);
                if (vol == null) return false;
                float f;
                vol.GetMasterVolumeLevelScalar(out f);
                percent = (int)Math.Round(f * 100);
                bool m;
                vol.GetMute(out m);
                muted = m;
                Marshal.ReleaseComObject(vol);
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(dev);
                Marshal.ReleaseComObject(en);
            }
        }

        public static bool SetVolume(int percent)
        {
            IMMDeviceEnumerator en = CreateEnumerator();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out dev) != 0) { Marshal.ReleaseComObject(en); return false; }
            try
            {
                var vol = GetVolumeInterface(dev);
                if (vol == null) return false;
                Guid ctx = Guid.Empty;
                float f = Math.Max(0, Math.Min(100, percent)) / 100f;
                int hr = vol.SetMasterVolumeLevelScalar(f, ref ctx);
                if (f > 0) vol.SetMute(false, ref ctx);
                Marshal.ReleaseComObject(vol);
                return hr == 0;
            }
            finally
            {
                Marshal.ReleaseComObject(dev);
                Marshal.ReleaseComObject(en);
            }
        }

        public static bool SetMute(bool mute)
        {
            IMMDeviceEnumerator en = CreateEnumerator();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out dev) != 0) { Marshal.ReleaseComObject(en); return false; }
            try
            {
                var vol = GetVolumeInterface(dev);
                if (vol == null) return false;
                Guid ctx = Guid.Empty;
                int hr = vol.SetMute(mute, ref ctx);
                Marshal.ReleaseComObject(vol);
                return hr == 0;
            }
            finally
            {
                Marshal.ReleaseComObject(dev);
                Marshal.ReleaseComObject(en);
            }
        }
    }
}
