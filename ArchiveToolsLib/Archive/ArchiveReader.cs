using GameFormatReader.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using WArchiveTools.FileSystem;

namespace WArchiveTools.Archives
{
    public partial class Archive
    {
        public VirtualFilesystemDirectory ReadFile(EndianBinaryReader reader)
        {
            if (reader.ReadUInt32() != 0x52415243) // "RARC"
                throw new InvalidDataException("Invalid Magic, not a RARC File");

            uint fileSize = reader.ReadUInt32();
            uint unknown0 = reader.ReadUInt32(); // Unknown
            uint dataOffset = reader.ReadUInt32() + 0x20;
            uint unknown1 = reader.ReadUInt32(); // Unknown - 4 unsigned ints
            uint unknown2 = reader.ReadUInt32(); // Unknown - 4 unsigned ints
            uint unknown3 = reader.ReadUInt32(); // Unknown - 4 unsigned ints
            uint unknown4 = reader.ReadUInt32(); // Unknown - 4 unsigned ints
            uint numNodes = reader.ReadUInt32();
            uint unknown5 = reader.ReadUInt32(); // Unknown - 2 unsigned ints
            uint unknown6 = reader.ReadUInt32();
            uint fileEntryOffset = reader.ReadUInt32() + 0x20;
            uint unknown7 = reader.ReadUInt32(); // Unknown
            uint stringTableOffset = reader.ReadUInt32() + 0x20;
            uint unknown8 = reader.ReadUInt32(); // Unknown
            uint unknown9 = reader.ReadUInt32(); // Unknown

#if ALT_EXTRACTION_METHOD
            Console.WriteLine("U0: {0} U1: {1} U2: {2} U3: {3} U4: {4} U5: {5} U6: {6} U7: {7} U8: {8} U9: {9}", unknown0, unknown1, unknown2, unknown3, unknown4, unknown5, unknown6, unknown7, unknown8, unknown9);
#endif

            // Read all of the node headers.
            Node[] nodes = new Node[numNodes];

            for (int i = 0; i < numNodes; i++)
            {
                nodes[i] = new Node
                {
                    Type = new string(reader.ReadChars(4)),
                    Name = ReadStringAtOffset(reader, stringTableOffset, reader.ReadUInt32()),
                    NameHashcode = reader.ReadUInt16(),
                    Entries = new FileEntry[reader.ReadUInt16()],
                    FirstFileOffset = reader.ReadUInt32()
                };
            }


            // Create a virtual directory for every folder within the ARC before we process any of them.
            List<VirtualFilesystemDirectory> allDirs = new List<VirtualFilesystemDirectory>(nodes.Length);
            foreach (Node node in nodes)
            {
                VirtualFilesystemDirectory vfDir = new VirtualFilesystemDirectory(node.Name);
                allDirs.Add(vfDir);
            }

            for (int k = 0; k < nodes.Length; k++)
            {
                Node node = nodes[k];
                VirtualFilesystemDirectory curDir = allDirs[k];

                for (int i = 0; i < node.Entries.Length; i++)
                {
                    // Jump to the entry's offset in the file.
                    reader.BaseStream.Position = fileEntryOffset + ((node.FirstFileOffset + i) * 0x14); // 0x14 is the size of a File Entry in bytes
                    node.Entries[i] = new FileEntry();
                    node.Entries[i].ID = reader.ReadUInt16();
                    node.Entries[i].NameHashcode = reader.ReadUInt16();
                    node.Entries[i].Type = reader.ReadByte();
                    Debug.Assert(reader.ReadByte() == 0); // Padding
                    node.Entries[i].Name = ReadStringAtOffset(reader, stringTableOffset, reader.ReadUInt16());

                    // Skip these ones cause I don't know how computers work.
                    if (node.Entries[i].Name == "." || node.Entries[i].Name == "..")
                        continue;

                    uint entryDataOffset = reader.ReadUInt32();
                    uint dataSize = reader.ReadUInt32();

                    // There's a couple of archives in Wind Waker which seem to have an ID that is set to zero to indicate a directory.
                    // However, this breaks literally every other archive in Wind Waker. Until we have a better idea of what it is,
                    // we'll just have to make a manual build of the extract to extract those.
#if ALT_EXTRACTION_METHOD
                    if(node.Entries[i].IsDirectory || node.Entries[i].ID == 0)
#else
                    if (node.Entries[i].IsDirectory)
#endif
                    {
                        // If it's a directory, then entryDataOffset contains the index of the parent node
                        node.Entries[i].SubDirIndex = entryDataOffset;
                        var newSubDir = allDirs[(int)entryDataOffset];
                        curDir.Children.Add(newSubDir);
                    }
                    else
                    {
                        node.Entries[i].Data = reader.ReadBytesAt(dataOffset + entryDataOffset, (int)dataSize);

                        string fileName = Path.GetFileNameWithoutExtension(node.Entries[i].Name);
                        string extension = Path.GetExtension(node.Entries[i].Name);

                        VirtualFilesystemFile vfFile = new VirtualFilesystemFile(fileName, extension, node.Entries[i].Data);
                        curDir.Children.Add(vfFile);
                    }

                    Debug.Assert(reader.ReadUInt32() == 0); // Padding
                }
            }

            // The ROOT directory should always be the first node. We don't have access to the node's TYPE anymore
            // so we're going to assume its always the first one listed.
            return allDirs.Count > 0 ? allDirs[0] : null;
        }

        private string ReadStringAtOffset(EndianBinaryReader reader, uint stringTableOffset, uint offset)
        {
            // Jump to the string table position, read the string and then restore the position.
            long curPos = reader.BaseStream.Position;
            uint stringOffset = offset + stringTableOffset;
            reader.BaseStream.Position = stringOffset;

            byte[] bytes = reader.ReadBytesUntil(0x00);
            string result = Encoding.GetEncoding("shift_jis").GetString(bytes);

            reader.BaseStream.Position = curPos;
            return result;
        }
    }
}
