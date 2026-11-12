namespace EccentricWare.BlazorCli.TemplateApp.Application;

public interface IApplicationStatusService
{
    ApplicationStatusDto GetStatus(AppMode mode);
}
