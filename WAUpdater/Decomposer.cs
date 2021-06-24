using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WAUpdater
{
    public class Decomposer
    {
        public Decomposer(long maxSize)
        {
            MaxSize = maxSize;
        }
        public long MaxSize { get; set; }

        public string[] GetDecomposedFiles(string fileName)
        {
            string[] files = Directory.GetFiles(Path.GetDirectoryName(fileName), $"{Path.GetFileName(fileName)}.???");
            return files; 
        }
        public bool NeedDecompose(string fileName)
        {
            FileInfo info = new FileInfo(fileName);
            return info.Length > MaxSize;
        }
        public bool IsDecomposed(string fileName)
        {
            string[] files = GetDecomposedFiles(fileName);
            return files.Length > 0;
        }
        public string[] Decompose(string fileName, string outDir = "")
        {
            List<string> decomposedFiles = new List<string>();
            using (FileStream src = new FileStream(fileName, FileMode.Open, FileAccess.Read))
            {
                byte[] buffer = new byte[MaxSize];
                int idx = 1;
                while (src.CanRead)
                {
                    string distName = Path.Combine(outDir, $"{fileName}.{idx:000}");
                    Helpers.PrepareDirectory(distName);

                    int readCount = src.Read(buffer, 0, buffer.Length);
                    if (readCount == 0)
                    {
                        break;
                    }
                    using (FileStream dist = new FileStream(distName, FileMode.Create, FileAccess.Write))
                    {
                        dist.Write(buffer, 0, readCount);
                    }
                    decomposedFiles.Add(distName);
                    idx++;
                }
            }
            return decomposedFiles.ToArray();
        }

        public void Compose(string fileName, string distName)
        {
            Helpers.PrepareDirectory(distName);
            using (FileStream dist = new FileStream(distName, FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = null;

                string[] files = GetDecomposedFiles(fileName);
                foreach (string srcName in files)
                {
                    using (FileStream src = new FileStream(srcName, FileMode.Open, FileAccess.Read))
                    {
                        if(buffer is null)
                        {
                            buffer = new byte[src.Length];
                        }

                        int readCount = src.Read(buffer, 0, buffer.Length);
                        dist.Write(buffer, 0, readCount);
                    }
                }
            }
        }
    }
}
