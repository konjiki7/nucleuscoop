﻿using Nucleus;
using Nucleus.Gaming;
using Nucleus.Gaming.Windows;
using Nucleus.Interop.User32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EasyHook;
using System.Runtime.InteropServices;
using Nucleus.Gaming.Coop;
using System.Security;

namespace StartGame
{
    class Program
    {
        private static int tries = 5;
        private static Process proc;
        private static string mt;

        private static bool isHook;
        private static bool isDelay;
        private static bool renameMutex;
        private static bool setWindow;

        private static string mutexToRename;

        private static int pOutPID = 0;

        [DllImport("EasyHook64.dll", CharSet = CharSet.Ansi)]
        private static extern int RhCreateAndInject(
            [MarshalAsAttribute(UnmanagedType.LPWStr)] string InEXEPath,
            [MarshalAsAttribute(UnmanagedType.LPWStr)] string InCommandLine,
            uint InProcessCreationFlags,
            uint InInjectionOptions,
            [MarshalAsAttribute(UnmanagedType.LPWStr)] string InLibraryPath_x86,
            [MarshalAsAttribute(UnmanagedType.LPWStr)] string InLibraryPath_x64,
            IntPtr InPassThruBuffer,
            uint InPassThruSize,
            IntPtr OutProcessId //Pointer to a UINT (the PID of the new process)
            );

