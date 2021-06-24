using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WAUpdater
{
    static class Helpers
    {
        public static void RemoveAll<T>(this LinkedList<T> list, Func<T, bool> predicate)
        {
            var currentNode = list.First;
            while (currentNode != null)
            {
                if (predicate(currentNode.Value))
                {
                    var toRemove = currentNode;
                    currentNode = currentNode.Next;
                    list.Remove(toRemove);
                }
                else
                {
                    currentNode = currentNode.Next;
                }
            }
        }

        public static void PrepareDirectory(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (dir != string.Empty)
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
