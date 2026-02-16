using System;
using System.Windows.Forms;

namespace SerialToTcp
{
    static class Program
    {
        [STAThread]
        static void Main()
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
    }
}
