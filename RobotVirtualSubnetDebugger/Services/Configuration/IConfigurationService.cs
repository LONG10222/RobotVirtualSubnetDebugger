using RobotNet.Windows.Wpf.Models;

namespace RobotNet.Windows.Wpf.Services.Configuration;

public interface IConfigurationService
{
    AppConfig Load();

    void Save(AppConfig config);

    AppConfig CreateDefault();
}
