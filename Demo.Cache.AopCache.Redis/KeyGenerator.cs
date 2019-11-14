using System;
using System.Reflection;
using System.Text;

namespace Demo.Cache.AopCache.Redis
{
	public static class KeyGenerator
	{
		public static string GetCacheKey(MethodInfo methodInfo, object[] args, string prefix)
		{
			StringBuilder cacheKey = new StringBuilder();
			cacheKey.Append($"{prefix}_");
			cacheKey.Append(methodInfo.DeclaringType.Name).Append($"_{methodInfo.Name}");
			foreach (var item in args)
			{
				cacheKey.Append($"_{item}");
			}
			return cacheKey.ToString();
		}

		public static string GetCacheKeyPrefix(MethodInfo methodInfo, string prefix)
		{
			StringBuilder cacheKey = new StringBuilder();
			cacheKey.Append(prefix);
			cacheKey.Append($"_{methodInfo.DeclaringType.Name}").Append($"_{methodInfo.Name}");
			return cacheKey.ToString();
		}
	}
}
