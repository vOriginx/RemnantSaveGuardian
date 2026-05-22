using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace RemnantSaveGuardian
{
    public class RemnantSave
    {
        private List<RemnantCharacter>? _Characters;
        public List<RemnantCharacter> Characters {
            get => _Characters ??= RemnantCharacter.GetCharactersFromSave(this, RemnantCharacter.CharacterProcessingMode.NoEvents);
        }

        public static readonly string DefaultWgsSaveFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Packages\PerfectWorldEntertainment.GFREMP2_jrajkyc4tsa6w\SystemAppData\wgs";
        private string savePath;
        private string profileFile;
        private RemnantSaveType saveType;
        private WindowsSave winSave;

        public static readonly Guid FOLDERID_SavedGames = new(0x4C5C32FF, 0xBB9D, 0x43B0, 0xB5, 0xB4, 0x2D, 0x72, 0xE5, 0x4E, 0xAA, 0xA4);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
        static extern string SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken = default);

        public RemnantSave(string path)
        {
            if (!Directory.Exists(path))
            {
                throw new Exception(path + " does not exist.");
            }

            if (File.Exists(path + @"\profile.sav"))
            {
                this.saveType = RemnantSaveType.Normal;
                this.profileFile = "profile.sav";
            }
            else
            {
                var winFiles = Directory.GetFiles(path, "container.*");
                if (winFiles.Length > 0)
                {
                    this.winSave = new WindowsSave(winFiles[0]);
                    this.saveType = RemnantSaveType.WindowsStore;
                    profileFile = winSave.Profile;
                }
                else
                {
                    throw new Exception(path + " is not a valid save.");
                }
            }
            this.savePath = path;
        }

        public string SaveFolderPath
        {
            get
            {
                return this.savePath;
            }
        }

        public string SaveProfilePath
        {
            get
            {
                return this.savePath + $@"\{this.profileFile}";
            }
        }
        public RemnantSaveType SaveType
        {
            get { return this.saveType; }
        }
        public string[] WorldSaves
        {
            get
            {
                if (this.saveType == RemnantSaveType.Normal)
                {
                    return Directory.GetFiles(SaveFolderPath, "save_*.sav");
                }
                else
                {
                    System.Console.WriteLine(this.winSave.Worlds.ToArray());
                    return this.winSave.Worlds.ToArray();
                }
            }
        }

        public bool Valid
        {
            get
            {
                return this.saveType == RemnantSaveType.Normal || this.winSave.Valid;
            }
        }

        public static bool ValidSaveFolder(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return false;
            }

            if (File.Exists(folder + "\\profile.sav"))
            {
                return true;
            }
            else
            {
                var winFiles = Directory.GetFiles(folder, "container.*");
                if (winFiles.Length > 0)
                {
                    return true;
                }
            }
            return false;
        }

        public void UpdateCharacters()
        {
            Characters.Clear();
            Characters.AddRange(RemnantCharacter.GetCharactersFromSave(this));
        }

        public string GetProfileData()
        {
            return DecompressSaveAsString(this.SaveProfilePath);
        }

        public static string DefaultSaveFolder()
        {
            var saveFolder = SHGetKnownFolderPath(FOLDERID_SavedGames, 0) + @"\Remnant2";
            if (Directory.Exists($@"{saveFolder}\Steam"))
            {
                saveFolder += @"\Steam";
                var userFolders = Directory.GetDirectories(saveFolder);
                if (userFolders.Length > 0)
                {
                    return userFolders[0];
                }
            }
            else
            {
                var folders = Directory.GetDirectories(saveFolder);
                if (folders.Length > 0)
                {
                    return folders[0];
                }
            }
            return saveFolder;
        }

        public static string? FindWgsSaveFolder(string? rootPath = null)
        {
            var searchRoot = rootPath ?? DefaultWgsSaveFolder;
            if (!Directory.Exists(searchRoot))
                return null;
            foreach (var dir in Directory.GetDirectories(searchRoot))
            {
                if (File.Exists(Path.Combine(dir, "containers.index")))
                    return dir;
            }
            return null;
        }

        /// <summary>
        /// Parses the WGS containers.index binary file and returns a mapping of
        /// container name (e.g. "profile", "save_0") to the GUID-named subfolder on disk.
        /// </summary>
        public static Dictionary<string, string> ParseContainersIndex(string indexPath)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(indexPath))
                return result;

            try
            {
                var data = File.ReadAllBytes(indexPath);
                int i = 0;
                while (i < data.Length - 4)
                {
                    int nameLen = BitConverter.ToInt32(data, i);
                    if (nameLen < 1 || nameLen > 64 || i + 4 + nameLen * 2 > data.Length)
                    {
                        i++;
                        continue;
                    }

                    string name1 = Encoding.Unicode.GetString(data, i + 4, nameLen * 2).TrimEnd('\0');

                    // Container names are short ASCII identifiers
                    if (name1.Length == 0 || name1.Any(c => c < 32 || c > 126))
                    {
                        i++;
                        continue;
                    }

                    int afterName1 = i + 4 + nameLen * 2;
                    if (afterName1 + 4 > data.Length) { i++; continue; }

                    // Container name appears twice consecutively
                    int nameLen2 = BitConverter.ToInt32(data, afterName1);
                    if (nameLen2 != nameLen) { i++; continue; }

                    int name2Start = afterName1 + 4;
                    if (name2Start + nameLen * 2 > data.Length) { i++; continue; }

                    string name2 = Encoding.Unicode.GetString(data, name2Start, nameLen * 2).TrimEnd('\0');
                    if (name2 != name1) { i++; continue; }

                    int afterName2 = name2Start + nameLen * 2;
                    if (afterName2 + 4 > data.Length) { i++; continue; }

                    // Type string follows
                    int typeLen = BitConverter.ToInt32(data, afterName2);
                    if (typeLen < 0 || typeLen > 128 || afterName2 + 4 + typeLen * 2 > data.Length) { i++; continue; }

                    int afterType = afterName2 + 4 + typeLen * 2;

                    // 5 flag bytes then 16-byte binary GUID
                    if (afterType + 5 + 16 > data.Length) { i++; continue; }

                    var guidBytes = new byte[16];
                    Array.Copy(data, afterType + 5, guidBytes, 0, 16);
                    var guid = new Guid(guidBytes).ToString("N").ToUpperInvariant();

                    result[name1] = guid;
                    i = afterType + 5 + 16;
                }
            }
            catch
            {
                // Return whatever was successfully parsed
            }

            return result;
        }

        public static void SyncFromWgs(string wgsUserFolder, string destFolder)
        {
            if (!Directory.Exists(destFolder))
                Directory.CreateDirectory(destFolder);

            foreach (var file in Directory.GetFiles(destFolder))
                File.Delete(file);

            // Primary path: use containers.index to resolve exact GUID subfolder per save slot
            var nameToGuid = ParseContainersIndex(Path.Combine(wgsUserFolder, "containers.index"));
            if (nameToGuid.Count > 0)
            {
                foreach (var kvp in nameToGuid)
                {
                    var guidFolderPath = Path.Combine(wgsUserFolder, kvp.Value);
                    if (!Directory.Exists(guidFolderPath))
                        continue;

                    var source = Directory.GetFiles(guidFolderPath)
                        .FirstOrDefault(f => !Path.GetFileName(f).StartsWith("container", StringComparison.OrdinalIgnoreCase));
                    if (source == null)
                        continue;

                    var destName = kvp.Key.Equals("profile", StringComparison.OrdinalIgnoreCase)
                        ? "profile.sav"
                        : $"{kvp.Key}.sav";
                    File.Copy(source, Path.Combine(destFolder, destName), overwrite: true);
                }
                return;
            }

            // Fallback: sort by subfolder creation time then file size
            var files = new List<(string path, DateTime folderCreated, long size)>();
            foreach (var subdir in Directory.GetDirectories(wgsUserFolder))
            {
                var subdirInfo = new DirectoryInfo(subdir);
                foreach (var file in subdirInfo.GetFiles())
                {
                    if (!file.Name.StartsWith("container", StringComparison.OrdinalIgnoreCase))
                        files.Add((file.FullName, subdirInfo.CreationTime, file.Length));
                }
            }

            var sorted = files.OrderBy(f => f.folderCreated).ThenBy(f => f.size).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var destName = i == 0 ? "profile.sav" : $"save_{i - 1}.sav";
                File.Copy(sorted[i].path, Path.Combine(destFolder, destName), overwrite: true);
            }
        }

        /// <summary>
        /// Copies named .sav files from sourceFolder back to their correct GUID files in the WGS
        /// package folder so the game picks up the restored saves.
        /// Each target file is backed up as {target}.rsg_bak before it is overwritten; if any write
        /// fails, all previously overwritten files are automatically rolled back from their backups.
        /// Returns false if containers.index cannot be parsed or if the operation is rolled back.
        /// </summary>
        public static bool RestoreToWgs(string sourceFolder, string wgsUserFolder)
        {
            var nameToGuid = ParseContainersIndex(Path.Combine(wgsUserFolder, "containers.index"));
            if (nameToGuid.Count == 0)
                return false;

            var written = new List<(string target, string backup)>();

            try
            {
                foreach (var savFile in Directory.GetFiles(sourceFolder, "*.sav"))
                {
                    var containerName = Path.GetFileNameWithoutExtension(savFile); // "profile", "save_0", …
                    if (!nameToGuid.TryGetValue(containerName, out var guidSubfolder))
                        continue;

                    var guidFolderPath = Path.Combine(wgsUserFolder, guidSubfolder);
                    if (!Directory.Exists(guidFolderPath))
                        continue;

                    var target = Directory.GetFiles(guidFolderPath)
                        .FirstOrDefault(f => !Path.GetFileName(f).StartsWith("container", StringComparison.OrdinalIgnoreCase));
                    if (target == null)
                        continue;

                    // Create a safety backup before overwriting
                    var backup = target + ".rsg_bak";
                    File.Copy(target, backup, overwrite: true);
                    written.Add((target, backup));

                    File.Copy(savFile, target, overwrite: true);
                }

                // All writes succeeded — clean up backups
                foreach (var (_, backup) in written)
                {
                    try { File.Delete(backup); } catch { }
                }

                return true;
            }
            catch
            {
                // Roll back every file already overwritten, then clean up backup copies
                foreach (var (target, backup) in written)
                {
                    try { File.Copy(backup, target, overwrite: true); } catch { }
                    try { File.Delete(backup); } catch { }
                }
                return false;
            }
        }

        // Credit to https://gist.github.com/crackedmind

        internal class FileHeader
        {
            public uint Crc32;
            public uint TotalSize;
            public uint Unknown;

            public static FileHeader ReadFromStream(Stream stream)
            {
                FileHeader header = new();
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                header.Crc32 = reader.ReadUInt32();
                header.TotalSize = reader.ReadUInt32();
                header.Unknown = reader.ReadUInt32();
                return header;
            }
        }
        internal class ChunkHeader
        {
            public ulong ChunkHeaderTag; // always 0x222222229E2A83C1
            public ulong ChunkSize;      // always 0x20000
            public byte DecompressionMethod; // 3 - zlib
            public ulong CompressedSize1;
            public ulong DecompressedSize1; // <= ChunkSize
            public ulong CompressedSize2;
            public ulong DecompressedSize2; // <= ChunkSize

            public static ChunkHeader ReadFromStream(Stream stream)
            {
                ChunkHeader header = new ChunkHeader();
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                header.ChunkHeaderTag = reader.ReadUInt64();
                header.ChunkSize = reader.ReadUInt64();
                header.DecompressionMethod = reader.ReadByte();
                header.CompressedSize1 = reader.ReadUInt64();
                header.DecompressedSize1 = reader.ReadUInt64();
                header.CompressedSize2 = reader.ReadUInt64();
                header.DecompressedSize2 = reader.ReadUInt64();
                return header;
            }
        }

        public static byte[] DecompressSave(string saveFilePath)
        {
            if (File.Exists(saveFilePath))
            {
                using var fileStream = File.Open(saveFilePath, FileMode.Open);
                var fileHeader = FileHeader.ReadFromStream(fileStream);

                var saveContent = new byte[fileHeader.TotalSize];
                using var memstream = new MemoryStream(saveContent);
                while (fileStream.Position < fileStream.Length)
                {
                    ChunkHeader header = ChunkHeader.ReadFromStream(fileStream);
                    byte[] buffer = new byte[header.CompressedSize1];
                    fileStream.Read(buffer);

                    using var bufferStream = new MemoryStream(buffer);
                    using var decompressor = new ZLibStream(bufferStream, CompressionMode.Decompress);
                    decompressor.CopyTo(memstream);
                }

                return saveContent;
            }
            return Array.Empty<byte>();
        }
        public static string DecompressSaveAsString(string saveFilePath)
        {
            return Encoding.ASCII.GetString(DecompressSave(saveFilePath));
        }
    }

    public enum RemnantSaveType
    {
        Normal,
        WindowsStore
    }
}
