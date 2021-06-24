using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    }
    public class VersionFile
    {
        public VersionFile(string name)
        {
            fileName = Path.GetFullPath(name);
        }
        string GetPath(FileInfo info)
        {
            return info.FullName.Replace(WorkDirectory, "").Substring(1);
        }
        public void Read()
        {
            FileVersionInfos.Clear();
            XElement file = XElement.Load(fileName);
            foreach (XElement element in file.Elements())
            {
                var info = new FileVersionInfo(element);
                FileVersionInfos.Add(info.Path, info);
            }
        }
        bool IsInHiddenDirectory(FileInfo info)
        {
            string dir = info.FullName;
            while(dir != string.Empty && dir != WorkDirectory)
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
            FileVersionInfos.Clear();
            var dir = new DirectoryInfo(WorkDirectory);
            List<FileInfo> list = dir.GetFiles("*", SearchOption.AllDirectories).ToList();
            list = (from info in list
                    where info.Attributes != FileAttributes.Hidden
                    where IsInHiddenDirectory(info) == false
                    where info.FullName != fileName
                    select info).ToList();

            foreach (FileInfo info in list)
            {
                string relPath = GetPath(info);
                if (ignore.Exists(rex => rex.IsMatch(relPath)) == false)
                {
                    var versionInfo = new FileVersionInfo(relPath);
                    if (decomposer != null && decomposer.NeedDecompose(relPath))
                    {
                        versionInfo.IsDecomposed = true;
                        string[] volumns = decomposer.Decompose(relPath, "Volumns");
                        versionInfo.Volumns = (from volumn in volumns
                                               select new FileVersionInfo(volumn, isVolumn: true)
                                               ).ToList();
                    }

                    FileVersionInfos.Add(relPath, versionInfo);
                }
            }
        }

        public void Write()
        {
            var file = new XElement("version");
            foreach (var pair in FileVersionInfos)
            {
                string path = pair.Key;
                FileVersionInfo info = pair.Value;
                file.Add(info.GetXElement());
            }
            file.Save(fileName);
        }

        public DiffResult GetDiff(VersionFile versionFile)
        {
            DiffResult tmp;
            List<string> from = this.FileVersionInfos.Keys.ToList();
            List<string> to = versionFile.FileVersionInfos.Keys.ToList();

            tmp.Changeds = (from _old in this.FileVersionInfos
                            from _new in versionFile.FileVersionInfos
                            where _old.Key == _new.Key
                            where _old.Value.Checksum != _new.Value.Checksum
                            select _old.Key
                            ).ToList();
            tmp.Addeds = to.Except(from).ToList();
            tmp.Removeds = from.Except(to).ToList();

            return tmp;
        }

        public string[] GetUploadableFiles()
        {
            List<string> files = new List<string>();
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
            return files.ToArray();
        }

        string fileName;
        string WorkDirectory => Path.GetDirectoryName(fileName);
        public Dictionary<string, FileVersionInfo> FileVersionInfos { get; set; } = new Dictionary<string, FileVersionInfo>();
    }
}
