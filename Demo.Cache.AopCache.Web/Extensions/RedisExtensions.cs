using Demo.Cache.AopCache.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Demo.Cache.AopCache.Web.Extensions
{
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
}
