using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace dotVFile.WebAPI.Controllers
{
	[ApiController]
	[Route("[controller]/[action]", Name = "[controller]_[action]")]
	public class VFileController : ControllerBase
	{
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
		public Response<IEnumerable<VDirectory>> GetDirectories(GetDirectoryRequest request)
		{
			var (vfile, error) = GetVFile(request);

			if (vfile == null) return new(VFileError(request, error));

			return new(vfile.GetDirectories(new(request.Directory), false));
		}

		[HttpPost]
		public Response<IEnumerable<VFileInfo>> GetFiles(GetDirectoryRequest request)
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
	}
}
