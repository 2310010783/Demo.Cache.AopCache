using System;
using System.Collections.Generic;
using System.Text;

namespace Demo.Cache.AopCache.AspectCore.Attributes
{
	public class CacheAbleAttribute : Attribute
	{
		/// <summary>
		/// 过期时间（秒）
		/// </summary>
		public int Expiration { get; set; } = 300;

		/// <summary>
		/// Key值前缀
		/// </summary>
		public string CacheKeyPrefix { get; set; } = string.Empty;

		/// <summary>
		/// 是否高可用（异常时执行原方法）
		/// </summary>
		public bool IsHighAvailability { get; set; } = true;

		/// <summary>
		/// 只允许一个线程更新缓存（带锁）
		/// </summary>
		public bool OnceUpdate { get; set; } = false;
	}
}
