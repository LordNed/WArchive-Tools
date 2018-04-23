﻿using GameFormatReader.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WArchiveTools.FileSystem;

namespace WArchiveTools.ISOs
{
    public partial class ISO
    {
        const uint DolAlignment = 1024;
        const uint FstAlignment = 256;
        const uint OffsetOfDolOffset = 0x420;

        #region Dumping files
        public void DumpToDisk(VirtualFilesystemDirectory root, string path)
        {
            // Make sure root path exists
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            foreach (VirtualFilesystemNode node in root.Children)
                DumpDirsRecursive(node, path);
        }

        private void DumpDirsRecursive(VirtualFilesystemNode vfsObj, string root)
        {
            if (vfsObj.Type == NodeType.Directory)
            {
                VirtualFilesystemDirectory dir = (VirtualFilesystemDirectory)vfsObj;

                string testRoot = root + @"\" + dir.Name;

                Directory.CreateDirectory(testRoot);

                foreach (VirtualFilesystemNode child in dir.Children)
                {
                    if (child.Type == NodeType.Directory)
                    {
                        Directory.CreateDirectory(testRoot + @"\" + child.Name);

                        Console.WriteLine("Wrote directory: " + testRoot + @"\" + child.Name);

                        DumpDirsRecursive(child, testRoot);
                    }

                    else
                    {
                        DumpDirsRecursive(child, testRoot);
                    }
                }
            }

            else
            {
                VirtualFilesystemFile file = (VirtualFilesystemFile)vfsObj;

                using (FileStream stream = new FileStream(root + @"\" + file.Name + file.Extension, FileMode.Create))
                {
                    EndianBinaryWriter writer = new EndianBinaryWriter(stream, Endian.Big);

                    writer.Write(file.Data);

                    Console.WriteLine("Wrote file: " + root + @"\" + file.Name + file.Extension);
                }
            }
        }
        #endregion

        #region Creating whole ISO
        public void WriteISO(VirtualFilesystemDirectory root, string path)
        {
            using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                EndianBinaryWriter writer = new EndianBinaryWriter(output, Endian.Big);

                List<byte> fstNameBank = new List<byte>();
                List<FSTEntry> outputFST = new List<FSTEntry>();
                List<VirtualFilesystemFile> fileList = new List<VirtualFilesystemFile>();
                FSTEntry rootFST = new FSTEntry();
                
                VirtualFilesystemDirectory sysDir = (VirtualFilesystemDirectory)root.Children.SingleOrDefault(e => e.Name == "&&systemdata") 
                    ?? (VirtualFilesystemDirectory)root.Children.SingleOrDefault(e => e.Name == "sys");
                VirtualFilesystemFile header = (VirtualFilesystemFile)sysDir.Children.Single(e => e is VirtualFilesystemFile && ((VirtualFilesystemFile)e).NameWithExtension == "iso.hdr");
                writer.Write(header.Data);

                VirtualFilesystemFile apploader = (VirtualFilesystemFile)sysDir.Children.SingleOrDefault(e => e is VirtualFilesystemFile && ((VirtualFilesystemFile)e).NameWithExtension == "AppLoader.ldr") 
                    ?? (VirtualFilesystemFile)sysDir.Children.SingleOrDefault(e => e is VirtualFilesystemFile && ((VirtualFilesystemFile)e).NameWithExtension == "apploader.img");
                writer.Write(apploader.Data);

                var dolOffsetWithoutPadding = writer.BaseStream.Position;
                var dolOffset = AlignTo(dolOffsetWithoutPadding, DolAlignment);
                for (long i = dolOffsetWithoutPadding; i < dolOffset; i++)
                {
                    writer.Write((byte)0);
                }

                VirtualFilesystemFile dol = (VirtualFilesystemFile)sysDir.Children.Single(e => e is VirtualFilesystemFile && ((VirtualFilesystemFile)e).Extension == ".dol");
                writer.Write(dol.Data);

                var fstListOffsetWithoutPadding = writer.BaseStream.Position;
                var fstListOffset = AlignTo(fstListOffsetWithoutPadding, FstAlignment);
                for (long i = fstListOffsetWithoutPadding; i < fstListOffset; i++)
                {
                    writer.Write((byte)0);
                }

                int fstLength = 12;

                root.Children.RemoveAt(0);

                foreach (VirtualFilesystemNode node in root.Children)
                {
                    fstLength = GetFSTSkipValue(fstLength, node);
                }

                byte[] dummyFST = new byte[fstLength];

                writer.Write(dummyFST);

                rootFST.Type = FSTNodeType.Directory;
                outputFST.Add(rootFST); //Placeholder FST entry for the root

                foreach (VirtualFilesystemNode node in root.Children)
                {
                    DoOutputPrep(node, outputFST, fstNameBank, writer, 0);
                }

                rootFST.FileSizeNextDirIndex = outputFST.Count();
                outputFST[0] = rootFST; //Add actual root FST entry

                writer.BaseStream.Position = fstListOffset;

                foreach (FSTEntry entry in outputFST)
                {
                    writer.Write((byte)entry.Type);
                    writer.Write((byte)0);
                    writer.Write((ushort)entry.FileNameOffset);
                    writer.Write(entry.FileOffsetParentDir);
                    writer.Write(entry.FileSizeNextDirIndex);
                }

                writer.Write(fstNameBank.ToArray());

                writer.BaseStream.Position = OffsetOfDolOffset;
                writer.Write((uint)dolOffset);
                writer.Write((uint)fstListOffset);
                writer.Write((uint)fstLength);
                writer.Write((uint)fstLength); // Doesn't work for multi disks
            }
        }

        private static long AlignTo(long addr, long align)
        {
            return (addr + (align - 1)) / align * align;
        }

        private int GetFSTSkipValue(int curValue, VirtualFilesystemNode node)
        {
            if (node.Type == NodeType.Directory)
            {
                VirtualFilesystemDirectory dir = (VirtualFilesystemDirectory)node;
                curValue += (12 + dir.Name.Length + 1);

                foreach (VirtualFilesystemNode child in dir.Children)
                {
                    curValue = GetFSTSkipValue(curValue, child);
                }
            }
            else
            {
                VirtualFilesystemFile file = (VirtualFilesystemFile)node;
                curValue += (int)(12 + file.Name.Length + file.Extension.Length + 1);
            }

            return curValue;
        }

        private void DoOutputPrep(VirtualFilesystemNode vfsNode, List<FSTEntry> outputFST, List<byte> fstNameBank, EndianBinaryWriter writer, int curParentDirIndex)
        {
            FSTEntry fstEnt = new FSTEntry();

            if (vfsNode.Type == NodeType.Directory)
            {
                VirtualFilesystemDirectory dir = (VirtualFilesystemDirectory)vfsNode;

                fstEnt.Type = FSTNodeType.Directory;
                fstEnt.FileNameOffset = fstNameBank.Count();

                fstNameBank.AddRange(Encoding.ASCII.GetBytes(dir.Name.ToCharArray()));
                fstNameBank.Add(0);

                fstEnt.FileOffsetParentDir = curParentDirIndex;
                curParentDirIndex = outputFST.Count();

                int thisDirIndex = curParentDirIndex;

                outputFST.Add(fstEnt); //Placeholder for this dir

                foreach (VirtualFilesystemNode child in dir.Children)
                {
                    DoOutputPrep(child, outputFST, fstNameBank, writer, curParentDirIndex);
                }

                int dirEndIndex = outputFST.Count();
                fstEnt.FileSizeNextDirIndex = (dirEndIndex - thisDirIndex) + thisDirIndex;
                outputFST[thisDirIndex] = fstEnt; //Add the actual entry after giving it the rest of the info
            }
            else
            {
                VirtualFilesystemFile file = (VirtualFilesystemFile)vfsNode;

                fstEnt.Type = FSTNodeType.File;
                fstEnt.FileSizeNextDirIndex = file.Data.Length;
                fstEnt.FileNameOffset = fstNameBank.Count();

                fstNameBank.AddRange(Encoding.ASCII.GetBytes(file.Name.ToCharArray()));
                fstNameBank.AddRange(Encoding.ASCII.GetBytes(file.Extension.ToCharArray()));
                fstNameBank.Add((byte)0);

                writer.BaseStream.Position = (int)writer.BaseStream.Position + (32 - ((int)writer.BaseStream.Position % 32)) % 32;

                fstEnt.FileOffsetParentDir = (int)writer.BaseStream.Position;

                writer.Write(file.Data);

                for (int i = 0; i < ((32 - (file.Data.Length - 32)) % 32); i++)
                {
                    writer.Write((byte)0);
                }

                //Console.WriteLine("Wrote file: " + file.Name);

                outputFST.Add(fstEnt);
            }
        }
        #endregion
    }
}
