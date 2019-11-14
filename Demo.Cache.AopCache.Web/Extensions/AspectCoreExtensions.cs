using AspectCore.Configuration;
using AspectCore.Extensions.DependencyInjection;
using Demo.Cache.AopCache.AspectCore.Interceptors;
using Demo.Cache.AopCache.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Demo.Cache.AopCache.Web.Extensions
{
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
}
