using StackExchange.Redis;
using System;
using System.Collections.Concurrent;

namespace Demo.Cache.AopCache.Redis
{
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
}
