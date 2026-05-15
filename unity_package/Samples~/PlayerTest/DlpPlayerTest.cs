using System;
using System.Threading.Tasks;
using UnityEngine;
using YtDlp;

/// <summary>
/// Drop on any GameObject, build, and run to verify the full init + extract
/// pipeline on device. Uses OnGUI so no canvas setup is required.
/// </summary>
public sealed class DlpPlayerTest : MonoBehaviour
{
    [Tooltip("URL to extract on startup or when the Extract button is tapped")]
    public string TestUrl = "https://vimeo.com/76979871";

    private string _status = "Initializing…";
    private string _result = "";
    private bool _busy = true;

    private async void Start()
    {
        try
        {
            await DlpBootstrap.EnsureInitAsync();
            _status = $"Ready  •  {YtDlpApi.Version()}";
        }
        catch (Exception e)
        {
            _status = $"Init FAILED:\n{e.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnGUI()
    {
        // Scale UI so it's legible on both desktop and device screens.
        float scale = Screen.height / 900f;
        GUI.matrix  = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float w = Screen.width  / scale;
        float h = Screen.height / scale;
        float pad = 24f;

        var label  = new GUIStyle(GUI.skin.label)    { fontSize = 26, wordWrap = true, richText = true };
        var box    = new GUIStyle(GUI.skin.box)       { fontSize = 22, wordWrap = true, alignment = TextAnchor.UpperLeft };
        var field  = new GUIStyle(GUI.skin.textField) { fontSize = 22 };
        var button = new GUIStyle(GUI.skin.button)    { fontSize = 26, fixedHeight = 60 };

        GUILayout.BeginArea(new Rect(pad, pad, w - pad * 2, h - pad * 2));

        GUILayout.Label("<b>dlp-native  –  player test</b>", label);
        GUILayout.Space(6);

        GUILayout.Label(_status, label);
        GUILayout.Space(6);

        TestUrl = GUILayout.TextField(TestUrl, field);
        GUILayout.Space(6);

        GUI.enabled = !_busy;
        if (GUILayout.Button("Extract", button))
            _ = RunExtractAsync(TestUrl);
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(_result))
        {
            GUILayout.Space(10);
            GUILayout.Label(_result, box, GUILayout.ExpandHeight(false));
        }

        GUILayout.EndArea();
    }

    private async Task RunExtractAsync(string url)
    {
        _busy   = true;
        _result = "";
        _status = $"Extracting…\n{url}";
        try
        {
            var info    = await YtDlpApi.ExtractAsync(url);
            var fmtCount = info.Formats?.Count ?? 0;
            var bestUrl  = fmtCount > 0
                ? info.Formats[fmtCount - 1].Url
                : info.DirectUrl ?? "(none)";

            _status = "OK";
            _result =
                $"title    {info.Title}\n" +
                $"id       {info.Id}\n" +
                $"duration {info.Duration:F0}s\n" +
                $"formats  {fmtCount}\n" +
                $"best url {bestUrl}";
        }
        catch (Exception e)
        {
            _status = "FAILED";
            _result = e.Message;
        }
        finally
        {
            _busy = false;
        }
    }
}
