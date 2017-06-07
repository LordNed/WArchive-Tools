using GameFormatReader.Common;
using System.Collections.Generic;
using System.IO;
using WArchiveTools.FileSystem;

namespace WArchiveTools.Archives
{
    public partial class Archive
    {
        List<Node> exportNodes;
        List<FileEntry> exportFileEntries;
        List<char> exportStringTable;
        List<byte> exportFileData;

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

                writer.Write(entry.Flags);

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
                if (entry.Flags == 0x02)
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
                if (entry.Flags == 0x11)
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
                        Flags = 0x11,
                        Name = virtFile.Name + virtFile.Extension,
                        Data = virtFile.Data
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
                        Flags = 0x02,
                        Name = virtDir.Name,
                        Data = new byte[] { (byte)(exportNodes.IndexOf(exportNodes.Find(i => i.Name == virtDir.Name))) },
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
                Flags = 0x02,
                Name = ".",
                Data = new byte[] { (byte)(exportNodes.IndexOf(exportNodes.Find(i => i.Name == currentDir.Name))) },
            };

            if (parentDir != null)
            {
                // Parent dir reference. This isn't the root, so we get the parent
                dot2 = new FileEntry
                {
                    ID = ushort.MaxValue,
                    NameHashcode = HashName(".."),
                    Flags = 0x02,
                    Name = "..",
                    Data = new byte[] { (byte)(exportNodes.IndexOf(exportNodes.Find(i => i.Name == parentDir.Name))) },
                };
            }

            else
            {
                // Parent dir reference. This IS the root, so we say the parent dir is null
                dot2 = new FileEntry
                {
                    ID = ushort.MaxValue,
                    NameHashcode = HashName(".."),
                    Flags = 0x02,
                    Name = "..",
                    Data = new byte[] { (byte)(255) },
                };
            }

            exportFileEntries.Add(dot1);

            exportFileEntries.Add(dot2);
        }
    }
}
