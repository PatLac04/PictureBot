// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Configuration;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Builder.Dialogs;

namespace Microsoft.PictureBot
{
    /// <summary>
    /// The Startup class configures services and the request pipeline.
    /// </summary>
    public class Startup
    {
        private ILoggerFactory _loggerFactory;
        private bool _isProduction = false;

        public Startup(IHostingEnvironment env)
        {
            _isProduction = env.IsProduction();
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();
        }

        /// <summary>
        /// Gets the configuration that represents a set of key/value application configuration properties.
        /// </summary>
        /// <value>
        /// The <see cref="IConfiguration"/> that represents a set of key/value application configuration properties.
        /// </value>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// This method gets called by the runtime. Use this method to add services to the container.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> specifies the contract for a collection of service descriptors.</param>
        /// <seealso cref="IStatePropertyAccessor{T}"/>
        /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/web-api/overview/advanced/dependency-injection"/>
        /// <seealso cref="https://docs.microsoft.com/en-us/azure/bot-service/bot-service-manage-channels?view=azure-bot-service-4.0"/>
        public void ConfigureServices(IServiceCollection services)
        {
            var secretKey = Configuration.GetSection("botFileSecret")?.Value;
            var botFilePath = Configuration.GetSection("botFilePath")?.Value;

            // Add botConfiguration singleton
            // See https://aka.ms/about-bot-file to learn more about .bot file its use and bot configuration.
            var botConfig = BotConfiguration.Load(botFilePath ?? @".\BotConfiguration.bot", secretKey);
            services.AddSingleton(sp => botConfig ?? throw new InvalidOperationException($"The .bot config file could not be loaded. ({botConfig})"));

            // Add BotServices singleton.
            // Create the connected services from .bot file.
            services.AddSingleton(sp => new BotServices(botConfig));

            // The Memory Storage used here is for local bot debugging only. When the bot
            // is restarted, everything stored in memory will be gone.
            IStorage dataStore = new MemoryStorage();

            // For production bots use the Azure Blob or
            // Azure CosmosDB storage providers. For the Azure
            // based storage providers, add the Microsoft.Bot.Builder.Azure
            // Nuget package to your solution. That package is found at:
            // https://www.nuget.org/packages/Microsoft.Bot.Builder.Azure/
            // Uncomment the following lines to use Azure Blob Storage
            // //Storage configuration name or ID from the .bot file.
            // const string StorageConfigurationId = "<STORAGE-NAME-OR-ID-FROM-BOT-FILE>";
            // var blobConfig = botConfig.FindServiceByNameOrId(StorageConfigurationId);
            // if (!(blobConfig is BlobStorageService blobStorageConfig))
            // {
            //    throw new InvalidOperationException($"The .bot file does not contain an blob storage with name '{StorageConfigurationId}'.");
            // }
            // // Default container name.
            // const string DefaultBotContainer = "botstate";
            // var storageContainer = string.IsNullOrWhiteSpace(blobStorageConfig.Container) ? DefaultBotContainer : blobStorageConfig.Container;
            // IStorage dataStore = new Microsoft.Bot.Builder.Azure.AzureBlobStorage(blobStorageConfig.ConnectionString, storageContainer);

            // Create Conversation State object.
            // The Conversation State object is where we persist anything at the conversation-scope.
            ////var conversationState = new ConversationState(dataStore);
            ////options.State.Add(conversationState);

            services.AddSingleton(dataStore);
            var userState = new UserState(dataStore);
            var conversationState = new ConversationState(dataStore);

            services.AddSingleton(userState);
            services.AddSingleton(conversationState);

            // The BotStateSet enables read() and write() in parallel on multiple BotState instances.
            services.AddSingleton(new BotStateSet(userState, conversationState));

            services.AddBot<PictureBot>(options =>
            {
                // Retrieve current endpoint.
                var environment = _isProduction ? "production" : "development";
                var service = botConfig.Services.Where(s => s.Type == "endpoint" && s.Name == environment).FirstOrDefault();
                if (!(service is EndpointService endpointService))
                {
                    throw new InvalidOperationException($"The .bot file does not contain an endpoint with name '{environment}'.");
                }

                options.CredentialProvider = new SimpleCredentialProvider(endpointService.AppId, endpointService.AppPassword);

                // Creates a logger for the application to use.
                ILogger logger = _loggerFactory.CreateLogger<PictureBot>();

                // Catches any errors that occur during a conversation turn and logs them.
                options.OnTurnError = async (context, exception) =>
                {
                    logger.LogError($"Exception caught : {exception}");
                    await context.SendActivityAsync("Sorry, it looks like something went wrong.");
                };

                var middleware = options.Middleware;
                // Add middleware below with "middleware.Add(...."

                // Typing Middleware (automatically shows typing when the bot is responding/working)
                options.Middleware.Add(new ShowTypingMiddleware());

                // Add Regex below
                middleware.Add(new RegExpRecognizerMiddleware()
                .AddIntent("search", new Regex("search picture(?:s)*(.*)|search pic(?:s)*(.*)", RegexOptions.IgnoreCase))
                .AddIntent("share", new Regex("share picture(?:s)*(.*)|share pic(?:s)*(.*)", RegexOptions.IgnoreCase))
                .AddIntent("order", new Regex("order picture(?:s)*(.*)|order print(?:s)*(.*)|order pic(?:s)*(.*)", RegexOptions.IgnoreCase))
                .AddIntent("help", new Regex("help(.*)", RegexOptions.IgnoreCase)));
            });

            // Create and register state accesssors.
            // Acessors created here are passed into the IBot-derived class on every turn.
            services.AddSingleton<PictureBotAccessors>(sp =>
            {
                // Create the custom state accessor.
                // State accessors enable other components to read and write individual properties of state.
                var accessors = new PictureBotAccessors(conversationState)
                {
                    PictureState = conversationState.CreateProperty<PictureState>(PictureBotAccessors.PictureStateName),
                    DialogStateAccessor = conversationState.CreateProperty<DialogState>("DialogState"),
                };

                return accessors;
            });
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;

            app.UseDefaultFiles()
                .UseStaticFiles()
                .UseBotFramework();
        }
    }
}