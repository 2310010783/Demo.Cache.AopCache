using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Demo.Cache.AopCache.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Cache.AopCache.Web.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	public class HomeController : ControllerBase
	{
		private readonly DemoService _demoService;

		public HomeController(DemoService demoService)
		{
			_demoService = demoService;
		}

		public async Task<string> Get()
		{
			var model = await _demoService.GetTime();
			return $"{model.Id}:{model.Time}";
		}
	}
}