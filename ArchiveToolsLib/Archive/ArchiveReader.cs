using GameFormatReader.Common;
using System.Collections.Generic;
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
            reader.SkipUInt32(); // Unknown
            uint dataOffset = reader.ReadUInt32() + 0x20;
            reader.Skip(16); // Unknown - 4 unsigned ints
            uint numNodes = reader.ReadUInt32();
            reader.Skip(8); // Unknown - 2 unsigned ints
            uint fileEntryOffset = reader.ReadUInt32() + 0x20;
            reader.SkipUInt32(); // Unknown
            uint stringTableOffset = reader.ReadUInt32() + 0x20;
            reader.Skip(8); // Unknown - 2 unsigned ints.

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
                    node.Entries[i].Flags = reader.ReadByte();
                    reader.SkipByte(); // Padding
                    node.Entries[i].Name = ReadStringAtOffset(reader, stringTableOffset, reader.ReadUInt16());

                    // Skip these ones cause I don't know how computers work.
                    if (node.Entries[i].Name == "." || node.Entries[i].Name == "..")
                        continue;

                    uint entryDataOffset = reader.ReadUInt32();
                    uint dataSize = reader.ReadUInt32();

                    // If it's a directory, then entryDataOffset contains the index of the parent node
                    if (node.Entries[i].IsDirectory)
                    {
                        node.Entries[i].SubDirIndex = entryDataOffset;
                        var newSubDir = allDirs[(int)entryDataOffset];
                        newSubDir.NodeID = node.Entries[i].ID;
                        curDir.Children.Add(newSubDir);
                    }
                    else
                    {
                        node.Entries[i].Data = reader.ReadBytesAt(dataOffset + entryDataOffset, (int)dataSize);

                        string fileName = Path.GetFileNameWithoutExtension(node.Entries[i].Name);
                        string extension = Path.GetExtension(node.Entries[i].Name);

                        VirtualFilesystemFile vfFile = new VirtualFilesystemFile(fileName, extension, node.Entries[i].Data);
                        vfFile.NodeID = node.Entries[i].ID;
                        curDir.Children.Add(vfFile);
                    }

                    reader.SkipInt32(); // Padding
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
