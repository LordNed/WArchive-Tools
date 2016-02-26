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

        List<Node> exportNodes;
        List<FileEntry> exportFileEntries;
        List<char> exportStringTable;
        List<byte> exportFileData;

        ushort exportFileID;

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

        public byte[] WriteFile(VirtualFilesystemDirectory root)
        {
            // These will hold our data until we're ready to put it in a byte[]
            exportNodes = new List<Node>();
            exportFileEntries = new List<FileEntry>();
            exportStringTable = new List<char>();
            exportFileData = new List<byte>();

            // The first two string table entries, for working (.) and parent (..) directories (?)
            exportStringTable.AddRange(".\0".ToCharArray());
            exportStringTable.AddRange("..\0".ToCharArray());

            // Create the root node
            Node rootNode = new Node
            {
                Type = "ROOT",
                Name = root.Name,
                NameHashcode = HashName(root.Name),
                FirstFileOffset = 0,
            };

            exportNodes.Add(rootNode);

            // Fill the root's file entries, and in the process fill out all of the other data we need, too
            rootNode.Entries = GetDirDataRecursive(root, null);

            MemoryStream dataBuffer = new MemoryStream();

            EndianBinaryWriter writer = new EndianBinaryWriter(dataBuffer, Endian.Big);

            writer.Write("RARC".ToCharArray());

            // Placeholder for the file size. We'll come back and fill it in at the end.
            // Offset is 0x04
            writer.Write((int)0);

            // An unknown int in the header.
            writer.Write((int)0x20);

            // Placeholder for the data start offset. We'll come back and fill it in at the end.
            // Offset is 0x0C
            writer.Write((int)0);

            // Unknown ints in the header. There are 4, but we'll just write two 64-bit zeros to save space.
            writer.Write((ulong)0);
            writer.Write((ulong)0);

            // Node count
            writer.Write((int)exportNodes.Count);

            // Two unknown ints. We'll write them as one 64-bit zero.
            writer.Write((int)0x20);

            writer.Write((int)exportFileEntries.Count);

            // Get the aligned offset to the start of the file entries table.
            // Alignment formula: ((data length) + 0x1F) & ~0x1F
            int alignedFileEntriesOffset =
                0x20 + (((exportNodes.Count * 0x10) + 0x1F) & ~0x1F);

            writer.Write(alignedFileEntriesOffset);

            // An unknown int in the header.
            writer.Write((int)0);

            // Get the aligned offset to the string table.
            int alignedStringTableOffset = 
                (alignedFileEntriesOffset + (((exportFileEntries.Count * 0x14) + 0x1F) & ~0x1F));

            writer.Write(alignedStringTableOffset);

            // Two unknown ints. We'll write them as one 64-bit zero.
            writer.Write((ulong)0);

            // Write each node
            foreach (Node nod in exportNodes)
            {
                writer.Write(nod.Type.ToCharArray());

                writer.Write((int)exportStringTable.Count);

                exportStringTable.AddRange(nod.Name.ToCharArray());

                // Strings must be null terminated
                exportStringTable.Add('\0');

                writer.Write(nod.NameHashcode);

                writer.Write((short)(nod.Entries.Length + 2));

                writer.Write(exportFileEntries.FindIndex(i => i.Name == nod.Entries[0].Name));
            }

            Pad32(writer);

            // Write each entry
            foreach (FileEntry entry in exportFileEntries)
            {
                writer.Write(entry.ID);

                writer.Write(entry.NameHashcode);

                writer.Write(entry.Type);

                // Zero padding
                writer.Write((byte)0);

                if ((entry.Name == "."))
                {
                    writer.Write((ushort)0);
                }

                else if ((entry.Name == ".."))
                {
                    writer.Write((ushort)2);
                }

                else
                {
                    if (exportNodes.Find(i => i.Name == entry.Name) != null)
                    {
                        string test = new string(exportStringTable.ToArray());

                        int testt = test.IndexOf(entry.Name);

                        writer.Write((ushort)test.IndexOf(entry.Name));
                    }

                    else
                    {
                        // Offset of name in the string table
                        writer.Write((ushort)exportStringTable.Count);

                        // Add name to string table
                        exportStringTable.AddRange(entry.Name.ToCharArray());

                        // Strings must be null terminated
                        exportStringTable.Add('\0');
                    }
                }

                // Entry is a directory
                if (entry.Type == 0x02)
                {
                    if (entry.Data[0] != 255)
                    {
                        // First element should always be set to the index of the entry's parent node.
                        // If it's not that laws of physics no longer apply. Or I fucked up
                        writer.Write((int)entry.Data[0]);
                    }

                    else
                    {
                        writer.Write((int)-1);
                    }

                    // Entries will always have a data size of 0x10, the size of a node
                    writer.Write((int)0x10);
                }

                // Entry is a file
                if (entry.Type == 0x11)
                {
                    // Offset of the file's data
                    writer.Write((int)exportFileData.Count);

                    writer.Write(entry.Data.Length);

                    // Add data to the running list of file data
                    exportFileData.AddRange(entry.Data);
                }

                writer.Write((int)0);
            }

            Pad32(writer);

            // Write string table
            writer.Write(exportStringTable.ToArray());

            Pad32(writer);

            // Write file data
            writer.Write(exportFileData.ToArray());

            Pad32(writer);

            writer.BaseStream.Position = 4;

            writer.Write((int)writer.BaseStream.Length);

            writer.BaseStream.Position = 0xC;

            writer.Write((int)alignedStringTableOffset + (((exportStringTable.Count) + 0x1F) & ~0x1F));

            return dataBuffer.ToArray();
        }

        private FileEntry[] GetDirDataRecursive(VirtualFilesystemDirectory rootDir, VirtualFilesystemDirectory parentDir)
        {
            List<FileEntry> dirFileEntries = new List<FileEntry>();

            FileEntry file;

            Node dirNode;

            // I'll admit this isn't ideal. If I'm looking at the native archives right, they tend
            // to follow the rule of "files first, directories second" when it comes to file entries.
            // Therefore, I'm going to set it up right now so that it will get files first, *then* directories.

            foreach (VirtualFilesystemNode node in rootDir.Children)
            {
                // We just need a file entry here
                if (node.Type == NodeType.File)
                {
                    VirtualFilesystemFile virtFile = node as VirtualFilesystemFile;

                    file = new FileEntry
                    {
                        ID = (ushort)exportFileEntries.Count,
                        NameHashcode = HashName(virtFile.Name + virtFile.Extension),
                        Type = 0x11,
                        Padding = 0,
                        Name = virtFile.Name + virtFile.Extension,
                        Data = virtFile.File.GetData(),
                        ZeroPadding = 0
                    };

                    dirFileEntries.Add(file);
                }
            }

            foreach (VirtualFilesystemNode node in rootDir.Children)
            {
                // We need a node and a file entry here
                if (node.Type == NodeType.Directory)
                {
                    VirtualFilesystemDirectory virtDir = node as VirtualFilesystemDirectory;

                    dirNode = new Node
                    {
                        Type = virtDir.Name.Substring(0, 3).ToUpper() + " ",
                        Name = virtDir.Name,
                        NameHashcode = HashName(virtDir.Name),
                        FirstFileOffset = (uint)exportFileEntries.Count
                    };

                    exportNodes.Add(dirNode);

                    file = new FileEntry
                    {
                        ID = ushort.MaxValue,
                        NameHashcode = HashName(virtDir.Name),
                        Type = 0x02,
                        Padding = 0,
                        Name = virtDir.Name,
                        Data = new byte[] { (byte)(exportNodes.IndexOf(exportNodes.Find(i => i.Name == virtDir.Name))) },
                        ZeroPadding = 0
                    };

                    dirFileEntries.Add(file);
                }
            }

            exportFileEntries.AddRange(dirFileEntries.ToArray());

            InsertDirOperatorEntries(rootDir, parentDir);

            // The recursive part. One more foreach!

            foreach (VirtualFilesystemNode node in rootDir.Children)
            {
                if (node.Type == NodeType.Directory)
                {
                    VirtualFilesystemDirectory dir = node as VirtualFilesystemDirectory;

                    Node tempNode = exportNodes.Find(i => i.Name == node.Name);

                    tempNode.Entries = GetDirDataRecursive(dir, rootDir);
                }
            }

            return dirFileEntries.ToArray();
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

        private ushort HashName(string name)
        {
            short hash = 0;

            short multiplier = 1;

            if (name.Length + 1 == 2)
            {
                multiplier = 2;
            }

            if (name.Length + 1 >= 3)
            {
                multiplier = 3;
            }

            foreach (char c in name)
            {
                hash = (short)(hash * multiplier);
                hash += (short)c;
            }

            return (ushort)hash;
        }

        private void Pad32(EndianBinaryWriter writer)
        {
            // Pad up to a 32 byte alignment
            // Formula: (x + (n-1)) & ~(n-1)
            long nextAligned = (writer.BaseStream.Length + 0x1F) & ~0x1F;

            long delta = nextAligned - writer.BaseStream.Length;
            writer.BaseStream.Position = writer.BaseStream.Length;
            writer.Write(new byte[delta]);
        }

        private void InsertDirOperatorEntries(VirtualFilesystemDirectory currentDir, VirtualFilesystemDirectory parentDir)
        {
            FileEntry dot1;

            FileEntry dot2;

            // Working dir reference
            dot1 = new FileEntry
            {
                ID = ushort.MaxValue,
                NameHashcode = HashName("."),
                Type = 0x02,
                Padding = 0,
                Name = ".",
                Data = new byte[] { (byte)(exportNodes.IndexOf(exportNodes.Find(i => i.Name == currentDir.Name))) },
                ZeroPadding = 0
            };

            if (parentDir != null)
            {
                // Parent dir reference. This isn't the root, so we get the parent
                dot2 = new FileEntry
                {
                    ID = ushort.MaxValue,
                    NameHashcode = HashName(".."),
                    Type = 0x02,
                    Padding = 0,
                    Name = "..",
                    Data = new byte[] { (byte)(exportNodes.IndexOf(exportNodes.Find(i => i.Name == parentDir.Name))) },
                    ZeroPadding = 0
                };
            }

            else
            {
                // Parent dir reference. This IS the root, so we say the parent dir is null
                dot2 = new FileEntry
                {
                    ID = ushort.MaxValue,
                    NameHashcode = HashName(".."),
                    Type = 0x02,
                    Padding = 0,
                    Name = "..",
                    Data = new byte[] { (byte)(255) },
                    ZeroPadding = 0
                };
            }

            exportFileEntries.Add(dot1);

            exportFileEntries.Add(dot2);
        }
    }
}
