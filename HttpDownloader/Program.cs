using System;
using System.Net;

namespace HttpDownloader
{
    class Program
    {
        static void Main(string[] args)
        {
            var file = new HttpFile();
            var progressText = new[]
            {
                ".       ",
                "..      ",
                "...     ",
                "....    ",
                ".....   ",
                "......  ",
                "....... ",
                "........",
            };
            const string url = "http://www.meituan.com/api/v2/rushan/deals";
            const string filePath = "E:\\Test\\DownloadTest\\Test.txt";

            int index = 0;
            bool result = file.GetFileWithProgress(url, filePath,
                (readSize, totalSize) =>
                {
                    // print progress
                    Console.Write("\r" + readSize + " / " + totalSize + ", " + (readSize*100/totalSize) + " % " +
                                  progressText[index]);
                    ++index;
                    if(index >=progressText.Length)
                        index = 0;
                    return true;
                });
            Console.WriteLine("\r\nDownload " + (result ? "Ok" : "Failed"));
        }
    }
}
