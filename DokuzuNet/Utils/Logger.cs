using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokuzuNet.Utils
{
    internal class Logger
    {
        public static void Info(string msg)
        {
            Console.WriteLine($"[INFO] {msg}");
        }

        public static void Error(string msg)
        {
            Console.WriteLine($"[ERROR] {msg}");
        }
    }
}
