using GameFormatReader.Common;
using System;
using System.Collections.Generic;
using WArchiveTools.FileSystem;

namespace WArchiveTools.ISOs
{
    public partial class ISO
    {
        public VirtualFilesystemDirectory LoadISO(EndianBinaryReader reader)
        {
            // This will hold the file system entries that we grab from the ISO.
            List<FSTEntry> FSTTest = new List<FSTEntry>();

            int FSTOffset = reader.ReadInt32At(0x424);
            reader.BaseStream.Position = FSTOffset;

            int numEntries = reader.ReadInt32At(reader.BaseStream.Position + 8);
            int stringTableOffset = numEntries * 0xC; // Each FST entry is 12/0xC bytes long

            for (int i = 0; i < numEntries; i++)
            {
                FSTEntry fst = new FSTEntry();

                fst.Type = (FSTNodeType)reader.ReadByte();
                reader.SkipByte(); // Padding

                int curPos = (int)reader.BaseStream.Position; // Save the current stream position
                ushort stringOffset = (ushort)reader.ReadInt16(); // Get the offset to the entry's name

                reader.BaseStream.Position = stringOffset + stringTableOffset + FSTOffset; // Seek to entry's name

                string name = reader.ReadStringUntil('\0');
                fst.RelativeFileName = name;

                reader.BaseStream.Position = curPos + 2; // Return to where we were so that we can read additional data for this entry

                fst.FileOffsetParentDir = reader.ReadInt32();
                fst.FileSizeNextDirIndex = reader.ReadInt32();

                FSTTest.Add(fst);
            }
            
            // Root of the ISO. The function will return this.
            VirtualFilesystemDirectory rootDir = new VirtualFilesystemDirectory("root");
            // Holds system data. DOL, apploader, etc. Named to provide compatibility with GCRebuilder
            VirtualFilesystemDirectory sysData = new VirtualFilesystemDirectory("&&systemdata");

            // Header data for the ISO
            byte[] headerData = reader.ReadBytesAt(0, 0x2440);
            sysData.Children.Add(new VirtualFilesystemFile("iso", ".hdr", headerData));

            int headerOffset = reader.ReadInt32At(0x420);

            // Executable data
            byte[] dolData = reader.ReadBytesAt(headerOffset, FSTOffset - headerOffset);
            sysData.Children.Add(new VirtualFilesystemFile("Start", ".dol", dolData));

            // Apploader
            byte[] appLoaderData = reader.ReadBytesAt(0x2440, headerOffset - 0x2440);
            sysData.Children.Add(new VirtualFilesystemFile("AppLoader", ".ldr", appLoaderData));

            // Table of contents (FST)
            byte[] fstData = reader.ReadBytesAt(FSTOffset, reader.ReadInt32At(0x428));
            sysData.Children.Add(new VirtualFilesystemFile("Game", ".toc", fstData));

            rootDir.Children.Add(sysData);

            int count = 1;

            while (count < numEntries)
            {
                if (FSTTest[count].Type == FSTNodeType.Directory)
                {
                    VirtualFilesystemDirectory dir = new VirtualFilesystemDirectory(FSTTest[count].RelativeFileName);
                    FSTEntry curEnt = FSTTest[count];

                    while (count < curEnt.FileSizeNextDirIndex - 1)
                    {
                        count = GetDirStructureRecursive(count + 1, FSTTest, FSTTest[count + 1], dir, reader);
                    }

                    rootDir.Children.Add(dir);
                }
                else
                {
                    VirtualFilesystemFile file = GetFileData(FSTTest[count], reader);
                    rootDir.Children.Add(file);
                }

                count += 1;
            }

            return rootDir;
        }

        private int GetDirStructureRecursive(int curIndex, List<FSTEntry> FST, FSTEntry parentFST, VirtualFilesystemDirectory parentDir, EndianBinaryReader image)
        {
            FSTEntry curEntry = FST[curIndex];

            if (curEntry.Type == FSTNodeType.Directory)
            {
                VirtualFilesystemDirectory dir = new VirtualFilesystemDirectory(curEntry.RelativeFileName);

                Console.WriteLine("Created directory: " + dir.Name);

                while (curIndex < curEntry.FileSizeNextDirIndex - 1)
                {
                    curIndex = GetDirStructureRecursive(curIndex + 1, FST, curEntry, dir, image);
                }

                parentDir.Children.Add(dir);

                Console.WriteLine("Leaving directory: " + dir.Name);

                return curIndex;
            }
            else
            {
                VirtualFilesystemFile file = GetFileData(curEntry, image);
                parentDir.Children.Add(file);

                return curIndex;
            }
        }

        private VirtualFilesystemFile GetFileData(FSTEntry fstData, EndianBinaryReader image)
        {
            string[] fileNameAndExtension = fstData.RelativeFileName.Split('.');
            image.BaseStream.Position = fstData.FileOffsetParentDir;
            byte[] data = image.ReadBytes((int)fstData.FileSizeNextDirIndex);

            VirtualFilesystemFile file;

            if (fileNameAndExtension.Length != 2)
            {
                file = new VirtualFilesystemFile(fileNameAndExtension[0], "", data);
            }

            else
            {
                file = new VirtualFilesystemFile(fileNameAndExtension[0], "." + fileNameAndExtension[1], data);
            }

            Console.WriteLine("Created file: " + file.Name);

            return file;
        }
    }
}