        [DllImport("kernel32.dll")]
        public static extern bool CreateProcess(string lpApplicationName,
        string lpCommandLine, IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles, ProcessCreationFlags dwCreationFlags,
        IntPtr lpEnvironment, string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll")]
        public static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll")]
        public static extern uint SuspendThread(IntPtr hThread);

        public struct STARTUPINFO
        {
            public uint cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        public enum ProcessCreationFlags : uint
        {
            ZERO_FLAG = 0x00000000,
            CREATE_BREAKAWAY_FROM_JOB = 0x01000000,
            CREATE_DEFAULT_ERROR_MODE = 0x04000000,
            CREATE_NEW_CONSOLE = 0x00000010,
            CREATE_NEW_PROCESS_GROUP = 0x00000200,
            CREATE_NO_WINDOW = 0x08000000,
            CREATE_PROTECTED_PROCESS = 0x00040000,
            CREATE_PRESERVE_CODE_AUTHZ_LEVEL = 0x02000000,
            CREATE_SEPARATE_WOW_VDM = 0x00001000,
            CREATE_SHARED_WOW_VDM = 0x00001000,
            CREATE_SUSPENDED = 0x00000004,
            CREATE_UNICODE_ENVIRONMENT = 0x00000400,
            DEBUG_ONLY_THIS_PROCESS = 0x00000002,
            DEBUG_PROCESS = 0x00000001,
            DETACHED_PROCESS = 0x00000008,
            EXTENDED_STARTUPINFO_PRESENT = 0x00080000,
            INHERIT_PARENT_AFFINITY = 0x00010000
        }

        [DllImport("user32.dll")]
        static extern uint WaitForInputIdle(IntPtr hProcess, uint dwMilliseconds);

        public enum MachineType : ushort
        {
            IMAGE_FILE_MACHINE_UNKNOWN = 0x0,
            IMAGE_FILE_MACHINE_AM33 = 0x1d3,
            IMAGE_FILE_MACHINE_AMD64 = 0x8664,
            IMAGE_FILE_MACHINE_ARM = 0x1c0,
            IMAGE_FILE_MACHINE_EBC = 0xebc,
            IMAGE_FILE_MACHINE_I386 = 0x14c,
            IMAGE_FILE_MACHINE_IA64 = 0x200,
            IMAGE_FILE_MACHINE_M32R = 0x9041,
            IMAGE_FILE_MACHINE_MIPS16 = 0x266,
            IMAGE_FILE_MACHINE_MIPSFPU = 0x366,
            IMAGE_FILE_MACHINE_MIPSFPU16 = 0x466,
            IMAGE_FILE_MACHINE_POWERPC = 0x1f0,
            IMAGE_FILE_MACHINE_POWERPCFP = 0x1f1,
            IMAGE_FILE_MACHINE_R4000 = 0x166,
            IMAGE_FILE_MACHINE_SH3 = 0x1a2,
            IMAGE_FILE_MACHINE_SH3DSP = 0x1a3,
            IMAGE_FILE_MACHINE_SH4 = 0x1a6,
            IMAGE_FILE_MACHINE_SH5 = 0x1a8,
            IMAGE_FILE_MACHINE_THUMB = 0x1c2,
            IMAGE_FILE_MACHINE_WCEMIPSV2 = 0x169,
        }

        public static MachineType GetDllMachineType(string dllPath)
        {
            // See http://www.microsoft.com/whdc/system/platform/firmware/PECOFF.mspx
            // Offset to PE header is always at 0x3C.
            // The PE header starts with "PE\0\0" =  0x50 0x45 0x00 0x00,
            // followed by a 2-byte machine type field (see the document above for the enum).
            //
            FileStream fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read);
            BinaryReader br = new BinaryReader(fs);
            fs.Seek(0x3c, SeekOrigin.Begin);
            Int32 peOffset = br.ReadInt32();
            fs.Seek(peOffset, SeekOrigin.Begin);
            UInt32 peHead = br.ReadUInt32();

            if (peHead != 0x00004550) // "PE\0\0", little-endian
                throw new Exception("Can't find PE header");

            MachineType machineType = (MachineType)br.ReadUInt16();
            br.Close();
            fs.Close();
            return machineType;
        }

        // Returns true if the exe is 64-bit, false if 32-bit, and null if unknown
        public static bool? Is64Bit(string exePath)
        {
            switch (GetDllMachineType(exePath))
            {
                case MachineType.IMAGE_FILE_MACHINE_AMD64:
                case MachineType.IMAGE_FILE_MACHINE_IA64:
                    return true;
                case MachineType.IMAGE_FILE_MACHINE_I386:
                    return false;
                default:
                    return null;
            }
        }

        static void StartGame(string path, string args = "", string workingDir = null)
        {
            System.IO.Stream str = new System.IO.MemoryStream();
            GenericGameInfo gen = new GenericGameInfo(null, null, str);

            if (!Path.IsPathRooted(path))
            {
                string root = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                path = Path.Combine(root, path);
            }

            int tri = 0;
            ProcessStartInfo startInfo;
            startInfo = new ProcessStartInfo();
            startInfo.FileName = path;
            startInfo.Arguments = args;
            
            if (!string.IsNullOrWhiteSpace(workingDir))
            {
                startInfo.WorkingDirectory = workingDir;
            }

#if RELEASE
            try
#endif
            {
                //proc = Process.Start(startInfo);
                string currDir = Directory.GetCurrentDirectory();
 
                //bool is64 = EasyHook.RemoteHooking.IsX64Process((int)pi.dwProcessId);

                if (isHook || renameMutex || setWindow)
                {

                    var targetsBytes = Encoding.Unicode.GetBytes(mutexToRename);
                    int targetsBytesLength = targetsBytes.Length;
                    int size = 7 + targetsBytesLength;
                    var data = new byte[size];
                    data[0] = isHook == true ? (byte)1 : (byte)0;
                    data[1] = renameMutex == true ? (byte)1 : (byte)0;
                    data[2] = setWindow == true ? (byte)1 : (byte)0;

                    data[3] = (byte)(targetsBytesLength >> 24);
                    data[4] = (byte)(targetsBytesLength >> 16);
                    data[5] = (byte)(targetsBytesLength >> 8);
                    data[6] = (byte)targetsBytesLength;

                    Array.Copy(targetsBytes, 0, data, 7, targetsBytesLength);

                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    Marshal.Copy(data, 0, ptr, size);

                    if (!isDelay) // CreateandInject method
                    {
                        if (Is64Bit(path) == true)
                        {
                            try
                            {
                                IntPtr pid = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(uint)));
                                RhCreateAndInject(path, args, 0, 0, Path.Combine(currDir, "Nucleus.SHook32.dll"), Path.Combine(currDir, "Nucleus.SHook64.dll"), ptr, (uint)size, pid);
                                pOutPID = Marshal.ReadInt32(pid);
                                Marshal.FreeHGlobal(pid);
                            }
                            catch (Exception ex)
                            {
                                using (StreamWriter writer = new StreamWriter("error-log.txt", true))
                                {
                                    writer.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]" + "ex msg: {0}, ex str: {1}", ex.Message, ex.ToString());
                                }
                            }
                        }
                        else if (Is64Bit(path) == false)
                        {
                            try
                            {
                                //pidTest = gen.Inject(path, args, 0, 0, Path.Combine(currDir, "Nucleus.Hook32.dll"), null, IntPtr.Zero, 0);
                                string injectorPath = Path.Combine(currDir, "Nucleus.Inject32.exe");
                                ProcessStartInfo injstartInfo = new ProcessStartInfo();
                                injstartInfo.FileName = injectorPath;
                                object[] injargs = new object[]
                                {
                                    0, path, args, 0, 0, Path.Combine(currDir, "Nucleus.SHook32.dll"), null, isHook, renameMutex, mutexToRename, setWindow
                                };
                                var sbArgs = new StringBuilder();
                                foreach (object arg in injargs)
                                {
                                    sbArgs.Append(" \"");
                                    sbArgs.Append(arg);
                                    sbArgs.Append("\"");
                                }

                                string arguments = sbArgs.ToString();
                                injstartInfo.Arguments = arguments;
                                //injstartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                                //injstartInfo.CreateNoWindow = true;
                                injstartInfo.UseShellExecute = false;
                                injstartInfo.RedirectStandardOutput = true;

                                Process injectProc = Process.Start(injstartInfo);
                                injectProc.OutputDataReceived += proc_OutputDataReceived;
                                injectProc.BeginOutputReadLine();

                                //using (StreamWriter writer = new StreamWriter("important.txt", true))
                                //{
                                //    writer.WriteLine("readtoend: {0}, readline: {1}", injectProc.StandardOutput.ReadToEnd(), injectProc.StandardOutput.ReadLine());
                                //}


                                injectProc.WaitForExit();

                                //GenericGameHandler.RhCreateAndInject(path, args, 0, 0, Path.Combine(currDir, "Nucleus.Hook32.dll"), Path.Combine(currDir, "Nucleus.Hook64.dll"), IntPtr.Zero, 0, pid);
                                //pidTest = Nucleus.Injector32.Injector32.RhCreateAndInject(path, args, 0, 0, Path.Combine(currDir, "Nucleus.Hook32.dll"), Path.Combine(currDir, "Nucleus.Hook64.dll"), IntPtr.Zero, 0, pid);                        
                            }
                            catch (Exception ex)
                            {
                                using (StreamWriter writer = new StreamWriter("error-log.txt", true))
                                {
                                    writer.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]" + "is64: false, ex msg: {0}, ex str: {1}", ex.Message, ex.ToString());
                                }
                            }
                        }
                        else
                        {
                            using (StreamWriter writer = new StreamWriter("error-log.txt", true))
                            {
                                writer.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]" + "Machine type: '{0}' not implemented.", GetDllMachineType(path));
                            }
                        }
                    }
                    else // delay method
                    {
                        string directoryPath = Path.GetDirectoryName(path);
                        STARTUPINFO si = new STARTUPINFO();
                        PROCESS_INFORMATION pi = new PROCESS_INFORMATION();
                        bool success = CreateProcess(path, args, IntPtr.Zero, IntPtr.Zero, false, ProcessCreationFlags.CREATE_SUSPENDED, IntPtr.Zero, directoryPath, ref si, out pi);

                        if (!success)
                        {
                            using (StreamWriter writer = new StreamWriter("error-log.txt", true))
                            {
                                writer.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]" + "createprocess failed - startGamePath: {0}, startArgs: {1}, dirpath: {2}", path, args, directoryPath);
                            }
                            return;
                        }

