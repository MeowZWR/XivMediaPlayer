using System;
using System.IO;

namespace XivMediaPlayer.LocalUi
{
    internal static class LocalMediaPaths
    {
        public static bool IsLocalFile(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            url = url.Trim().Trim('"');
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
            if (url.StartsWith(@"\\") || url.StartsWith("//")) return true;

            if (url.Length >= 3
                && char.IsLetter(url[0])
                && url[1] == ':'
                && (url[2] == '\\' || url[2] == '/'))
            {
                return true;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile) return true;
            return File.Exists(url);
        }

        public static bool IsMissing(string? url)
        {
            if (!IsLocalFile(url)) return false;
            try
            {
                return !File.Exists(Normalize(url!));
            }
            catch
            {
                return true;
            }
        }

        public static string Normalize(string path)
        {
            path = path.Trim().Trim('"');
            if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(path, UriKind.Absolute, out var uri)
                && uri.IsFile)
            {
                return uri.LocalPath;
            }

            return path;
        }
    }
}
