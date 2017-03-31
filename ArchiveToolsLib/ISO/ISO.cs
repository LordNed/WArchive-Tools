using WArchiveTools.FileSystem;

namespace WArchiveTools.ISOs
{
    public partial class ISO
    {
        public VirtualFilesystemDirectory Root { get; internal set; }

        private enum FSTNodeType
        {
            File = 0,
            Directory = 1
        }

        private struct FSTEntry
        {
            /// <summary>
            /// The node type. It's either file (0) or directory (1).
            /// </summary>
            public FSTNodeType Type;

            public string RelativeFileName;

            public int FileOffsetParentDir; //If Type is File, this is the offset of the file's data. If it's Directory, this is the index of its parent.

            public int FileSizeNextDirIndex; //If Type is File, this is the size of the file data. If it's Directory, this is the index of the next dir on the same level as itself.

            public int FileNameOffset;
        }
    }
}
