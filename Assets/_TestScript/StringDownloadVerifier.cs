using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.Data;
using TMPro;

namespace QueshuSDK3
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StringDownloadVerifier : UdonSharpBehaviour
    {
        [Header("Test URLs (Different sizes)")]
        [SerializeField]
        VRCUrl[] _testUrls = new VRCUrl[]
        {
            new VRCUrl("https://queshu-official.github.io/poster_mobile.json"),
            // 他のURLをInspectorから追加可能
        };

        [SerializeField] int _retryCountPerUrl = 3;
        [SerializeField] float _retryDelay = 3f;
        [SerializeField] float _urlSwitchDelay = 5f;
        [SerializeField] TMP_Text _debugText;

        int _currentUrlIndex = 0;
        int _currentAttempt = 0;
        int[][] _downloadedLengths;
        string[][] _firstChars;
        string[][] _lastChars;
        bool[][] _isJsonValid;

        void Start()
        {
            if (_testUrls == null || _testUrls.Length == 0)
            {
                _DebugPrintOnWorldError("No test URLs configured!");
                return;
            }

            // 各URLごとの結果を記録する配列を初期化
            _downloadedLengths = new int[_testUrls.Length][];
            _firstChars = new string[_testUrls.Length][];
            _lastChars = new string[_testUrls.Length][];
            _isJsonValid = new bool[_testUrls.Length][];

            for (int i = 0; i < _testUrls.Length; i++)
            {
                _downloadedLengths[i] = new int[_retryCountPerUrl];
                _firstChars[i] = new string[_retryCountPerUrl];
                _lastChars[i] = new string[_retryCountPerUrl];
                _isJsonValid[i] = new bool[_retryCountPerUrl];
            }

            SendCustomEventDelayedSeconds(nameof(_StartVerification), 5f);
        }

        public void _StartVerification()
        {
#if UNITY_ANDROID
            string platform = "Android";
#elif UNITY_IOS
            string platform = "iOS";
#else
            string platform = "PC";
#endif

            _DebugPrintOnWorld($"=== String Download Verification Start ===");
            _DebugPrintOnWorld($"Platform: {platform}");
            _DebugPrintOnWorld($"Testing {_testUrls.Length} URLs with {_retryCountPerUrl} attempts each");

            _currentUrlIndex = 0;
            _StartUrlTest();
        }

        public void _StartUrlTest()
        {
            if (_currentUrlIndex >= _testUrls.Length)
            {
                _PrintAllResults();
                return;
            }

            _DebugPrintOnWorld($"\n=== Testing URL {_currentUrlIndex + 1}/{_testUrls.Length} ===");
            _DebugPrintOnWorld($"URL: {_testUrls[_currentUrlIndex]}");

            _currentAttempt = 0;
            _DownloadNext();
        }

        public void _DownloadNext()
        {
            if (_currentAttempt >= _retryCountPerUrl)
            {
                _PrintUrlResults(_currentUrlIndex);
                _currentUrlIndex++;
                SendCustomEventDelayedSeconds(nameof(_StartUrlTest), _urlSwitchDelay);
                return;
            }

            _DebugPrintOnWorld($"--- URL {_currentUrlIndex + 1}, Attempt {_currentAttempt + 1}/{_retryCountPerUrl} ---");
            VRCStringDownloader.LoadUrl(_testUrls[_currentUrlIndex], (IUdonEventReceiver)this);
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            var data = result.Result;
            var length = data.Length;

            _downloadedLengths[_currentUrlIndex][_currentAttempt] = length;
            _firstChars[_currentUrlIndex][_currentAttempt] = length > 0 ? data.Substring(0, Mathf.Min(50, length)) : "";
            _lastChars[_currentUrlIndex][_currentAttempt] = length > 30 ? data.Substring(length - 30, 30) : data;
            _isJsonValid[_currentUrlIndex][_currentAttempt] = _CheckJsonValidity(data);

            _DebugPrintOnWorld($"Attempt {_currentAttempt + 1} - Length: {length}");
            _DebugPrintOnWorld($"First 50: {_firstChars[_currentUrlIndex][_currentAttempt]}");
            _DebugPrintOnWorld($"Last 30: {_lastChars[_currentUrlIndex][_currentAttempt]}");
            _DebugPrintOnWorld($"Valid JSON: {_isJsonValid[_currentUrlIndex][_currentAttempt]}");
            _DebugPrintOnWorld($"Ends with '}}': {data.EndsWith("}")}");
            _DebugPrintOnWorld($"Ends with ']': {data.EndsWith("]")}");

            if (length > 100)
            {
                var lastHundred = data.Substring(length - 100, 100);
                _DebugPrintOnWorld($"Last 100 chars: {lastHundred}");
            }

            _currentAttempt++;
            SendCustomEventDelayedSeconds(nameof(_DownloadNext), _retryDelay);
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            _DebugPrintOnWorldError($"Attempt {_currentAttempt + 1} - Download Failed");
            _DebugPrintOnWorldError($"Error: {result.Error}");
            _DebugPrintOnWorldError($"ErrorCode: {result.ErrorCode}");

            _downloadedLengths[_currentUrlIndex][_currentAttempt] = -1;
            _firstChars[_currentUrlIndex][_currentAttempt] = "ERROR";
            _lastChars[_currentUrlIndex][_currentAttempt] = "ERROR";
            _isJsonValid[_currentUrlIndex][_currentAttempt] = false;

            _currentAttempt++;
            SendCustomEventDelayedSeconds(nameof(_DownloadNext), _retryDelay);
        }

        bool _CheckJsonValidity(string jsonString)
        {
            if (string.IsNullOrEmpty(jsonString)) return false;

            jsonString = jsonString.Trim();

            bool startsValid = jsonString.StartsWith("{") || jsonString.StartsWith("[");
            bool endsValid = jsonString.EndsWith("}") || jsonString.EndsWith("]");

            if (!startsValid || !endsValid) return false;

            if (VRCJson.TryDeserializeFromJson(jsonString, out DataToken result))
            {
                return true;
            }

            return false;
        }

        void _PrintUrlResults(int urlIndex)
        {
            _DebugPrintOnWorld($"\n=== Results for URL {urlIndex + 1} ===");
            _DebugPrintOnWorld($"URL: {_testUrls[urlIndex]}");

            int successCount = 0;
            int minLength = int.MaxValue;
            int maxLength = 0;

            for (int i = 0; i < _retryCountPerUrl; i++)
            {
                if (_downloadedLengths[urlIndex][i] > 0)
                {
                    successCount++;
                    if (_downloadedLengths[urlIndex][i] < minLength) minLength = _downloadedLengths[urlIndex][i];
                    if (_downloadedLengths[urlIndex][i] > maxLength) maxLength = _downloadedLengths[urlIndex][i];
                }
            }

            bool hasLengthVariation = (maxLength - minLength) > 0;

            _DebugPrintOnWorld($"Success Rate: {successCount}/{_retryCountPerUrl}");
            if (successCount > 0)
            {
                _DebugPrintOnWorld($"Length Range: {minLength} - {maxLength} chars");
                _DebugPrintOnWorld($"Length Variation: {hasLengthVariation} (diff: {maxLength - minLength})");
            }

            for (int i = 0; i < _retryCountPerUrl; i++)
            {
                _DebugPrintOnWorld($"[{i + 1}] Length: {_downloadedLengths[urlIndex][i]}, Valid: {_isJsonValid[urlIndex][i]}, Last30: {_lastChars[urlIndex][i]}");
            }

            if (hasLengthVariation)
            {
                _DebugPrintOnWorldWarning("!!! Length variation detected - Possible truncation issue !!!");
            }
        }

        void _PrintAllResults()
        {
            _DebugPrintOnWorld("\n\n=== ALL URLs VERIFICATION SUMMARY ===");

            for (int urlIndex = 0; urlIndex < _testUrls.Length; urlIndex++)
            {
                int successCount = 0;
                int validCount = 0;
                int minLength = int.MaxValue;
                int maxLength = 0;

                for (int i = 0; i < _retryCountPerUrl; i++)
                {
                    if (_downloadedLengths[urlIndex][i] > 0)
                    {
                        successCount++;
                        if (_downloadedLengths[urlIndex][i] < minLength) minLength = _downloadedLengths[urlIndex][i];
                        if (_downloadedLengths[urlIndex][i] > maxLength) maxLength = _downloadedLengths[urlIndex][i];
                    }
                    if (_isJsonValid[urlIndex][i]) validCount++;
                }

                _DebugPrintOnWorld($"\n[URL {urlIndex + 1}] {_testUrls[urlIndex]}");
                _DebugPrintOnWorld($"  Success: {successCount}/{_retryCountPerUrl}, Valid JSON: {validCount}/{_retryCountPerUrl}");
                if (successCount > 0)
                {
                    _DebugPrintOnWorld($"  Length: {minLength} - {maxLength} (variation: {maxLength - minLength})");
                }
            }

            _DebugPrintOnWorld("\n=== End of All Verifications ===");
        }


        void _DebugPrintOnWorld(string message)
        {
            if (_debugText != null)
            {
                _debugText.text += message + "\n";
            }
            Debug.Log(message);
        }

        void _DebugPrintOnWorldError(string message)
        {
            if (_debugText != null)
            {
                _debugText.text += $"<color=red>{message}</color>\n";
            }
            Debug.LogError(message);
        }

        void _DebugPrintOnWorldWarning(string message)
        {
            if (_debugText != null)
            {
                _debugText.text += $"<color=yellow>{message}</color>\n";
            }
            Debug.LogWarning(message);
        }
    }
}