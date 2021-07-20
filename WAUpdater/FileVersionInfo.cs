using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WAUpdater
{
    public class FileVersionInfo
    {
        public FileVersionInfo(string path, bool isDecomposed = false, bool isVolumn = false, List<FileVersionInfo> volumns = null)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(Path);
            Checksum = WAUpdater.Checksum.CalcFileChecksum(path);
            Size = new FileInfo(path).Length;
            IsDecomposed = isDecomposed;
            IsVolumn = isVolumn;
            Volumns = volumns;
        }

        public FileVersionInfo(string path, Decomposer decomposer)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(Path);
            Checksum = WAUpdater.Checksum.CalcFileChecksum(path);
            Size = new FileInfo(path).Length;
            IsVolumn = false;

            if (decomposer != null && decomposer.NeedDecompose(path))
            {
                IsDecomposed = true;
                string[] volumns = decomposer.Decompose(path, "Volumns");
                Volumns = (from volumn in volumns
                                       select new FileVersionInfo(volumn, isVolumn: true)
                                       ).ToList();
            }
        }

        public FileVersionInfo(XElement element)
        {
            Path = (string)element.Attribute("Path");
            Checksum = (string)element.Attribute("Hash");
            Size = (long)element.Attribute("Size");
            IsDecomposed = element.Element("Volumn") != null;
            IsVolumn = element.Name == "Volumn";
            if (IsDecomposed)
            {
                Volumns = (from elem in element.Elements("Volumn") select new FileVersionInfo(elem)).ToList();
            }
        }

        public XElement GetXElement()
        {
            List<object> content = new List<object>();
            content.Add(new XAttribute("Path", Path));
            content.Add(new XAttribute("Hash", Checksum));
            content.Add(new XAttribute("Size", Size));
            if (IsDecomposed)
            {
                foreach (var item in Volumns)
                {
                    content.Add(item.GetXElement());
                }
            }
            return new XElement(IsVolumn ? "Volumn" : "File", content.ToArray());
        }

        public string[] GetVolumns()
        {
            return (from volumn in Volumns select volumn.Path).ToArray();
        }

        public string Name { get; internal set; }
        public string Path { get; internal set; }
        public string Checksum { get; internal set; }
        public long Size { get; internal set; }
        public bool IsDecomposed { get; internal set; }
        public bool IsVolumn { get; internal set; }
        public List<FileVersionInfo> Volumns { get; set; }
    }
}
