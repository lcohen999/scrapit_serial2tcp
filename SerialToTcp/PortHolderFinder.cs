using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace SerialToTcp
{
    /// <summary>
    /// Finds which process has a COM port open, the same way Sysinternals Handle does: snapshot every open
    /// handle on the system, duplicate the file handles into this process, and compare their kernel object
    /// name with the port's device name (e.g. COM2 -> \Device\Serial1, COM5 -> \Device\VCP0).
    /// Without admin rights, processes belonging to other users, services and elevated programs can't be inspected.
    /// </summary>
    public static class PortHolderFinder
    {
        public record Holder(int Pid, string ProcessName, string? Path);

        public class Result
        {
            public Dictionary<string, List<Holder>> Holders { get; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>Processes with open files that we weren't allowed to look inside.</summary>
            public int UninspectableProcesses { get; set; }
            public bool Elevated { get; set; }
            public string? Error { get; set; }
        }

        public static async Task<Result> FindAsync(IReadOnlyCollection<string> comPorts, TimeSpan timeout)
        {
            // Dedicated thread + timeout: querying handle names is a known place for Windows calls to hang.
            var work = Task.Factory.StartNew(() => Find(comPorts), TaskCreationOptions.LongRunning);
            if (await Task.WhenAny(work, Task.Delay(timeout)) == work)
                return await work;

            var r = new Result { Error = "timed out while scanning processes" };
            foreach (var p in comPorts) r.Holders[p] = new();
            return r;
        }

        public static Result Find(IReadOnlyCollection<string> comPorts)
        {
            var result = new Result { Elevated = IsElevated() };
            var portByDevice = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var port in comPorts)
            {
                result.Holders[port] = new();
                var device = QueryDosDevice(port);
                if (device != null) portByDevice[device] = port;
            }
            if (portByDevice.Count == 0) return result;

            IntPtr buffer = IntPtr.Zero;
            var processHandles = new Dictionary<int, IntPtr>();
            var holderPids = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                int ownPid = Environment.ProcessId;
                ushort fileTypeIndex;
                long count;

                // Hold a file handle of our own open during the snapshot so we can learn the "File" object type index.
                using (var probe = File.OpenRead(Environment.ProcessPath ?? typeof(PortHolderFinder).Assembly.Location))
                {
                    buffer = SnapshotHandles();
                    count = Marshal.ReadIntPtr(buffer).ToInt64();
                    long probeHandle = probe.SafeFileHandle.DangerousGetHandle().ToInt64();
                    fileTypeIndex = 0;
                    for (long i = 0; i < count; i++)
                    {
                        var e = ReadEntry(buffer, i);
                        if (e.Pid == ownPid && e.Handle == probeHandle) { fileTypeIndex = e.TypeIndex; break; }
                    }
                }
                if (fileTypeIndex == 0)
                {
                    result.Error = "could not determine the file handle type";
                    return result;
                }

                IntPtr self = GetCurrentProcess();
                var uninspectable = new HashSet<int>();

                for (long i = 0; i < count; i++)
                {
                    var e = ReadEntry(buffer, i);
                    if (e.TypeIndex != fileTypeIndex || e.Pid <= 4) continue;

                    if (!processHandles.TryGetValue(e.Pid, out var proc))
                    {
                        proc = OpenProcess(PROCESS_DUP_HANDLE, false, e.Pid);
                        processHandles[e.Pid] = proc;
                    }
                    if (proc == IntPtr.Zero) { uninspectable.Add(e.Pid); continue; }

                    if (!DuplicateHandle(proc, (IntPtr)e.Handle, self, out var dup, 0, false, DUPLICATE_SAME_ACCESS))
                        continue;
                    try
                    {
                        // Only character devices (serial ports, consoles, NUL). This also skips pipes, whose name query can hang.
                        if (GetFileType(dup) != FILE_TYPE_CHAR) continue;
                        var name = GetObjectName(dup);
                        if (name != null && portByDevice.TryGetValue(name, out var port))
                        {
                            if (!holderPids.TryGetValue(port, out var set)) holderPids[port] = set = new();
                            set.Add(e.Pid);
                        }
                    }
                    finally
                    {
                        CloseHandle(dup);
                    }
                }

                result.UninspectableProcesses = uninspectable.Count;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                foreach (var h in processHandles.Values)
                    if (h != IntPtr.Zero) CloseHandle(h);
            }

            foreach (var (port, pids) in holderPids)
                result.Holders[port] = pids.Select(DescribeProcess).ToList();
            return result;
        }

        /// <summary>One or two sentences for the user about who holds <paramref name="comPort"/>.</summary>
        public static string Explain(Result r, string comPort)
        {
            var holders = r.Holders.TryGetValue(comPort, out var h) ? h : new List<Holder>();
            if (holders.Count > 0)
            {
                var parts = holders.Select(x =>
                {
                    bool isUs = string.Equals(x.ProcessName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase);
                    string who = isUs ? $"another copy of Serial-to-TCP Bridge (PID {x.Pid})" : $"{x.ProcessName} (PID {x.Pid})";
                    return x.Path != null ? $"{who} — {x.Path}" : who;
                });
                return $"{comPort} is currently held by: {string.Join("; ", parts)}. Close that program to free the port.";
            }

            if (r.Error != null)
                return $"Could not identify which program holds {comPort} ({r.Error}).";
            if (!r.Elevated && r.UninspectableProcesses > 0)
                return $"Could not identify which program holds {comPort}. It is probably a Windows service or a program running as " +
                       $"administrator ({r.UninspectableProcesses} processes could not be inspected without admin rights). " +
                       "Run Serial-to-TCP Bridge as administrator once to see the program's name.";
            return $"No program on this PC was found holding {comPort}. It may be held by a driver or a protected system process — " +
                   "unplugging/replugging the device or rebooting will release it.";
        }

        private static Holder DescribeProcess(int pid)
        {
            string name = $"PID {pid}";
            string? path = null;
            try
            {
                using var p = Process.GetProcessById(pid);
                name = p.ProcessName;
                try { path = p.MainModule?.FileName; } catch { }
            }
            catch { }
            return new Holder(pid, name, path);
        }

        private readonly record struct Entry(int Pid, long Handle, ushort TypeIndex);

        // SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX: { PVOID Object; ULONG_PTR Pid; ULONG_PTR Handle; ULONG Access;
        //                                     USHORT CreatorBackTraceIndex; USHORT ObjectTypeIndex; ULONG Attributes; ULONG Reserved; }
        // preceded by a header of { ULONG_PTR NumberOfHandles; ULONG_PTR Reserved; }.
        private static Entry ReadEntry(IntPtr buffer, long index)
        {
            int ptr = IntPtr.Size;
            int entrySize = 3 * ptr + 16;
            IntPtr e = buffer + 2 * ptr + (int)(index * entrySize);
            return new Entry(
                (int)Marshal.ReadIntPtr(e, ptr).ToInt64(),
                Marshal.ReadIntPtr(e, 2 * ptr).ToInt64(),
                (ushort)Marshal.ReadInt16(e, 3 * ptr + 6));
        }

        private static IntPtr SnapshotHandles()
        {
            int size = 4 * 1024 * 1024;
            while (true)
            {
                IntPtr buf = Marshal.AllocHGlobal(size);
                int status = NtQuerySystemInformation(SystemExtendedHandleInformation, buf, size, out int needed);
                if (status == 0) return buf;
                Marshal.FreeHGlobal(buf);
                if (status != STATUS_INFO_LENGTH_MISMATCH || size > 512 * 1024 * 1024)
                    throw new InvalidOperationException($"NtQuerySystemInformation failed (0x{status:X8})");
                // Handles are created constantly, so leave headroom beyond what was asked for.
                size = Math.Max(size * 2, needed + 1024 * 1024);
            }
        }

        private static string? GetObjectName(IntPtr handle)
        {
            const int size = 2048;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (NtQueryObject(handle, ObjectNameInformation, buf, size, out _) != 0) return null;
                // UNICODE_STRING { USHORT Length; USHORT MaximumLength; PWSTR Buffer; }
                int length = (ushort)Marshal.ReadInt16(buf);
                IntPtr str = Marshal.ReadIntPtr(buf, IntPtr.Size);
                return length == 0 || str == IntPtr.Zero ? null : Marshal.PtrToStringUni(str, length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        internal static string? QueryDosDevice(string comPort)
        {
            var sb = new StringBuilder(1024);
            if (QueryDosDeviceW(comPort, sb, sb.Capacity) == 0) return null;
            return sb.ToString(); // first string of the multi-string, e.g. \Device\Serial1
        }

        private static bool IsElevated()
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private const int SystemExtendedHandleInformation = 64;
        private const int ObjectNameInformation = 1;
        private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
        private const uint PROCESS_DUP_HANDLE = 0x0040;
        private const uint DUPLICATE_SAME_ACCESS = 0x2;
        private const uint FILE_TYPE_CHAR = 0x0002;

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryObject(IntPtr handle, int infoClass, IntPtr info, int length, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess,
            out IntPtr targetHandle, uint access, bool inherit, uint options);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern uint GetFileType(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint QueryDosDeviceW(string deviceName, StringBuilder targetPath, int max);
    }
}
