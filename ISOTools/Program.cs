using GameFormatReader.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WArchiveTools.FileSystem;

namespace ISOTools
{
    class Program
    {
        public const string SystemDataName = "&&SystemData";

        private struct FileSystemTableEntry
        {
            public enum EntryType
            {
                File = 0,
                Directory = 1,
            }

            public EntryType Type;
            public string AbsoluteFileName;
            public string RelativeFileName;
            public int FileOffsetParentDir; // If Type == EntryType::File, this is the offset of the file's data. If it's a EntryType::Directory, it's the index of its parent.
            public int FileSizeNextDirIndex; //If Type is File, this is the size of the file data. If it's Directory, this is the index of the next dir on the same level as itself.

            public int FileNameOffset;
        }

        static void Main(string[] args)
        {
            string isoPathName = @"E:\New_Data_Drive\WindwakerModding\_old\The Legend of Zelda - The Wind Waker.gcm";
            List<FileSystemTableEntry> fstEntries = new List<FileSystemTableEntry>();

            using (EndianBinaryReader reader = new EndianBinaryReader(File.Open(isoPathName, FileMode.Open, FileAccess.Read), Endian.Big))
            {
                if (!CheckGCNImageHeader(reader))
                {
                    Console.WriteLine("Specified file is not a valid GCN image.");
                    return;
                }

                int fileSystemTableOffset = reader.ReadInt32At(0x424);
                int numEntries = reader.ReadInt32At(fileSystemTableOffset + 0x8);
                int stringTableOffset = numEntries * 0xC;

                reader.BaseStream.Position = fileSystemTableOffset;

                for (int i = 0; i < numEntries; i++)
                {
                    FileSystemTableEntry entry = new FileSystemTableEntry();
                    entry.Type = (FileSystemTableEntry.EntryType)reader.ReadByte();
                    reader.SkipByte();

                    long curStreamPos = reader.BaseStream.Position;
                    ushort stringOffset = reader.ReadUInt16();

                    reader.BaseStream.Position = stringOffset + stringTableOffset + fileSystemTableOffset;
                    string name = reader.ReadStringUntil('\0');
                    reader.BaseStream.Position = curStreamPos + 0x2; // Skip over the ushort for stringOffset

                    entry.RelativeFileName = name;
                    entry.FileOffsetParentDir = reader.ReadInt32();
                    entry.FileSizeNextDirIndex = reader.ReadInt32();

                    fstEntries.Add(entry);
                }

                VirtualFilesystemDirectory rootDirectory = new VirtualFilesystemDirectory("root");

                // Read the files that are stored inside the &&SystemData folder, as they're not normal FileSystemTableEntry's
                VirtualFilesystemFile isoHeader = new VirtualFilesystemFile("ISOHeader", "hdr", reader.ReadBytes(0x2440));

                int dolFileOffset = reader.ReadInt32At(0x420); //???
                VirtualFilesystemFile executable = new VirtualFilesystemFile("Start", "dol", reader.ReadBytesAt(dolFileOffset, fileSystemTableOffset - dolFileOffset));

                VirtualFilesystemFile appLoader = new VirtualFilesystemFile("AppLoader", "ldr", reader.ReadBytesAt(0x2440, dolFileOffset - 0x2440));

                VirtualFilesystemFile gameTOC = new VirtualFilesystemFile("Game", "toc", reader.ReadBytesAt(fileSystemTableOffset, reader.ReadInt32At(0x428))); //lolwut

                VirtualFilesystemDirectory sysDataDirectory = new VirtualFilesystemDirectory(SystemDataName);
                sysDataDirectory.Children.Add(isoHeader);
                sysDataDirectory.Children.Add(executable);
                sysDataDirectory.Children.Add(appLoader);
                sysDataDirectory.Children.Add(gameTOC);

                rootDirectory.Children.Add(sysDataDirectory);

                // Read the data into our VFS
                int count = 1;
                while (count < numEntries)
                {
                    FileSystemTableEntry entry = fstEntries[count];

                    if (entry.Type == FileSystemTableEntry.EntryType.Directory)
                    {
                        VirtualFilesystemDirectory vfsDir = new VirtualFilesystemDirectory(entry.RelativeFileName);
                        Console.WriteLine("Created Directory {0}", vfsDir.Name);

                        while (count < entry.FileSizeNextDirIndex - 1)
                        {
                            count = ReadDirectoryStructureRecursive(count + 1, fstEntries, fstEntries[count + 1], vfsDir, reader);
                        }
                        Console.WriteLine("Left Directory {0}", vfsDir.Name);

                        rootDirectory.Children.Add(vfsDir);
                    }
                    else
                    {
                        VirtualFilesystemFile vfsFile = ReadFileData(entry, reader);
                        Console.WriteLine("Created File {0}", vfsFile.Name);
                        rootDirectory.Children.Add(vfsFile);
                    }

                    count++;
                }



                // DUMP ISO TO DISK.
                Directory.CreateDirectory(@"E:\New_Data_Drive\WindwakerModding\Test_ISO_Dump");
                foreach(var node in rootDirectory.Children)
                {
                    WriteDirectoryContentsRecursive(node, @"E:\New_Data_Drive\WindwakerModding\Test_ISO_Dump");
                }

                Console.WriteLine("Finished writing contents to disk.");
            }
        }

