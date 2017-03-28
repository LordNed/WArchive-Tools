using GameFormatReader.Common;
using System;
using System.IO;
using WArchiveTools.Compression;

namespace WArchiveTools
{
    public static class FileUtilities
    {
        /// <summary>
        /// Loads a file into a <see cref="EndianBinaryReader"/>, automatically de-compressing the file if required.
        /// Throws an exception if the filepath to the file is not valid.
        /// </summary>
        /// <param name="filePath">Filepath of file to (optionally) decompress and load.</param>
        /// <returns><see cref="EndianBinaryReader"/> containing the contents.</returns>
        public static EndianBinaryReader LoadFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException("filePath", "Cannot load archive from empty file path!");

            if (!File.Exists(filePath))
                throw new ArgumentException("Cannot load archive from non-existant file!", "filePath");

            MemoryStream decompressedFile = null;
            using (EndianBinaryReader fileReader = new EndianBinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Endian.Big))
            {
                // Read the first 4 bytes to see if it's a compressed file (Yaz0, Yay0, etc.)
                uint fileMagic = fileReader.ReadUInt32();
                fileReader.BaseStream.Position = 0L; // Reset to the start so that the next thing to read it is at the start like it expects.

                switch (fileMagic)
                {
                    case 0x59617A30: // Yaz0 Compression
                        decompressedFile = Yaz0.Decode(fileReader);
                        break;

                    case 0x59617930: // Yay0 Compression
                        decompressedFile = Yay0.Decode(fileReader);
                        break;

                    default: // Uncompressed
                        decompressedFile = new MemoryStream((int)fileReader.BaseStream.Length);
                        fileReader.BaseStream.CopyTo(decompressedFile);

                        // CopyTo modifies the decompressedFile's read head (places it at new location) so we rewind.
                        decompressedFile.Position = 0L;
                        break;
                }
            }

            // Return the decompressed file
            return new EndianBinaryReader(decompressedFile, Endian.Big);
        }

        /// <summary>
        /// Creates an new file out of the specified <see cref="EndianBinaryWriter"/>, optionally compressing the resulting file.
        /// </summary>
        /// <param name="outputPath">Filepath to which to write the file to.</param>
        /// <param name="stream"><see cref="MemoryStream"/> to create an archive out of.</param>
        /// <param name="compression">Optionally compress with Yaz0 or Yay0 compression.</param>
        public static void SaveFile(string outputPath, MemoryStream stream, ArchiveCompression compression = ArchiveCompression.Uncompressed)
        {
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentNullException("filePath", "Cannot write archive to empty file path!");

            if (stream == null)
                throw new ArgumentNullException("root", "Cannot write null EndianBinaryWriter to archive.");

            MemoryStream compressedStream = new MemoryStream();

            switch (compression)
            {
                case ArchiveCompression.Yay0:
                    throw new NotImplementedException("Yay0 Compression not implemented.");
                    //compressedStream = Yay0.Encode(uncompressedStream);
                    //break;

                case ArchiveCompression.Yaz0:
                    EndianBinaryWriter encoded = Yaz0.Encode(stream);
                    encoded.Seek(0, SeekOrigin.Begin);
                    encoded.BaseStream.CopyTo(compressedStream);
                    break;

                case ArchiveCompression.Uncompressed:

                    // Well, that was easy.
                    compressedStream = stream;
                    break;
            }

            compressedStream.Seek(0, SeekOrigin.Begin);
            using (var fileStream = File.Create(outputPath))
                compressedStream.WriteTo(fileStream);
        }
    }
}
