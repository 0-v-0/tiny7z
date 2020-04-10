using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tiny7z.Archive;

namespace Tiny7z
{
    class Program
    {
        public static readonly string InternalBase = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                File.Delete(Path.Combine(InternalBase, "debuglog.txt"));
            }
            catch { }

            var ctl = new ConsoleTraceListener();
            Trace.Listeners.Clear();
            Trace.Listeners.Add(ctl);
            Trace.Listeners.Add(new TextWriterTraceListener(
                Path.Combine(InternalBase, "debuglog.txt")));
            Trace.AutoFlush = true;

            // try compression

            DateTime now;
            TimeSpan ela;
            try
            {
                /*if (Directory.Exists(Path.Combine(InternalBase, "test")))
                {
                    System.Threading.Thread.Sleep(100);
                    Directory.Delete(Path.Combine(InternalBase, "test"), true);
                    System.Threading.Thread.Sleep(100);
                }
                Directory.CreateDirectory(Path.Combine(InternalBase, "test"));

                FolderBrowserDialog fbd = new FolderBrowserDialog();
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    if (!Directory.Exists(fbd.SelectedPath))
                        throw new ApplicationException("Path not found.");

                    string destFileName = Path.Combine(InternalBase, "OutputTest.7z");
                    SevenZipArchive f = new SevenZipArchive(File.Create(destFileName), FileAccess.Write);
                    var cmp = f.Compressor();
                    cmp.Solid = true;
                    cmp.CompressHeader = true;
                    cmp.AddDirectory(fbd.SelectedPath);
                    now = DateTime.Now; cmp.Finalize(); ela = DateTime.Now.Subtract(now);
                    f.Close();
                    Trace.TraceInformation($"Compression took {ela.TotalMilliseconds}ms.");
                }
                else
                {
                    Trace.TraceWarning("Cancelling...");
                }*/

                // try decompression

                string sourceFileName = Path.Combine(InternalBase, "OutputTest.7z");
                var f2 = new SevenZipArchive(File.OpenRead(sourceFileName), FileAccess.Read);
                var ext = f2.Extractor();
                ext.OverwriteExistingFiles = true;
                now = DateTime.Now; ext.ExtractArchive(Path.Combine(InternalBase, "test")); ela = DateTime.Now.Subtract(now);
                Trace.TraceInformation($"Decompression took {ela.TotalMilliseconds}ms.");
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message + ex.StackTrace);
            }

            Console.WriteLine("Press any key to end...");
            Console.ReadKey();
        }
    }
}
