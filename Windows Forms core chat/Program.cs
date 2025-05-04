using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Windows_Forms_Chat
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.ThreadException += (sender, args) =>
            {
                MessageBox.Show("An unexpected error occurred:\n" + args.Exception.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