        private static bool CheckGCNImageHeader(EndianBinaryReader reader)
        {
            uint fileMagic = reader.ReadUInt32At(0x1C);

            // 0xC2339F3D is the GameCube's identifier, while 0x5D1C9EA3 is the Wii one.
            return fileMagic == 0xC2339F3D;
        }

        private static int ReadDirectoryStructureRecursive(int curIndex, List<FileSystemTableEntry> fstEntries, FileSystemTableEntry parentFST, VirtualFilesystemDirectory parentDir, EndianBinaryReader reader)
        {
            FileSystemTableEntry curEntry = fstEntries[curIndex];
            if (curEntry.Type == FileSystemTableEntry.EntryType.Directory)
            {
                VirtualFilesystemDirectory vfsDir = new VirtualFilesystemDirectory(curEntry.RelativeFileName);
                Console.WriteLine("Created Directory {0}", vfsDir.Name);

                while (curIndex < curEntry.FileSizeNextDirIndex - 1)
                {
                    curIndex = ReadDirectoryStructureRecursive(curIndex + 1, fstEntries, curEntry, vfsDir, reader);
                }
                Console.WriteLine("Left Directory {0}", vfsDir.Name);

                parentDir.Children.Add(vfsDir);
            }
            else
            {
                VirtualFilesystemFile vfsFile = ReadFileData(curEntry, reader);
                Console.WriteLine("Created File {0}", vfsFile.Name);
                parentDir.Children.Add(vfsFile);
            }

            return curIndex;
        }

        private static VirtualFilesystemFile ReadFileData(FileSystemTableEntry entry, EndianBinaryReader reader)
        {
            string[] fileNameAndExtension = entry.RelativeFileName.Split('.');
            reader.BaseStream.Position = entry.FileOffsetParentDir;

            byte[] data = reader.ReadBytes(entry.FileSizeNextDirIndex);
            if(fileNameAndExtension.Length != 2)
            {
                fileNameAndExtension = new string[] { fileNameAndExtension[0], "" };
            }

            return new VirtualFilesystemFile(fileNameAndExtension[0], fileNameAndExtension[1], data);
        }

        private static void WriteDirectoryContentsRecursive(VirtualFilesystemNode node, string outputFolder)
        {
            if(node.Type == NodeType.Directory)
            {
                VirtualFilesystemDirectory vfsDir = (VirtualFilesystemDirectory)node;
                string subFolder = outputFolder + "\\" + vfsDir.Name;

                Directory.CreateDirectory(subFolder);
                foreach (var child in vfsDir.Children)
                {
                    WriteDirectoryContentsRecursive(child, subFolder);
                }
            }
            else
            {
                VirtualFilesystemFile vfsFile = (VirtualFilesystemFile)node;
                using (FileStream outStream = new FileStream(string.Format("{0}\\{1}.{2}", outputFolder, vfsFile.Name, vfsFile.Extension), FileMode.Create))
                {
                    outStream.Write(vfsFile.Data, 0, vfsFile.Data.Length);
                }
            }
        }
    }
}
