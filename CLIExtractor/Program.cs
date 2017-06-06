using GameFormatReader.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WArchiveTools.Archives;
using WArchiveTools.Compression;
using WArchiveTools.FileSystem;

namespace CLIExtractor
{
    class Program
    {
        private static bool m_wasError;
        private static bool m_verboseOutput;
        private static bool m_printFS;
        private static bool m_useInternalNames;

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("===== RARC Extractor =====");
                Console.WriteLine("        Written by        ");
                Console.WriteLine("Lord Ned & Sage of Mirrors");
                Console.WriteLine("  yaz0 decoder by thakis  ");
                Console.WriteLine(" yaz0 encoder by shevious ");
                Console.WriteLine(" RARC loading by Lioncash ");
                Console.WriteLine("   Built on the backs of  ");
                Console.WriteLine(" those who come before us ");
                Console.WriteLine("==========================");

                Console.WriteLine("usage: WArcExtract.exe <list of archives or folders separated by space>");
                Console.WriteLine("arguments: -help, -verbose -printFS -useInternalNames");
                Console.WriteLine("Press any key to continue.");
                Console.ReadKey();
                return;
            }

            bool displayedHelp = ProcessArguments(args);

            if (displayedHelp)
            {
                Console.ReadKey();
                return;
            }

            string rootFolder = GetRootFolderForArguments(args);
            rootFolder += "/extracted_archives/";
            Directory.CreateDirectory(rootFolder);

            // Users can drop in either archives or folders (which presumably contain archives). For each given
            // argument on the command line, determine if it's a directory, or a file and handle appropriately.
            foreach (string arg in args)
            {
                if (arg.StartsWith("-"))
                    continue;

                try
                {
                    if (Directory.Exists(arg))
                    {
                        RecursivelyExtractArchivesFromDir(rootFolder, arg);
                    }
                    else if (File.Exists(arg))
                    {
                        ExtractArchive(rootFolder, arg);
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception: " + ex.ToString());
                    m_wasError = true;
                }

            }

            if (m_wasError)
            {
                Console.WriteLine("Caught exception while extracting files. See above for more information if possible.");
                Console.ReadKey();
            }

            // If we printed out the FS tree, wait for them to read it before dismissing.   
            if (m_printFS)
            {
                Console.WriteLine("Press any key to continue.");
                Console.ReadKey();
            }
        }

        private static void PrintFileSystem(VirtualFilesystemDirectory root)
        {
            Console.WriteLine("Archive Filesystem:");
            Console.WriteLine(string.Format("{0} (Root)", root.Name));
            RecursivePrintFS(1, root);
        }

        private static void RecursivePrintFS(int indentCount, VirtualFilesystemDirectory dir)
        {
            foreach (var node in dir.Children)
            {
                string indentStr = string.Empty;

                if (node.Type == NodeType.File)
                {
                    for (int i = 0; i <= indentCount; i++)
                        indentStr += " ";
                    indentStr += "-";

                    Console.WriteLine("{0}{1}{2}", indentStr, node.Name, (node as VirtualFilesystemFile).Extension);
                }
                else
                {
                    for (int i = 0; i < indentCount; i++)
                        indentStr += " ";
                    indentStr += "=";

                    Console.WriteLine("{0}{1}", indentStr, node.Name);
                    RecursivePrintFS(indentCount + 1, node as VirtualFilesystemDirectory);
                }
            }
        }

        private static void ExtractArchive(string outputFolder, string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine("Warning: Tried to extract archive from filePath \"{0}\" but not a file!", filePath);
                return;
            }

            if (m_verboseOutput)
                Console.Write("Extracting archive {0}... ", Path.GetFileName(filePath));

            try
            {
                MemoryStream decompressedFile = null;
                using (EndianBinaryReader fileReader = new EndianBinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Endian.Big))
                {
                    // Read the first 4 bytes to see if it's a compressed file (Yaz0) or a plain RARC file.
                    uint fileMagic = fileReader.ReadUInt32();
                    fileReader.BaseStream.Position = 0L; // Reset to the start so that the next thing to read it is at the start like it expects.

                    if (fileMagic == 0x59617A30) // Yaz0
                    {
                        if (m_verboseOutput)
                            Console.Write("Archive compressed with Yaz0, decompressing... ");

                        decompressedFile = Yaz0.Decode(fileReader);
                    }
                    if (fileMagic == 0x59617930) // Yay0
                    {
                        if (m_verboseOutput)
                            Console.Write("Archive compressed with Yay0, decompressing... ");

                        decompressedFile = Yay0.Decode(fileReader);
                    }
                    else if (fileMagic == 0x52415243) // RARC
                    {
                        // Copy the fileReader stream to a new memorystream.
                        decompressedFile = new MemoryStream((int)fileReader.BaseStream.Length);
                        fileReader.BaseStream.CopyTo(decompressedFile);
                        decompressedFile.Position = 0L;
                    }
                }

