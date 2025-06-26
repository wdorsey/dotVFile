using BlobVFS;

(var name, var ext) = Util.FileNameAndExtension("file.json");

Console.WriteLine($"{name}{ext}");
