using System.IO;
using System.IO.Compression;
using System.Net;
using System;
using System.Net.Cache;

namespace HttpDownloader
{
    public class HttpUtil
    {
        static private WebProxy _proxy = null;
        static readonly private CookieContainer CookieContainer = new CookieContainer();

        /// <summary>
        /// set proxy of HttpUtil
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        static public void SetProxy(string address, int port)
        {
            _proxy = new WebProxy(address, port);
        }

        /// <summary>
        /// create HttpWebRequest, set some options, and replace host with ip, to accelerate HttpRequest
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        static public HttpWebRequest GetHttpWebRequest(string url)
        {
            var uri = new Uri(url);
            HttpWebRequest request;
            string ip = DnsCache.GetCache().Resolve(uri.Host);
            if (ip == null)
            {
                request = (HttpWebRequest)WebRequest.Create(uri);
            }
            else
            {
                int start = url.IndexOf(uri.Host);
                string newUrl = url.Substring(0, start) + ip + url.Substring(start + uri.Host.Length);
                request = (HttpWebRequest)WebRequest.Create(newUrl);
            }
            request.Referer = uri.Scheme + "://" + uri.Host;
            request.Host = uri.Host;
            return request;
        }

        /// <summary>
        /// Set common options
        /// </summary>
        /// <param name="request"></param>
        static public void SetupClient(HttpWebRequest request)
        {
            request.Timeout = Math.Min(20 * 1000, request.Timeout);  // 默认20s
            request.ReadWriteTimeout = 20 * 1000;

            request.Proxy = _proxy ?? WebRequest.DefaultWebProxy;
            request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
            request.UserAgent =
                "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/35.0.1916.153 Safari/537.36";
            // with the fowlling, content-length won't be replied
            // request.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip,deflate");

            request.ServicePoint.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = 50;

            request.CookieContainer = CookieContainer;

            request.KeepAlive = false;
            request.AllowWriteStreamBuffering = false;
            request.AllowAutoRedirect = true;
            request.AutomaticDecompression = DecompressionMethods.None;
            request.CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore);

            GC.Collect();
        }

        /// <summary>
        /// download http file within the specified range
        /// </summary>
        /// <param name="url"></param>
        /// <param name="startPos"></param>
        /// <param name="endPos"></param>
        /// <param name="func"></param>
        /// <returns></returns>
        static public bool GetFileRange(string url, long startPos, long endPos, Func<byte[], int, bool> func)
        {
            HttpWebRequest request = null;
            HttpWebResponse response = null;
            try
            {
                request = GetHttpWebRequest(url);
                SetupClient(request);
                request.AddRange("bytes", startPos, endPos);

                using (response = (HttpWebResponse) request.GetResponse())
                {
                    // 验证Response中的字节与要求的是否一致
                    string ranges = response.Headers["Content-Range"];
                    if (String.IsNullOrEmpty(ranges))
                        return false;
                    ranges = ranges.ToLower();
                    if (ranges.IndexOf("bytes") != 0)
                        return false;

                    ranges = ranges.Replace(" ", "");
                    ranges = ranges.Replace("\t", "");
                    ranges = ranges.Remove(0, 5);
                    int pos = ranges.IndexOf('-');
                    int pos2 = ranges.IndexOf('/');
                    if (pos <= 0 || pos2 < pos)
                        return false;

                    long tmpStart = Int64.Parse(ranges.Substring(0, pos));
                    long tmpEnd = Int64.Parse(ranges.Substring(pos + 1, pos2 - pos - 1));
                    if (tmpStart != startPos || tmpEnd != endPos)
                        return false;

                    var buffer = new byte[10240];
                    using (var stream = response.GetResponseStream())
                    {
                        if (stream == null)
                            return false;
                        long totalReadCount = 0;
                        int count;
                        while ((count = stream.Read(buffer, 0, 10240)) > 0)
                        {
                            if (!func(buffer, count))
                                return false;
                            totalReadCount += count;
                            if (totalReadCount >= endPos - startPos)
                                break;
                        }
                    }
                    return true;
                }
            }
            catch (Exception e)
            {
                ULogger.Error("HttpUtil.GetFileRange 失败，原因：" + e.Message + "，Url：" + url);
                ULogger.Error(e.StackTrace);
                return false;
            }
            finally
            {
                if (request != null)
                {
                    request.Abort();
                }
                if (response != null)
                {
                    response.Close();
                }
            }
        }

        static public bool GetFile(string url, string filePath)
        {
            return GetFileWithProgress(url, filePath, null);
        }

        /// <summary>
        /// download http file directly
        /// </summary>
        /// <param name="url"></param>
        /// <param name="filePath"></param>
        /// <param name="func"></param>
        /// <returns></returns>
        static public bool GetFileWithProgress(string url, string filePath, Func<long, long, bool> func)
        {
            FileStream fsData = null;
            long totalSize = 0;
            long readSize = 0;
            HttpWebResponse response = null;
            HttpWebRequest request = null;
            try
            {
                fsData = new FileStream(filePath, FileMode.Create);

                request = GetHttpWebRequest(url);
                SetupClient(request);

                var buffer = new Byte[10240];
                using (response = (HttpWebResponse)request.GetResponse())
                {
                    var readStream = response.GetResponseStream();
                    if (readStream == null)
                        throw new Exception("response.GetResponseStream 失败");

                    totalSize = response.ContentLength;
                    readSize = 0;
                    if (response.ContentEncoding.ToLower().Contains("gzip"))
                        readStream = new GZipStream(readStream, CompressionMode.Decompress);
                    else if (response.ContentEncoding.ToLower().Contains("deflate"))
                        readStream = new DeflateStream(readStream, CompressionMode.Decompress);

                    using (var reader = new BinaryReader(readStream))
                    {
                        int count;
                        while ((count = reader.Read(buffer, 0, 10240)) > 0)
                        {
                            if (func != null)
                                func(readSize, totalSize);
                            fsData.Write(buffer, 0, count);
                            readSize += count;
                        }
                    }
                }
                return true;
            }
            catch (WebException e)
            {
                ULogger.Error("HttpGetToFile 下载失败，已下载" + readSize + "/" + totalSize + "，原因：" + e.Message + "，URL：" + url + "，filePath：" + filePath);
                return false;
            }
            finally
            {
                if (fsData != null)
                {
                    fsData.Close();
                    fsData.Dispose();
                }
                if (response != null)
                {
                    response.Close();
                }
                if (request != null)
                {
                    request.Abort();
                }
            }
        }

        /// <summary>
        /// test if http file could be download resume-from-break
        /// </summary>
        /// <param name="url"></param>
        /// <param name="totalSize"></param>
        /// <returns></returns>
        static public bool TestSupportRange(string url, out long totalSize)
        {
            for (;;)
            {
                HttpWebRequest request = null;
                HttpWebResponse response = null;
                try
                {
                    request = GetHttpWebRequest(url);
                    SetupClient(request);
                    // request.Method = "HEAD";
                    using (response = (HttpWebResponse)request.GetResponse())
                    {
                        totalSize = response.ContentLength;
                        string data = response.Headers.Get("accept-ranges");
                        return !String.IsNullOrEmpty(data) && data == "bytes";
                    }
                }
                catch (Exception e)
                {
                    ULogger.Error("TestSupportRange 失败，Url：" + url + "，原因：" + e.Message);
                }
                finally
                {
                    if (request != null)
                    {
                        request.Abort();
                    }
                    if (response != null)
                    {
                        response.Close();
                    }
                }
            }
        }
    }
}
