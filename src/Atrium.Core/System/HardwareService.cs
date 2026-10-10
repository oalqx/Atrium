using System.Management;
using System.Runtime.InteropServices;
using Vortice.DXGI;

namespace Atrium.Core.System;

public static class HardwareService
{
    public static HardwareInfo Detect()
    {
        var gpus = DetectGpus();
        var (cpu, ramGb) = DetectCpuAndRam();
        var display = DetectPrimaryDisplay();

        return new HardwareInfo(gpus, cpu, ramGb, display);
    }

    public static IReadOnlyList<GpuInfo> DetectGpus()
    {
        var gpus = new List<GpuInfo>();

        try
        {
            if (DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).Success && factory != null)
            {
                uint index = 0;
                while (factory.EnumAdapters1(index, out IDXGIAdapter1? adapter).Success && adapter != null)
                {
                    index++;
                    var desc = adapter.Description1;

                    // Skip software rasterizer / Microsoft Basic Render Driver
                    if ((desc.Flags & AdapterFlags.Software) != 0 || desc.VendorId == 0x1414)
                    {
                        adapter.Dispose();
                        continue;
                    }

                    var vendor = (uint)desc.VendorId switch
                    {
                        0x10DE => GpuVendor.Nvidia,
                        0x1002 => GpuVendor.Amd,
                        0x8086 => GpuVendor.Intel,
                        _ => GpuVendor.Unknown
                    };

                    long vram = (long)(ulong)desc.DedicatedVideoMemory;

                    // If dedicated VRAM >= 1.5 GB or is discrete NVIDIA card, treat as high performance
                    bool isHighPerf = vendor == GpuVendor.Nvidia || vram >= (1536L * 1024 * 1024);

                    gpus.Add(new GpuInfo(
                        Name: desc.Description.Trim(),
                        Vendor: vendor,
                        DedicatedVideoMemoryBytes: vram,
                        IsHighPerformance: isHighPerf));

                    adapter.Dispose();
                }
                factory.Dispose();
            }
        }
        catch { }

        // Fallback to WMI if DXGI failed to return any adapters
        if (gpus.Count == 0)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                foreach (ManagementObject mo in searcher.Get())
                {
                    var name = mo["Name"]?.ToString() ?? "Unknown GPU";
                    long vram = 0;
                    if (mo["AdapterRAM"] != null)
                    {
                        long.TryParse(mo["AdapterRAM"]!.ToString(), out vram);
                    }

                    var vendor = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia :
                                 name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd :
                                 name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel : GpuVendor.Unknown;

                    bool isHighPerf = vendor == GpuVendor.Nvidia || vram >= (1536L * 1024 * 1024);
                    gpus.Add(new GpuInfo(name, vendor, vram, isHighPerf));
                }
            }
            catch { }
        }

        return gpus;
    }

    private static (string Cpu, int RamGb) DetectCpuAndRam()
    {
        string cpuName = "Unknown CPU";
        int ramGb = 16;

        try
        {
            using var searcherCpu = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (ManagementObject mo in searcherCpu.Get())
            {
                var n = mo["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(n))
                {
                    cpuName = n.Trim();
                    break;
                }
            }

            using var searcherRam = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject mo in searcherRam.Get())
            {
                if (mo["TotalPhysicalMemory"] != null && long.TryParse(mo["TotalPhysicalMemory"]!.ToString(), out var bytes))
                {
                    ramGb = (int)Math.Round((double)bytes / (1024 * 1024 * 1024));
                    break;
                }
            }
        }
        catch { }

        return (cpuName, ramGb);
    }

    private static DisplayInfo DetectPrimaryDisplay()
    {
        try
        {
            var devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode))
            {
                return new DisplayInfo(devMode.dmPelsWidth, devMode.dmPelsHeight, devMode.dmDisplayFrequency);
            }
        }
        catch { }

        return new DisplayInfo(1920, 1080, 60);
    }

    private const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll")]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public short dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}
