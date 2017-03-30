namespace WArchiveTools.Archives
{
    public partial class Archive
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
            /// <summary>File/subdirectory name string table offset.</summary>
            public string Name { get; internal set; }
            /// <summary>Data bytes. If this entry is a directory, it will be the node index.</summary>
            public byte[] Data { get; internal set; }

            /// <summary>Whether or not this entry is a directory.</summary>
            public bool IsDirectory { get { return ID == 0xFFFF; } }
            /// <summary>Node index representing the subdirectory. Will only be non-zero if IsDirectory is true.</summary>
            public uint SubDirIndex { get; internal set; }

            public override string ToString()
            {
                return Name;
            }
        }
    }
}
