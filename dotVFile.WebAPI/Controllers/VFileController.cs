using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace dotVFile.WebAPI.Controllers
{
	[ApiController]
	[Route("[controller]", Name = "[controller]_[action]")]
	public class VFileController : ControllerBase
	{
		[HttpPost]
		public IEnumerable<VDirectory> GetDirectories(string vfilePath, string directory)
		{
			var vfile = GetVFile(vfilePath);

			return vfile.GetDirectories(new(directory), false);
		}

		private static readonly ConcurrentDictionary<string, VFile> _VFileCache = [];
		private static VFile GetVFile(string vfilePath)
		{
			if (_VFileCache.TryGetValue(vfilePath, out var vfile))
				return vfile;

			vfile = new VFile(VFileOptions.FromDatabaseFilePath(vfilePath, null));

			if (vfile == null) throw new Exception("null vfile");

			_VFileCache.AddOrUpdate(vfilePath, vfile, (_, __) => vfile);

			return vfile;
		}
	}
}
