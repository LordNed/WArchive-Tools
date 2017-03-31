using GameFormatReader.Common;
using System.Collections.Generic;
using System.IO;
using WArchiveTools.FileSystem;
using WArchiveTools.ISOs;

namespace WArchiveTools
{
    public static class ISOUtilities
    {
        /// <summary>
        /// Returns the root of the given ISO file in the form of a VirtualFilesystemDirectory.
        /// </summary>
        /// <param name="filePath">Path to the ISO file</param>
        /// <returns></returns>
        public static VirtualFilesystemDirectory LoadISO(string filePath)
        {
            VirtualFilesystemDirectory rootDir;

            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                EndianBinaryReader reader = new EndianBinaryReader(stream, Endian.Big);
                ISO iso = new ISO();
                rootDir = iso.LoadISO(reader);
            }

            return rootDir;
        }

        /// <summary>
        /// Dumps an ISO's contents from the specified root to the provided output path.
        /// </summary>
        /// <param name="root">ISO to dump</param>
        /// <param name="outputPath">Path to dump to</param>
        public static void DumpISOContents(VirtualFilesystemDirectory root, string outputPath)
        {
            ISO iso = new ISO();
            iso.DumpToDisk(root, outputPath);
        }

        /// <summary>
        /// Dumps an ISO's contents from the specified ISO file to the provided output path.
        /// </summary>
        /// <param name="inputPath">Path of the ISO to dump</param>
        /// <param name="outputPath">Path to dump to</param>
        public static void DumpISOContents(string inputPath, string outputPath)
        {
            ISO iso = new ISO();

            using (FileStream stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
            {
                EndianBinaryReader reader = new EndianBinaryReader(stream, Endian.Big);
                iso.DumpToDisk(iso.LoadISO(reader), outputPath);
            }
        }

        /// <summary>
        /// Outputs an ISO file containing the specified root to the provided output path.
        /// </summary>
        /// <param name="root">Directory to output to ISO</param>
        /// <param name="outputPath">Path to output ISO to</param>
        public static void DumpToISO(VirtualFilesystemDirectory root, string outputPath)
        {
            ISO iso = new ISO();
            iso.WriteISO(root, outputPath);
        }

        /// <summary>
        /// Returns the directory at the provided path, if it exists. If it doesn't, it will return null.
        /// </summary>
        /// <param name="dirPath">Path of the directory to serach for</param>
        /// <returns></returns>
        public static VirtualFilesystemDirectory FindDirectory(VirtualFilesystemDirectory root, string dirPath)
        {
            VirtualFilesystemDirectory result = root;
            VirtualFilesystemDirectory currentDir = root;
            List<string> dividedPath = new List<string>(dirPath.ToLower().Split('\\'));
            // This will remember where we are in dividePath above
            int pathIndex = 0;

            // We're going to run through the current root's children and compare them to dividedPath[pathIndex].
            // If their names are the same and the child is a directory, we will move that directory to result
            // and restart the loop, moving on to the next element in dividedPath.
            while (pathIndex < dividedPath.Count)
            {
                for (int i = 0; i < result.Children.Count; i++)
                {
                    if (currentDir.Children[i].Name.ToLower() == dividedPath[pathIndex] && currentDir.Children[i].Type == NodeType.Directory)
                    {
                        currentDir = currentDir.Children[i] as VirtualFilesystemDirectory;
                        break;
                    }
                }

                // If the current dir is the same as the result, that means we didn't find the next dir in the list.
                // In turn, that means the dir we're looking for doesn't exist.
                if (currentDir == result)
                    return null;
                // Otherwise it does exist, so we set result to the current dir to set up the next check.
                else
                    result = currentDir;

                pathIndex++;
            }

            return result;
        }

        /// <summary>
        /// Returns the file at the given path, if it exists. If the file does not exist, it will return null.
        /// </summary>
        /// <param name="root">ISO to search</param>
        /// <param name="filePath">File path to search for</param>
        /// <returns></returns>
        public static VirtualFilesystemFile FindFile(VirtualFilesystemDirectory root, string filePath)
        {
            VirtualFilesystemFile result = null;
            VirtualFilesystemDirectory curDir = root;

            List<string> dividedPath = new List<string>(filePath.ToLower().Split('\\'));
            int pathIndex = 0;

            // For each element of the filepath, we'll look at the current directory's children,
            // starting with the root, and see if the name matches.
            // If we find a file, we'll check to see if its name and extension match the current
            // filepath element. If it does, we can break the loop and leave.
            // If not, we check if it's a directory instead, and if it is, we set curDir to it.
            while (pathIndex < dividedPath.Count)
            {
                for (int i = 0; i < curDir.Children.Count; i++)
                {
                    if (curDir.Children[i].Type == NodeType.File)
                    {
                        VirtualFilesystemFile cand = curDir.Children[i] as VirtualFilesystemFile;
                        string fileNameWithExtension = cand.Name.ToLower() + cand.Extension.ToLower();

                        if (fileNameWithExtension == dividedPath[pathIndex])
                        {
                            result = cand;
                            break;
                        }
                    }
                    else if (curDir.Children[i].Name.ToLower() == dividedPath[pathIndex])
                    {
                        if (curDir.Children[i].Type == NodeType.Directory)
                            curDir = curDir.Children[i] as VirtualFilesystemDirectory;
                        break;
                    }
                }

                pathIndex++;
            }

            return result;
        }
    }
}
