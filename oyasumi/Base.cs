using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using oyasumi.Database;
using oyasumi.Database.Models;
using oyasumi.Enums;
using oyasumi.Objects;

namespace oyasumi
{
	public class Base
	{
		private static Assembly Assembly;
		public static Type[] Types;

		// ConcurrentDictonary here for prevent using locks and because I'm too lazy to write them
		public static readonly ConcurrentDictionary<string, string> PasswordCache = new ConcurrentDictionary<string, string>();
		public static readonly ConcurrentDictionary<PacketType, MethodInfo> MethodCache = new ConcurrentDictionary<PacketType, MethodInfo>();
		public static readonly ConcurrentDictionary<string, User> UserCache = new ConcurrentDictionary<string, User>();
		public static readonly ConcurrentDictionary<User, (UserStats, OyasumiDbContext)> UserStatsCache = new ConcurrentDictionary<User, (UserStats, OyasumiDbContext)>();

		public static void Main(string[] args)
		{
			Assembly = Assembly.GetEntryAssembly();
			Types = Assembly.GetTypes();

			CreateHostBuilder(args).Build().Run();
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.UseStartup<Startup>();
				});
	}
}