                if (decompressedFile == null)
                {
                    if (m_verboseOutput)
                        Console.WriteLine("Skipping archive, not a Yaz0 or RARC file.");
                    return;
                }

                // Decompress the archive into the folder. It'll generate a sub-folder with the Archive's ROOT name.
                Archive rarc = new Archive();
                using (EndianBinaryReader reader = new EndianBinaryReader(decompressedFile, Endian.Big))
                {
                    VirtualFilesystemDirectory root = rarc.ReadFile(reader);
                    if (m_printFS)
                        PrintFileSystem(root);

                    // Many archives use the same internal root name, which causes a conflict when they export.
                    // To solve this, we use the file name of the file as the root name, instead of the internal
                    // name.
                    if (!m_useInternalNames)
                        root.Name = Path.GetFileNameWithoutExtension(filePath);

                    // Write it to disk.
                    root.ExportToDisk(outputFolder);
                }

                if (m_verboseOutput)
                    Console.WriteLine("Completed.");

            }
            catch (Exception ex)
            {
                Console.WriteLine("Caught Exception: " + ex.ToString());
                m_wasError = true;
            }
        }

        private static void RecursivelyExtractArchivesFromDir(string outputFolder, string dir)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(dir);

            // Replicate our current directory into the output folder.
            outputFolder += dirInfo.Name + "/";
            foreach (var subDir in dirInfo.GetDirectories())
            {
                RecursivelyExtractArchivesFromDir(outputFolder, subDir.FullName);
            }

            foreach (var subFile in dirInfo.GetFiles())
            {
                ExtractArchive(outputFolder, subFile.FullName);
            }
        }

        private static string GetRootFolderForArguments(string[] args)
        {
            // Get a list of the arguments without exta parameters.
            List<string> argList = new List<string>(args);
            argList.RemoveAll(x => x.StartsWith("-"));

            if (argList.Count == 0)
                return string.Empty;

            // Fix up slashes to always use the same separator.
            for (int i = 0; i < argList.Count; i++)
            {
                argList[i] = argList[i].Replace("\\", "/");
            }

            string commonPath = FindCommonPath("/", argList);

            // If it's the same path as any of our actual arguments, we need to back up one folder
            // since it's trying to extract them to inside of one of the specified folders.
            bool isArgument = false;
            for (int i = 0; i < argList.Count; i++)
            {
                if (string.Compare(commonPath, argList[i], StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    isArgument = true;
                    break;
                }
            }

            // Alternatively, if argList only has one entry and its a file, commonPath points to that file and not to the folder its in.
            if (isArgument || File.Exists(commonPath))
                commonPath = commonPath.Substring(0, commonPath.LastIndexOf("/"));

            return commonPath;
        }

        private static bool ProcessArguments(string[] args)
        {
            List<string> argList = new List<string>(args);

            m_verboseOutput = argList.Contains("-verbose");
            m_printFS = argList.Contains("-printFS");
            m_useInternalNames = argList.Contains("-useInternalNames");

            if (argList.Contains("-help"))
            {
                Console.WriteLine("Documentation:");
                Console.WriteLine("-verbose");
                Console.WriteLine("\tDisplays verbose output and percentages of extraction. Can be slow on large numbers of files.");
                Console.WriteLine("-printFS");
                Console.WriteLine("\tPrints out the internal filesystem of the archive after extracting them.");
                Console.WriteLine("-useInternalNames");
                Console.WriteLine("\tWrite archives out to folders which use the internal root name, instead of the file name. " +
                                    "This is disabled by default as many archives have their internal name set to 'Archive' which " +
                                    "conflicts when you export multiple archives at once.");
                return true;
            }

            return false;
        }

        public static string FindCommonPath(string separator, List<string> paths)
        {
            string CommonPath = string.Empty;
            List<string> SeparatedPath = paths
                .First(str => str.Length == paths.Max(st2 => st2.Length))
                .Split(new string[] { separator }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            foreach (string PathSegment in SeparatedPath.AsEnumerable())
            {
                if (CommonPath.Length == 0 && paths.All(str => str.StartsWith(PathSegment)))
                {
                    CommonPath = PathSegment;
                }
                else if (paths.All(str => str.StartsWith(CommonPath + separator + PathSegment)))
                {
                    CommonPath += separator + PathSegment;
                }
                else
                {
                    break;
                }
            }

            return CommonPath;
        }
    }
}
