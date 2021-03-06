using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WAUpdater;

namespace WAUpdate.CUI
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Program.args = args;
#if DEBUG
                Console.ReadLine();
#endif
                switch (args[0])
                {
                    case "-sync":
                        Sync();
                        break;
                    case "-update":
                        Update();
                        break;
                    case "-calc":
                        Calc();
                        break;
                }
            }
            catch (Exception ex)
            {
                ShowException(ex);
            }
        }

        private static void ShowException(Exception ex)
        {
            System.Windows.Forms.MessageBox.Show(
                                $"Message: {ex.Message}{Environment.NewLine}" +
                                $"Source: {ex.Source}{Environment.NewLine}" +
                                $"TargetSite.Name: {ex.TargetSite?.Name}{Environment.NewLine}" +
                                $"Stacktrace: {ex.StackTrace}",
                                "Error!",
                                System.Windows.Forms.MessageBoxButtons.OK,
                                System.Windows.Forms.MessageBoxIcon.Error);
        }

        static string[] args;
        static volatile Task downloadTask = null;
        static void Sync()
        {
            UpdateMirror mirror = new UpdateMirror(args[1], args[3], args[2], long.Parse(args[4]));

            const string OLD_UPDATER = "WAUpdate.exe.old";
            if (File.Exists(OLD_UPDATER))
            {
                Console.WriteLine("delete {0}.", OLD_UPDATER);
                File.Delete(OLD_UPDATER);
            }

            Updater updater = new Updater(mirror);
            Console.WriteLine("checking update from {0}.", mirror);
            if (updater.CheckUpdate(out DiffResult diff))
            {
                Task displayTask = new Task(() =>
                {
                    while (downloadTask == null)
                    {
                        Thread.Sleep(50);
                    }
                    while (downloadTask.Wait(1000) == false)
                    {
                        Console.WriteLine("-----------------------------------");
                        updater.Downloader.RWLock.EnterReadLock();
                        foreach (DownloadTask task in updater.Downloader.Tasks)
                        {
                            if (task.State == DownloadState.Downloading)
                            {
                                if(task.Length > 0)
                                {
                                    Console.WriteLine("{0}: {1}/{2} ({3:0.##%})", task.FileName, task.Current, task.Length, task.Progress);
                                }
                                else
                                {
                                    Console.WriteLine("{0}: {1}/? (?%)", task.FileName, task.Current);
                                }
                            }
                        }
                        updater.Downloader.RWLock.ExitReadLock();
                        Console.WriteLine("-----------------------------------");
                    }
                }, TaskCreationOptions.HideScheduler
                );

                displayTask.Start();
                Console.WriteLine("downloading...");
                downloadTask = updater.DownloadFiles(diff);
                downloadTask.Start();

                Console.WriteLine("writing checksums.");
                updater.VersionFile.Write();
                downloadTask.Wait();

                Console.WriteLine("starting update progress...");
                File.Copy("WAUpdate.exe", OLD_UPDATER, true);

                Process process = new Process();
                process.StartInfo.FileName = OLD_UPDATER;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.Arguments = "-update";
                process.Start();
            }
            else
            {
                Console.WriteLine("could not fetch version file!");
            }
        }
        static void Update()
        {
            string[] processesToWait = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.exe", SearchOption.AllDirectories);
            foreach (string procPath in processesToWait)
            {
                string procName = Path.GetFileNameWithoutExtension(procPath);
                foreach (Process process in Process.GetProcessesByName(procName))
                {
                    Console.WriteLine("waiting {0}...", procName);
                    Console.WriteLine("if {0} is not close, you can close it manually.", procName);
                    process.WaitForExit();
                }
            }

            // replace it before it loaded
            string dll_old = "WAUpdater.dll";
            string dll_new = Path.Combine("Update", dll_old);

            while (File.Exists(dll_new))
            {
                try
                {
                    Console.WriteLine("replace old {0}...", dll_old);
                    File.Copy(dll_new, dll_old, true);
                    File.Delete(dll_new);
                }
                catch (Exception)
                {
                    Console.WriteLine("fail. retrying.");
                }
            }

            Console.WriteLine("updating...");
            UpdateFiles();
        }

        // avoid loading WAUpdater.dll in inlined funciton.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void UpdateFiles()
        {
            Updater updater = new Updater();
            VersionFile versionFile = updater.VersionFile;
            VersionFile versionFileRemote = updater.VersionFileRemote;
            versionFile.Read();
            versionFileRemote.Read();
            updater.UpdateFiles(versionFile.GetDiff(versionFileRemote));
        }

        static void Calc()
        {
            UpdateMirror mirror = new UpdateMirror(args[1], args[3], args[2], long.Parse(args[4]));

            Updater updater = new Updater(mirror);
            VersionFile versionFile = updater.VersionFile;
            Console.WriteLine("calculate checksums...");
            versionFile.Calculate(updater.Ignore, updater.Decomposer);
            Console.WriteLine("writing checksums...");
            versionFile.Write();
            versionFile.Read();
        }
    }
}