                        ResumeThread(pi.hThread);

                        WaitForInputIdle(pi.hProcess, uint.MaxValue);

                        SuspendThread(pi.hThread);

                        if (Is64Bit(path) == true)
                        {
                            NativeAPI.RhInjectLibrary((int)pi.dwProcessId, 0, 0, null, Path.Combine(currDir, "Nucleus.Hook64.dll"), ptr, size);
                            pOutPID = (int)pi.dwProcessId;
                        }
                        else if (Is64Bit(path) == false)
                        {
                            try
                            {
                                string injectorPath = Path.Combine(currDir, "Nucleus.Inject32.exe");
                                ProcessStartInfo injstartInfo = new ProcessStartInfo();
                                injstartInfo.FileName = injectorPath;
                                object[] injargs = new object[]
                                {
                                    1, (int)pi.dwProcessId, 0, 0, Path.Combine(currDir, "Nucleus.SHook32.dll"), null, isHook, renameMutex, mutexToRename, setWindow
                                };
                                var sbArgs = new StringBuilder();
                                foreach (object arg in injargs)
                                {
                                    sbArgs.Append(" \"");
                                    sbArgs.Append(arg);
                                    sbArgs.Append("\"");
                                }

                                string arguments = sbArgs.ToString();
                                injstartInfo.Arguments = arguments;
                                //injstartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                                //injstartInfo.CreateNoWindow = true;
                                injstartInfo.UseShellExecute = false;
                                injstartInfo.RedirectStandardOutput = true;
                                Process injectProc = Process.Start(injstartInfo);
                                //injectProc.OutputDataReceived += proc_OutputDataReceived;
                                //injectProc.BeginOutputReadLine();

                                //using (StreamWriter writer = new StreamWriter("important.txt", true))
                                //{
                                //    writer.WriteLine("readtoend: {0}, readline: {1}", injectProc.StandardOutput.ReadToEnd(), injectProc.StandardOutput.ReadLine());
                                //}


                                injectProc.WaitForExit();
                            }
                            catch (Exception ex)
                            {
                                using (StreamWriter writer = new StreamWriter("error-log.txt", true))
                                {
                                    writer.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]" + "ex msg: {0}, ex str: {1}", ex.Message, ex.ToString());
                                }
                            }
                        }
                        ResumeThread(pi.hThread);
                        pOutPID = (int)pi.dwProcessId;
                    }
                }
                else // regular method (no hooks)
                {
                    proc = Process.Start(startInfo);

                    pOutPID = proc.Id;
                }                

                ConsoleU.WriteLine("Game started, process ID:" + pOutPID /*Marshal.ReadInt32(pid)*/ /*proc.Id*/ /*(int)pi.dwProcessId*/, Palette.Success);
            }
