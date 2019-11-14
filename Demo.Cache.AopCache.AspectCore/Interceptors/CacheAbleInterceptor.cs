using AspectCore.DynamicProxy;
using AspectCore.Injector;
using Demo.Cache.AopCache.AspectCore.Attributes;
using Demo.Cache.AopCache.AspectCore.Extensions;
using Demo.Cache.AopCache.Redis;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Demo.Cache.AopCache.AspectCore.Interceptors
{
	public class CacheAbleInterceptor : AbstractInterceptor
	{
		[FromContainer]
		private RedisClient RedisClient { get; set; }

		private IDatabase Database;

		private static readonly ConcurrentDictionary<Type, MethodInfo> TypeofTaskResultMethod = new ConcurrentDictionary<Type, MethodInfo>();

		public async override Task Invoke(AspectContext context, AspectDelegate next)
		{
			CacheAbleAttribute attribute = context.GetAttribute<CacheAbleAttribute>();

			if (attribute == null)
			{
				await context.Invoke(next);
				return;
			}

			try
			{
				Database = RedisClient.GetDatabase();

				string cacheKey = KeyGenerator.GetCacheKey(context.ServiceMethod, context.Parameters, attribute.CacheKeyPrefix);

				string cacheValue = await GetCacheAsync(cacheKey);

				Type returnType = context.GetReturnType();

				if (string.IsNullOrWhiteSpace(cacheValue))
				{
					if (attribute.OnceUpdate)
					{
						string lockKey = $"Lock_{cacheKey}";
						RedisValue token = Environment.MachineName;

						if (await Database.LockTakeAsync(lockKey, token, TimeSpan.FromSeconds(10)))
						{
							try
							{
								var result = await RunAndGetReturn(context, next);
								await SetCache(cacheKey, result, attribute.Expiration);
								return;
							}
							finally
							{
								await Database.LockReleaseAsync(lockKey, token);
							}
						}
						else
						{
							for (int i = 0; i < 5; i++)
							{
								Thread.Sleep(i * 100 + 500);
								cacheValue = await GetCacheAsync(cacheKey);
								if (!string.IsNullOrWhiteSpace(cacheValue))
								{
									break;
								}
							}
							if (string.IsNullOrWhiteSpace(cacheValue))
							{
								var defaultValue = CreateDefaultResult(returnType);
								context.ReturnValue = ResultFactory(defaultValue, returnType, context.IsAsync());
								return;
							}
						}
					}
					else
					{
						var result = await RunAndGetReturn(context, next);
						await SetCache(cacheKey, result, attribute.Expiration);
						return;
					}
				}
				var objValue = await DeserializeCache(cacheKey, cacheValue, returnType);
				//缓存值不可用
				if (objValue == null)
				{
					await context.Invoke(next);
					return;
				}

				context.ReturnValue = ResultFactory(objValue, returnType, context.IsAsync());
			}
			catch (Exception)
			{
				if (context.ReturnValue == null)
				{
					await context.Invoke(next);
				}
			}
		}

		private async Task<string> GetCacheAsync(string cacheKey)
		{
			string cacheValue = null;
			try
			{
				cacheValue = await Database.StringGetAsync(cacheKey);
			}
			catch (Exception)
			{
				return null;
			}
			return cacheValue;
		}

		private async Task<object> RunAndGetReturn(AspectContext context, AspectDelegate next)
		{
			await context.Invoke(next);
			return context.IsAsync()
			? await context.UnwrapAsyncReturnValue()
			: context.ReturnValue;
		}

		private async Task SetCache(string cacheKey, object cacheValue, int expiration)
		{
			string jsonValue = JsonConvert.SerializeObject(cacheValue);
			await Database.StringSetAsync(cacheKey, jsonValue, TimeSpan.FromSeconds(expiration));
		}

		private async Task Remove(string cacheKey)
		{
			await Database.KeyDeleteAsync(cacheKey);
		}

		private async Task<object> DeserializeCache(string cacheKey, string cacheValue, Type returnType)
		{
			try
			{
				return JsonConvert.DeserializeObject(cacheValue, returnType);
			}
			catch (Exception)
			{
				await Remove(cacheKey);
				return null;
			}
		}

		private object CreateDefaultResult(Type returnType)
		{
			return Activator.CreateInstance(returnType);
		}

		private object ResultFactory(object result, Type returnType, bool isAsync)
		{
			if (isAsync)
			{
				return TypeofTaskResultMethod
					.GetOrAdd(returnType, t => typeof(Task)
					.GetMethods()
					.First(p => p.Name == "FromResult" && p.ContainsGenericParameters)
					.MakeGenericMethod(returnType))
					.Invoke(null, new object[] { result });
			}
			else
			{
				return result;
			}
		}
	}
}