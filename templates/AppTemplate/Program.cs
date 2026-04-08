using AppTemplate;
using XSTH.Blueprint.Helpers;

Adw.Module.Initialize();
GResourceHelper.RegisterAssemblyResources();

var app = new App();
return app.RunWithSynchronizationContext(args);