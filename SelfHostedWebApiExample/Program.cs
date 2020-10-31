using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using log4net;
using Owin;
using Topshelf;
using Topshelf.Runtime;

namespace SelfHostedWebApiExample
{

    [RoutePrefix("example")]
    public class ExampleController : ApiController
    {
        private static readonly Random Random = new Random();

        [HttpGet]
        [Route("random")]
        public int GetRandomNumber()
        {
            return Random.Next();
        }
    }

    public class WindowsServiceController : ServiceControl
    {

        private static readonly ILog Logger = LogManager.GetLogger(nameof(WindowsServiceController));

        private IDisposable _webApp;
        public WindowsServiceController(HostSettings hostSettings)
        {
        }

        public bool Start(HostControl hostControl)
        {

            //Log
            Logger.Info("*************************************");
            Logger.Info("********[Starting Service]***********");
            Logger.Info("*************************************");

            hostControl.RequestAdditionalTime(TimeSpan.FromMinutes(1));

            //Initalize Service Workers

            var settings = new Settings();

            // Start OWIN host 
            _webApp = Microsoft.Owin.Hosting.WebApp.Start<WebAppConfig>(url: settings.GetServiceUrl());

            Logger.Info("*************************************");
            Logger.Info("********[Service Started]***********");
            Logger.Info("*************************************");

            return true;
        }

        public bool Stop(HostControl hostControl)
        {
            Logger.Info("*************************************");
            Logger.Info("********[Stopping Service]***********");
            Logger.Info("*************************************");

            //Create Task
            var cts = new CancellationTokenSource();
            var stopTask = Task.Factory.StartNew(() =>
            {

                //Stop Service
                _webApp?.Dispose();

            }, cts.Token);


            if (!Task.WaitAll(new Task[] { stopTask }, 30000))
            {
                //Not Finsihed On Time
                cts.Cancel();
                Logger.Warn("Service Stop Not Finsihed On Time");
            }
            else
            {
                Logger.Info("*************************************");
                Logger.Info("********[Service Stopped]***********");
                Logger.Info("*************************************");
            }

            return true;
        }

    }

    public enum EEnvironment
    {
        Dev,
        QA,
        Production
    }

    public class Settings
    {
        public EEnvironment GetEnvironment()
        {
            return (EEnvironment)Enum.Parse(typeof(EEnvironment), ConfigurationManager.AppSettings["Environment"]);
        }

        public string GetServiceName()
        {
            return ConfigurationManager.AppSettings["ServiceName"];
        }

        public string GetServiceUrl()
        {
            return "http://localhost:5000/";
        }
    }

    public class WebAppConfig
    {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(WebAppConfig));

        public void Configuration(IAppBuilder appBuilder)
        {
            // Configure Web API for self-host. 
            HttpConfiguration config = new HttpConfiguration();
            config.MapHttpAttributeRoutes();
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );


            Settings settings = new Settings();

            config.Formatters.Remove(config.Formatters.XmlFormatter);
            config.Formatters.Add(config.Formatters.JsonFormatter);

            config.EnsureInitialized();

            //Example Custome MiddleWare
            appBuilder.Use((context, next) =>
            {
                var requestPath = context.Get<string>("owin.RequestPath");
                Logger.Info("My MiddleWare Example RequestPath:" + requestPath);

                return next.Invoke();
            });

            appBuilder.UseWebApi(config);

            Console.WriteLine("Available WebApi Endpoints:");
            foreach (var api in config.Services.GetApiExplorer().ApiDescriptions)
                Console.WriteLine($"{api.HttpMethod}  {settings.GetServiceUrl()}{api.RelativePath}");
            
        }
    }

    public class Program
    {

        private static readonly ILog Logger = LogManager.GetLogger(nameof(Program));

        public static void Main(string[] args)
        {
            var settings = new Settings();

            HostFactory.Run(x =>
            {
                x.Service<WindowsServiceController>(s =>
                {
                    s.ConstructUsing(hostSettings => new WindowsServiceController(hostSettings));
                    s.WhenStarted((service, control) => service.Start(control));
                    s.WhenStopped((service, control) => service.Stop(control));
                });

                x.EnableServiceRecovery(r =>
                {
                    r.RestartService(1);
                    r.RestartService(1);
                    r.RestartService(1);
                    r.OnCrashOnly();
                });


                if (settings.GetEnvironment() == EEnvironment.Dev)
                {
                    x.RunAsPrompt();
                }
                else
                {
                    x.RunAsLocalService();
                }

                x.OnException(ex => { Logger.Error(ex); });

                //Each service on the system must have a unique name.
                x.SetServiceName(settings.GetServiceName());
            });
        }

    }
}
