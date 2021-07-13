using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WAUpdater;

namespace WAUpdate
{
    public enum VersionState
    {
        Unknown,
        Mismatched,
        VersionCheckInProgress,
        UpdateInProgress,
        UpToDate,
        Outdated
    }

    public static class WAUpdateManager
    {
#if DEBUG
        static WAUpdateManager()
        {
            System.Threading.Thread.Sleep(10000);
        }
#endif
        public delegate void DownloadProgressChangedCallback(string currFileName, int currFilePercentage, int totalPercentage);

        public static event EventHandler OnBeforeRestart;
        public static Action<Exception> OnUpdateFailed;
        public static Action OnVersionStateChanged;
        public static DownloadProgressChangedCallback DownloadProgressChanged;

        public static List<UpdateMirror> Mirrors { get; internal set; }

        public static VersionState VersionState { get; private set; }
        public static string GameVersion { get; set; }
        public static string ServerGameVersion { get; set; }
        public static DiffResult Diff => _diff;
        public static long UpdateSize
        {
            get
            {
                if (_updater == null)
                {
                    return -1;
                }

                List<string> filesToDownload = _diff.FilesToDownload;
                var infos = (from info in _updater.VersionFileRemote.FileVersionInfos
                             where filesToDownload.Contains(info.Value.Path)
                             select info.Value).ToList();
                return infos.Sum(info => info.Size);
            }
        }

        const string OLD_UPDATER = "WAUpdate.exe.old";
        static Updater _updater;
        static DiffResult _diff;
        static CancellationTokenSource _downloadCTS;

        private static void ChangeVersionState(VersionState newState)
        {
            VersionState = newState;
            OnVersionStateChanged?.Invoke();
        }

        public static void Initialize()
        {
            Mirrors = new List<UpdateMirror>();

            VersionState = VersionState.Unknown;
        }

        public static UpdateMirror SelectMirror()
        {
            return Mirrors[0];
        }

        public static void CalculateVersion()
        {
            Updater updater = new Updater(SelectMirror());
            updater.VersionFile.Read();
            GameVersion = updater.VersionFile.VersionNumber;
            updater.CalculateVersion();
            _updater = updater;
        }

        public static void CheckVersion()
        {
            if (File.Exists(OLD_UPDATER))
            {
                Console.WriteLine("delete {0}.", OLD_UPDATER);
                File.Delete(OLD_UPDATER);
            }

            ChangeVersionState(VersionState.VersionCheckInProgress);
            if (_updater.CheckUpdate(out _diff))
            {
                if(_diff.FilesToDownload.Count <= 0)
                {
                    ChangeVersionState(VersionState.UpToDate);
                }
                else
                {
                    ChangeVersionState(VersionState.Outdated);
                }
                ServerGameVersion = _updater.VersionFileRemote.VersionNumber;
            }
            else
            {
                ChangeVersionState(VersionState.Unknown);
            }
        }

        public static void CheckVersionAsync()
        {
            Task.Run(() =>
            {
                CheckVersion();
            });
        }

        public static void FetchUpdate()
        {
            try
            {
                ChangeVersionState(VersionState.UpdateInProgress);
                _updater.Downloader.OnStartDownloadTask += (sender, args) =>
                {
                    args.Task.ProgressChanged += Task_ProgressChanged;
                };
                _downloadCTS = new CancellationTokenSource();
                Task downloadTask = _updater.DownloadFiles(_diff, _downloadCTS);
                downloadTask.Start();

                _updater.VersionFile.Write();
                downloadTask.Wait();

                OnBeforeRestart?.Invoke(null, EventArgs.Empty);

                File.Copy("WAUpdate.exe", OLD_UPDATER, true);

                Process process = new Process();
                process.StartInfo.FileName = OLD_UPDATER;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.Arguments = "-update";
                process.Start();
            }
            catch (Exception ex)
            {
                ChangeVersionState(VersionState.Unknown);
                OnUpdateFailed?.Invoke(ex);
            }
        }

        public static void FetchUpdateAsync()
        {
            Task.Run(() =>
            {
                FetchUpdate();
            });
        }

        private static void Task_ProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            var task = sender as DownloadTask;
            _updater.Downloader.RWLock.EnterReadLock();
            long totalDownloadSize = _updater.Downloader.Tasks.Sum(t => t.Current);
            _updater.Downloader.RWLock.ExitReadLock();
            DownloadProgressChanged?.Invoke(task.FileName, e.ProgressPercentage, (int)(totalDownloadSize / (double)UpdateSize * 100));
        }

        public static void Cancel()
        {
            _downloadCTS.Cancel();
            Task.Run(() =>
            {
                while (true)
                {
                    _updater.Downloader.RWLock.EnterWriteLock();
                    foreach (DownloadTask task in _updater.Downloader.Tasks)
                    {
                        _updater.Downloader.Cancel(task);
                    }
                    _updater.Downloader.RWLock.ExitWriteLock();

                    _updater.Downloader.ClearFinishedTasks();
                    Thread.Sleep(200);
                    if (_updater.Downloader.Tasks.Count == 0)
                    {
                        break;
                    }
                }

                ChangeVersionState(VersionState.Outdated);
            });
        }
    }
}
