using UnityEditor;
using UnityEngine;

namespace Capstone.EditorTools
{
    /// <summary>
    /// Resources/Sounds 임포트 규칙 (2026-11-03). 짧은 소리는 메모리에 풀어 지연 없이, 긴 곡은 스트리밍.
    /// </summary>
    public class SoundImportRules : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            if (!assetPath.Contains("/Resources/Sounds/")) return;
            var importer = (AudioImporter)assetImporter;
            var s = importer.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            s.loadType = assetPath.EndsWith("_bgm.mp3") ? AudioClipLoadType.Streaming
                       : assetPath.Contains("_ambience") || assetPath.Contains("escape_succeed") || assetPath.Contains("player_dead") ? AudioClipLoadType.CompressedInMemory
                       : AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = s;
        }
    }
}
