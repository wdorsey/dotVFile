using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc;

namespace dotVFile.WebAPI.Controllers
{
	[ApiController]
	[Route("[controller]/[action]", Name = "[controller]_[action]")]
	public class VFileController(ILogger<VFileController> logger) : ControllerBase
	{
		private readonly ILogger _logger = logger;

		[HttpPost]
		public Response<bool> VerifyVFile(VFileRequest request)
		{
			var (vfile, error) = GetVFile(request);

			return new(vfile != null, error);
		}

		[HttpPost]
		public Response<VFileStats> GetStats(VFileRequest request)
		{
			var (vfile, error) = GetVFile(request);

			if (vfile == null) return new(VFileError(request, error));

			var stats = vfile.GetStats();

			return new(stats);
		}

		[HttpPost]
		public Response<IEnumerable<ApiVDirectory>> GetDirectories(DirectoryRequest request)
		{
			var (vfile, error) = GetVFile(request);

			if (vfile == null) return new(VFileError(request, error));

			var dirs = vfile.GetDirectories(new(request.Directory), false);

			var result = new ConcurrentBag<ApiVDirectory>();

			Parallel.ForEach(dirs, dir =>
			{
				var stats = vfile.GetDirectoryStats(dir);
				result.Add(new(dir, stats));
			});

			return new(result);
		}

		[HttpPost]
		public Response<DirectoryStats> GetDirectoryStats(DirectoryRequest request)
		{
			var (vfile, error) = GetVFile(request);

			if (vfile == null) return new(VFileError(request, error));

			var stats = vfile.GetDirectoryStats(new(request.Directory));

			return new(stats);
		}

		[HttpPost]
		public Response<IEnumerable<VFileInfo>> GetFiles(DirectoryRequest request)
		{
			var (vfile, error) = GetVFile(request);

			if (vfile == null) return new(VFileError(request, error));

			return new(vfile.Get(new(request.Directory), false));
		}

		[HttpPost]
		public Response<string> GetFileBytes(GetFileBytesRequest request)
		{
			var (vfile, error) = GetVFile(request);

			if (vfile == null) return new(VFileError(request, error));

			var bytes = vfile.GetBytes(new VFilePath(request.FilePath));

			var err = bytes == null ? new Error("NOT_FOUND", $"vfile not found at path: {request.FilePath}") : null;

			var result = bytes != null ? Convert.ToBase64String(bytes) : null;

			return new(result, err);
		}

		[HttpPost]
		public Response<ExportResponse> Export(ExportRequest request)
		{
			var (vfile, error) = GetVFile(request);

			if (vfile == null) return new(VFileError(request, error));

			var vdir = new VDirectory(request.DirectoryPath);

			var exportPath = Path.Combine(
				GetDownloadsFolder(),
				vdir.Name);

			_logger.Log(LogLevel.Information, "exportPath: {exportPath}", exportPath);

			var exported = vfile.ExportDirectory(
				vdir,
				exportPath,
				vdir,
				true);

			return new(new ExportResponse(exported));
		}

		private static Error VFileError(VFileRequest request, Error? error)
		{
			return error ?? new Error("VFILE_NOT_FOUND", $"VFile not found at path: {request.VFilePath}");
		}

		private static readonly ConcurrentDictionary<string, VFile> _VFileCache = [];
		private static (VFile? VFile, Error? Error) GetVFile(VFileRequest request)
		{
			if (_VFileCache.TryGetValue(request.VFilePath, out var vfile))
				return (vfile, null);

			try
			{
				vfile = new VFile(request.VFilePath);
			}
			catch (Exception e)
			{
				return (null, new Error("VFILE_EXCEPTION", e.Message));
			}

			_VFileCache.AddOrUpdate(request.VFilePath, vfile, (_, __) => vfile);

			return (vfile, null);
		}

		private static string GetDownloadsFolder()
		{
			var fallbackPath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
				"Downloads");

			if (Environment.OSVersion.Version.Major < 6)
				return fallbackPath;

			IntPtr pathPtr = IntPtr.Zero;
			try
			{
				// c# shgetknownfolderpath directory path
#pragma warning disable CA1806 // Do not ignore method results
				SHGetKnownFolderPath(ref folderDownloads, 0, IntPtr.Zero, out pathPtr);
#pragma warning restore CA1806 // Do not ignore method results
				var downloadsPath = Marshal.PtrToStringUni(pathPtr);
				return downloadsPath ?? fallbackPath;
			}
			finally
			{
				Marshal.FreeCoTaskMem(pathPtr);
			}
		}

		// declare DownloadsFolder GUI and import SHGetKnownFolderPath method
		static Guid folderDownloads = new("374DE290-123F-4565-9164-39C4925E467B");
		[DllImport("shell32.dll", CharSet = CharSet.Auto)]
		private static extern int SHGetKnownFolderPath(ref Guid id, int flags, IntPtr token, out IntPtr path);
	}
}
