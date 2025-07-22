using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace dotVFile.WebAPI.Controllers
{
	[ApiController]
	[Route("[controller]/[action]", Name = "[controller]_[action]")]
	public class VFileController : ControllerBase
	{
		[HttpPost]
		public Response<IEnumerable<VDirectory>> GetDirectories(GetDirectoriesRequest request)
		{
			var vfile = GetVFile(request);

			return new(vfile.GetDirectories(new(request.Directory), false));
		}

		private static readonly ConcurrentDictionary<string, VFile> _VFileCache = [];
		private static VFile GetVFile(VFileRequest request)
		{
			if (_VFileCache.TryGetValue(request.VFilePath, out var vfile))
				return vfile;

			vfile = new VFile(request.VFilePath);

			if (vfile == null) throw new Exception("null vfile");

			_VFileCache.AddOrUpdate(request.VFilePath, vfile, (_, __) => vfile);

			return vfile;
		}
	}
}
