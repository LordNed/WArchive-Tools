using GameFormatReader.Common;
using GCMRebuilderUI.ViewModel;
using System;
using System.Collections.Generic;
using System.IO;
using WArchiveTools.FileSystem;

namespace GCMRebuilderUI.Model
{
    public class DiskImage : ObservableObject
    {
        public VirtualFilesystemNode Data
        {
            get { return m_diskData; }
            set
            {
                m_diskData = value;
                OnPropertyChanged("Data");
            }
        }

        private VirtualFilesystemNode m_diskData;


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

        public DiskImage()
        {
            Load();
        }

        public void Load(/* todo, stream path*/)
        {
            string isoPathName = @"E:\New_Data_Drive\WindwakerModding\_old\The Legend of Zelda - The Wind Waker.gcm";
            List<FileSystemTableEntry> fstEntries = new List<FileSystemTableEntry>();

            using (EndianBinaryReader reader = new EndianBinaryReader(File.Open(isoPathName, FileMode.Open, FileAccess.Read), Endian.Big))
            {
                // Ensure this is a valid GameCube Disk Image
                if (!CheckDiskImageMagic(reader))
                {
                    throw new ArgumentException("Invalid File Magic, not a GameCube image!");
                }

                // Read the Filesystem Table 
                fstEntries = ReadFilesystemTable(reader);

                // debug
                for(int i = 0; i < fstEntries.Count; i++)
                {
                    //Console.WriteLine(fstEntries[i].RelativeFileName);
                }


                // Load the actual data stored by the Filesystem Table into a Virtual Filesystem.
                VirtualFilesystemDirectory vfsRoot = ReadFSTDataFromImage(reader, fstEntries);
                Data = vfsRoot;
            }
        }
        private bool CheckDiskImageMagic(EndianBinaryReader reader)
        {
            uint fileMagic = reader.ReadUInt32At(0x1C);

            // 0xC2339F3D is the GameCube's identifier, while 0x5D1C9EA3 is the Wii one.
            return fileMagic == 0xC2339F3D;
        }

        private List<FileSystemTableEntry> ReadFilesystemTable(EndianBinaryReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException("reader", "Cannot read Filesystem Table from null EndianBinaryReader!");

            List<FileSystemTableEntry> fstEntries = new List<FileSystemTableEntry>();

            int fileSystemTableOffset = reader.ReadInt32At(0x424);
            int numEntries = reader.ReadInt32At(fileSystemTableOffset + 0x8);
            int stringTableOffset = numEntries * 0xC;

            reader.BaseStream.Position = fileSystemTableOffset;

            for (int i = 0; i < numEntries; i++)
            {
                FileSystemTableEntry entry = new FileSystemTableEntry();
                entry.Type = (FileSystemTableEntry.EntryType)reader.ReadByte();
                reader.SkipByte();

                ushort stringOffset = reader.ReadUInt16();
                long curStreamPos = reader.BaseStream.Position;

                // Jump ahead to the string table and read the string from that.
                reader.BaseStream.Position = stringOffset + stringTableOffset + fileSystemTableOffset;
                string name = reader.ReadStringUntil('\0');

                reader.BaseStream.Position = curStreamPos;

                entry.RelativeFileName = name;
                entry.FileOffsetParentDir = reader.ReadInt32();
                entry.FileSizeNextDirIndex = reader.ReadInt32();

                fstEntries.Add(entry);
            }

            return fstEntries;
        }

        private VirtualFilesystemDirectory ReadFSTDataFromImage(EndianBinaryReader reader, List<FileSystemTableEntry> fstEntries)
        {
            // Yay magic jumping around. :(
            int dolFileOffset = reader.ReadInt32At(0x420);
            int fileSystemTableOffset = reader.ReadInt32At(0x424);

            VirtualFilesystemDirectory rootDirectory = new VirtualFilesystemDirectory("root");

            // Read the files that are stored inside the &&SystemData folder, as they're not normal FileSystemTableEntry's
            VirtualFilesystemFile isoHeader = new VirtualFilesystemFile("ISOHeader", "hdr", reader.ReadBytes(0x2440));
            VirtualFilesystemFile executable = new VirtualFilesystemFile("Start", "dol", reader.ReadBytesAt(dolFileOffset, fileSystemTableOffset - dolFileOffset));
            VirtualFilesystemFile appLoader = new VirtualFilesystemFile("AppLoader", "ldr", reader.ReadBytesAt(0x2440, dolFileOffset - 0x2440));
            VirtualFilesystemFile gameTOC = new VirtualFilesystemFile("Game", "toc", reader.ReadBytesAt(fileSystemTableOffset, reader.ReadInt32At(0x428)));

            VirtualFilesystemDirectory sysDataDirectory = new VirtualFilesystemDirectory("&&SystemData");
            sysDataDirectory.Children.Add(isoHeader);
            sysDataDirectory.Children.Add(executable);
            sysDataDirectory.Children.Add(appLoader);
            sysDataDirectory.Children.Add(gameTOC);

            rootDirectory.Children.Add(sysDataDirectory);

            if(fstEntries.Count > 0)
            {
                ReadDirectoryStructureRecursive(0, fstEntries, fstEntries[0], rootDirectory, reader);
            }

            return rootDirectory;
        }

        private int ReadDirectoryStructureRecursive(int curIndex, List<FileSystemTableEntry> fstEntries, FileSystemTableEntry parentFST, VirtualFilesystemDirectory parentDir, EndianBinaryReader reader)
        {
            FileSystemTableEntry curEntry = fstEntries[curIndex];
            if (curEntry.Type == FileSystemTableEntry.EntryType.Directory)
            {
                VirtualFilesystemDirectory vfsDir = new VirtualFilesystemDirectory(curEntry.RelativeFileName);
                //Console.WriteLine("Created Directory {0}", vfsDir.Name);

                while (curIndex < curEntry.FileSizeNextDirIndex - 1)
                {
                    curIndex = ReadDirectoryStructureRecursive(curIndex + 1, fstEntries, curEntry, vfsDir, reader);
                }
                //Console.WriteLine("Left Directory {0}", vfsDir.Name);

                parentDir.Children.Add(vfsDir);
            }
            else
            {
                VirtualFilesystemFile vfsFile = ReadFileData(curEntry, reader);
                //Console.WriteLine("Created File {0}", vfsFile.Name);
                parentDir.Children.Add(vfsFile);
            }

            return curIndex;
        }

        private VirtualFilesystemFile ReadFileData(FileSystemTableEntry entry, EndianBinaryReader reader)
        {
            string[] fileNameAndExtension = entry.RelativeFileName.Split('.');
            reader.BaseStream.Position = entry.FileOffsetParentDir;

            //byte[] data = reader.ReadBytes(entry.FileSizeNextDirIndex);
            byte[] data = new byte[0];
            if (fileNameAndExtension.Length != 2)
            {
                fileNameAndExtension = new string[] { fileNameAndExtension[0], "" };
            }

            return new VirtualFilesystemFile(fileNameAndExtension[0], fileNameAndExtension[1], data);
        }

    }
}
