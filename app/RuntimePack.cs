using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace CampusNetwork {
    static class RuntimePack {
        public static string Prepare(string[] args) {
            string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CampusNetworkAssistant","1.1.0");
            int at=Array.IndexOf(args,"--data-dir");
            if(at>=0 && at+1<args.Length) root=Path.GetFullPath(args[at+1]);
            Directory.CreateDirectory(root);
            string marker=Path.Combine(root,".payload-version");
            // Each release has its own folder; settings and login files are never embedded.
            if(!File.Exists(marker)) {
                using(Stream input=Assembly.GetExecutingAssembly().GetManifestResourceStream("Campus.Payload.zip"))
                using(var zip=new ZipArchive(input,ZipArchiveMode.Read)) {
                    foreach(var entry in zip.Entries) {
                        string target=Path.GetFullPath(Path.Combine(root,entry.FullName));
                        if(!target.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("无效的内置文件路径。");
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        if(entry.FullName=="network-rules.json" && File.Exists(target)) continue;
                        using(var output=File.Create(target)) using(var stream=entry.Open()) stream.CopyTo(output);
                    }
                }
                File.WriteAllText(marker,"1.1.0");
            }
            return root;
        }
    }
}
