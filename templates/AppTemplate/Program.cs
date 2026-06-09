using AppTemplate;
using XSTH.Blueprint.Helpers;

Adw.Module.Initialize();
GResourceHelper.RegisterAssemblyResources(typeof(Program).Assembly);

var app = new App();
return app.RunWithSynchronizationContext(args);