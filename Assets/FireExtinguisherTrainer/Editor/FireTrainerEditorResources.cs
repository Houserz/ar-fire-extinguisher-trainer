#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FireExtinguisherTrainerEditor
{
    internal static class FireTrainerEditorResources
    {
        private const string TmpSettingsResourceName = "TMP Settings";
        private const string TmpEssentialPackageRelativePath = "Package Resources/TMP Essential Resources.unitypackage";

        public static TMP_FontAsset EnsureTextMeshProEssentialResources()
        {
            TMP_FontAsset fontAsset = LoadDefaultTmpFontAsset();
            if (fontAsset != null)
            {
                return fontAsset;
            }

            string packagePath = ResolveTextMeshProEssentialsPackagePath();
            if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
            {
                throw new InvalidOperationException(
                    "TextMesh Pro essentials are missing and the TMP Essential Resources unitypackage could not be found.");
            }

            AssetDatabase.ImportPackage(packagePath, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();

            fontAsset = LoadDefaultTmpFontAsset();
            if (fontAsset == null)
            {
                ExtractUnityPackageContents(packagePath);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                AssetDatabase.SaveAssets();
                fontAsset = LoadDefaultTmpFontAsset();
            }

            if (fontAsset == null)
            {
                throw new InvalidOperationException(
                    "TextMesh Pro essentials were imported, but TMP Settings still has no default font asset.");
            }

            return fontAsset;
        }

        public static void ApplyDefaultFont(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            TMP_FontAsset fontAsset = EnsureTextMeshProEssentialResources();
            if (fontAsset != null)
            {
                text.font = fontAsset;
                EditorUtility.SetDirty(text);
            }
        }

        public static void RequireTextMeshProEssentialResources()
        {
            EnsureTextMeshProEssentialResources();
        }

        private static TMP_FontAsset LoadDefaultTmpFontAsset()
        {
            TMP_Settings settings = Resources.Load<TMP_Settings>(TmpSettingsResourceName);
            if (settings == null)
            {
                return null;
            }

            SerializedProperty fontProperty = new SerializedObject(settings).FindProperty("m_defaultFontAsset");
            return fontProperty != null ? fontProperty.objectReferenceValue as TMP_FontAsset : null;
        }

        private static string ResolveTextMeshProEssentialsPackagePath()
        {
            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                string resolvedPath = Path.Combine(packageInfo.resolvedPath, TmpEssentialPackageRelativePath);
                if (File.Exists(resolvedPath))
                {
                    return resolvedPath;
                }
            }

            string projectRelative = "Packages/com.unity.ugui/" + TmpEssentialPackageRelativePath;
            string absoluteProjectRelative = Path.GetFullPath(projectRelative);
            if (File.Exists(absoluteProjectRelative))
            {
                return absoluteProjectRelative;
            }

            string packageCache = Path.Combine(Application.dataPath, "../Library/PackageCache");
            if (!Directory.Exists(packageCache))
            {
                return null;
            }

            string[] packageCandidates = Directory.GetFiles(
                packageCache,
                "TMP Essential Resources.unitypackage",
                SearchOption.AllDirectories);
            return packageCandidates.Length > 0 ? packageCandidates[0] : null;
        }

        private sealed class UnityPackageAsset
        {
            public string PathName;
            public byte[] Asset;
            public byte[] Meta;
        }

        private static void ExtractUnityPackageContents(string packagePath)
        {
            Dictionary<string, UnityPackageAsset> assets = ReadUnityPackage(packagePath);
            foreach (UnityPackageAsset asset in assets.Values)
            {
                if (string.IsNullOrWhiteSpace(asset.PathName) ||
                    !asset.PathName.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    continue;
                }

                string assetPath = asset.PathName.Replace('\\', '/');
                if (asset.Asset != null)
                {
                    EnsureParentDirectory(assetPath);
                    File.WriteAllBytes(assetPath, asset.Asset);
                }
                else
                {
                    Directory.CreateDirectory(assetPath);
                }

                if (asset.Meta != null)
                {
                    EnsureParentDirectory(assetPath + ".meta");
                    File.WriteAllBytes(assetPath + ".meta", asset.Meta);
                }
            }
        }

        private static Dictionary<string, UnityPackageAsset> ReadUnityPackage(string packagePath)
        {
            var assets = new Dictionary<string, UnityPackageAsset>();
            using FileStream fileStream = File.OpenRead(packagePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);

            var header = new byte[512];
            while (ReadBlock(gzipStream, header))
            {
                if (IsZeroBlock(header))
                {
                    break;
                }

                string entryName = ReadTarString(header, 0, 100);
                string prefix = ReadTarString(header, 345, 155);
                if (!string.IsNullOrEmpty(prefix))
                {
                    entryName = prefix + "/" + entryName;
                }

                long size = ReadTarSize(header);
                byte typeFlag = header[156];
                byte[] data = ReadBytes(gzipStream, size);
                SkipPadding(gzipStream, size);

                if (typeFlag == (byte)'5' || string.IsNullOrEmpty(entryName))
                {
                    continue;
                }

                string[] parts = entryName.TrimStart('.', '/').Split('/');
                if (parts.Length != 2)
                {
                    continue;
                }

                if (!assets.TryGetValue(parts[0], out UnityPackageAsset asset))
                {
                    asset = new UnityPackageAsset();
                    assets.Add(parts[0], asset);
                }

                switch (parts[1])
                {
                    case "pathname":
                        asset.PathName = Encoding.UTF8.GetString(data).TrimEnd('\0', '\r', '\n');
                        break;
                    case "asset":
                        asset.Asset = data;
                        break;
                    case "asset.meta":
                        asset.Meta = data;
                        break;
                }
            }

            return assets;
        }

        private static bool ReadBlock(Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                {
                    return offset != 0;
                }

                offset += read;
            }

            return true;
        }

        private static byte[] ReadBytes(Stream stream, long size)
        {
            if (size < 0 || size > int.MaxValue)
            {
                throw new InvalidOperationException($"Unsupported unitypackage entry size: {size}.");
            }

            var data = new byte[(int)size];
            int offset = 0;
            while (offset < data.Length)
            {
                int read = stream.Read(data, offset, data.Length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("Unexpected end of unitypackage stream.");
                }

                offset += read;
            }

            return data;
        }

        private static void SkipPadding(Stream stream, long size)
        {
            long padding = (512 - (size % 512)) % 512;
            if (padding == 0)
            {
                return;
            }

            _ = ReadBytes(stream, padding);
        }

        private static bool IsZeroBlock(byte[] block)
        {
            foreach (byte value in block)
            {
                if (value != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static long ReadTarSize(byte[] header)
        {
            string sizeText = ReadTarString(header, 124, 12).Trim();
            return string.IsNullOrEmpty(sizeText) ? 0 : Convert.ToInt64(sizeText, 8);
        }

        private static string ReadTarString(byte[] buffer, int offset, int count)
        {
            int length = 0;
            while (length < count && buffer[offset + length] != 0)
            {
                length++;
            }

            return Encoding.UTF8.GetString(buffer, offset, length);
        }

        private static void EnsureParentDirectory(string path)
        {
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }
        }
    }
}
#endif
