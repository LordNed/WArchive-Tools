# WArchive-Tools
A set of tools designed to extract and (eventually) repack archives from The Legend of Zelda: The Wind Waker (and other Nintendo games from the GCN). Mostly a port of existing compression/decompression and archive extracting tools wrapped up into one bundle for easy use.

This is a simple CLI wrapper around the archive tools library written for Wind Editor. The current release supports unpacking both compressed (yaz0) and decompressed RARC archives. Packing will come in the future once Wind Editor is at the point where it requires repacking.

usage:
Drag and drop one or more folders or files onto the exe. The exe will recursively unpack any RARC or Yaz0 encoded files within the specified folder, as well as any loose archives dropped onto it. Results will be placed in an "archives_extracted" folder located at the common root of all items dropped onto the folder.

WArcExtract.exe "C:/arc1.arc" "C:/My Archives" "C:/compressedArc.rarc"

Optional Arguments:

-help (Displays Help Text)

-verbose (Extra debug printouts/status updates of progress)

-printFS (displays a ascii tree of the archive contents (but also dumps contents))
