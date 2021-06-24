using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace WAUpdater
{
    class Checksum
    {
        public static string CalcFileChecksum(string fileName)
        {
            FileStream file = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            MD5 md5 = new MD5CryptoServiceProvider();
            byte[] hash = md5.ComputeHash(file);
            file.Close();
            string checksum = string.Join(";", from val in hash select val.ToString("x2"));
            return checksum;
        }
    }
}
