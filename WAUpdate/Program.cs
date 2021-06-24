using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WAUpdater;

namespace Test
{
    class Program
    {
        static void Main(string[] args)
        {
            Program.args = args;
            Console.ReadLine();
            Console.WriteLine("TEST");
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
                    var tasks = updater.Downloader.Tasks;
                    while (downloadTask == null)
                    {
                        Thread.Sleep(50);
                    }
                    while (downloadTask.Wait(1000) == false)
                    {
                        Console.WriteLine("-----------------------------------");
                        foreach (DownloadTask task in tasks)
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
                process.StartInfo.UseShellExecute = true;
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
                    process.WaitForExit();
                }
            }

            // replace it before it loaded
            string dll_old = "WAUpdater.dll";
            string dll_new = Path.Combine("Update", dll_old);
            if (File.Exists(dll_new))
            {
                Console.WriteLine("replace old {0}...", dll_old);
                File.Copy(dll_new, dll_old, true);
                File.Delete(dll_new);
            }

            Console.WriteLine("updating...");
            UpdateFiles();
        }

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
