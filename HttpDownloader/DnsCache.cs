using System;
using System.Collections.Generic;
using System.Net;

namespace HttpDownloader
{
    /// <summary>
    /// cache for dns, to accelerate http
    /// </summary>
    public class DnsCache
    {
        static private readonly DnsCache Instance = new DnsCache();

        // used to balance several ips of one host
        private readonly Random _random = new Random();

        // lowered host => ips
        private readonly Dictionary<string, List<string>> _caches = new Dictionary<string, List<string>>(); 

        /// <summary>
        /// Singleton
        /// </summary>
        /// <returns></returns>
        static public DnsCache GetCache()
        {
            return Instance;
        }

        /// <summary>
        /// Resolve ip of host
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        public string Resolve(string host)
        {
            List<string> ips;
            if (_caches.TryGetValue(host.ToLower(), out ips))
                return ips[_random.Next(0, ips.Count)];
            try
            {
                var entry = Dns.GetHostEntry(host);
                if (entry.AddressList.Length <= 0)
                    return null;
                ips = new List<string>();
                foreach(var address in entry.AddressList)
                {
                    ips.Add(address.ToString());
                }
                return ips[_random.Next(0, ips.Count)];
            }
            catch (Exception e)
            {
                ULogger.Error("DnsCache.Resolve Failed，Reason：" + e.Message);
                return null;
            }
        }
    }
}
