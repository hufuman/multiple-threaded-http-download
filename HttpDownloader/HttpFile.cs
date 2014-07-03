using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace HttpDownloader
{
    /// <summary>
    /// Helper class which could download http file, and support multiple-threaded, and resume-from-break
    /// </summary>
    public class HttpFile
    {
        public const int MaxRetryCount = 10;

        /// <summary>
        /// HTTP GET file
        /// </summary>
        /// <param name="url"></param>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public bool GetFile(string url, string filePath)
        {
            return GetFileWithProgress(url, filePath, null);
        }

        /// <summary>
        /// HTTP GET file, and notify progress
        /// </summary>
        /// <param name="url"></param>
        /// <param name="filePath"></param>
        /// <param name="func">readSize, totalSize</param>
        /// <returns></returns>
        public bool GetFileWithProgress(string url, string filePath, Func<long, long, bool> func)
        {
            var fsData = new FileStream(filePath, FileMode.Create);

            long totalSize;
            bool supportRange = HttpUtil.TestSupportRange(url, out totalSize);
            if (func != null)
                func(0, totalSize);

            bool result;
            string tmpFilePath = filePath + ".tmp";
            if (!supportRange || totalSize <= 50)
            {
                result = DirectDownload(url, tmpFilePath, func);
            }
            else
            {
                result = RangeDownload(url, tmpFilePath, totalSize, func);
            }
            if (result)
            {
                fsData.Close();
                File.Delete(filePath);
                File.Move(tmpFilePath, filePath);
            }
            else
            {
                File.Delete(tmpFilePath);
            }
            return result;
        }

        /// <summary>
        /// download file directly, if HttpFile found url doesn't support resume-from-break
        /// </summary>
        /// <param name="url"></param>
        /// <param name="filePath"></param>
        /// <param name="func"></param>
        /// <returns></returns>
        private bool DirectDownload(string url, string filePath, Func<long, long, bool> func)
        {
            for (int i = 0; i < MaxRetryCount; ++i)
            {
                if (HttpUtil.GetFileWithProgress(url, filePath, func))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// download file with Multiple-threaded, resume-from-break
        /// </summary>
        /// <param name="url"></param>
        /// <param name="filePath"></param>
        /// <param name="totalSize"></param>
        /// <param name="func"></param>
        /// <returns></returns>
        private bool RangeDownload(string url, string filePath, long totalSize, Func<long, long, bool> func)
        {
            if (!FileUtil.EnsureFileSize(filePath, totalSize))
                return false;

            const int threadCount = 5;
            long length = totalSize/threadCount;
            var workers = new List<RangeDownloadWorker>();
            long readSize = 0;
            for (int i = 0; i < threadCount; ++i)
            {
                long start = i*length;
                long end = (i + 1 == threadCount) ? (totalSize - 1) : (start + length);
                var worker = new RangeDownloadWorker(url, filePath, start, end, count =>
                {
                    if (func == null)
                        return true;
                    lock (func)
                    {
                        readSize += count;
                        func(readSize, totalSize);
                    }
                    return true;
                });
                workers.Add(worker);
                if (!worker.Start())
                    break;
            }
            bool result = true;
            for (int i = 0; i < workers.Count; ++i)
            {
                if (!result)
                {
                    workers[i].Stop();
                }
                result = workers[i].Join();
            }
            return true;
        }
    }

    /// <summary>
    /// helper class
    /// </summary>
    internal class RangeDownloadWorker
    {
        private FileStream _fsData;
        private readonly long _start;
        private readonly long _end;
        private Thread _thread;
        private bool _result;
        private readonly string _url;
        private bool _stopped;
        private readonly string _filePath;
        private readonly Func<long, bool> _func;

        public RangeDownloadWorker(string url, string filePath, long start, long end, Func<long, bool> func)
        {
            _url = url;
            _start = start;
            _end = end;
            _thread = null;
            _result = false;
            _stopped = false;
            _filePath = filePath;
            _func = func;
        }

        public bool Start()
        {
            _stopped = false;
            _result = false;

            _fsData = new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            if (_fsData.Seek(_start, SeekOrigin.Begin) != _start)
            {
                _fsData.Close();
                return false;
            }

            _thread = new Thread(() =>
            {
                long pos = _start;
                for (int i = 0; !_stopped && i < HttpFile.MaxRetryCount; ++i)
                {
                    if (HttpUtil.GetFileRange(_url, pos, _end, (buffer, bufferLen) =>
                    {
                        if (_stopped)
                            return false;
                        _fsData.Write(buffer, 0, bufferLen);
                        pos += bufferLen;
                        if (_func != null)
                        {
                            _func(bufferLen);
                        }
                        return true;
                    }))
                    {
                        if (_fsData != null)
                        {
                            _fsData.Close();
                            _fsData = null;
                        }
                        _result = true;
                        break;
                    }
                }
            });
            _thread.Start();
            return true;
        }

        public void Stop()
        {
            _stopped = true;
        }

        public bool Join()
        {
            if (_thread != null)
            {
                _thread.Join();
                _thread = null;
            }
            if (_fsData != null)
            {
                _fsData.Close();
                _fsData = null;
            }
            return _result;
        }
    }
}
