using AspectCore.DynamicProxy;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace Demo.Cache.AopCache.AspectCore.Extensions
{
	public static class AspectContextExtension
	{
		private static readonly ConcurrentDictionary<MethodInfo, object[]>
					MethodAttributes = new ConcurrentDictionary<MethodInfo, object[]>();

		public static Type GetReturnType(this AspectContext context)
		{
			return context.IsAsync()
					? context.ServiceMethod.ReturnType.GetGenericArguments().First()
					: context.ServiceMethod.ReturnType;
		}

		public static T GetAttribute<T>(this AspectContext context) where T : Attribute
		{
			MethodInfo method = context.ServiceMethod;
			var attributes = MethodAttributes.GetOrAdd(method, method.GetCustomAttributes(true));
			var attribute = attributes.FirstOrDefault(x => typeof(T).IsAssignableFrom(x.GetType()));
			if (attribute is T)
			{
				return (T)attribute;
			}
			return null;
		}
	}
}
