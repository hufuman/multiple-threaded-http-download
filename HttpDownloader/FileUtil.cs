using System;
using System.IO;

namespace HttpDownloader
{
    public class FileUtil
    {
        /// <summary>
        /// Ensure that file has the specified size
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="totalSize"></param>
        /// <returns></returns>
        static public bool EnsureFileSize(string filePath, long totalSize)
        {
            FileStream fsData = null;
            try
            {
                fsData = new FileStream(filePath, FileMode.OpenOrCreate);
                fsData.SetLength(totalSize);
                return true;
            }
            catch (Exception e)
            {
                ULogger.Error("FileUtil.EnsureFileSize Failed，Reason：" + e.Message + ", Path：" + filePath);
                return false;
            }
            finally
            {
                if (fsData != null)
                    fsData.Close();
            }
        }
    }
}
