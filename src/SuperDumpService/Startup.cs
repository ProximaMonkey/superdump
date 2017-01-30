﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Hangfire;
using SuperDumpService.Models;
using Microsoft.Extensions.PlatformAbstractions;
using Swashbuckle.Swagger.Model;
using Hangfire.Logging;
using Hangfire.Logging.LogProviders;
using SuperDumpService.Helpers;
using Hangfire.Dashboard;
using Hangfire.Annotations;
using Microsoft.AspNetCore.Http.Features;
using System.IO;
using Microsoft.Extensions.Options;

namespace SuperDumpService {
	public class Startup {
		public Startup(IHostingEnvironment env) {
			var builder = new ConfigurationBuilder()
				.SetBasePath(env.ContentRootPath)
				.AddJsonFile(Path.Combine(PathHelper.GetConfDirectory(), "appsettings.json"), optional: false, reloadOnChange: true)
				.AddJsonFile(Path.Combine(PathHelper.GetConfDirectory(), $"appsettings.{env.EnvironmentName}.json"), optional: true)
				.AddEnvironmentVariables();
			Configuration = builder.Build();
		}

		public IConfigurationRoot Configuration { get; }

		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services) {
			services.Configure<SuperDumpSettings>(Configuration.GetSection("SuperDumpSettings"));

			IEnumerable<int> test = new List<int>();

			//configure DB
			string connString;
			Console.WriteLine(Environment.CurrentDirectory);
			using (SqlConnection conn = LocalDBAccess.GetLocalDB("HangfireDB")) {
				connString = conn.ConnectionString;
			}
			if (string.IsNullOrEmpty(connString)) {
				throw new Exception("DB could not be created, please check if LocalDB is installed.");
			}
			services.Configure<FormOptions>(opt => opt.MultipartBodyLengthLimit = 16L * 1024 * 1024 * 1024); // 16GB

			services.AddHangfire(x => x.UseSqlServerStorage(connString));
			// Add framework services.
			services.AddMvc();
			//services.AddCors();
			services.AddSwaggerGen();

			services.ConfigureSwaggerGen(options => {
				options.SingleApiVersion(new Info {
					Version = "v1",
					Title = "SuperDump API",
					Description = "REST interface for SuperDump analysis tool",
					TermsOfService = "None",
					Contact = new Contact { Url = "https://github.com/Dynatrace/superdump" }
				});

				//Determine base path for the application.
				var basePath = PlatformServices.Default.Application.ApplicationBasePath;

				//Set the comments path for the swagger json and ui.
				options.IncludeXmlComments(basePath + "\\SuperDumpService.xml");
			});

			// register repository as singleton
			services.AddSingleton<IDumpRepository, DumpRepository>();
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IOptions<SuperDumpSettings> settings) {
			loggerFactory.AddConsole(Configuration.GetSection("Logging"));
			loggerFactory.AddDebug();

			app.UseHangfireDashboard("/hangfire", new DashboardOptions {
				Authorization = new[] { new CustomAuthorizeFilter() }
			});

			app.UseHangfireServer(new BackgroundJobServerOptions {
				Queues = new[] { "bundles" },
				WorkerCount = settings.Value.MaxConcurrentBundleExtraction
			});
			app.UseHangfireServer(new BackgroundJobServerOptions {
				Queues = new[] { "analysis" },
				WorkerCount = settings.Value.MaxConcurrentAnalysis
			});

			GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 0 });

			app.UseSwagger();
			app.UseSwaggerUi();

			LogProvider.SetCurrentLogProvider(new ColouredConsoleLogProvider());

			if (env.IsDevelopment()) {
				app.UseDeveloperExceptionPage();
				app.UseBrowserLink();
			} else {
				app.UseExceptionHandler("/Home/Error");
			}

			app.UseStaticFiles();

			app.UseMvc(routes => {
				routes.MapRoute(
					name: "default",
					template: "{controller=Home}/{action=Index}/{id?}");
			});
		}
	}

	public class CustomAuthorizeFilter : IDashboardAuthorizationFilter {
		public bool Authorize([NotNull] DashboardContext context) {
			return true; // let everyone see hangfire
		}
	}
}