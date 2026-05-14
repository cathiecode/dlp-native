#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace YtDlp.Editor
{
    [InitializeOnLoad]
    internal static class YtDlpSmokeTest
    {
        static YtDlpSmokeTest()
        {
            try
            {
                var version = YtDlpApi.Version();
                Debug.Log($"[YtDlp] native library loaded — version: {version}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[YtDlp] failed to load native library: {e.Message}");
            }
        }

        [MenuItem("Tools/YtDlp/Run Smoke Test")]
        public static void Run()
        {
            try
            {
                var version = YtDlpApi.Version();
                Debug.Log($"[YtDlp] version: {version}");

                YtDlpApi.EnsureInit();
                Debug.Log("[YtDlp] EnsureInit() succeeded");
            }
            catch (Exception e)
            {
                Debug.LogError($"[YtDlp] {e}");
            }
        }
    }
}
#endif
