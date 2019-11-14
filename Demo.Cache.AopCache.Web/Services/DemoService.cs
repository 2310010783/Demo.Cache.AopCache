using Demo.Cache.AopCache.AspectCore.Attributes;
using Demo.Cache.AopCache.Web.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Demo.Cache.AopCache.Web.Services
{
	public class DemoService
	{
		[CacheAble(CacheKeyPrefix = "test", Expiration = 30, OnceUpdate = true)]
		public async virtual Task<DateTimeModel> GetTime()
		{
			return await Task.Run(() =>
			{
				return new DateTimeModel
				{
					Id = GetHashCode(),
					Time = DateTime.Now
				};
			});
		}
	}
}
