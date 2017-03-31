using GameFormatReader.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace WArchiveTools.FileSystem
{
    public enum NodeType
    {
        None,
        Directory,
        File
    }

    public abstract class VirtualFilesystemNode : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// <see cref="NodeType"/> of this node. This allows you to tell if the node is a <see cref="VirtualFilesystemDirectory"/> or a <see cref="VirtualFilesystemFile"/>.
        /// </summary>
        public NodeType Type
        {
            get { return m_type; }
            protected set { m_type = value; }
        }

        /// <summary>
        /// The string name of the node. If a <see cref="VirtualFilesystemDirectory"/> the name is the name of the directory. If a <see cref="VirtualFilesystemFile"/> then it is the name of the file.
        /// </summary>
        public string Name
        {
            get { return m_name; }
            set
            {
                m_name = value;
                OnPropertyChanged("Name");
            }
        }

        /// <summary>
        /// The ID of the node. If a <see cref="VirtualFilesystemDirectory"/> the ID is -1. If a <see cref="VirtualFilesystemFile"/> then it is a non-negative value.  
        /// </summary>
        public ushort NodeID
        {
            get { return m_nodeID; }
            set
            {
                m_nodeID = value;
                OnPropertyChanged("NodeID");
            }
        }

        private NodeType m_type;
        private string m_name;
        private ushort m_nodeID;

        protected VirtualFilesystemNode(NodeType type, string name)
        {
            Type = type;
            Name = name;
        }

        protected void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class VirtualFilesystemDirectory : VirtualFilesystemNode
    {
        public BindingList<VirtualFilesystemNode> Children { get; private set; }

        /// <summary>
        /// Represents a virtual directory which can have any number of children 
        /// who are either more <see cref="VirtualFilesystemDirectory"/>s or <see cref="VirtualFilesystemFile"/>s.
        /// </summary>
        /// <param name="dirName">Name of the directory.</param>
        public VirtualFilesystemDirectory(string dirName) : base(NodeType.Directory, dirName)
        {
            Children = new BindingList<VirtualFilesystemNode>();
        }

        /// <summary>
        /// Returns a list of files with the given extension, or an empty list if no files are found. Searches
        /// all child directories recursively to look for files.
        /// </summary>
        /// <param name="extension">File extension (including period, ie: ".arc")</param>
        /// <returns>List of files matching that extension</returns>
        public List<VirtualFilesystemFile> FindByExtension(params string[] extensions)
        {
            if (extensions.Length == 0)
                throw new ArgumentException("You must specify at least one extension", "extensions");

            List<VirtualFilesystemFile> validFiles = new List<VirtualFilesystemFile>();

            foreach (var child in Children)
            {
                if (child.Type == NodeType.File)
                {
                    VirtualFilesystemFile file = (VirtualFilesystemFile)child;
                    for (int i = 0; i < extensions.Length; i++)
                    {
                        string extension = extensions[i];
                        if (string.Compare(file.Extension, extension, System.StringComparison.InvariantCultureIgnoreCase) == 0)
                        {
                            validFiles.Add(file);
                            break;
                        }
                    }
                }
                else if (child.Type == NodeType.Directory)
                {
                    VirtualFilesystemDirectory dir = (VirtualFilesystemDirectory)child;
                    validFiles.AddRange(dir.FindByExtension(extensions));
                }
            }

            return validFiles;
        }

        public VirtualFilesystemFile GetFileAtPath(string relativePath)
        {
            // Search for the next directory indicator (Either '/' or '\') to see if we need to navigate into a subfolder.
            int dirIndicator = relativePath.IndexOf("/");
            if (dirIndicator < 0)
                dirIndicator = relativePath.IndexOf("\\");

            // There is a sub-folder to navigate into. Search our children for this folder.
            if(dirIndicator >= 0)
            {
                string folderName = relativePath.Substring(0, dirIndicator);
                foreach(var child in Children)
                {
                    if (child.Type == NodeType.File)
                        continue;

                    if(child.Name == folderName)
                    {
                        return ((VirtualFilesystemDirectory)child).GetFileAtPath(relativePath.Substring(dirIndicator+1));
                    }
                }

                // They requested a directory which doesn't exist as a child of this current directory. Thus, our search ends here.
                return null;
            }
            else
            {
                // We're looking for children.
                foreach(var child in Children)
                {
                    if (child.Type == NodeType.Directory)
                        continue;

                    VirtualFilesystemFile file = (VirtualFilesystemFile)child;
                    if (file.NameWithExtension == relativePath)
                        return file;
                }

                // They requested a file which doesn't exist as a child of this current directory.
                return null;
            }
        }

        /// <summary>
        /// Returns the file whose node has the given NodeID, or null if there is no such file. Searches
        /// all child directories recursively to look for the file.
        /// </summary>
        /// <param name="id">NodeID</param>
        /// <returns>File with a matching NodeID</returns>
        public VirtualFilesystemFile FindByID(ushort id)
        {
            VirtualFilesystemFile foundFile = null;

            foreach (VirtualFilesystemNode child in Children)
            {
                if (child.Type == NodeType.File)
                {
                    if (child.NodeID == id)
                        foundFile = child as VirtualFilesystemFile;
                }
                else if (child.Type == NodeType.Directory)
                {
                    VirtualFilesystemDirectory dir = (VirtualFilesystemDirectory)child;
                    foundFile = dir.FindByID(id);
                }
            }

            return foundFile;
        }

        /// <summary>
        /// Recursively iterates through the Virtual Filesystem directory and creates directories on disk for the
        /// child directories and writes <see cref="VirtualFilesystemFile"/>'s to disk in their appropriate location.
        /// </summary>
        /// <param name="folder">Path to place this directory (and its children) into.</param>
        public void ExportToDisk(string folder)
        {
            ExportToDiskRecursive(folder, this);
        }

        /// <summary>
        /// Recursively iterates through the filesystem directory and creates a <see cref="VirtualFilesystemDirectory"/> 
        /// replicate, including <see cref="VirtualFilesystemFile"/>s.
        /// </summary>
        /// <param name="folder"></param>
        public void ImportFromDisk(string folder)
        {
            ImportFromDiskRecursive(folder, this);
        }

        private void ExportToDiskRecursive(string folder, VirtualFilesystemDirectory dir)
        {
            // Create the directory that this node represents.
            // If it's a directory, append the directory name to the folder and onwards!
            folder = string.Format("{0}{1}/", folder, dir.Name);
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Caught exception while trying to create folder {0}: {1}", folder, ex.ToString());
            }

            foreach (var node in dir.Children)
            {
                VirtualFilesystemDirectory vfDir = node as VirtualFilesystemDirectory;
                VirtualFilesystemFile vfFile = node as VirtualFilesystemFile;

                if (vfDir != null)
                {
                    ExportToDiskRecursive(folder, vfDir);
                }
                else if (vfFile != null)
                {
                    // However, if it's a file we're going to write it to disk.
                    string filePath = string.Format("{0}{1}{2}", folder, vfFile.Name, vfFile.Extension);
                    try
                    {
                        using (EndianBinaryWriter writer = new EndianBinaryWriter(File.Create(filePath), Endian.Big))
                        {
                            writer.Write(vfFile.Data);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Caught exception while trying to write file {0}: {1}", filePath, ex.ToString());
                    }
                }
            }
        }

        private void ImportFromDiskRecursive(string folder, VirtualFilesystemDirectory dir)
        {
            if (!Directory.Exists(folder))
                throw new ArgumentException("You must specify a directory that exists", "folder");

            // For each directory that is a child of the specified folder into ourselves, and then import the contents.
            DirectoryInfo dirInfo = new DirectoryInfo(folder);
            foreach(var diskDir in dirInfo.GetDirectories())
            {
                VirtualFilesystemDirectory vfDir = new VirtualFilesystemDirectory(diskDir.Name);
                dir.Children.Add(vfDir);

                ImportFromDiskRecursive(diskDir.FullName, vfDir);
            }

            foreach(var diskFile in dirInfo.GetFiles())
            {
                using(BinaryReader reader = new BinaryReader(File.Open(diskFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    string fileName = Path.GetFileNameWithoutExtension(diskFile.Name);
                    string fileExt = Path.GetExtension(diskFile.Name);

                    byte[] data = reader.ReadBytes((int)reader.BaseStream.Length);
                    VirtualFilesystemFile vfFile = new VirtualFilesystemFile(fileName, fileExt, data);
                    dir.Children.Add(vfFile);
                }
            }
        }

        public override string ToString()
        {
            return string.Format("[Dir] {0}", Name);
        }
    }

    public sealed class VirtualFilesystemFile : VirtualFilesystemNode
    {
        /// <summary>
        /// Contents of the file that this <see cref="VirtualFilesystemFile"/> represents.
        /// </summary>
        public byte[] Data;

        /// <summary>
        /// Extension of the file including the period.
        /// </summary>
        public string Extension
        {
            get { return m_extension; }
            private set
            {
                m_extension = value;
                OnPropertyChanged("Extension");
            }
        }

        public string NameWithExtension
        {
            get { return string.Format("{0}{1}", Name, Extension); }
        }

        private string m_extension;

        /// <summary>
        /// Represents a file inside of a <see cref="VirtualFilesystemDirectory"/>. Stores the file data in memory, as well as the file name/extension.
        /// </summary>
        /// <param name="name">Name of the file (without extension)</param>
        /// <param name="extension">Extension of the file (including period)</param>
        /// <param name="data">Contents of the file for this node to store.</param>
        public VirtualFilesystemFile(string name, string extension, byte[] data) : base (NodeType.File, name)
        {
            Extension = extension;
            Data = data;
        }

        public override string ToString()
        {
            return string.Format("[File] {0}{1}", Name, Extension);
        }
    }
}
