using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using Newtonsoft.Json;

namespace VstPluginScanner
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct VstPluginProbeInfoNative
    {
        public int IsValid;
        public int IsAudioEffect;
        public int HasAudioInput;
        public int HasAudioOutput;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string Path;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Name;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Vendor;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string Version;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Subcategories;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ClassId;
    }

    sealed class ProbeResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public int ProbeResultCode { get; set; }
        public int IsValid { get; set; }
        public int IsAudioEffect { get; set; }
        public int HasAudioInput { get; set; }
        public int HasAudioOutput { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public string Vendor { get; set; }
        public string Version { get; set; }
        public string Subcategories { get; set; }
        public string ClassId { get; set; }
    }

    sealed class MultiProbeResult
    {
        public List<ProbeResult> Results { get; set; }

        public MultiProbeResult()
        {
            Results = new List<ProbeResult>();
        }
    }

    static class Program
    {
        private const string NativeLibrary = "VstHostBridge.dll";
        private const uint SEM_FAILCRITICALERRORS = 0x0001;
        private const uint SEM_NOGPFAULTERRORBOX = 0x0002;
        private const uint SEM_NOOPENFILEERRORBOX = 0x8000;
        private const int MaxProbeClasses = 64;

        [DllImport(NativeLibrary, EntryPoint = "VST_ProbePluginMetadataOnly", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int NativeProbePluginMetadataOnly(string pluginPath, out VstPluginProbeInfoNative info);

        [DllImport(NativeLibrary, EntryPoint = "VST_ProbePluginAllClasses", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int NativeProbePluginAllClasses(string pluginPath, [Out] VstPluginProbeInfoNative[] infos, int maxCount, out int actualCount);

        [DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectoryW([MarshalAs(UnmanagedType.LPWStr)] string lpPathName);

        [DllImport("ole32.dll")]
        private static extern int OleInitialize(IntPtr pvReserved);

        [STAThread]
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        static int Main(string[] args)
        {
            ProbeResult result = new ProbeResult();
            VstPluginProbeInfoNative nativeInfo;

            try
            {
                SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);

                int oleResult = OleInitialize(IntPtr.Zero);
                if (oleResult < 0)
                {
                    result.Success = false;
                    result.Error = string.Format("OleInitialize failed: 0x{0:X8}", oleResult);
                    WriteResult(result);
                    return 3;
                }

                if (args == null || args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
                {
                    result.Success = false;
                    result.Error = "Missing plugin path.";
                    WriteResult(result);
                    return 2;
                }

                result.Path = args[0];

                try
                {
                    string pluginDir = Path.GetDirectoryName(args[0]);
                    if (!string.IsNullOrWhiteSpace(pluginDir))
                        SetDllDirectoryW(pluginDir);
                }
                catch
                {
                }

                string dllName = Path.GetFileName(args[0]);
                WriteProgress(string.Format("Probing {0}...", dllName));

                VstPluginProbeInfoNative[] allInfos = new VstPluginProbeInfoNative[MaxProbeClasses];
                int actualCount = 0;
                int probeResultCode = NativeProbePluginAllClasses(args[0], allInfos, MaxProbeClasses, out actualCount);

                WriteProgress(string.Format("Enumerating classes from {0}...", dllName));

                if (actualCount > 1)
                {
                    MultiProbeResult multiResult = new MultiProbeResult();
                    for (int i = 0; i < actualCount; i++)
                    {
                        VstPluginProbeInfoNative info = allInfos[i];
                        ProbeResult pr = new ProbeResult();
                        pr.Success = true;
                        pr.ProbeResultCode = probeResultCode;
                        pr.IsValid = info.IsValid;
                        pr.IsAudioEffect = info.IsAudioEffect;
                        pr.HasAudioInput = info.HasAudioInput;
                        pr.HasAudioOutput = info.HasAudioOutput;
                        pr.Path = string.IsNullOrWhiteSpace(info.Path) ? args[0] : info.Path;
                        pr.Name = info.Name;
                        pr.Vendor = info.Vendor;
                        pr.Version = info.Version;
                        pr.Subcategories = info.Subcategories;
                        pr.ClassId = info.ClassId;
                        multiResult.Results.Add(pr);
                    }
                    WriteMultiResult(multiResult);
                    return probeResultCode == 0 ? 0 : 1;
                }
                else if (actualCount == 1)
                {
                    VstPluginProbeInfoNative info = allInfos[0];
                    result.Success = true;
                    result.ProbeResultCode = probeResultCode;
                    result.IsValid = info.IsValid;
                    result.IsAudioEffect = info.IsAudioEffect;
                    result.HasAudioInput = info.HasAudioInput;
                    result.HasAudioOutput = info.HasAudioOutput;
                    result.Path = string.IsNullOrWhiteSpace(info.Path) ? args[0] : info.Path;
                    result.Name = info.Name;
                    result.Vendor = info.Vendor;
                    result.Version = info.Version;
                    result.Subcategories = info.Subcategories;
                    result.ClassId = info.ClassId;
                    if (!result.Success)
                        result.Error = string.Format("Probe failed (code {0}).", result.ProbeResultCode);
                    WriteResult(result);
                    return result.Success ? 0 : 1;
                }
                else
                {
                    result.ProbeResultCode = NativeProbePluginMetadataOnly(args[0], out nativeInfo);
                    result.Success = result.ProbeResultCode == 0;
                    result.IsValid = nativeInfo.IsValid;
                    result.IsAudioEffect = nativeInfo.IsAudioEffect;
                    result.HasAudioInput = nativeInfo.HasAudioInput;
                    result.HasAudioOutput = nativeInfo.HasAudioOutput;
                    result.Path = string.IsNullOrWhiteSpace(nativeInfo.Path) ? args[0] : nativeInfo.Path;
                    result.Name = nativeInfo.Name;
                    result.Vendor = nativeInfo.Vendor;
                    result.Version = nativeInfo.Version;
                    result.Subcategories = nativeInfo.Subcategories;
                    result.ClassId = nativeInfo.ClassId;
                    if (!result.Success)
                        result.Error = string.Format("Probe failed (code {0}).", result.ProbeResultCode);
                    WriteResult(result);
                    return result.Success ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                if (ex is AccessViolationException)
                {
                    result.ProbeResultCode = -102;
                    result.Error = "Scanner access violation.";
                }
                else
                {
                    result.Error = ex.GetBaseException().Message;
                }
                if (args != null && args.Length > 0)
                    result.Path = args[0];
                WriteResult(result);
                return 3;
            }
        }

        private static void WriteProgress(string message)
        {
            try
            {
                Console.Error.WriteLine(message);
                Console.Error.Flush();
            }
            catch
            {
            }
        }

        private static void WriteResult(ProbeResult result)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(JsonConvert.SerializeObject(result));
        }

        private static void WriteMultiResult(MultiProbeResult result)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(JsonConvert.SerializeObject(result));
        }
    }
}
