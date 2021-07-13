using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace WAUpdater
{
    public class StartDownloadTaskEventArgs : EventArgs
    {
        public StartDownloadTaskEventArgs(DownloadTask task)
        {
            Task = task;
        }

        public DownloadTask Task { get; }
    }
    public delegate void StartDownloadTaskEventHandler(object sender, StartDownloadTaskEventArgs e);

    public class Downloader
    {
        public Downloader()
        {
            TaskScheduler scheduler = TaskScheduler.Default;
            TaskFactory = new TaskFactory(scheduler);
        }
        public DownloadTask Create(Uri uri, Stream stream)
        {
            DownloadTask downloadeTask = null;
            downloadeTask = new DownloadTask(() =>
            {
                WebRequest request = null;
                WebResponse response = null;

                try
                {
                    downloadeTask.ChangeState(DownloadState.Fetching);

                    request = WebRequest.Create(downloadeTask.Uri);
                    response = request.GetResponse();
                    if (downloadeTask.Length == 0)
                    {
                        downloadeTask.Length = response.ContentLength;
                    }

                    downloadeTask.ChangeState(DownloadState.Downloading);

                    byte[] buffer = new byte[1024 * 50];
                    Stream responseStream = response.GetResponseStream();

                    CancellationTokenSource tokenSource = downloadeTask.TokenSource;
                    int i;
                    while (tokenSource.IsCancellationRequested == false)
                    {
                        i = responseStream.Read(buffer, 0, buffer.Length);
                        if (i <= 0)
                        {
                            break;
                        }
                        downloadeTask.Current += i;
                        stream.Write(buffer, 0, i);
                    }

                    if (tokenSource.IsCancellationRequested)
                    {
                        downloadeTask.ChangeState(DownloadState.Canceled);
                    }
                    else
                    {
                        downloadeTask.ChangeState(DownloadState.Success);
                    }
                }
                catch (Exception)
                {
                    downloadeTask.ChangeState(DownloadState.Fail);
                    throw;
                }
                finally
                {
                    response?.Close();
                    request?.Abort();
                }
            }, new CancellationTokenSource());
            downloadeTask.CreateTime = DateTime.Now;
            downloadeTask.Uri = uri;
            return downloadeTask;
        }
        public DownloadTask Create(Uri uri, string fileName)
        {
            DownloadTask downloadeTask = null;
            downloadeTask = new DownloadTask(() =>
            {
                Helpers.PrepareDirectory(downloadeTask.FileName);

                FileStream fs = new FileStream(downloadeTask.FileName + downloadeTask.TmpExtension, FileMode.Create, FileAccess.Write);
                DownloadTask realTask = Create(uri, fs);
                realTask.TokenSource = downloadeTask.TokenSource;
                realTask.StateChanged += (sender, args) =>
                {
                    if(args.State == DownloadState.Downloading && downloadeTask.Length == 0)
                    {
                        downloadeTask.Length = realTask.Length;
                    }
                    downloadeTask.ChangeState(args.State);
                };
                realTask.ProgressChanged += (sender, args) =>
                {
                    downloadeTask.Current = args.BytesReceived;
                };

                try
                {
                    realTask.RunSynchronously();
                }
                catch (Exception)
                {
                    downloadeTask.ChangeState(DownloadState.Fail);
                    throw;
                }
                finally
                {
                    fs?.Close();

                    if (downloadeTask.State == DownloadState.Success)
                    {
                        if (File.Exists(downloadeTask.FileName))
                        {
                            File.Delete(downloadeTask.FileName);
                        }
                        File.Move(fs.Name, downloadeTask.FileName);
                    }
                }
            }, new CancellationTokenSource());
            downloadeTask.CreateTime = DateTime.Now;
            downloadeTask.Uri = uri;
            downloadeTask.FileName = fileName;
            return downloadeTask;
        }

        public void Start(DownloadTask task)
        {
            task.Start(TaskFactory.Scheduler);
            RWLock.EnterWriteLock();
            Tasks.AddLast(task);
            RWLock.ExitWriteLock();

            OnStartDownloadTask?.Invoke(this, new StartDownloadTaskEventArgs(task));
        }

        //public long GetTaskContentLength(DownloadTask task)
        //{
        //    HttpWebRequest request = null;
        //    HttpWebResponse response = null;
        //    try
        //    {
        //        request = (HttpWebRequest)WebRequest.Create(task.Uri);
        //        request.Method = "HEAD";
        //        request.Headers.Set("Accept-Encoding", "identity");
        //        response = (HttpWebResponse)request.GetResponse();
        //        return response.ContentLength;
        //    }
        //    finally
        //    {
        //        response?.Close();
        //        request?.Abort();
        //    }
        //}

        public void ClearTasks(DownloadState state)
        {
            RWLock.EnterWriteLock();
            Tasks.RemoveAll(task => task.State == state);
            RWLock.ExitWriteLock();
        }
        public void ClearFinishedTasks()
        {
            ClearTasks(DownloadState.Fail);
            ClearTasks(DownloadState.Canceled);
            ClearTasks(DownloadState.Success);
        }
        public void Cancel(DownloadTask task)
        {
            task.TokenSource.Cancel();
        }

        public ReaderWriterLockSlim RWLock { get; } = new ReaderWriterLockSlim();
        public LinkedList<DownloadTask> Tasks { get; } = new LinkedList<DownloadTask>();
        public TaskFactory TaskFactory;
        public event StartDownloadTaskEventHandler OnStartDownloadTask;
    }
}
