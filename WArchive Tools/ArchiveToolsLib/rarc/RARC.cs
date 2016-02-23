using GameFormatReader.Common;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WEditor.FileSystem;

namespace WArchiveTools.rarc
{
    public class RARC
    {
        /// <summary>
        /// Represents a directory within the RARC archive.
        /// </summary>
        private class Node
        {
            /// <summary>4-character string describing the node's type.</summary>
            public string Type { get; internal set; }
            /// <summary>Directory name's offset within the string table.</summary>
            public string Name { get; internal set; }
            /// <summary>Hash of the <see cref="Name"/> field.</summary>
            public ushort NameHashcode { get; internal set; }
            /// <summary>The offset for the first file in this node.</summary>
            public uint FirstFileOffset { get; internal set; }
            /// <summary>The entries within this Node.</summary>
            public FileEntry[] Entries { get; internal set; }

            public override string ToString()
            {
                return Name;
            }
        }

        /// <summary>
        /// Represents a file or subdirectory within a RARC archive.
        /// </summary>
        private class FileEntry
        {
            /// <summary>File ID. If 0xFFFF, then this entry is a subdirectory link.</summary>
            public ushort ID { get; internal set; }
            /// <summary>String hash of the <see cref="Name"/> field.</summary>
            public ushort NameHashcode { get; internal set; }
            /// <summary>Type of entry. 0x2 = Directory, 0x11 = File.</summary>
            public byte Type { get; internal set; }
            /// <summary>Padding byte. Included here for the sake of documentation. </summary>
            public byte Padding { get; internal set; }
            /// <summary>File/subdirectory name string table offset.</summary>
            public string Name { get; internal set; }
            /// <summary>Data bytes. If this entry is a directory, it will be the node index.</summary>
            public byte[] Data { get; internal set; }
            /// <summary>Always zero.</summary>
            public uint ZeroPadding { get; internal set; }

            // Non actual struct items

            /// <summary>Whether or not this entry is a directory.</summary>
            public bool IsDirectory { get { return ID == 0xFFFF; } }
            /// <summary>Node index representing the subdirectory. Will only be non-zero if IsDirectory is true.</summary>
            public uint SubDirIndex { get; internal set; }

            public override string ToString()
            {
                return Name;
            }
        }

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

            for(int i = 0; i < numNodes; i++)
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
            foreach(Node node in nodes)
            {
                VirtualFilesystemDirectory vfDir = new VirtualFilesystemDirectory(node.Name);
                allDirs.Add(vfDir);
            }

            for(int k = 0; k < nodes.Length; k++)
            {
                Node node = nodes[k];
                VirtualFilesystemDirectory curDir = allDirs[k];

                for(int i = 0; i < node.Entries.Length; i++)
                {
                    // Jump to the entry's offset in the file.
                    reader.BaseStream.Position = fileEntryOffset + ((node.FirstFileOffset + i) * 0x14); // 0x14 is the size of a File Entry in bytes
                    node.Entries[i] = new FileEntry
                    {
                        ID = reader.ReadUInt16(),
                        NameHashcode = reader.ReadUInt16(),
                        Type = reader.ReadByte(),
                        Padding = reader.ReadByte(),
                        Name = ReadStringAtOffset(reader, stringTableOffset, reader.ReadUInt16())
                    };

                    // Skip these ones cause I don't know how computers work.
                    if (node.Entries[i].Name == "." || node.Entries[i].Name == "..")
                        continue;

                    uint entryDataOffset = reader.ReadUInt32();
                    uint dataSize = reader.ReadUInt32();

                    // If it's a directory, then entryDataOffset contains the index of the parent node
                    if(node.Entries[i].IsDirectory)
                    {
                        node.Entries[i].SubDirIndex = entryDataOffset;
                        var newSubDir = allDirs[(int)entryDataOffset];
                        curDir.Children.Add(newSubDir);
                    }
                    else
                    {
                        node.Entries[i].Data = reader.ReadBytesAt(dataOffset + entryDataOffset, (int)dataSize);

                        string fileName = Path.GetFileNameWithoutExtension(node.Entries[i].Name);
                        string extension = Path.GetExtension(node.Entries[i].Name);

                        var vfFileContents = new VirtualFileContents(node.Entries[i].Data);
                        VirtualFilesystemFile vfFile = new VirtualFilesystemFile(fileName, extension, vfFileContents);
                        curDir.Children.Add(vfFile);
                    }
                    node.Entries[i].ZeroPadding = reader.ReadUInt32();
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
