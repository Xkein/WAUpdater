using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WAUpdater
{
    public enum DownloadState
    {
        Created, Fetching, Downloading, Stop, Fail, Canceled, Success
    }
    public class DownloadStateChangedEventArgs : EventArgs
    {
        public DownloadStateChangedEventArgs(DownloadState state)
        {
            State = state;
        }

        public DownloadState State { get; }
    }
    public class DownloadProgressChangedEventArgs : System.ComponentModel.ProgressChangedEventArgs
    {
        internal DownloadProgressChangedEventArgs(long bytesReceived, long totalBytesToReceive, object userState = null) : base((totalBytesToReceive <= 0L) ? 0 : ((int)(bytesReceived * 100L / totalBytesToReceive)), userState)
        {
            BytesReceived = bytesReceived;
            TotalBytesToReceive = totalBytesToReceive;
        }
        public long BytesReceived { get; }
        public long TotalBytesToReceive { get; }
    }

    public delegate void DownloadStateChangedEventHandler(object sender, DownloadStateChangedEventArgs args);
    public delegate void DownloadProgressChangedEventHandler(object sender, DownloadProgressChangedEventArgs e);
    public class DownloadTask : Task
    {
        public DownloadTask(Action action, CancellationTokenSource cts) : base(action, cts.Token)
        {
            State = DownloadState.Created;
            TokenSource = cts;
        }

        internal void ChangeState(DownloadState newState)
        {
            State = newState;
            StateChanged?.Invoke(this, new DownloadStateChangedEventArgs(newState));
        }

        public long Length { get; internal set; }
        private long current;
        public long Current
        {
            get => current;
            internal set
            {
                if (current != value)
                {
                    current = value;
                    ProgressChanged?.Invoke(this, new DownloadProgressChangedEventArgs(current, Length));
                }
            }
        }
        public double Progress => Current / (double)Length;
        public DateTime CreateTime { get; internal set; }
        public DownloadState State { get; private set; }
        public CancellationTokenSource TokenSource { get; internal set; }
        public Uri Uri { get; internal set; }
        public string FileName { get; internal set; }
        public string TmpExtension { get; set; } = ".tmp";
        public Exception CurrentException { get; internal set; }

        public event DownloadStateChangedEventHandler StateChanged;
        public event DownloadProgressChangedEventHandler ProgressChanged;
    }
}
