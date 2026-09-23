using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SerialToTcp
{
    static class Program
    {
        // Must match AppMutex in installer.iss. "Global\" makes it machine-wide: COM ports are
        // machine-wide too, so a second copy in another user session would fight over the same ports.
        public const string MutexName = @"Global\ScrapItSerialToTcp_SingleInstance";

        // Broadcast by a second launch to tell the running copy to restore its window.
        public static readonly int WM_SHOWME = NativeMethods.RegisterWindowMessage("ScrapItSerialToTcp_ShowMe");

        [STAThread]
        static void Main()
        {
            Mutex mutex;
            try
            {
                mutex = new Mutex(true, MutexName, out bool createdNew);
                if (!createdNew)
                {
                    // Already running in this session: bring it forward (it may be hidden in the tray).
                    NativeMethods.PostMessage((IntPtr)NativeMethods.HWND_BROADCAST, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // The mutex exists but was created by another Windows user, so we can't open it.
                MessageBox.Show(
                    "Serial-to-TCP Bridge is already running under another Windows user account " +
                    "(for example another Remote Desktop session).\r\n\r\n" +
                    "Only one copy can run per PC, because each COM port can only be opened by one program at a time.",
                    "Serial-to-TCP Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                {
                    MessageBox.Show(e.Exception.ToString(), "Serial-to-TCP Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    MessageBox.Show(e.ExceptionObject.ToString(), "Serial-to-TCP Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };

                Application.Run(new MainForm());
            }
            finally
            {
                mutex.ReleaseMutex();
                mutex.Dispose();
            }
        }
    }

    internal static class NativeMethods
    {
        public const int HWND_BROADCAST = 0xffff;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
