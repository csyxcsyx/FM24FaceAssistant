using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

internal static class SkinPackageChecks
{
    static void CreateBundle(string archive, string? hashOverride = null)
    {
        var entries = new Dictionary<string, string>
        {
            [FM24FaceAssistant.SkinPackage.Name + "/skin_config.xml"] = "<record><string id=\"name\" value=\"FLAYUS skin 24 YUXIANde\" /></record>",
            [FM24FaceAssistant.SkinPackage.Name + "/panels/human/inbox item.xml"] = "<panel />",
            [FM24FaceAssistant.SkinPackage.Name + "/settings/config.xml"] = "<record />"
        };
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            foreach (var item in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(item.Key).Open());
                writer.Write(item.Value);
            }
        }
        var manifest = new FM24FaceAssistant.SkinBundleManifest(
            hashOverride ?? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))),
            entries.Count,
            entries.Values.Sum(value => System.Text.Encoding.UTF8.GetByteCount(value)));
        FM24FaceAssistant.Files.Save(Path.ChangeExtension(archive, ".json"), manifest);
    }

    public static void Run(Action<bool, string> check, string root)
    {
        var package = Path.Combine(root, "skin-package-checks");
        Directory.CreateDirectory(package);
        var archive = Path.Combine(package, "skin.zip");
        CreateBundle(archive);
        var target = Path.Combine(package, "installed", FM24FaceAssistant.SkinPackage.Name);
        var installed = FM24FaceAssistant.SkinPackage.Install(archive, target, CancellationToken.None);
        check(installed == target && FM24FaceAssistant.SkinPackage.IsInstalled(target)
            && File.Exists(Path.Combine(target, "panels", "human", "inbox item.xml")), "皮肤包校验后完整安装并可识别");

        var occupied = Path.Combine(package, "occupied");
        Directory.CreateDirectory(occupied);
        var keep = Path.Combine(occupied, "keep.txt");
        File.WriteAllText(keep, "original");
        bool overwriteBlocked = false;
        try { FM24FaceAssistant.SkinPackage.Install(archive, occupied, CancellationToken.None); }
        catch (IOException) { overwriteBlocked = true; }
        check(overwriteBlocked && File.ReadAllText(keep) == "original", "皮肤安装拒绝覆盖非空目录");

        var corruptArchive = Path.Combine(package, "corrupt.zip");
        CreateBundle(corruptArchive, new string('0', 64));
        var corruptTarget = Path.Combine(package, "corrupt-install");
        bool corruptBlocked = false;
        try { FM24FaceAssistant.SkinPackage.Install(corruptArchive, corruptTarget, CancellationToken.None); }
        catch (IOException) { corruptBlocked = true; }
        check(corruptBlocked && !Directory.Exists(corruptTarget), "皮肤包哈希不符时不写入安装目录");

        var documents = Path.Combine(package, "documents");
        var discovered = FM24FaceAssistant.SkinPackage.FindInstalled(documents, null);
        check(discovered == null, "未安装皮肤时检测结果为空");
        var defaultFolder = FM24FaceAssistant.SkinPackage.DefaultPath(documents);
        Directory.CreateDirectory(Path.GetDirectoryName(defaultFolder)!);
        Directory.Move(target, defaultFolder);
        check(FM24FaceAssistant.SkinPackage.FindInstalled(documents, null) == defaultFolder, "启动检测可发现默认目录皮肤");
    }
}
