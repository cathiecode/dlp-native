using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using YtDlp.Native;

namespace YtDlp
{
    /// <summary>
    /// Public Unity API for yt-dlp media extraction.
    ///
    /// Call <see cref="EnsureInit"/> once (e.g. in Awake) before using
    /// <see cref="ExtractAsync"/>. All Extract calls run on a thread-pool
    /// thread — never call <see cref="Extract"/> directly on the main thread.
    /// </summary>
    public static class YtDlpApi
    {
        private static int _initialized;

        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static void EnsureInit()
        {
            if (Interlocked.Exchange(ref _initialized, 1) == 0)
            {
                var rc = NativeLib.unity_dlp_init();
                if (rc != NativeLib.OK)
                    throw new InvalidOperationException(
                        $"unity_dlp_init failed with code {rc}");
            }
        }

        public static string Version()
        {
            var ptr = NativeLib.unity_dlp_version();
            return ptr == IntPtr.Zero ? "(unknown)" : Marshal.PtrToStringUTF8(ptr);
        }

        public static Task<VideoInfo> ExtractAsync(
            string url,
            ExtractOptions opts = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Extract(url, opts), cancellationToken);
        }

        public static VideoInfo Extract(string url, ExtractOptions opts = null)
        {
            EnsureInit();

            var optsJson = opts is null
                ? null
                : JsonConvert.SerializeObject(opts, SerializerSettings);

            const int InitialCapacity = 1 << 16;
            var buf = ArrayPool<byte>.Shared.Rent(InitialCapacity);
            try
            {
                var rc = NativeLib.unity_dlp_extract(url, optsJson, buf, buf.Length, out var needed);

                if (rc == NativeLib.ERR_BUF)
                {
                    ArrayPool<byte>.Shared.Return(buf);
                    buf = ArrayPool<byte>.Shared.Rent(needed);
                    rc = NativeLib.unity_dlp_extract(url, optsJson, buf, buf.Length, out needed);
                }

                ThrowIfError(rc);

                var json = Encoding.UTF8.GetString(buf, 0, needed);
                return JsonConvert.DeserializeObject<VideoInfo>(json)
                    ?? throw new InvalidOperationException("Null JSON result from native library");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        private static void ThrowIfError(int rc)
        {
            if (rc == NativeLib.OK) return;
            var errMsg = ReadLastError();
            throw rc switch
            {
                NativeLib.ERR_INIT => new InvalidOperationException($"Library not initialized: {errMsg}"),
                NativeLib.ERR_PY   => new YtDlpException($"Python error: {errMsg}"),
                NativeLib.ERR_JS   => new YtDlpException($"JavaScript error: {errMsg}"),
                NativeLib.ERR_NET  => new YtDlpException($"Network error: {errMsg}"),
                NativeLib.ERR_BUF  => new InvalidOperationException($"Buffer overflow after retry: {errMsg}"),
                _                  => new YtDlpException($"Native error ({rc}): {errMsg}"),
            };
        }

        private static string ReadLastError()
        {
            var buf = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                var rc = NativeLib.unity_dlp_last_error(buf, buf.Length, out var len);
                return rc == NativeLib.OK
                    ? Encoding.UTF8.GetString(buf, 0, len)
                    : "(error message unavailable)";
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }
    }

    public sealed class YtDlpException : Exception
    {
        public YtDlpException(string message) : base(message) { }
        public YtDlpException(string message, Exception inner) : base(message, inner) { }
    }
}
