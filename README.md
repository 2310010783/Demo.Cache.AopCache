# 使用AspectCore实现AOP模式的Redis缓存

这次的目标是实现通过标注Attribute实现缓存的功能，精简代码，减少缓存的代码侵入业务代码。

缓存内容即为Service查询汇总的内容，不做其他高大上的功能，提升短时间多次查询的响应速度，适当减轻数据库压力。

在做之前，也去看了EasyCaching的源码，这次的想法也是源于这里，AOP的方式让代码减少耦合，但是缓存策略有限。经过考虑决定，自己实现类似功能，在之后的应用中也方便对缓存策略的扩展。

本文内容也许有点不严谨的地方，仅供参考。同样欢迎各位路过的大佬提出建议。

### 在项目中加入AspectCore
之前有做AspectCore的总结，相关内容就不再赘述了。
- [ASP.NET Core 3.0 使用AspectCore-Framework实现AOP](https://www.cnblogs.com/king-23100/p/11821020.html)
- [GitHub:相关代码](https://github.com/2310010783/Demo.Aop.AspectCore)

### 在项目中加入Stackexchange.Redis
在stackexchange.Redis和CSRedis中纠结了很久，也没有一个特别的有优势，最终选择了stackexchange.Redis,没有理由。至于连接超时的问题，可以用异步解决。
- 安装Stackexchange.Redis
```shell
Install-Package StackExchange.Redis -Version 2.0.601
```
- 在appsettings.json配置Redis连接信息
```json
{
	"Redis": {
		"Default": {
			"Connection": "127.0.0.1:6379",
			"InstanceName": "RedisCache:",
			"DefaultDB": 0
		}
	}
}
```
- RedisClient

用于连接Redis服务器，包括创建连接，获取数据库等操作
```C#
public class RedisClient : IDisposable
{
	private string _connectionString;
	private string _instanceName;
	private int _defaultDB;
	private ConcurrentDictionary<string, ConnectionMultiplexer> _connections;
	public RedisClient(string connectionString, string instanceName, int defaultDB = 0)
	{
		_connectionString = connectionString;
		_instanceName = instanceName;
		_defaultDB = defaultDB;
		_connections = new ConcurrentDictionary<string, ConnectionMultiplexer>();
	}

	private ConnectionMultiplexer GetConnect()
	{
		return _connections.GetOrAdd(_instanceName, p => ConnectionMultiplexer.Connect(_connectionString));
	}

	public IDatabase GetDatabase()
	{
		return GetConnect().GetDatabase(_defaultDB);
	}

	public IServer GetServer(string configName = null, int endPointsIndex = 0)
	{
		var confOption = ConfigurationOptions.Parse(_connectionString);
		return GetConnect().GetServer(confOption.EndPoints[endPointsIndex]);
	}

	public ISubscriber GetSubscriber(string configName = null)
	{
		return GetConnect().GetSubscriber();
	}

	public void Dispose()
	{
		if (_connections != null && _connections.Count > 0)
		{
			foreach (var item in _connections.Values)
			{
				item.Close();
			}
		}
	}
}
```

- 注册服务

Redis是单线程的服务，多几个RedisClient的实例也是无济于事，所以依赖注入就采用singleton的方式。
```C#
public static class RedisExtensions
{
	public static void ConfigRedis(this IServiceCollection services, IConfiguration configuration)
	{
		var section = configuration.GetSection("Redis:Default");
		string _connectionString = section.GetSection("Connection").Value;
		string _instanceName = section.GetSection("InstanceName").Value;
		int _defaultDB = int.Parse(section.GetSection("DefaultDB").Value ?? "0");
		services.AddSingleton(new RedisClient(_connectionString, _instanceName, _defaultDB));
	}
}

public class Startup
{
	public void ConfigureServices(IServiceCollection services)
	{
		services.ConfigRedis(Configuration);
	}
}
```

- KeyGenerator

创建一个缓存Key的生成器，以Attribute中的CacheKeyPrefix作为前缀，之后可以扩展批量删除的功能。被拦截方法的方法名和入参也同样作为key的一部分，保证Key值不重复。
```C#
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
```

### 写一套缓存拦截器
- CacheAbleAttribute
```C#
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
```

- CacheAbleInterceptor
```C#
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
```

- 注册拦截器
```C#
public static class AspectCoreExtensions
{
	public static void ConfigAspectCore(this IServiceCollection services)
	{
		services.ConfigureDynamicProxy(config =>
		{
			config.Interceptors.AddTyped<CacheAbleInterceptor>(Predicates.Implement(typeof(DemoService)));
		});
		services.BuildAspectInjectorProvider();
	}
}
```

### 测试缓存功能
- 在需要缓存的接口/方法上标注Attribute
```C#
[CacheAble(CacheKeyPrefix = "test", Expiration = 30, OnceUpdate = true)]
public virtual DateTimeModel GetTime()
{
    return new DateTimeModel
	{
	    Id = GetHashCode(),
		Time = DateTime.Now
    };
}
```
- 测试结果截图

![](https://images.cnblogs.com/cnblogs_com/king-23100/1543322/o_1911140259397339ab91a4f8cfd928ed1003f323b7d.png)