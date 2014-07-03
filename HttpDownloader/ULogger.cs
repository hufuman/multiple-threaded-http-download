using System;

namespace HttpDownloader
{
    public class ULogger
    {
        static public void Error(string msg)
        {
            Console.WriteLine("\r\n" + msg);
        }
    }
}