#if RELEASE
            catch
            {
                tri++;
                if (tri < tries)
                {
                    ConsoleU.WriteLine("Failed to start process. Retrying...");
                    StartGame(path, args);
                }
            }
#endif
        }

        public static void proc_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }
            Console.WriteLine($"Redirected output: {e.Data}");
            int.TryParse(e.Data, out pOutPID);
        }

        static void Main(string[] args)
        {
            // We need this, else Windows will fake
            // all the data about monitors inside the application
            User32Util.SetProcessDpiAwareness(ProcessDPIAwareness.ProcessPerMonitorDPIAware);

            if (args.Length == 0)
            {
                ConsoleU.WriteLine("Invalid usage! Need arguments to proceed!", Palette.Error);
                return;
            }

#if RELEASE
            try
#endif
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    ConsoleU.WriteLine("Parsing line " + i + ": " + arg, Palette.Feedback);

                    string argument = "";
                    for (int j = i; j < args.Length; j++)
                    {
                        string skey = args[j];
                        if (!skey.Contains("monitors")
                             && !skey.Contains("game")
                             && !skey.Contains("mutextype")
                             && !skey.Contains("mutex")
                             && !skey.Contains("proc")
                             && !skey.Contains("hook")
                             && !skey.Contains("delay")
                             && !skey.Contains("renamemutex")
                             && !skey.Contains("mutextorename")
                             && !skey.Contains("setwindow")
                             && !skey.Contains("output"))
                        {
                            i++;
                            if (string.IsNullOrEmpty(argument))
                            {
                                argument = skey;
                            }
                            else
                            {
                                argument = argument + " " + skey;
                            }
                        }
                    }
                    ConsoleU.WriteLine("Extra arguments:" + argument, Palette.Feedback);

                    string[] splited = (arg + argument).Split(':');
                    string key = splited[0].ToLower();

                    if (key.Contains("monitors"))
                    {

                    }
                    else if (key.Contains("hook"))
                    {
                        isHook = Boolean.Parse(splited[1]);
                    }
                    else if (key.Contains("delay"))
                    {
                        isDelay = Boolean.Parse(splited[1]);
                    }
                    else if (key.Contains("renamemutex"))
                    {
                        renameMutex = Boolean.Parse(splited[1]);
                    }
                    else if (key.Contains("mutextorename"))
                    {
                        mutexToRename = splited[1];
                    }
                    else if (key.Contains("setwindow"))
                    {
                        setWindow = Boolean.Parse(splited[1]);
                    }
                    else if (key.Contains("game"))
                    {
                        string data = splited[1];
                        string[] subArgs = data.Split(';');
                        string path = subArgs[0];

                        string argu = null;
                        if (subArgs.Length > 1)
                        {
                            argu = subArgs[1];
                        }

                        string workingDir = null;
                        if (path.Contains("|"))
                        {
                            string[] div = path.Split('|');
                            path = div[0];
                            workingDir = div[1];
                        }
                        ConsoleU.WriteLine($"Start game: EXE: {path} ARGS: {argu} WORKDIR: {workingDir}", Palette.Feedback);
                        StartGame(path, argu, workingDir);
                    }
                    else if (key.Contains("mutextype"))
                    {
                        mt = splited[1];
                    }
                    else if (key.Contains("mutex"))
                    {
                        string[] mutex = splited[1].Split(';');
                        ConsoleU.WriteLine("Trying to kill mutexes", Palette.Wait);
                        for (int j = 0; j < mutex.Length; j++)
                        {
                            string m = mutex[j];
                            ConsoleU.WriteLine("Trying to kill mutex: " + m, Palette.Feedback);
                            if (!ProcessUtil.KillMutex(proc, mt, m))
                            {
                                ConsoleU.WriteLine("Mutex " + m + " could not be killed", Palette.Error);
                            }
                            else
                            {
                                ConsoleU.WriteLine("Mutex killed " + m, Palette.Success);
                            }
                            Thread.Sleep(150);
                        }
                    }
                    else if (key.Contains("proc"))
                    {
                        string procId = splited[1];
                        int id = int.Parse(procId);
                        try
                        {
                            proc = Process.GetProcessById(id);
                            ConsoleU.WriteLine($"Process ID {id} found!", Palette.Success);
                        }
                        catch
                        {
                            ConsoleU.WriteLine($"Process ID {id} not found", Palette.Error);
                        }
                    }
                    else if (key.Contains("output"))
                    {
                        string[] mutex = splited[1].Split(';');
                        bool all = true;

                        for (int j = 0; j < mutex.Length; j++)
                        {
                            string m = mutex[j];
                            ConsoleU.WriteLine("Requested mutex: " + m, Palette.Error);
                            bool exists = ProcessUtil.MutexExists(proc, mt, m);
                            if (!exists)
                            {
                                all = false;
                            }
                            
                            Thread.Sleep(500);
                        }
                        Console.WriteLine(all.ToString());
                    }
                }
            }
#if RELEASE
            catch (Exception ex)
            {
                ConsoleU.WriteLine(ex.Message);
            }
#endif
        }
    }
}