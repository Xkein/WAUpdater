using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WAUpdater
{
    public class Updater
    {
        const string IGNORE_FILE = "version_ignore";
        const string VERSION_FILE = "version_file";
        const string VERSION_FILE_REMOTE = VERSION_FILE + "_remote";
        const string UPDATE_DIR = "Update";

        public Updater() : this(new UpdateMirror("local", Directory.GetCurrentDirectory(), "local"))
        {
        }
        public Updater(UpdateMirror mirror, int maxDownloadCount = 4)
        {
            Downloader = new Downloader();
            VersionFile = new VersionFile(VERSION_FILE);
            VersionFileRemote = new VersionFile(VERSION_FILE_REMOTE);
            Mirror = mirror;
            MaxDownloadCount = maxDownloadCount;
            Decomposer = new Decomposer(Mirror.FileSizeLimit);

            ReadIgnore();
        }

        public Downloader Downloader { get; internal set; }
        public VersionFile VersionFile { get; internal set; }
        public VersionFile VersionFileRemote { get; internal set; }
        public UpdateMirror Mirror { get; }
        public List<Regex> Ignore { get; internal set; }
        public Decomposer Decomposer { get; internal set; }
        public int MaxDownloadCount
        {
            get => maxDownloadCount;
            set
            {
                if (ServicePointManager.DefaultConnectionLimit < value)
                {
                    ServicePointManager.DefaultConnectionLimit = value;
                }
                maxDownloadCount = value;
            }
        }
        private int maxDownloadCount;

        void ReadIgnore()
        {
            List<Regex> ignore = new List<Regex>();

            if (File.Exists(IGNORE_FILE))
            {
                using (FileStream file = new FileStream(IGNORE_FILE, FileMode.Open, FileAccess.Read))
                {
                    StreamReader reader = new StreamReader(file);
                    while (reader.EndOfStream == false)
                    {
                        string wildCard = reader.ReadLine();
                        if (wildCard != string.Empty)
                        {
                            string rex = "^" + Regex.Escape(wildCard).Replace("\\?", ".").Replace("\\*", ".*") + "$";
                            ignore.Add(new Regex(rex, RegexOptions.IgnoreCase));
                        }
                    }
                }
            }
            Ignore = ignore;
        }

        public void CalculateVersion()
        {
            VersionFile.Calculate(Ignore);
        }

        public bool CheckUpdate(out DiffResult diff)
        {
            if (File.Exists(VERSION_FILE_REMOTE))
            {
                File.Delete(VERSION_FILE_REMOTE);
            }

            DownloadTask task = Downloader.Create(Map(VERSION_FILE), VERSION_FILE_REMOTE);
            Downloader.Start(task);
            task.Wait();
            CalculateVersion();

            if (task.State == DownloadState.Success)
            {
                VersionFileRemote.Read();
                diff = VersionFile.GetDiff(VersionFileRemote);
                return true;
            }

            diff = new DiffResult();
            return false;
        }

        public Task DownloadFiles(DiffResult diff, CancellationTokenSource cancellationTokenSource = null)
        {
            return new Task(() =>
            {
                Downloader.ClearFinishedTasks();

                Directory.CreateDirectory(UPDATE_DIR);

                Semaphore semaphore = new Semaphore(MaxDownloadCount, MaxDownloadCount);
                DownloadStateChangedEventHandler handler = (object sender, DownloadStateChangedEventArgs args) =>
                {
                    var task = sender as DownloadTask;
                    if (args.State != DownloadState.Canceled || task.Status > TaskStatus.Running)
                    {
                        switch (args.State)
                        {
                            case DownloadState.Stop:
                            case DownloadState.Fail:
                            case DownloadState.Canceled:
                            case DownloadState.Success:
                                semaphore.Release();
                                break;
                        }
                    }
                };
                Func<FileVersionInfo, DownloadTask> CreateDownloadTask = (FileVersionInfo versionInfo) =>
                {
                    DownloadTask task = null;
                    if (versionInfo.IsDecomposed)
                    {
                        task = new DownloadTask(() =>
                        {
                            task.ChangeState(DownloadState.Fetching);

                            List<DownloadTask> volumnTasks = new List<DownloadTask>();
                            foreach (FileVersionInfo volumn in versionInfo.Volumns)
                            {
                                DownloadTask volumnTask = Downloader.Create(Map(volumn.Path), GetUpdatePath(volumn.Path));
                                volumnTask.TokenSource = task.TokenSource;
                                volumnTask.StateChanged += handler;
                                volumnTask.Length = volumn.Size;
                                volumnTasks.Add(volumnTask);
                            }

                            CancellationTokenSource tokenSource = task.TokenSource;

                            Task downloadTask = new Task(() =>
                            {
                                task.ChangeState(DownloadState.Downloading);
                                semaphore.Release();
                                foreach (DownloadTask volumnTask in volumnTasks)
                                {
                                    // download task canceled, stop starting other new download task.
                                    if (tokenSource.IsCancellationRequested)
                                    {
                                        volumnTask.ChangeState(DownloadState.Canceled);
                                        break;
                                    }
                                    else
                                    {
                                        semaphore.WaitOne();
                                        Downloader.Start(volumnTask);
                                    }
                                }
                            });

                            try
                            {
                                downloadTask.Start();
                                DownloadTask[] downloadTasks = volumnTasks.ToArray();
                                while (Task.WaitAll(downloadTasks, TimeSpan.FromSeconds(1)) == false)
                                {
                                    // download task canceled, stop waiting.
                                    if (tokenSource.IsCancellationRequested)
                                    {
                                        break;
                                    }
                                    task.Current = downloadTasks.Sum(t => t.Current);
                                }

                                task.ChangeState(tokenSource.IsCancellationRequested ? DownloadState.Canceled : DownloadState.Success);
                                if (task.State == DownloadState.Success)
                                {
                                    string fileName = downloadTasks[0].FileName;
                                    fileName = Path.Combine(Path.GetDirectoryName(fileName), Path.GetFileNameWithoutExtension(fileName));
                                    Decomposer.Compose(fileName, task.FileName);
                                }
                            }
                            catch (Exception ex)
                            {
                                task.CurrentException = ex;
                                task.ChangeState(DownloadState.Fail);
                                throw;
                            }
                        }, new CancellationTokenSource());
                        task.FileName = GetUpdatePath(versionInfo.Path);
                    }
                    else
                    {
                        task = Downloader.Create(Map(versionInfo.Path), GetUpdatePath(versionInfo.Path));
                        task.StateChanged += handler;
                    }
                    task.Length = versionInfo.Size;
                    return task;
                };

                List<string> filesToDownload = diff.FilesToDownload;
                List<DownloadTask> tasks = new List<DownloadTask>();
                do
                {
                    tasks.Clear();
                    foreach (string file in filesToDownload)
                    {
                        if (IsDownloaded(file) == false)
                        {
                            tasks.Add(CreateDownloadTask(VersionFileRemote.FileVersionInfos[file]));
                        }
                    }

                    foreach (DownloadTask task in tasks)
                    {
                        // download task canceled, stop starting other new download task.
                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            task.ChangeState(DownloadState.Canceled);
                            break;
                        }
                        else
                        {
                            semaphore.WaitOne();
                            Downloader.Start(task);
                        }
                    }

                    try
                    {
                        Task.WaitAll(tasks.ToArray());
                    }
                    catch (AggregateException)
                    {
                    }
                } while (tasks.Count > 0 && cancellationTokenSource.IsCancellationRequested == false);
            });
        }

        private bool IsDownloaded(string fileName)
        {
            if (File.Exists(GetUpdatePath(fileName)))
            {
                return Checksum.CalcFileChecksum(GetUpdatePath(fileName)) == VersionFileRemote.FileVersionInfos[fileName].Checksum;
            }

            return false;
        }

        // map file path to server
        private Uri Map(string fileName)
        {
            string uriString = Path.Combine(Mirror.UriBase, fileName).Replace("\\", "/");
            return new Uri(uriString);
        }
        // map file path to update directory
        private static string GetUpdatePath(string fileName)
        {
            return Path.Combine(UPDATE_DIR, fileName);
        }

        public void UpdateFiles(DiffResult diff)
        {
            List<string> filesToCopy = diff.FilesToDownload;
            foreach (string file in filesToCopy)
            {
                Helpers.PrepareDirectory(file);

                string file_new = GetUpdatePath(file);
                if (File.Exists(file_new))
                {
                    File.Copy(file_new, file, true);
                }
            }

            foreach (string file in diff.Removeds)
            {
                File.Delete(file);
                string dir = Path.GetDirectoryName(file);
                if (string.IsNullOrEmpty(dir) == false && Directory.GetFiles(dir).Length == 0)
                {
                    Directory.Delete(dir);
                }
            }

            Directory.Delete(UPDATE_DIR, true);
            if (Directory.Exists("Volumns"))
            {
                Directory.Delete("Volumns", true);
            }

            VersionFile.Calculate(Ignore);
            VersionFile.VersionNumber = VersionFileRemote.VersionNumber;
            VersionFile.Write();

            File.Delete(VERSION_FILE_REMOTE);
        }
    }
}
