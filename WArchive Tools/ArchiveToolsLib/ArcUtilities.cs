using GameFormatReader.Common;
using System;
using System.IO;
using WArchiveTools.Archives;
using WArchiveTools.Compression;
using WArchiveTools.FileSystem;

namespace WArchiveTools
{
    public enum ArchiveCompression
    {
        Yay0, // Yay0 compression used by early Nintendo games.
        Yaz0, // The more common Yaz0 used by later Nintendo games.
        Uncompressed // No compression.
    }

    public static class ArchiveUtilities
    {
        /// <summary>
        /// Loads an archive into a <see cref="VirtualFilesystemDirectory"/>, automatically de-compressing the archive if required.
        /// 
        /// </summary>
        /// <param name="filePath">Filepath of file to (optionally) decompress and load.</param>
        /// <returns><see cref="VirtualFilesystemDirectory"/> containing the contents, or null if filepath is not a valid archive.</returns>
        public static VirtualFilesystemDirectory LoadArchive(string filePath)
        {
            if(string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException("filePath", "Cannot load archive from empty file path!");

            if (!File.Exists(filePath))
                throw new ArgumentException("Cannot load archive from non-existant file!", "filePath");

            MemoryStream decompressedFile = null;
            using (EndianBinaryReader fileReader = new EndianBinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Endian.Big))
            {
                // Read the first 4 bytes to see if it's a compressed file (Yaz0) or a plain RARC file.
                uint fileMagic = fileReader.ReadUInt32();
                fileReader.BaseStream.Position = 0L; // Reset to the start so that the next thing to read it is at the start like it expects.

                switch(fileMagic)
                {
                    case 0x59617A30: // Yaz0 Compression
                        decompressedFile = Yaz0.Decode(fileReader);
                        break;

                    case 0x59617930: // Yay0 Compression
                        decompressedFile = Yay0.Decode(fileReader);
                        break;

                    case 0x52415243: // RARC - Uncompressed
                        decompressedFile = new MemoryStream((int)fileReader.BaseStream.Length);
                        fileReader.BaseStream.CopyTo(decompressedFile);

                        // Copying modifies the decompressedFile's read head (places it at new location) so we rewind.
                        decompressedFile.Position = 0L;
                        break;
                    default:
                        throw new NotImplementedException(string.Format("Unknown magic: {0}. If this is a Nintendo archive, open an Issue on GitHub!", fileMagic.ToString("X8")));
                }
            }

            // Not an archive we know how to handle.
            if (decompressedFile == null)
                return null;

            // Decompress the archive into the folder. It'll generate a sub-folder with the Archive's ROOT name.
            Archive rarc = new Archive();
            using (EndianBinaryReader reader = new EndianBinaryReader(decompressedFile, Endian.Big))
            {
                return rarc.ReadFile(reader);
            }
        }

        /// <summary>
        /// Creates an archive out of the specified <see cref="VirtualFilesystemDirectory"/>, optionally compressing the resulting file.
        /// </summary>
        /// <param name="outputPath">Filepath to which to write the file to.</param>
        /// <param name="root"><see cref="VirtualFilesystemDirectory"/> to create an archive out of.</param>
        /// <param name="compression">Optionally compress with Yaz0 or Yay0 compression.</param>
        public static void WriteArchive(string outputPath, VirtualFilesystemDirectory root, ArchiveCompression compression = ArchiveCompression.Uncompressed)
        {
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentNullException("filePath", "Cannot write archive to empty file path!");

            if (root == null)
                throw new ArgumentNullException("root", "Cannot write null VirtualFilesystemDirectory to archive.");

            Archive rarc = new Archive();
            MemoryStream outputData = new MemoryStream();

            // Create an archive structure from the given root and write it to file. Compression will be applied if specified.
            MemoryStream uncompressedStream = new MemoryStream();
            using (EndianBinaryWriter fileWriter = new EndianBinaryWriter(uncompressedStream, Endian.Big))
            {
                byte[] rawData = rarc.WriteFile(root);

                fileWriter.Write(rawData);
                fileWriter.Seek(0, SeekOrigin.Begin);
                fileWriter.BaseStream.CopyTo(outputData);
            }

            MemoryStream compressedStream = new MemoryStream();

            switch(compression)
            {
                case ArchiveCompression.Yay0:
                    throw new NotImplementedException("Yay0 Compression not implemented.");
                    //compressedStream = Yay0.Encode(uncompressedStream);
                    //break;

                case ArchiveCompression.Yaz0:
                    EndianBinaryWriter encoded = Yaz0.Encode(uncompressedStream);
                    encoded.Seek(0, SeekOrigin.Begin);
                    encoded.BaseStream.CopyTo(compressedStream);
                    break;

                case ArchiveCompression.Uncompressed:

                    // Well, that was easy.
                    compressedStream = uncompressedStream;
                    break;
            }

            compressedStream.Seek(0, SeekOrigin.Begin);
            compressedStream.WriteTo(File.Create(outputPath));
        }
    }
}
