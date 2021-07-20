using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace WAUpdater
{
    public struct DiffResult
    {
        public List<string> Addeds;
        public List<string> Removeds;
        public List<string> Changeds;

        public bool NoDiff()
        {
            return Addeds.Count + Removeds.Count + Changeds.Count == 0;
        }

        public List<string> FilesToDownload => Addeds.Concat(Changeds).ToList();
    }
    public class VersionFile
    {
        public VersionFile(string name)
        {
            fileName = Path.GetFullPath(name);
            VersionNumber = "unverified!";
        }
        string GetPath(FileInfo info)
        {
            return info.FullName.Replace(BaseDirectory, "").Substring(1);
        }
        public void Read()
        {
            RWLock.EnterWriteLock();

            FileVersionInfos.Clear();
            if (File.Exists(fileName))
            {
                XElement file = XElement.Load(fileName);
                VersionNumber = (string)file.Attribute("Number");
                foreach (XElement element in file.Elements("AllowUpdateVersion"))
                {
                    AllowUpdateVersions.Add((string)element.Attribute("Number"));
                }
                foreach (XElement element in file.Elements("File"))
                {
                    var info = new FileVersionInfo(element);
                    FileVersionInfos.Add(info.Path, info);
                }
            }

            RWLock.ExitWriteLock();
        }
        bool IsInHiddenDirectory(FileInfo info)
        {
            string dir = info.FullName;
            while(dir != string.Empty && dir != BaseDirectory)
            {
                dir = Path.GetDirectoryName(dir);
                var dirInfo = new DirectoryInfo(dir);
                if((dirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                {
                    return true;
                }
            }

            return false;
        }

        public void Calculate(List<Regex> ignore, Decomposer decomposer = null)
        {
            RWLock.EnterWriteLock();
            FileVersionInfos.Clear();

            var dir = new DirectoryInfo(BaseDirectory);

            List<FileInfo> list = dir.GetFiles("*", SearchOption.AllDirectories).ToList();

            list = (from info in list
                    where info.Attributes != FileAttributes.Hidden
                    where IsInHiddenDirectory(info) == false
                    where info.FullName != fileName
                    select info).ToList();

            List<string> relPaths = (from info in list select GetPath(info)).ToList();

            List<string> toIgnore = new List<string>();
            foreach (Regex rex in ignore)
            {
                foreach (string relPath in relPaths)
                {
                    if (rex.IsMatch(relPath))
                    {
                        toIgnore.Add(relPath);
                    }
                }
            }

            List<string> toCalc = relPaths.Except(toIgnore).ToList();

            List<FileVersionInfo> fileVersionInfos = (from relPath in toCalc.AsParallel()
                                                      select new FileVersionInfo(relPath, decomposer))
                                                      .ToList();

            foreach (FileVersionInfo info in fileVersionInfos)
            {
                FileVersionInfos.Add(info.Path, info);
            }
            RWLock.ExitWriteLock();
        }

        public void Write()
        {
            var file = new XElement("Version", new XAttribute("Number", VersionNumber));

            RWLock.EnterReadLock();
            if (AllowUpdateVersions.Contains(VersionNumber) == false)
            {
                AllowUpdateVersions.Add(VersionNumber);
            }
            foreach (string version in AllowUpdateVersions)
            {
                file.Add(new XElement("AllowUpdateVersion", new XAttribute("Number", version)));
            }
            foreach (var pair in FileVersionInfos)
            {
                string path = pair.Key;
                FileVersionInfo info = pair.Value;
                file.Add(info.GetXElement());
            }
            RWLock.ExitReadLock();

            file.Save(fileName);
        }

        public DiffResult GetDiff(VersionFile versionFile)
        {
            DiffResult tmp;
            RWLock.EnterReadLock();
            List<string> from = this.FileVersionInfos.Keys.ToList();
            List<string> to = versionFile.FileVersionInfos.Keys.ToList();

            tmp.Changeds = (from _old in this.FileVersionInfos
                            from _new in versionFile.FileVersionInfos
                            where _old.Key == _new.Key
                            where _old.Value.Checksum != _new.Value.Checksum
                            select _old.Key
                            ).ToList();
            RWLock.ExitReadLock();
            tmp.Addeds = to.Except(from).ToList();
            tmp.Removeds = from.Except(to).ToList();

            return tmp;
        }

        public string[] GetUploadableFiles()
        {
            List<string> files = new List<string>();
            RWLock.EnterReadLock();
            foreach (FileVersionInfo info in FileVersionInfos.Values)
            {
                if (info.IsDecomposed)
                {
                    files.AddRange(info.GetVolumns());
                }
                else
                {
                    files.Add(info.Path);
                }
            }
            RWLock.ExitReadLock();
            return files.ToArray();
        }

        string fileName;
        string BaseDirectory => Path.GetDirectoryName(fileName);
        public Dictionary<string, FileVersionInfo> FileVersionInfos { get; private set; } = new Dictionary<string, FileVersionInfo>();
        public ReaderWriterLockSlim RWLock { get; } = new ReaderWriterLockSlim();
        public string VersionNumber { get; set; }
        public List<string> AllowUpdateVersions { get; set; } = new List<string>();
    }
}
